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

        /// <summary>
        /// Last logged active-selector signature for the action screen.
        /// Used to change-gate the per-frame diagnostic so it only logs on change.
        /// </summary>
        private static string _icActiveSetSig;

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

        /// <summary>Last logged result-selector diagnostic signature (change-gated).</summary>
        private static string _icResultDiagSig;

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
            _icResultDiagSig = null;
            _icResultSeenSig = null;

            _icActionState.Reset();
            _icActiveSelector = null;
            _icActionListBase = null;
            _icLastCharTab = -1;
            _icActiveSetSig = null;
            _icActiveSkillCategory = null;
            _icTrainSwitchSelector = null;
            _icTrainSwitchLastIndex = -1;
            _icScoutSelector = window.scoutSelector;
            _icScoutActionListBase = null;
            _icScoutLastIndex = -1;
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
            // Train and Scout have dedicated selectors with their own detection.
            // Gate on _icActiveSkillCategory (set by the creation hook) and handle
            // them before generic focus tracking so they never interfere.
            if (_icActiveSkillCategory == "Train" && PollTrainSwitchSelector()) return;
            if (_icActiveSkillCategory == "Scouting" && PollScoutActionSelector()) return;

            // Every selector reports activeInHierarchy == true for the whole IC session,
            // so we can't use that flag to know which skill is on screen. Instead, find
            // the selector the user is actually navigating: the one whose action list
            // just became populated (entry) or whose cursor just moved (navigation).
            int focused = ResolveFocusedActionSelector();

            LogActiveActionSelectorsDiag(focused >= 0 ? _icAllSelectors[focused] : null);

            if (focused >= 0 && focused != _icFocusedIdx)
            {
                // Focus switched to a different skill's action list. Re-point all the
                // cached state at it and force the current item to re-announce.
                _icFocusedIdx = focused;
                _icActiveSelector = _icAllSelectors[focused];
                try { _icActionListBase = _icActiveSelector.actionSelector?.TryCast<UIListSelectorBase>(); }
                catch { _icActionListBase = null; }
                _icActionState.LastIndex = -1;
                // Seed the character tab to the new selector's current value (not -1)
                // so merely entering a skill does NOT blurt the character name —
                // TrackCharacterTab then only speaks on a real L/R tab change.
                try { _icLastCharTab = _icActiveSelector.currentTabIndex; }
                catch { _icLastCharTab = -1; }
                ResetCreateModeState();
                _icActionPresenter = null;
                DebugLogger.LogState($"CampIC_Action: focus -> #{focused}.");
            }

            if (_icFocusedIdx < 0 || _icActionListBase == null) return;

            _icActiveSelector = _icAllSelectors[_icFocusedIdx];

            // Track character tab changes.
            TrackCharacterTab();

            // Track Create mode (material confirmed, count adjustable).
            PollCreateMode();

            // If the creation hook fired, it handles the announcement (rich data).
            // Otherwise poll the action list directly.
            if (_icCreationHookFired)
            {
                _icCreationHookFired = false;
                try { _icActionState.LastIndex = _icActionListBase.currentIndex; }
                catch { /* ignore */ }
                return;
            }

            PollActionListFallback();
        }

        /// <summary>
        /// Finds the skill selector the user is currently navigating, working around
        /// the fact that every selector stays activeInHierarchy == true for the whole
        /// IC session. A selector qualifies when its action list just became populated
        /// (the user entered it) or its cursor index changed since last frame (the user
        /// moved within it). Lists the user is not on never move, and the menu pre-load
        /// does not move any cursor, so this is a clean focus signal.
        ///
        /// Updates <see cref="_icSelLastIndex"/> for every selector as a side effect.
        /// Returns the index into _icAllSelectors of the focused selector, or -1 if
        /// nothing changed this frame. A "moved" detection wins over a fresh "entry"
        /// detection if both happen in the same frame, to avoid focus flicker.
        /// </summary>
        private static int ResolveFocusedActionSelector()
        {
            if (_icSelLastIndex == null || _icSelLastIndex.Length != _icAllSelectors.Count)
                return -1;

            int entryIdx = -1;
            int moveIdx = -1;

            for (int i = 0; i < _icAllSelectors.Count; i++)
            {
                try
                {
                    var sel = _icAllSelectors[i];
                    var listBase = (sel?.gameObject?.activeInHierarchy == true)
                        ? sel.actionSelector?.TryCast<UIListSelectorBase>()
                        : null;
                    int count = listBase?.currentDataList?.Count ?? 0;

                    if (count <= 0)
                    {
                        _icSelLastIndex[i] = -1; // not populated
                        continue;
                    }

                    int idx = listBase.currentIndex;
                    int prev = _icSelLastIndex[i];
                    _icSelLastIndex[i] = idx;

                    if (prev == -1)
                        entryIdx = i;        // newly populated = entry
                    else if (idx != prev)
                        moveIdx = i;         // cursor moved = navigation
                }
                catch { /* skip broken refs */ }
            }

            return moveIdx >= 0 ? moveIdx : entryIdx;
        }

        /// <summary>
        /// Debug-only diagnostic for the action screen. The log confirmed that EVERY
        /// skill selector reports activeInHierarchy == true for the whole IC session
        /// (stale-active), so that flag can't tell us which skill is on screen. This
        /// version instead lists only selectors whose action list has options
        /// (count &gt; 0) and, for each, the UISelectorBase.isPause / isDisableInput
        /// flags — the candidate signal for "which list is actually focused".
        ///
        /// Format per entry: <c>#index:count:pause:input</c>
        ///   - index   = position in _icAllSelectors (0 craft, 1 alchemy, 2 cooking,
        ///               3 art, 4 machinery, 5 writing, 6 duplicate, 7 appraisal, ...).
        ///   - count   = action-list item count.
        ///   - pause   = "PAUSE" if isPause else "-".
        ///   - input   = "NOINPUT" if isDisableInput else "-".
        /// If the focused skill is the one with pause="-" (input enabled), that flag
        /// is the fix's selection signal; if all read the same, we fall back to
        /// per-selector index-change detection instead.
        ///
        /// Logs only when this signature changes. Build cost is gated behind
        /// Main.DebugMode, so zero overhead when off.
        /// </summary>
        private static void LogActiveActionSelectorsDiag(UICampSpecialSkillSelectorBase picked)
        {
            if (!Main.DebugMode) return;

            var sb = new StringBuilder();
            for (int i = 0; i < _icAllSelectors.Count; i++)
            {
                var sel = _icAllSelectors[i];
                try
                {
                    if (sel?.gameObject?.activeInHierarchy != true) continue;

                    var actionSel = sel.actionSelector;
                    int cnt = actionSel?.TryCast<UIListSelectorBase>()?.currentDataList?.Count ?? -1;
                    if (cnt <= 0) continue; // only selectors that actually hold options

                    string pause = "-", input = "-";
                    try
                    {
                        var selBase = actionSel?.TryCast<UISelectorBase>();
                        if (selBase != null)
                        {
                            if (selBase.isPause) pause = "PAUSE";
                            if (selBase.isDisableInput) input = "NOINPUT";
                        }
                    }
                    catch { /* leave defaults */ }

                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append('#').Append(i).Append(':').Append(cnt)
                      .Append(':').Append(pause).Append(':').Append(input);
                }
                catch { /* skip broken refs */ }
            }

            string sig = $"populated={{{sb}}} picked={picked?.GetType().Name ?? "none"}";
            if (sig == _icActiveSetSig) return;
            _icActiveSetSig = sig;
            DebugLogger.LogState($"CampIC_Action DIAG: {sig}");
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
        /// Polls the Train switch selector (ON/OFF toggle per party member).
        /// Scans _icAllSelectors directly for UICampSpecialSkillTrainingSelector
        /// because the stale-active selector may not be the Training one.
        /// Returns true if Train is active and handled, false otherwise.
        /// </summary>
        private bool PollTrainSwitchSelector()
        {
            // Try to detect the Training selector by scanning all selectors.
            if (_icTrainSwitchSelector == null)
            {
                foreach (var sel in _icAllSelectors)
                {
                    try
                    {
                        if (sel?.gameObject?.activeInHierarchy != true) continue;
                        var trainingSel = sel.TryCast<UICampSpecialSkillTrainingSelector>();
                        if (trainingSel?.switchSelector != null)
                        {
                            _icTrainSwitchSelector = trainingSel.switchSelector;
                            _icTrainSwitchLastIndex = -1;
                            DebugLogger.LogState("CampIC: Train switch selector found.");
                            break;
                        }
                    }
                    catch { /* skip */ }
                }
            }

            if (_icTrainSwitchSelector == null) return false;

            // Verify still active.
            try
            {
                if (_icTrainSwitchSelector.gameObject?.activeInHierarchy != true)
                {
                    _icTrainSwitchSelector = null;
                    _icTrainSwitchLastIndex = -1;
                    return false;
                }
            }
            catch
            {
                _icTrainSwitchSelector = null;
                _icTrainSwitchLastIndex = -1;
                return false;
            }

            try
            {
                int idx = _icTrainSwitchSelector.currentIndex;
                int charCount = _icTrainSwitchSelector.currentDataCount;
                if (charCount <= 0) return true; // Train active but no data yet.

                if (idx == _icTrainSwitchLastIndex) return true;
                _icTrainSwitchLastIndex = idx;

                // Total items = characters + "All On" + "All Off".
                int totalCount = charCount + 2;

                if (idx < charCount)
                {
                    // Character toggle item — get name and ON/OFF state.
                    UICampSpecialSkillTrainingSelector trainingSel = null;
                    foreach (var sel in _icAllSelectors)
                    {
                        try
                        {
                            trainingSel = sel?.TryCast<UICampSpecialSkillTrainingSelector>();
                            if (trainingSel != null) break;
                        }
                        catch { /* skip */ }
                    }

                    if (trainingSel != null)
                    {
                        var dataList = trainingSel.CreateDataList();
                        if (dataList != null && idx >= 0 && idx < dataList.Count)
                        {
                            var item = dataList[idx];
                            string name = item?.itemName ?? "";
                            bool isOn = item?.isOn ?? false;
                            string state = isOn ? Loc.Get("ic_train_on") : Loc.Get("ic_train_off");
                            ScreenReader.Say(Loc.Get("ic_train_item", name, state, idx + 1, totalCount));
                            DebugLogger.LogState($"CampIC: Train switch [{idx}] {name} = {(isOn ? "ON" : "OFF")}");
                            return true;
                        }
                    }
                }
                else if (idx == charCount)
                {
                    ScreenReader.Say(Loc.Get("ic_train_all_on", idx + 1, totalCount));
                    DebugLogger.LogState($"CampIC: Train switch [{idx}] All On");
                    return true;
                }
                else if (idx == charCount + 1)
                {
                    ScreenReader.Say(Loc.Get("ic_train_all_off", idx + 1, totalCount));
                    DebugLogger.LogState($"CampIC: Train switch [{idx}] All Off");
                    return true;
                }

                ScreenReader.Say(Loc.Get("ic_action_position", idx + 1, totalCount));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: Train switch error: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Polls the Scout action selector (Search / Escape / Do Nothing).
        /// Uses the cached _icScoutSelector from camp open.
        /// Returns true if Scout is active and handled, false otherwise.
        /// </summary>
        private bool PollScoutActionSelector()
        {
            if (_icScoutSelector == null) return false;

            // Try to get the action list from the cached scout selector.
            if (_icScoutActionListBase == null)
            {
                try
                {
                    var actionSel = _icScoutSelector.ActionSelector;
                    var listBase = actionSel?.TryCast<UIListSelectorBase>();
                    if (listBase != null)
                    {
                        _icScoutActionListBase = listBase;
                        _icScoutLastIndex = -1;
                    }
                }
                catch { /* skip */ }
            }

            if (_icScoutActionListBase == null) return false;

            // Only poll when the scout action list has data (i.e. the sub-menu is open).
            try
            {
                var list = _icScoutActionListBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0) return false;

                int idx = _icScoutActionListBase.currentIndex;
                if (idx == _icScoutLastIndex) return true;
                _icScoutLastIndex = idx;

                if (idx < 0 || idx >= count) return true;

                var item = list[idx]?.TryCast<UISpecialSkillConsumeListItemData>();
                if (item != null)
                {
                    string name = item.actionName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        var sb = new StringBuilder();
                        sb.Append(name);
                        if (!item.canDecision)
                            sb.Append(", ").Append(Loc.Get("ic_unavailable"));
                        sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');
                        ScreenReader.Say(sb.ToString());
                        DebugLogger.LogState($"CampIC: Scout action [{idx}] {name}");
                        return true;
                    }
                }

                ScreenReader.Say(Loc.Get("ic_action_position", idx + 1, count));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: Scout action error: {ex.Message}");
            }

            return true;
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

            LogResultDiag();

            // Detect a freshly appeared result and schedule its announcement. The
            // create-count flow (PollCreateMode) handles regular item creation, but
            // appraisal bypasses it — so trigger off the result list gaining content.
            // Both paths funnel through the single _icResultReadyTime, so a regular
            // creation that fires both still resets LastIndex once -> one announcement.
            DetectNewResult();

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
                // Skip resultText when it just repeats the success/failure status
                // (appraisal stores "Success"/"Failure" in both fields).
                if (!string.IsNullOrEmpty(resultText) &&
                    !string.Equals(resultText.Trim(), status.Trim(), StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Detects when the result list gains a new result (e.g. an appraisal outcome)
        /// by watching its first item's content signature, and schedules the
        /// announcement via the shared <see cref="_icResultReadyTime"/>. Regular item
        /// creation is handled by the create-count flow (PollCreateMode); appraisal
        /// bypasses that, so this is what makes its result speak. Both paths write the
        /// single _icResultReadyTime, so a creation that triggers both still resets the
        /// cursor once -> one announcement.
        ///
        /// Known limitation: two appraisals in a row that yield an identical result
        /// string won't re-announce the second (no content change to detect). Distinct
        /// results — the normal case — always read.
        /// </summary>
        private void DetectNewResult()
        {
            string sig;
            try
            {
                var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
                var list = listBase?.currentDataList;
                sig = "";
                if (list != null && list.Count > 0)
                {
                    var it = list[0]?.TryCast<UICampSpecialSkillResultListItemData>();
                    if (it != null)
                        sig = $"{list.Count}:{it.itemName}:{it.isSuccess}";
                }
            }
            catch { return; }

            if (sig == _icResultSeenSig) return;
            _icResultSeenSig = sig;

            if (!string.IsNullOrEmpty(sig))
            {
                // Small delay lets the result view settle; the ready-time handler then
                // resets LastIndex so the poll reads the result item.
                _icResultReadyTime = UnityEngine.Time.time + 0.5f;
                DebugLogger.LogState($"CampIC: new result detected ({sig}), announce scheduled.");
            }
        }

        /// <summary>
        /// Debug-only diagnostic for the result selector. Appraisal does not go through
        /// the create-count flow that schedules the normal result announcement, so its
        /// result is currently never read. This logs the result selector's observable
        /// state — activeInHierarchy, list count, resultDataList count, specialSkillID,
        /// and the first result item's name/result — whenever it changes, so we can see
        /// exactly how an appraisal result surfaces and pick the right trigger.
        /// Change-gated; build cost gated behind Main.DebugMode (zero overhead when off).
        /// </summary>
        private void LogResultDiag()
        {
            if (!Main.DebugMode) return;

            bool act = false;
            try { act = _icResultSelector.gameObject.activeInHierarchy; } catch { }

            var listBase = _icResultSelector.TryCast<UIListSelectorBase>();
            int cur = listBase?.currentDataList?.Count ?? -1;

            int rdl = -1;
            try { rdl = _icResultSelector.resultDataList?.Count ?? -1; } catch { }

            string sid = "?";
            try { sid = _icResultSelector.specialSkillID.ToString(); } catch { }

            string first = "none";
            try
            {
                var list = listBase?.currentDataList;
                if (list != null && list.Count > 0)
                {
                    var it = list[0]?.TryCast<UICampSpecialSkillResultListItemData>();
                    if (it != null)
                        first = $"{it.itemName}/{it.result}/{(it.isSuccess ? "ok" : "fail")}";
                }
            }
            catch { }

            string sig = $"act={act} cur={cur} rdl={rdl} sid={sid} first={first} ready={_icResultReadyTime > 0f} last={_icResultState.LastIndex}";
            if (sig == _icResultDiagSig) return;
            _icResultDiagSig = sig;
            DebugLogger.LogState($"CampIC_Result DIAG: {sig}");
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
