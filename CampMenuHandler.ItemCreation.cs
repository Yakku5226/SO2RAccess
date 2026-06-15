using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Item Creation sub-screen accessibility (Camp → Item Creation).
    ///
    /// Flow:
    ///   Screen 1 — Skill Selection (UICampSelectSpecialSkillSelector : UIListSelectorBase)
    ///     Tabs: ItemCreation / SpecialSkill / SuperSpecialSkill.
    ///     Hook: UISpecialSkillInformationPresenter.Set(skillName, skillDescription, ...).
    ///
    ///   Screen 2 — Action List (UICampSpecialSkillActionSelectorBase : UIListSelectorBase)
    ///     Generic for all skill types (Craft, Alchemy, Cooking, etc.).
    ///     Hook: UIItemCreationInformationPresenter.Set(UIItemCreationInformationData).
    ///     Data: UISpecialSkillConsumeListItemData — actionName, itemCount, canDecision.
    ///
    ///   Screen 3 — Material Selection (UICampSpecialSkillAddMaterialSelector)
    ///     Two sub-states: SelectMaterial (slot list) and ItemList (inventory picker).
    ///     Hook: AddMaterialSelector.Set (CallerCount 1) for entry detection.
    ///     Data: InvestFactorItemData (slots), UIItemListItemData (inventory items).
    ///     Factor rate from UIPercentagePresenter.targetPercentage.
    ///
    ///   Screen 4 — Result (UICampSpecialSkillResultSelector : UIListSelectorBase)
    ///     Data: UICampSpecialSkillResultListItemData — itemName, result, isSuccess.
    ///
    /// All navigation is native-only (polling). Hooks capture info panel data.
    /// </summary>
    public partial class CampMenuHandler
    {
        #region Item Creation Fields

        /// <summary>True when IC opened via field shortcut (D-pad Down), not camp root menu.</summary>
        private static bool _isFieldShortcutIC;

        // --- Screen 1: Skill Selection ---
        private static UICampSelectSpecialSkillSelector _icSkillSelector;
        private static readonly SubScreenState _icSkillState = new SubScreenState();
        private static int _icLastTab = -1;

        // --- Screen 2: Action List (generic for all skill types) ---
        /// <summary>All special skill selectors from UICampWindow, for activeInHierarchy scanning.</summary>
        private static readonly List<UICampSpecialSkillSelectorBase> _icAllSelectors = new List<UICampSpecialSkillSelectorBase>();
        private static UICampSpecialSkillSelectorBase _icActiveSelector;
        private static UIListSelectorBase _icActionListBase;
        private static readonly SubScreenState _icActionState = new SubScreenState();
        private static int _icLastCharTab = -1;

        // --- Screen 2a: Active skill category (set by creation hook to gate polling) ---
        private static string _icActiveSkillCategory;

        // --- Screen 2a: Train switch selector (toggle ON/OFF per party member) ---
        private static UICampSpecialSkillSwitchSelector _icTrainSwitchSelector;
        private static int _icTrainSwitchLastIndex = -1;

        // --- Screen 2a: Scout action selector (Search/Escape/Nothing) ---
        private static UICampSpecialSkillScoutSelector _icScoutSelector;
        private static UIListSelectorBase _icScoutActionListBase;
        private static int _icScoutLastIndex = -1;

        // --- Screen 2b: Create mode (after selecting material) ---
        private static UICampSpecialSkillActionSelectorBase _icActionSelectorBase;
        private static UICampSpecialSkillActionPresenter _icActionPresenter;
        private static int _icLastCreateCount = -1;

        // --- Screen 3: Result ---
        private static UICampSpecialSkillResultSelector _icResultSelector;
        private static readonly SubScreenState _icResultState = new SubScreenState();
        /// <summary>Time.time after which the result index reset takes effect (animation delay).</summary>
        private static float _icResultReadyTime;

        // --- Hook data ---
        private static string _icPendingSkillName;
        private static bool _icCreationHookFired;

        // --- Screen 3b: Material Selection ---
        private static UICampSpecialSkillAddMaterialSelector _icAddMaterialSelector;
        private static bool _icMaterialSetHookFired;
        private static int _icMaterialLastState = -1;
        private static readonly SubScreenState _icMaterialSelectState = new SubScreenState();
        private static readonly SubScreenState _icMaterialItemListState = new SubScreenState();
        private static UIListSelectorBase _icMaterialSelectListBase;
        private static UIListSelectorBase _icMaterialItemListBase;

        // --- Action-screen focus tracking ---
        /// <summary>
        /// Per-selector last-seen action-list currentIndex, parallel to
        /// _icAllSelectors. -1 means the selector's list is not populated / unseen.
        /// Used to detect which skill the user is actually navigating, since every
        /// selector reports activeInHierarchy == true for the whole IC session.
        /// </summary>
        private static int[] _icSelLastIndex;

        /// <summary>Index (into _icAllSelectors) of the focused skill's selector, or -1.</summary>
        private static int _icFocusedIdx = -1;

        /// <summary>
        /// Signature of the result content we last reacted to. Used to detect a freshly
        /// appeared result (e.g. an appraisal outcome) and trigger its announcement, since
        /// appraisal bypasses the create-count flow that normally schedules the result.
        /// Cleared when the user navigates the action list again (so a later result reads).
        /// </summary>
        private static string _icResultSeenSig;

        #endregion

        #region Item Creation Caching (called from Open postfix)

        /// <summary>
        /// Caches item creation selectors from the camp window.
        /// Called from CampWindow_Open_Postfix (static context).
        /// </summary>
        private static void CacheItemCreationSelectors(UICampWindow window)
        {
            _icSkillSelector = window.selectSpecialSkillSelector;
            _icSkillState.Reset();
            _icLastTab = -1;

            _icResultSelector = window.specialSkillResultSelector;
            _icResultState.Reset();
            _icResultSeenSig = null;

            _icActionState.Reset();
            _icActiveSelector = null;
            _icActionListBase = null;
            _icLastCharTab = -1;
            _icActiveSkillCategory = null;
            _icTrainSwitchSelector = null;
            _icTrainSwitchLastIndex = -1;
            _icScoutSelector = window.scoutSelector;
            _icScoutActionListBase = null;
            _icScoutLastIndex = -1;
            ResetCreateModeState();
            _icPendingSkillName = null;
            _icCreationHookFired = false;

            // Collect all special skill selectors for generic active scanning.
            _icAllSelectors.Clear();
            TryAddSelector(window.craftSelector);
            TryAddSelector(window.alchemySelector);
            TryAddSelector(window.cookingSelector);
            TryAddSelector(window.artSelector);
            TryAddSelector(window.machinerySelector);
            TryAddSelector(window.writingSelector);
            TryAddSelector(window.duplicateSelector);
            TryAddSelector(window.appraisalSelector);
            TryAddSelector(window.superAppraisalSelector);
            TryAddSelector(window.customizeSelector);
            TryAddSelector(window.mixingSelector);
            TryAddSelector(window.publishingSelector);
            TryAddSelector(window.masterShefSelector);
            TryAddSelector(window.musicSelector);
            TryAddSelector(window.openEyesSelector);
            TryAddSelector(window.cutInSelector);
            TryAddSelector(window.comeonBunnySelector);
            TryAddSelector(window.orchestraSelector);
            TryAddSelector(window.fishingSelector);
            TryAddSelector(window.blackSmithSelector);
            TryAddSelector(window.familiarSelector);
            TryAddSelector(window.pickPocketSelector);
            TryAddSelector(window.oracleSelector);
            TryAddSelector(window.scoutSelector);
            TryAddSelector(window.trainingSelector);
            TryAddSelector(window.survivalSelector);
            TryAddSelector(window.remakeSelector);
            TryAddSelector(window.reverseSideSelector);

            // Material selection selector.
            _icAddMaterialSelector = window.addMaterialSelector;
            _icMaterialSetHookFired = false;
            _icMaterialLastState = -1;
            _icMaterialSelectState.Reset();
            _icMaterialItemListState.Reset();
            _icMaterialSelectListBase = null;
            _icMaterialItemListBase = null;

            if (_icSkillSelector != null)
            {
                DebugLogger.LogState("CampIC: skill selector cached.");
                try
                {
                    if (_icSkillSelector.gameObject.activeInHierarchy)
                    {
                        // Seed skill index, tab, and character tab to prevent stale
                        // announcements when scrolling past IC in root menu.
                        var listBase = _icSkillSelector.TryCast<UIListSelectorBase>();
                        if (listBase != null)
                            _icSkillState.SeedOnOpen(listBase.currentIndex);
                        else
                            _icSkillState.SuppressNextHeading();
                        _icLastTab = (int)_icSkillSelector.currentTab;

                        // Seed character tab from the first active action selector.
                        foreach (var sel in _icAllSelectors)
                        {
                            try
                            {
                                if (sel?.gameObject?.activeInHierarchy == true)
                                {
                                    _icLastCharTab = sel.currentTabIndex;
                                    break;
                                }
                            }
                            catch { /* skip */ }
                        }

                        DebugLogger.LogState($"CampIC_Skill: stale on open, index/tab/charTab seeded.");
                    }
                }
                catch
                {
                    _icSkillState.SuppressNextHeading();
                }
            }
            else
            {
                MelonLogger.Warning("[CAMP] campWindow.selectSpecialSkillSelector is null.");
            }

            // Suppress stale action selectors and seed index — at least one is
            // always active. Seeding prevents stale data re-announcement when
            // scrolling past IC in root menu.
            foreach (var sel in _icAllSelectors)
            {
                try
                {
                    if (sel?.gameObject?.activeInHierarchy == true)
                    {
                        var actionSel = sel.actionSelector;
                        var actionBase = actionSel?.TryCast<UIListSelectorBase>();
                        if (actionBase != null)
                            _icActionState.SeedOnOpen(actionBase.currentIndex);
                        else
                            _icActionState.SuppressNextHeading();
                        DebugLogger.LogState("CampIC_Action: stale on open, index seeded.");
                        break;
                    }
                }
                catch { /* skip */ }
            }

            if (_icResultSelector != null)
            {
                DebugLogger.LogState("CampIC: result selector cached.");
                // Seed index (not just suppress heading) so stale polling doesn't
                // re-announce old result data when scrolling past IC in root menu.
                try
                {
                    if (_icResultSelector.gameObject.activeInHierarchy)
                    {
                        var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
                        if (listBase != null)
                            _icResultState.SeedOnOpen(listBase.currentIndex);
                        else
                            _icResultState.SuppressNextHeading();
                        DebugLogger.LogState("CampIC_Result: stale on open, index seeded.");
                    }
                }
                catch
                {
                    _icResultState.SuppressNextHeading();
                }
            }
            if (_icAddMaterialSelector != null)
                DebugLogger.LogState("CampIC: addMaterialSelector cached.");

            DebugLogger.LogState($"CampIC: {_icAllSelectors.Count} skill type selectors cached.");

            // Seed focus-tracking state. Record each selector's current action-list
            // index (rather than leaving it -1) so an already-populated list on open
            // is NOT mistaken for a fresh entry — otherwise scrolling past ItemCreation
            // in the root menu would announce a spurious item. Same intent as the
            // stale-open suppression above.
            SeedActionFocusTracking();

            // Super Specialty selector (separate overlay, not on UICampWindow).
            CacheSuperSpecialtySelector();
        }

        /// <summary>
        /// Allocates and seeds <see cref="_icSelLastIndex"/> from the current state of
        /// each selector's action list, and clears <see cref="_icFocusedIdx"/>.
        /// Called once per camp open after _icAllSelectors is built.
        /// </summary>
        private static void SeedActionFocusTracking()
        {
            _icFocusedIdx = -1;
            _icSelLastIndex = new int[_icAllSelectors.Count];
            for (int i = 0; i < _icAllSelectors.Count; i++)
            {
                int seeded = -1;
                try
                {
                    var sel = _icAllSelectors[i];
                    if (sel?.gameObject?.activeInHierarchy == true)
                    {
                        var listBase = sel.actionSelector?.TryCast<UIListSelectorBase>();
                        int count = listBase?.currentDataList?.Count ?? 0;
                        if (count > 0) seeded = listBase.currentIndex;
                    }
                }
                catch { /* leave -1 */ }
                _icSelLastIndex[i] = seeded;
            }
        }

        private static void TryAddSelector(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase selector)
        {
            if (selector == null) return;
            var cast = selector.TryCast<UICampSpecialSkillSelectorBase>();
            if (cast != null) _icAllSelectors.Add(cast);
        }

        #endregion

        #region Item Creation Update

        /// <summary>
        /// Returns true if IC sub-screens should be active — either via camp root menu
        /// or via field shortcut (D-pad Down on field).
        /// </summary>
        private static bool IsICActive() =>
            _lastRootMenuItemName == "ItemCreation" || _isFieldShortcutIC;

        /// <summary>
        /// Polls item creation sub-screens. Called from Update() when IC is active
        /// (camp root menu on ItemCreation, or field shortcut).
        /// </summary>
        private void UpdateItemCreation()
        {
            if (!IsICActive()) return;

            try
            {
                UpdateICSkillSelection();
                UpdateICActionList();
                UpdateICMaterialSelection();
                UpdateICResult();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: Update error: {ex.Message}");
            }
        }

        #endregion

        #region Screen 1: Skill Selection

        private void UpdateICSkillSelection()
        {
            if (_icSkillSelector == null) return;

            bool isActive;
            try { isActive = _icSkillSelector.gameObject.activeInHierarchy; }
            catch { return; }

            bool shouldPoll = _icSkillState.CheckEntry(
                isActive,
                () => ScreenReader.Say(Loc.Get("ic_screen")),
                "CampIC_Skill",
                onHidden: () =>
                {
                    _icLastTab = -1;
                    _icPendingSkillName = null;
                    // If we were in field shortcut mode and the skill selector hid,
                    // the user backed out — clear shortcut flag so IC polling stops.
                    if (_isFieldShortcutIC)
                    {
                        _isFieldShortcutIC = false;
                        DebugLogger.LogState("CampIC: field shortcut IC cleared (skill selector hidden).");
                    }
                });

            if (!shouldPoll) return;

            // Track tab changes.
            try
            {
                int tab = (int)_icSkillSelector.currentTab;
                if (tab != _icLastTab)
                {
                    _icLastTab = tab;
                    // Clear pending hook data so fallback polling isn't blocked on new tab.
                    _icPendingSkillName = null;
                    string tabName = tab switch
                    {
                        0 => Loc.Get("ic_tab_itemcreation"),
                        1 => Loc.Get("ic_tab_specialskill"),
                        2 => Loc.Get("ic_tab_superspecialskill"),
                        _ => $"Tab {tab}"
                    };
                    ScreenReader.Say(tabName);
                    DebugLogger.LogState($"CampIC: tab changed to {tab} ({tabName})");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: tab poll error: {ex.Message}");
            }

            // The hook (SkillInfoPresenter_Set) handles cursor announcements for tabs 0/1.
            // Tab 2 (super specialty) uses a different info presenter whose Set is native-only,
            // so we poll and read its GameText fields directly.
            if (_icLastTab == 2)
                TryPollSuperSpecialtyTab();
            else
                TryPollSkillSelectionFallback();
        }

        /// <summary>
        /// Fallback polling for skill selection in case the hook doesn't fire.
        /// Reads currentIndex from the UIListSelectorBase.
        /// </summary>
        private void TryPollSkillSelectionFallback()
        {
            // If the hook already provides announcements, skip polling.
            if (_icPendingSkillName != null) return;

            try
            {
                var listBase = _icSkillSelector.TryCast<UIListSelectorBase>();
                if (listBase == null) return;

                int idx = listBase.currentIndex;
                if (idx == _icSkillState.LastIndex) return;
                _icSkillState.LastIndex = idx;

                int count = listBase.currentDataList?.Count ?? 0;
                if (count <= 0) return;

                // Data items don't have a name field; we just announce position.
                ScreenReader.Say(Loc.Get("ic_skill_position", idx + 1, count));
                DebugLogger.LogState($"CampIC: skill fallback idx={idx}/{count}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: skill fallback error: {ex.Message}");
            }
        }

        #endregion

        #region Hooks

        /// <summary>
        /// Postfix hook for UICampSpecialSkillAddMaterialSelector.Set.
        /// Fires when the material selection screen is initialized (CallerCount 1).
        /// Signals entry into the material selection flow.
        /// </summary>
        private static void AddMaterialSelector_Set_IC_Postfix()
        {
            if (!IsICActive()) return;

            _icMaterialSetHookFired = true;
            _icMaterialLastState = -1;
            _icMaterialSelectState.Reset();
            _icMaterialItemListState.Reset();
            _icMaterialSelectListBase = null;
            _icMaterialItemListBase = null;

            DebugLogger.LogState("CampIC: AddMaterialSelector.Set hook fired — material screen entry.");
        }

        /// <summary>
        /// Postfix hook for UISpecialSkillInformationPresenter.Set.
        /// Fires when the skill info panel updates (skill selection screen).
        /// Harmony calls this by name — must match nameof() in ApplyPatches.
        /// </summary>
        private static void SkillInfoPresenter_Set_IC_Postfix(
            string skillName, string skillDescription, int level)
        {
            if (string.IsNullOrEmpty(skillName)) return;
            if (!IsICActive()) return;

            _icPendingSkillName = skillName;

            var sb = new StringBuilder();
            sb.Append(skillName);
            if (level >= 0)
                sb.Append(", ").Append(Loc.Get("ic_skill_level", level));
            if (!string.IsNullOrEmpty(skillDescription))
                sb.Append(". ").Append(skillDescription);

            // Position from polling.
            try
            {
                var listBase = _icSkillSelector?.TryCast<UIListSelectorBase>();
                if (listBase != null)
                {
                    int idx = listBase.currentIndex;
                    int count = listBase.currentDataList?.Count ?? 0;
                    if (count > 0)
                        TextUtil.AppendPosition(sb, idx, count);
                }
            }
            catch { /* ignore */ }

            ScreenReader.Say(sb.ToString());
            DebugLogger.LogState($"CampIC: skill hook: {skillName} lv{level}");
        }

        /// <summary>
        /// Postfix hook for UIItemCreationInformationPresenter.Set.
        /// Fires when the creation info panel updates (action screen).
        /// Harmony calls this by name — must match nameof() in ApplyPatches.
        /// </summary>
        private static void CreationInfoPresenter_Set_IC_Postfix(
            UIItemCreationInformationData data)
        {
            if (data == null) return;
            if (!IsICActive()) return;

            // Skills like Scouting and Survival have no creation items —
            // their action lists use UISpecialSkillConsumeListItemData.actionName.
            // Don't announce or set hookFired so dedicated polls handle them.
            var creationListCheck = data.dataList;
            if (creationListCheck == null || creationListCheck.Count == 0)
            {
                _icActiveSkillCategory = data.categoryName;
                DebugLogger.LogState($"CampIC: creation hook (no items): {data.categoryName ?? "?"}, activeSkill={_icActiveSkillCategory}");
                return;
            }

            _icCreationHookFired = true;
            _icActiveSkillCategory = null; // Regular creation skill, clear special category.

            var sb = new StringBuilder();

            // Category + level.
            string catName = data.categoryName;
            if (!string.IsNullOrEmpty(catName))
                sb.Append(catName);

            if (data.isLevel && data.level > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Loc.Get("ic_skill_level", data.level));
            }

            // Possible creation results.
            try
            {
                var creationList = data.dataList;
                if (creationList != null && creationList.Count > 0)
                {
                    var first = creationList[0]?.TryCast<UISpecialSkillCreationListItemData>();
                    if (first != null)
                    {
                        string itemName = SanitizeItemName(first.itemName);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(Loc.Get("ic_creates", itemName));
                        }

                        string rate = first.creationRate;
                        if (!string.IsNullOrEmpty(rate))
                            sb.Append(", ").Append(rate);

                        int have = first.haveCount;
                        if (have > 0)
                            sb.Append(". ").Append(Loc.Get("ic_have_count", have));

                        if (first.isGrayout)
                            sb.Append(". ").Append(Loc.Get("ic_unavailable"));
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: creation hook dataList error: {ex.Message}");
            }

            // Factor info.
            string factorName = data.factorName;
            if (!string.IsNullOrEmpty(factorName))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(Loc.Get("ic_factor", factorName));
            }

            // Item effect.
            string effectDesc = data.itemEffectDescription;
            if (!string.IsNullOrEmpty(effectDesc))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(effectDesc);
            }

            // Consumable-material requirement (Machinist-style fixed recipes).
            // Read from the highlighted action item, which carries consumeItemID.
            try
            {
                if (_icActionListBase != null)
                {
                    int aidx = _icActionListBase.currentIndex;
                    var alist = _icActionListBase.currentDataList;
                    if (alist != null && aidx >= 0 && aidx < alist.Count)
                    {
                        var actionItem = alist[aidx]?.TryCast<UISpecialSkillConsumeListItemData>();
                        string need = ReadConsumeRequirement(actionItem);
                        if (!string.IsNullOrEmpty(need))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(need);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // Position from action list.
            try
            {
                if (_icActionListBase != null)
                {
                    int idx = _icActionListBase.currentIndex;
                    int count = _icActionListBase.currentDataList?.Count ?? 0;
                    if (count > 0)
                        TextUtil.AppendPosition(sb, idx, count);
                }
            }
            catch { /* ignore */ }

            if (sb.Length > 0)
            {
                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"CampIC: creation hook: {catName ?? "?"}, creates={data.dataList?.Count ?? 0} items");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Replaces punctuation-only item names (e.g. "????") with "Unknown"
        /// so screen readers don't skip them at low verbosity.
        /// </summary>
        private static string SanitizeItemName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Check if every character is '?' — the game uses this for undiscovered recipes.
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] != '?') return name;
            }
            return Loc.Get("ic_unknown_item");
        }

        /// <summary>
        /// Reads the consumable-material requirement from a fixed-recipe action item
        /// (Machinist's "Create Portable Item" etc.). Returns a localized "Needs X"
        /// string, or null when the action consumes no fixed item — free-material
        /// crafts (Cooking, Alchemy, ...) leave consumeItemID at 0, so they stay silent.
        /// </summary>
        private static string ReadConsumeRequirement(UISpecialSkillConsumeListItemData item)
        {
            if (item == null) return null;
            try
            {
                int consumeID = item.consumeItemID;
                if (consumeID <= 0) return null;

                int qty = item.consumeValue;
                string matName = ResolveConsumeItemName(consumeID, qty);
                if (string.IsNullOrEmpty(matName)) return null;

                return qty > 1
                    ? Loc.Get("ic_consumes_qty", matName, qty)
                    : Loc.Get("ic_consumes", matName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves a consumable's display name. GetItemParameter + TextManager returns
        /// a numeric placeholder for these recipe consumables (same limitation as fish
        /// names — the name key only resolves in native code), so we first ask the
        /// game's own CreateConsumeItemData, which builds the exact data shown on screen.
        /// Falls back to the parameter-based resolver if that is unavailable.
        /// </summary>
        private static string ResolveConsumeItemName(int consumeItemID, int consumeValue)
        {
            try
            {
                var selBase = _icActionSelectorBase;
                if (selBase == null && _icActiveSelector != null)
                    selBase = _icActiveSelector.actionSelector?.TryCast<UICampSpecialSkillActionSelectorBase>();

                if (selBase != null)
                {
                    var data = selBase.CreateConsumeItemData(consumeItemID, consumeValue);
                    string raw = data?.itemName;
                    DebugLogger.LogState($"CampIC: consume resolve id={consumeItemID} -> '{raw}' (have={data?.haveCount})");
                    string n = SanitizeItemName(raw);
                    if (!string.IsNullOrEmpty(n) && !IsNumericName(n))
                        return n;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: consume resolve id={consumeItemID} error: {ex.Message}");
            }

            string fallback = ResolveItemName(consumeItemID);
            return (string.IsNullOrEmpty(fallback) || IsNumericName(fallback)) ? null : fallback;
        }

        /// <summary>
        /// True when a resolved "name" is actually just a numeric placeholder key
        /// (e.g. "0456"), so callers can suppress it rather than read digits aloud.
        /// </summary>
        private static bool IsNumericName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
                if (!char.IsDigit(c)) return false;
            return true;
        }

        /// <summary>
        /// Reads the consumable requirement straight from the on-screen consume display
        /// (UICampSpecialSkillActionPresenter.consumeItemPresenter), which the game fills
        /// with the ACTUAL required items. This is authoritative where the list item's
        /// consumeItemID is unreliable — e.g. Writing, whose consumeItemID points back at
        /// the book being written rather than the Fountain Pen that is actually consumed.
        /// Returns a localized "Needs A, B" string, or null when nothing is displayed.
        /// Only safe to call from the per-frame poll (the display is current by then);
        /// the creation-info Harmony postfix may run before the display is refreshed,
        /// which is why that path keeps using consumeItemID instead.
        /// </summary>
        private static string ReadConsumeRequirementFromDisplay()
        {
            try
            {
                var rows = _icActionPresenter?.consumeItemPresenter?.consumeItemPresenterList;
                if (rows == null || rows.Count == 0) return null;

                var names = new List<string>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    try { if (row.gameObject?.activeInHierarchy != true) continue; }
                    catch { continue; }

                    string n = TextUtil.StripTags(row.itemNamePresenter?.itemName?.text);
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    names.Add(n.Trim());
                }

                if (names.Count == 0) return null;
                return Loc.Get("ic_consumes", string.Join(", ", names));
            }
            catch
            {
                return null;
            }
        }

        #endregion

    }
}
