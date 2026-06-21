using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler (Item Creation): action-list polling, focus resolution, character tab, Train/Scout pollers, create-mode.
    public partial class CampMenuHandler
    {
        #region Screen 2: Action List

        private void UpdateICActionList()
        {
            // Train and Scout have dedicated selectors with their own detection.
            // Gate on _icActiveSkillCategory (set by the creation hook) and handle
            // them before generic focus tracking so they never interfere.
            if (_icActiveSkillCategory == "Train" && PollTrainSwitchSelector()) return;
            if (_icActiveSkillCategory == "Scouting" && PollScoutActionSelector()) return;

            // Replication and Remaking show an item picker (itemListSelector) first, not
            // the generic action list — handle them before generic focus tracking. When
            // no picker is on screen this returns false and the count stage falls through
            // to the generic action poller below.
            if (PollItemListSkills()) return;

            // Every selector reports activeInHierarchy == true for the whole IC session,
            // so we can't use that flag to know which skill is on screen. Instead, find
            // the selector the user is actually navigating: the one whose action list
            // just became populated (entry) or whose cursor just moved (navigation).
            int focused = ResolveFocusedActionSelector();

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
                        TextUtil.AppendPosition(sb, idx, count);
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
        /// Polls the item-list-first special skills (Replication and Remaking). Each shows
        /// an inventory item list (itemListSelector) as its first screen rather than the
        /// generic action selector, so <see cref="ResolveFocusedActionSelector"/> never
        /// sees them. Only one is ever on screen, so this checks the shared count adjuster
        /// first (it overlays the still-populated item list), then each picker in turn.
        /// Returns true when one is active and handled (suppresses generic polling); false
        /// when none is on screen (skill selection or the count stage), letting the generic
        /// action poller take over.
        /// </summary>
        private bool PollItemListSkills()
        {
            // The count adjuster overlays on top of the still-populated item list, so check
            // it FIRST — otherwise a picker's "no move" path would swallow the frame and
            // the count would never read.
            if (TryPollItemSkillCreateMode(_icDuplicateSelector?.ActionSelector)) return true;
            if (TryPollItemSkillCreateMode(_icRemakeSelector?.ActionSelector)) return true;

            if (_icDuplicateSelector != null)
            {
                if (_icDuplicateItemListBase == null)
                    try { _icDuplicateItemListBase = _icDuplicateSelector.itemListSelector?.TryCast<UIListSelectorBase>(); }
                    catch { /* leave null */ }
                if (PollItemPicker(_icDuplicateItemListBase, ref _icDuplicateItemLastIndex, "ic_duplicate_screen", "duplicate"))
                    return true;
            }

            if (_icRemakeSelector != null)
            {
                if (_icRemakeItemListBase == null)
                    try { _icRemakeItemListBase = _icRemakeSelector.itemListSelector?.TryCast<UIListSelectorBase>(); }
                    catch { /* leave null */ }
                if (PollItemPicker(_icRemakeItemListBase, ref _icRemakeItemLastIndex, "ic_remake_screen", "remake"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Polls a single item-list-first picker (the inventory list the user scrolls to
        /// choose which item to replicate/remake). Detection mirrors the action-list focus
        /// trick: a populated list means the picker is on screen, and a previously-empty
        /// list (last index -1) means the user just entered it. Returns true when this
        /// picker is active and handled; false when its list is empty (off screen).
        /// </summary>
        private bool PollItemPicker(UIListSelectorBase itemList, ref int lastIndex, string headingKey, string logTag)
        {
            if (itemList == null) { lastIndex = -1; return false; }

            try
            {
                var list = itemList.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0)
                {
                    // Picker not on screen. Reset so the next populate counts as a fresh
                    // entry; the count adjuster was already handled by the create-mode poll.
                    lastIndex = -1;
                    return false;
                }

                int idx = itemList.currentIndex;
                bool entry = lastIndex == -1;
                // No cursor movement: do NOT claim the frame. The item list stays
                // populated even when this picker is off screen (a different skill is on
                // screen), so claiming it here would block the other item-list skill and
                // the generic action poller. Returning false lets them run; this picker
                // re-engages as soon as its own cursor actually moves.
                if (!entry && idx == lastIndex) return false;
                // Entering the picker clears any leftover count state from a cancelled
                // create, so re-entering the count stage announces its heading + rate.
                if (entry) ResetCreateModeState();
                lastIndex = idx;

                var sb = new StringBuilder();
                if (entry)
                    sb.Append(Loc.Get(headingKey)).Append(' ');

                if (idx >= 0 && idx < count)
                {
                    var item = list[idx]?.TryCast<UIItemListItemData>();
                    if (item != null)
                    {
                        string name = SanitizeItemName(item.itemName);
                        sb.Append(string.IsNullOrEmpty(name) ? Loc.Get("ic_unknown_item") : name);

                        int qty = item.itemCount;
                        if (qty > 1)
                            sb.Append(", x").Append(qty);

                        TextUtil.AppendPosition(sb, idx, count);
                        ScreenReader.Say(sb.ToString());
                        DebugLogger.LogState($"CampIC: {logTag} item [{idx}] {name}");
                        return true;
                    }
                }

                // Fallback: heading (if entering) plus bare position.
                sb.Append(Loc.Get("ic_action_position", idx + 1, count));
                ScreenReader.Say(sb.ToString());
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC: {logTag} item poll error: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Seeds the item-list-first pickers' last-index from their current state. The game
        /// keeps these item lists populated even across camp close/reopen, so a stale list
        /// would otherwise be mistaken for a fresh entry and re-announced whenever Item
        /// Creation is merely highlighted. Seeds to the current index when populated so no
        /// move is detected; leaves -1 when empty so a genuine first entry still reads.
        /// Called at camp open and on each Item Creation re-entry.
        /// </summary>
        private static void SeedItemPickersOnEntry()
        {
            SeedOnePicker(_icDuplicateSelector?.itemListSelector, ref _icDuplicateItemListBase, ref _icDuplicateItemLastIndex);
            SeedOnePicker(_icRemakeSelector?.itemListSelector, ref _icRemakeItemListBase, ref _icRemakeItemLastIndex);
        }

        private static void SeedOnePicker(
            Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase itemListSelector,
            ref UIListSelectorBase listBase, ref int lastIndex)
        {
            lastIndex = -1;
            if (itemListSelector == null) return;
            try
            {
                if (listBase == null)
                    listBase = itemListSelector.TryCast<UIListSelectorBase>();
                int count = listBase?.currentDataList?.Count ?? 0;
                if (count > 0)
                    lastIndex = listBase.currentIndex;
            }
            catch { lastIndex = -1; }
        }

        /// <summary>
        /// Drives the shared create-mode poller for an item-list-first skill's count
        /// adjuster, which appears after the user picks an item. These selectors expose
        /// their own typed action selector whose presenter's createCountParent is active
        /// only while the adjuster is on screen and currentCreateCount is positive then.
        /// Returns true when the adjuster is active (handled), false otherwise.
        /// </summary>
        private bool TryPollItemSkillCreateMode(UICampSpecialSkillActionSelectorBase actionBase)
        {
            if (actionBase == null) return false;

            UICampSpecialSkillActionPresenter presenter;
            try { presenter = actionBase.actionPresenter; }
            catch { return false; }
            if (presenter == null) return false;

            bool shown;
            int createCount;
            try
            {
                // createCountParent is toggled active only while the count adjuster is on
                // screen — a clean signal that survives the item list staying populated
                // underneath it.
                shown = presenter.createCountParent?.activeInHierarchy ?? false;
                createCount = presenter.currentCreateCount;
            }
            catch { return false; }

            // Diagnostic: confirm the adjuster is detected and what count it reports.
            if (shown != _icDupCountShownLast)
            {
                _icDupCountShownLast = shown;
                if (shown)
                    DebugLogger.LogState($"CampIC: count adjuster shown, count={createCount}");
            }

            if (!shown || createCount <= 0)
            {
                // Adjuster not on screen (or count not yet set). Clear leftover count state
                // so a later entry re-announces heading + rate, and let other flows
                // (item picker) handle this frame.
                if (_icLastCreateCount > 0 && _icActionPresenter == presenter)
                    ResetCreateModeState();
                return false;
            }

            // Point the shared create-mode state at this skill's action selector and reuse
            // PollCreateMode for entry/count-change/exit announcements.
            _icActionSelectorBase = actionBase;
            _icActionPresenter = presenter;
            PollCreateMode();
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

                        // Consumable requirement. Prefer the on-screen consume display
                        // (authoritative — e.g. Writing's Fountain Pen, which the list
                        // item's consumeItemID does NOT point to); fall back to the
                        // item's consumeItemID for skills with no separate display.
                        string need = ReadConsumeRequirementFromDisplay() ?? ReadConsumeRequirement(item);
                        if (!string.IsNullOrEmpty(need))
                            sb.Append(". ").Append(need);

                        if (!item.canDecision)
                            sb.Append(", unavailable");

                        TextUtil.AppendPosition(sb, idx, count);
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
    }
}
