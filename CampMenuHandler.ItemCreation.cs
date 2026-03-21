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
        private static string _icPendingSkillDesc;
        private static int _icPendingSkillLevel = -1;
        private static UIItemCreationInformationData _icPendingCreationData;
        private static bool _icCreationHookFired;

        // --- Screen 3b: Material Selection ---
        private static UICampSpecialSkillAddMaterialSelector _icAddMaterialSelector;
        private static bool _icMaterialSetHookFired;
        private static int _icMaterialLastState = -1;
        private static readonly SubScreenState _icMaterialSelectState = new SubScreenState();
        private static readonly SubScreenState _icMaterialItemListState = new SubScreenState();
        private static UIListSelectorBase _icMaterialSelectListBase;
        private static UIListSelectorBase _icMaterialItemListBase;

        // --- Diagnostics (first open only) ---
        private static bool _icDiagDone;

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

            _icActionState.Reset();
            _icActiveSelector = null;
            _icActionListBase = null;
            _icLastCharTab = -1;
            ResetCreateModeState();
            _icPendingSkillName = null;
            _icPendingSkillDesc = null;
            _icPendingSkillLevel = -1;
            _icPendingCreationData = null;
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

            // Super Specialty selector (separate overlay, not on UICampWindow).
            CacheSuperSpecialtySelector();
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
                    _icPendingSkillDesc = null;
                    // If we were in field shortcut mode and the skill selector hid,
                    // the user backed out — clear shortcut flag so IC polling stops.
                    if (_isFieldShortcutIC)
                    {
                        _isFieldShortcutIC = false;
                        DebugLogger.LogState("CampIC: field shortcut IC cleared (skill selector hidden).");
                    }
                });

            if (!shouldPoll) return;

            // Diagnostic dump on first activation.
            if (!_icDiagDone)
            {
                _icDiagDone = true;
                try
                {
                    var listBase = _icSkillSelector.TryCast<UIListSelectorBase>();
                    int idx = listBase?.currentIndex ?? -99;
                    int count = listBase?.currentDataList?.Count ?? -99;
                    int tab = (int)_icSkillSelector.currentTab;
                    DebugLogger.LogState($"CampIC DIAG: skillSel idx={idx}, count={count}, tab={tab}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC DIAG failed: {ex.Message}");
                }
            }

            // Track tab changes.
            try
            {
                int tab = (int)_icSkillSelector.currentTab;
                if (tab != _icLastTab)
                {
                    _icLastTab = tab;
                    // Clear pending hook data so fallback polling isn't blocked on new tab.
                    _icPendingSkillName = null;
                    _icPendingSkillDesc = null;
                    _icPendingSkillLevel = -1;
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

        #region Screen 2: Action List

        private void UpdateICActionList()
        {
            // Scan all skill selectors to find which one is active.
            UICampSpecialSkillSelectorBase foundSelector = null;
            foreach (var sel in _icAllSelectors)
            {
                try
                {
                    if (sel?.gameObject?.activeInHierarchy == true)
                    {
                        foundSelector = sel;
                        break;
                    }
                }
                catch { /* skip broken refs */ }
            }

            bool isActive = foundSelector != null;

            bool shouldPoll = _icActionState.CheckEntry(
                isActive,
                () =>
                {
                    // Announce heading from hook data if available.
                    if (_icPendingCreationData != null)
                    {
                        string heading = BuildCreationHeading(_icPendingCreationData);
                        ScreenReader.Say(heading);
                    }
                    else
                    {
                        ScreenReader.Say(Loc.Get("ic_action_screen"));
                    }
                },
                "CampIC_Action",
                onHidden: () =>
                {
                    _icActiveSelector = null;
                    _icActionListBase = null;
                    _icActionSelectorBase = null;
                    _icActionPresenter = null;
                    _icLastCharTab = -1;
                    _icCreationHookFired = false;
                    _icPendingCreationData = null;
                    ResetCreateModeState();
                });

            if (!shouldPoll) return;

            _icActiveSelector = foundSelector;

            // Get the action list base for polling.
            if (_icActionListBase == null)
            {
                try
                {
                    var actionSel = _icActiveSelector.actionSelector;
                    _icActionListBase = actionSel?.TryCast<UIListSelectorBase>();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC: action list cast error: {ex.Message}");
                }
            }

            // Track character tab changes.
            TrackCharacterTab();

            // Track Create mode (material confirmed, count adjustable).
            PollCreateMode();

            // If hook fired, it handles announcements. Otherwise poll.
            if (_icCreationHookFired)
            {
                _icCreationHookFired = false;
                // Hook already announced — just update LastIndex.
                if (_icActionListBase != null)
                {
                    try { _icActionState.LastIndex = _icActionListBase.currentIndex; }
                    catch { /* ignore */ }
                }
                return;
            }

            // Fallback: poll action list directly.
            PollActionListFallback();
        }

        private void TrackCharacterTab()
        {
            if (_icActiveSelector == null) return;
            try
            {
                int tabIdx = _icActiveSelector.currentTabIndex;
                if (tabIdx == _icLastCharTab) return;
                _icLastCharTab = tabIdx;

                // Try to get character name from executablePlayerIDList.
                string charName = null;
                var playerList = _icActiveSelector.executablePlayerIDList;
                if (playerList != null && tabIdx >= 0 && tabIdx < playerList.Count)
                {
                    var playerID = playerList[tabIdx];
                    try
                    {
                        charName = ParameterManager.Instance.GetCharacterFirstName(playerID);
                    }
                    catch { /* ignore */ }
                }
                if (!string.IsNullOrEmpty(charName))
                {
                    ScreenReader.Say(charName);
                    DebugLogger.LogState($"CampIC: character tab changed to {tabIdx} ({charName})");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: character tab error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tracks the Create mode on the action screen.
        /// When the user confirms a material, currentCreateCount transitions from
        /// -1 (inactive) to a positive value (Create button visible with count).
        /// The user can adjust the count with D-pad. Pressing Confirm triggers
        /// the "Implement IC?" dialog.
        /// Announces: entry with count + success rate, count changes, exit.
        /// </summary>
        private void PollCreateMode()
        {
            // Cache action selector base.
            if (_icActionSelectorBase == null && _icActiveSelector != null)
            {
                try
                {
                    var actionSel = _icActiveSelector.actionSelector;
                    _icActionSelectorBase = actionSel?.TryCast<UICampSpecialSkillActionSelectorBase>();
                }
                catch { /* skip */ }
            }
            if (_icActionSelectorBase == null) return;

            // Cache action presenter.
            if (_icActionPresenter == null)
            {
                try { _icActionPresenter = _icActionSelectorBase.actionPresenter; }
                catch { /* skip */ }
            }
            if (_icActionPresenter == null) return;

            try
            {
                int createCount = _icActionPresenter.currentCreateCount;
                if (createCount == _icLastCreateCount) return;

                int prevCount = _icLastCreateCount;
                _icLastCreateCount = createCount;

                if (createCount > 0 && prevCount <= 0)
                {
                    // Entering Create mode — announce with success rate.
                    var sb = new StringBuilder();
                    sb.Append(Loc.Get("ic_material_create"));
                    sb.Append(' ').Append(createCount);

                    // Try to read success rate from the presenter.
                    string rate = ReadSuccessRate();
                    if (!string.IsNullOrEmpty(rate))
                        sb.Append(". ").Append(Loc.Get("ic_material_rate", rate));

                    sb.Append('.');
                    ScreenReader.Say(sb.ToString());
                    DebugLogger.LogState($"CampIC: Create mode entered, count={createCount}, rate={rate ?? "?"}");
                }
                else if (createCount > 0 && prevCount > 0)
                {
                    // Count changed while in Create mode.
                    ScreenReader.Say(createCount.ToString());
                    DebugLogger.LogState($"CampIC: Create count changed to {createCount}");
                }
                else if (createCount <= 0 && prevCount > 0)
                {
                    // Exiting Create mode (cancelled or executing).
                    // Schedule result index reset with delay so the result animation
                    // has time to play before the screen reader announces.
                    _icResultReadyTime = UnityEngine.Time.time + 1.5f;
                    DebugLogger.LogState("CampIC: Create mode exited, result delayed 1.5s.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: CreateMode poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the success rate text from the action presenter.
        /// Returns the rate string (e.g. "90") or null if unreadable.
        /// </summary>
        private static string ReadSuccessRate()
        {
            try
            {
                var rateText = _icActionPresenter?.successRate;
                if (rateText == null) return null;

                string text = rateText.text;
                if (string.IsNullOrEmpty(text)) return null;

                // Strip "%" suffix if present for clean TTS.
                text = text.Trim();
                if (text.EndsWith("%"))
                    text = text.Substring(0, text.Length - 1).Trim();

                return text;
            }
            catch
            {
                return null;
            }
        }

        private static void ResetCreateModeState()
        {
            _icActionSelectorBase = null;
            _icActionPresenter = null;
            _icLastCreateCount = -1;
        }

        private void PollActionListFallback()
        {
            if (_icActionListBase == null) return;

            try
            {
                int idx = _icActionListBase.currentIndex;
                if (idx == _icActionState.LastIndex) return;
                _icActionState.LastIndex = idx;

                var list = _icActionListBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var item = list[idx]?.TryCast<UISpecialSkillConsumeListItemData>();
                if (item != null)
                {
                    string name = item.actionName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        var sb = new StringBuilder();
                        sb.Append(name);

                        if (!item.canDecision)
                            sb.Append(", unavailable");

                        sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');
                        ScreenReader.Say(sb.ToString());
                        DebugLogger.LogState($"CampIC: action fallback [{idx}] {name}");
                        return;
                    }
                }

                // Bare position if we can't read the data.
                ScreenReader.Say(Loc.Get("ic_action_position", idx + 1, count));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: action fallback error: {ex.Message}");
            }
        }

        #endregion

        #region Screen 3: Result

        private void UpdateICResult()
        {
            if (_icResultSelector == null) return;

            // Delayed reset: wait for result animation before announcing.
            if (_icResultReadyTime > 0f && UnityEngine.Time.time >= _icResultReadyTime)
            {
                _icResultState.LastIndex = -1;
                _icResultReadyTime = 0f;
                DebugLogger.LogState("CampIC: result index reset after delay.");
            }

            bool isActive;
            try { isActive = _icResultSelector.gameObject.activeInHierarchy; }
            catch { return; }

            bool shouldPoll = _icResultState.CheckEntry(
                isActive,
                () => ScreenReader.Say(Loc.Get("ic_result_heading")),
                "CampIC_Result");

            if (!shouldPoll) return;

            try
            {
                var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
                if (listBase == null) return;

                int idx = listBase.currentIndex;
                if (idx == _icResultState.LastIndex) return;
                _icResultState.LastIndex = idx;

                var list = listBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var item = list[idx]?.TryCast<UICampSpecialSkillResultListItemData>();
                if (item == null) return;

                string name = item.itemName ?? "Unknown";
                string resultText = item.result ?? "";
                string status = item.isSuccess
                    ? Loc.Get("ic_result_success")
                    : Loc.Get("ic_result_failure");

                var sb = new StringBuilder();
                sb.Append(name).Append(". ").Append(status);
                if (!string.IsNullOrEmpty(resultText))
                    sb.Append(". ").Append(resultText);
                sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');

                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"CampIC: result [{idx}] {name} - {status}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: result error: {ex.Message}");
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
            _icPendingSkillDesc = skillDescription;
            _icPendingSkillLevel = level;

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
                        sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');
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

            _icPendingCreationData = data;
            _icCreationHookFired = true;

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

            // Position from action list.
            try
            {
                if (_icActionListBase != null)
                {
                    int idx = _icActionListBase.currentIndex;
                    int count = _icActionListBase.currentDataList?.Count ?? 0;
                    if (count > 0)
                        sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');
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

        private static string BuildCreationHeading(UIItemCreationInformationData data)
        {
            if (data == null) return Loc.Get("ic_action_screen");

            var sb = new StringBuilder();
            string catName = data.categoryName;
            if (!string.IsNullOrEmpty(catName))
                sb.Append(catName);

            if (data.isLevel && data.level > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Loc.Get("ic_skill_level", data.level));
            }

            sb.Append('.');
            return sb.ToString();
        }

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

        #endregion

        #region Screen 3b: Material Selection

        /// <summary>
        /// Polls the material selection screen (addMaterialSelector).
        /// This screen has two sub-states:
        ///   SelectMaterial — material slot list (choose which slot to fill).
        ///   ItemList — inventory picker (choose an item for the slot).
        ///
        /// Detection: polls sub-selectors' activeInHierarchy. The parent
        /// addMaterialSelector has stale activeInHierarchy, but the children
        /// (selectMaterialSelector, itemListSelector) should only be active
        /// when actually on the material screen. The Set hook is a bonus
        /// signal but not required.
        /// </summary>
        private void UpdateICMaterialSelection()
        {
            if (_icAddMaterialSelector == null) return;

            // Detection: hook-only. All sub-selectors have stale activeInHierarchy
            // (true from IC start), so we CANNOT use activeInHierarchy for detection.
            // The Set hook signals actual entry into the material selection flow.
            if (!_icMaterialSetHookFired) return;

            // Once hook fired, poll currentState for sub-state switching.
            int detectedState = -1;
            try
            {
                if (_icAddMaterialSelector.gameObject.activeInHierarchy)
                    detectedState = (int)_icAddMaterialSelector.currentState;
            }
            catch { return; }

            if (detectedState == -1)
            {
                if (_icMaterialLastState != -1)
                {
                    // Was active, now hidden — reset.
                    _icMaterialLastState = -1;
                    _icMaterialSelectState.Reset();
                    _icMaterialItemListState.Reset();
                    _icMaterialSelectListBase = null;
                    _icMaterialItemListBase = null;
                    _icMaterialSetHookFired = false;
                    DebugLogger.LogState("CampIC_Material: screen closed.");
                }
                return;
            }

            try
            {
                if (detectedState != _icMaterialLastState)
                {
                    int prevState = _icMaterialLastState;
                    _icMaterialLastState = detectedState;
                    DebugLogger.LogState($"CampIC_Material: state changed to {detectedState}");

                    if (detectedState == 0) // SelectMaterial
                    {
                        _icMaterialItemListState.Reset();
                        _icMaterialItemListBase = null;

                        if (prevState == -1)
                        {
                            // First entry — announce heading.
                            ScreenReader.Say(Loc.Get("ic_material_screen"));
                        }
                        else
                        {
                            // Returning from item list — announce current slot.
                            ScreenReader.Say(Loc.Get("ic_material_slots"));
                        }

                        // Announce factor rate on entry/return.
                        AnnounceMaterialFactorRate();
                    }
                    else if (detectedState == 1) // ItemList
                    {
                        _icMaterialSelectState.Reset();
                        _icMaterialSelectListBase = null;
                        ScreenReader.Say(Loc.Get("ic_material_itemlist"));
                    }
                }

                // Poll the active sub-selector.
                if (detectedState == 0)
                    PollMaterialSelectCursor();
                else if (detectedState == 1)
                    PollMaterialItemListCursor();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the material slot list (SelectMaterial state).
        /// Items are InvestFactorItemData — resolve names via ParameterManager.
        /// </summary>
        private void PollMaterialSelectCursor()
        {
            if (_icMaterialSelectListBase == null)
            {
                try
                {
                    var selMatSel = _icAddMaterialSelector.selectMaterialSelector;
                    _icMaterialSelectListBase = selMatSel?.TryCast<UIListSelectorBase>();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC_Material: selectMaterial cast error: {ex.Message}");
                    return;
                }
            }

            if (_icMaterialSelectListBase == null) return;

            try
            {
                int idx = _icMaterialSelectListBase.currentIndex;
                if (idx == _icMaterialSelectState.LastIndex) return;
                _icMaterialSelectState.LastIndex = idx;

                // Try to read material data from AddMaterialDataList.
                var selectMatSel = _icAddMaterialSelector.selectMaterialSelector;
                var materialList = selectMatSel?.AddMaterialDataList;
                int slotCount = materialList?.Count ?? 0;

                var sb = new StringBuilder();

                if (slotCount > 0 && idx >= 0 && idx < slotCount)
                {
                    var matData = materialList[idx];
                    if (matData != null && matData.itemID > 0)
                    {
                        // Slot has an item — resolve name.
                        string itemName = ResolveItemName(matData.itemID);
                        sb.Append(itemName);
                    }
                    else
                    {
                        // Empty slot.
                        sb.Append(Loc.Get("ic_material_empty"));
                    }
                }
                else
                {
                    // Might be the "Create" button at the end of the list.
                    // Try currentDataList for more entries.
                    var dataList = _icMaterialSelectListBase.currentDataList;
                    int totalCount = dataList?.Count ?? 0;

                    if (idx >= slotCount && idx < totalCount)
                    {
                        // Beyond material slots — likely Create button.
                        sb.Append(Loc.Get("ic_material_create"));
                    }
                    else
                    {
                        sb.Append(Loc.Get("ic_material_slot", idx + 1));
                    }
                }

                // Position.
                var totalList = _icMaterialSelectListBase.currentDataList;
                int total = totalList?.Count ?? 0;
                if (total > 0)
                    sb.Append(". ").Append(idx + 1).Append(" of ").Append(total).Append('.');

                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"CampIC_Material: selectMat [{idx}] {sb}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: selectMat poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the item list (ItemList state).
        /// Items are UIItemListItemData with itemName, itemCount, itemDescription.
        /// </summary>
        private void PollMaterialItemListCursor()
        {
            if (_icMaterialItemListBase == null)
            {
                try
                {
                    var itemListSel = _icAddMaterialSelector.itemListSelector;
                    _icMaterialItemListBase = itemListSel?.TryCast<UIListSelectorBase>();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC_Material: itemList cast error: {ex.Message}");
                    return;
                }
            }

            if (_icMaterialItemListBase == null) return;

            try
            {
                int idx = _icMaterialItemListBase.currentIndex;
                if (idx == _icMaterialItemListState.LastIndex) return;
                _icMaterialItemListState.LastIndex = idx;

                var list = _icMaterialItemListBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var item = list[idx]?.TryCast<UIItemListItemData>();
                if (item != null)
                {
                    var sb = new StringBuilder();
                    string name = SanitizeItemName(item.itemName);
                    sb.Append(name ?? Loc.Get("ic_unknown_item"));

                    int qty = item.itemCount;
                    if (qty > 1)
                        sb.Append(", x").Append(qty);

                    sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');

                    ScreenReader.Say(sb.ToString());
                    DebugLogger.LogState($"CampIC_Material: itemList [{idx}] {name}");
                    return;
                }

                // Fallback: bare position.
                ScreenReader.Say(Loc.Get("ic_action_position", idx + 1, count));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: itemList poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the current factor success rate from the factorInformationPresenter.
        /// </summary>
        private void AnnounceMaterialFactorRate()
        {
            try
            {
                var factorPresenter = _icAddMaterialSelector.factorInformationPresenter;
                if (factorPresenter == null) return;

                var factorRate = factorPresenter.factorRate;
                if (factorRate == null) return;

                int rate = factorRate.targetPercentage;
                ScreenReader.Say(Loc.Get("ic_material_rate", rate));
                DebugLogger.LogState($"CampIC_Material: factor rate = {rate}%");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: factor rate error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves an item name from its itemID via ParameterManager and TextManager.
        /// Falls back to parsing the raw key if TextManager can't resolve it.
        /// </summary>
        private static string ResolveItemName(int itemID)
        {
            try
            {
                var param = ParameterManager.Instance?.GetItemParameter(itemID);
                if (param == null) return Loc.Get("ic_unknown_item");

                string nameID = param.itemNameID;
                if (string.IsNullOrEmpty(nameID)) return Loc.Get("ic_unknown_item");

                // Try TextManager resolution.
                var tm = TextManager.Instance;
                if (tm != null)
                {
                    string resolved = tm.GetMessage(nameID, TextManager.MessageType.Item);
                    if (!string.IsNullOrEmpty(resolved))
                        return SanitizeItemName(resolved);
                }

                // Fallback: parse key (e.g. "ITEM_BLUEBERRY" → "Blueberry").
                string fallback = nameID;
                if (fallback.StartsWith("ITEM_"))
                    fallback = fallback.Substring(5);
                fallback = fallback.Replace('_', ' ');
                if (fallback.Length > 0)
                    fallback = char.ToUpper(fallback[0]) + fallback.Substring(1).ToLower();
                return fallback;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: resolveItemName({itemID}) error: {ex.Message}");
                return Loc.Get("ic_unknown_item");
            }
        }

        #endregion
    }
}
