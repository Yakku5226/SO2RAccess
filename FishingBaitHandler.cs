using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the fishing BAIT selection screen: the list the game opens at a
    /// fishing spot when no bait is set (or the bait ran out), with the water
    /// place name, the bait rows (name, count, target size) and, per bait, the
    /// fish it can catch here with the game's own chance wording.
    ///
    /// The screen lives on <c>UIFieldWindow</c> (state
    /// <c>UIDefine.FieldState.FishingBait</c>, selector
    /// <c>UIFieldFishingBaitSelector</c>), the same window that hosts pickpocket,
    /// quick recovery and the fishing result. Its rows
    /// (<c>UIFieldFishingBaitListItemPresenter</c>) were on the universal list
    /// net's "a dedicated handler owns it" list without any handler existing, so
    /// the menu was silent (log 2026-09-26 12:22). Cursor movement is native and
    /// fires no hooks, so the cursor index is polled each frame, the same pattern
    /// as <see cref="ItemDiscardHandler"/>. The target list on the right is
    /// display-only and is read from its visible rows one frame after a cursor
    /// move, when the game has rebuilt it.
    /// </summary>
    public class FishingBaitHandler
    {
        #region Fields

        private static UIFieldWindow _window;
        private static int _findCooldown;

        private static bool _isOpen;
        private static UIFieldFishingBaitSelector _selector;
        private static int _lastIndex = -1;

        /// <summary>Row count and first row id seen last — a tab switch changes the list without moving the cursor.</summary>
        private static int _lastCount = -1;
        private static int _lastFirstItemId = -1;

        /// <summary>True while the opening announcement waits for the list to populate.</summary>
        private static bool _openingPending;
        private static float _openingDeadline;

        /// <summary>Frames to wait before reading a row, so the game's target list has been rebuilt.</summary>
        private static int _rowReadCountdown = -1;
        private static bool _rowReadIsOpening;

        private const float OpeningWaitSeconds = 1.0f;
        private const int RowReadDelayFrames = 2;

        /// <summary>Most target fish named per bait row (the game lists a handful; guard against a long list).</summary>
        private const int MaxTargetsSpoken = 8;

        /// <summary>The game's placeholder name for a fish not caught yet ("????(Small)").</summary>
        private const string UnknownFishMarker = "????";

        private bool _patchesApplied;

        /// <summary>Set by the OnDecision postfix: the row name pressed, when, and the bait set before the press.</summary>
        private static string _decisionName;
        private static float _decisionTime = -1f;
        private static string _baitBeforeDecision;

        /// <summary>
        /// The bait the header showed on the previous frame. The OnDecision postfix
        /// runs AFTER the game applied the choice, so the header it sees is already
        /// the new bait (log 2026-09-26 13:01: "before" equalled the pressed row);
        /// the frame-old value is the real "before".
        /// </summary>
        private static string _knownBait = "";

        /// <summary>
        /// Seconds after a decision press before the outcome is judged. The press
        /// marks the bait while the list stays open (log 2026-09-26 12:46), so the
        /// outcome is read from the selector's current-bait header, not from the
        /// menu closing.
        /// </summary>
        private const float DecisionSettleSeconds = 0.25f;

        #endregion

        #region Patches

        /// <summary>
        /// Hooks the decision button of the bait list. The press closes the menu, so
        /// the accepted bait is spoken from the row remembered at press time.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldFishingBaitSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldFishingBaitListItemData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldFishingBaitSelector),
                        nameof(UIFieldFishingBaitSelector.OnDecision)),
                    postfix: new HarmonyMethod(typeof(FishingBaitHandler),
                        nameof(Selector_OnDecision_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("[FISHINGBAIT] Patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[FISHINGBAIT] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIFieldFishingBaitSelector.OnDecision: remembers the focused
        /// row; Update() speaks it once the menu has closed (an accepted choice).
        /// </summary>
        private static void Selector_OnDecision_Postfix(UIFieldFishingBaitSelector __instance)
        {
            try
            {
                if (!_isOpen || __instance == null) return;
                var list = __instance.currentDataList;
                int idx = __instance.currentIndex;
                var item = list != null && idx >= 0 && idx < list.Count
                    ? list[idx]?.TryCast<UIFieldFishingBaitListItemData>() : null;
                _decisionName = TextUtil.StripTags(item?.itemName ?? "");
                _baitBeforeDecision = _knownBait;
                _decisionTime = UnityEngine.Time.unscaledTime;
                DebugLogger.LogState(
                    $"FishingBait: decision on row {idx} '{_decisionName}' (bait before: '{_baitBeforeDecision}', " +
                    $"header now: '{CurrentBaitName()}', {DescribeCacheIds()}).");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FishingBait: decision hook error: {ex.Message}");
            }
        }

        #endregion

        /// <summary>Called on scene change to reset cached references.</summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            _decisionTime = -1f;
            ResetScreenState();
        }

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            DetectWindow();
            AnnounceDecisionOutcome();
            if (!_isOpen) return;

            try
            {
                if (_openingPending)
                {
                    TryStartOpening();
                    return;
                }

                // Frame-old header value = the "before" of a decision press.
                if (_decisionTime < 0f) _knownBait = CurrentBaitName();

                if (_rowReadCountdown > 0)
                {
                    _rowReadCountdown--;
                    return;
                }
                if (_rowReadCountdown == 0)
                {
                    _rowReadCountdown = -1;
                    ReadCurrentRow(_rowReadIsOpening);
                    return;
                }

                PollList();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FishingBaitHandler.Update: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds UIFieldWindow lazily (throttled) and detects the bait screen opening
        /// and closing via the window's open state.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _window = UnityEngine.Object.FindObjectOfType<UIFieldWindow>();
                    if (_window != null)
                        DebugLogger.LogState("FishingBait: cached UIFieldWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"FishingBait: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                bool open = _window.IsOpened
                    && _window.OpenFieldState == UIDefine.FieldState.FishingBait;

                if (open && !_isOpen)
                {
                    _isOpen = true;
                    _selector = _window.fishingBaitSelector;
                    _lastIndex = -1;
                    _lastCount = -1;
                    _lastFirstItemId = -1;
                    _openingPending = true;
                    _openingDeadline = UnityEngine.Time.unscaledTime + OpeningWaitSeconds;
                    DebugLogger.LogState(
                        $"FishingBait: opened (selector={(_selector != null ? "ok" : "null")}).");
                }
                else if (!open && _isOpen)
                {
                    ResetScreenState();
                    DebugLogger.LogState("FishingBait: closed.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FishingBait: detection error: {ex.Message}");
                _window = null;
                _findCooldown = 0;
                ResetScreenState();
            }
        }

        private static void ResetScreenState()
        {
            _isOpen = false;
            _selector = null;
            _knownBait = "";
            _lastIndex = -1;
            _lastCount = -1;
            _lastFirstItemId = -1;
            _openingPending = false;
            _rowReadCountdown = -1;
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Speaks "X selected" a moment after a decision press when the menu has
        /// closed (the game accepted the bait). A press the game refused leaves the
        /// menu open and is only logged.
        /// </summary>
        private static void AnnounceDecisionOutcome()
        {
            if (_decisionTime < 0f) return;
            if (UnityEngine.Time.unscaledTime - _decisionTime < DecisionSettleSeconds) return;
            _decisionTime = -1f;

            string pressed = string.IsNullOrEmpty(_decisionName) ? Loc.Get("ic_unknown_item") : _decisionName;
            if (!_isOpen)
            {
                // The game closed the menu on the press: the choice was taken.
                ScreenReader.Say(Loc.Get("bait_selected", pressed));
                DebugLogger.LogState($"FishingBait: '{pressed}' selected, menu closed.");
                return;
            }

            // Menu still open: the press marks (or unmarks) the bait in place. The
            // selector's own current-bait header is the truth.
            string now = CurrentBaitName();
            DebugLogger.LogState(
                $"FishingBait: after decision on '{pressed}': bait '{_baitBeforeDecision}' -> '{now}', {DescribeCacheIds()}.");
            _knownBait = now;
            if (now == _baitBeforeDecision)
            {
                DebugLogger.LogState("FishingBait: decision changed nothing (refused, or the same bait again).");
                return;
            }
            ScreenReader.Say(string.IsNullOrEmpty(now)
                ? Loc.Get("bait_cleared")
                : Loc.Get("bait_selected", now));
        }

        /// <summary>The bait the game shows as set right now (header text), "" for none.</summary>
        private static string CurrentBaitName()
        {
            string name = ReadText(_selector?.baitItemName?.itemName);
            return IsPlaceholder(name) || IsNoneWord(name) ? "" : name;
        }

        /// <summary>The game's "None" label — the first list row carries it, so it is compared, not hard-coded.</summary>
        private static bool IsNoneWord(string name)
        {
            var first = GetRow(0);
            string none = first != null && first.itemID <= 0 ? TextUtil.StripTags(first.itemName ?? "") : null;
            return !string.IsNullOrEmpty(none) && name == none;
        }

        /// <summary>The selector's cached bait ids, for the log.</summary>
        private static string DescribeCacheIds()
        {
            try
            {
                var d = _selector?.cacheData;
                return d == null ? "cacheData=null"
                    : $"cache bait={d.fishingBaitItemID} sprinkle={d.fishingSprinkleBaitItemID} isDecision={d.isDecision}";
            }
            catch (Exception ex) { return $"cacheData unreadable: {ex.Message}"; }
        }

        /// <summary>
        /// True when the row is the bait currently set: its id matches the
        /// selector's cached bait (or sprinkle bait) id, else its name matches the
        /// current-bait header.
        /// </summary>
        private static bool IsCurrentBait(UIFieldFishingBaitListItemData item)
        {
            if (item == null || item.itemID <= 0) return false;
            try
            {
                var d = _selector?.cacheData;
                if (d != null && (d.fishingBaitItemID == item.itemID || d.fishingSprinkleBaitItemID == item.itemID))
                    return true;
            }
            catch { }
            string current = CurrentBaitName();
            return !string.IsNullOrEmpty(current) && current == TextUtil.StripTags(item.itemName ?? "");
        }

        /// <summary>
        /// Waits for the list data (or the deadline), then schedules the opening
        /// read: heading, place, current bait and the focused row with its targets.
        /// </summary>
        private void TryStartOpening()
        {
            if (_selector == null)
            {
                _openingPending = false;
                ScreenReader.Say(Loc.Get("bait_heading"), false);
                DebugLogger.LogState("FishingBait: no selector — heading only.");
                return;
            }

            int count = _selector.DataCount;
            if (count <= 0 && UnityEngine.Time.unscaledTime < _openingDeadline) return;
            _openingPending = false;
            _rowReadIsOpening = true;
            _rowReadCountdown = RowReadDelayFrames;
        }

        /// <summary>Polls the cursor and the list identity; schedules a row read on change.</summary>
        private void PollList()
        {
            if (_selector == null) return;

            int idx = _selector.currentIndex;
            int count = _selector.DataCount;
            int firstId = FirstRowItemId();
            bool listChanged = count != _lastCount || firstId != _lastFirstItemId;
            if (idx == _lastIndex && !listChanged) return;

            if (listChanged && _lastCount >= 0)
                DebugLogger.LogState($"FishingBait: list changed (rows {_lastCount} -> {count}, tab switch?).");

            _lastIndex = idx;
            _lastCount = count;
            _lastFirstItemId = firstId;
            _rowReadIsOpening = false;
            _rowReadCountdown = RowReadDelayFrames;
        }

        /// <summary>
        /// Speaks the focused row (and, on opening, the heading, place and current
        /// bait first). Runs a couple of frames after the cursor moved so the
        /// game's target list on the right already shows this bait's fish.
        /// </summary>
        private void ReadCurrentRow(bool opening)
        {
            if (_selector == null) return;

            int idx = _selector.currentIndex;
            int count = _selector.DataCount;
            _lastIndex = idx;
            _lastCount = count;
            _lastFirstItemId = FirstRowItemId();

            var parts = new List<string>();
            if (opening)
            {
                parts.Add(Loc.Get("bait_heading"));

                string place = ReadText(_selector.fishingBaitPresenter?.place);
                if (!string.IsNullOrEmpty(place)) parts.Add(place);

                string current = CurrentBaitName();
                if (!string.IsNullOrEmpty(current))
                {
                    string currentCount = ReadText(_selector.baitItemCount);
                    parts.Add(Loc.Get("bait_current",
                        string.IsNullOrEmpty(currentCount) ? current : $"{current} {currentCount}"));
                }
                else
                {
                    parts.Add(Loc.Get("bait_current_none"));
                }
                DebugLogger.LogState($"FishingBait: opening header bait='{current}', {DescribeCacheIds()}");
            }

            string row = BuildRowText(idx, count);
            if (row != null) parts.Add(row);
            else if (count <= 0) parts.Add(Loc.Get("bait_none"));

            string targets = BuildTargetsText();
            if (targets != null) parts.Add(targets);

            if (parts.Count == 0) return;
            // Queued on opening: the game's own fishing sounds and the bubble speech
            // precede this screen; a cursor move interrupts as usual.
            ScreenReader.Say(string.Join(". ", parts), !opening);
            DebugLogger.LogState(
                $"FishingBait: {(opening ? "opening" : "row")} idx={idx}/{count}: {string.Join(" | ", parts)}");
        }

        /// <summary>"Name, xN, size" plus "i of count" for the row, or null without data.</summary>
        private static string BuildRowText(int idx, int count)
        {
            var item = GetRow(idx);
            if (item == null) return null;

            var parts = new List<string>();
            string name = TextUtil.StripTags(item.itemName ?? "");
            parts.Add(string.IsNullOrEmpty(name) ? Loc.Get("ic_unknown_item") : name);
            if (item.itemCount > 1) parts.Add(Loc.Get("discard_quantity", item.itemCount));
            // The "None" row carries "-" as its size — not worth a word.
            string size = TextUtil.StripTags(item.sizeName ?? "");
            if (!string.IsNullOrEmpty(size) && !IsPlaceholder(size)) parts.Add(size);
            if (IsCurrentBait(item)) parts.Add(Loc.Get("bait_current_mark"));

            var sb = new StringBuilder(string.Join(", ", parts));
            TextUtil.AppendPosition(sb, idx, count);
            return sb.ToString();
        }

        /// <summary>
        /// "Catches: Name chance, Name chance (rare)" from the visible rows of the
        /// game's target list for the focused bait, or null when it shows none.
        /// The chance wording is the game's own text (Easy / Normal / Difficult…).
        /// </summary>
        private static string BuildTargetsText()
        {
            var listPresenter = _selector?.fishingTargetListPresenter;
            if (listPresenter == null) return null;

            UIFieldFishingTargetListItemPresenter[] rows;
            try
            {
                rows = listPresenter.GetComponentsInChildren<UIFieldFishingTargetListItemPresenter>(false);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"FishingBait: target rows unreadable: {ex.Message}");
                return null;
            }
            if (rows == null || rows.Length == 0) return null;

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Length && names.Count < MaxTargetsSpoken; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                try
                {
                    if (r.gameObject == null || !r.gameObject.activeInHierarchy) continue;
                    string name = SpeakableFishName(ReadText(r.itemName?.itemName));
                    if (string.IsNullOrEmpty(name)) continue;
                    string rate = ReadText(r.fishingRate);
                    bool rare = r.rareLabelObj != null && r.rareLabelObj.activeSelf;
                    string entry = string.IsNullOrEmpty(rate) ? name : $"{name} {rate}";
                    if (rare) entry = Loc.Get("bait_target_rare", entry);
                    if (seen.Add(entry)) names.Add(entry);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"FishingBait: target row {i} unreadable: {ex.Message}");
                }
            }
            return names.Count == 0 ? null : Loc.Get("bait_targets", string.Join(", ", names));
        }

        /// <summary>
        /// The game names a fish not caught yet "????(Small)". A screen reader
        /// would spell the question marks, so they become the "unknown fish" words
        /// and the size keeps its own space: "Unknown fish (Small)".
        /// </summary>
        private static string SpeakableFishName(string name)
        {
            if (string.IsNullOrEmpty(name) || IsPlaceholder(name)) return null;
            if (!name.Contains(UnknownFishMarker)) return name;
            string rest = name.Replace(UnknownFishMarker, "").Trim();
            return string.IsNullOrEmpty(rest)
                ? Loc.Get("bait_unknown_fish")
                : $"{Loc.Get("bait_unknown_fish")} {rest}";
        }

        private static bool IsPlaceholder(string text) =>
            string.IsNullOrWhiteSpace(text) || text == "-" || text == "—";

        private static string ReadText(GameText text)
        {
            if (text == null) return "";
            try { return TextUtil.StripTags(text.text ?? ""); }
            catch { return ""; }
        }

        private static int FirstRowItemId()
        {
            var first = GetRow(0);
            return first != null ? first.itemID : -1;
        }

        private static UIFieldFishingBaitListItemData GetRow(int idx)
        {
            var list = _selector?.currentDataList;
            if (list == null || idx < 0 || idx >= list.Count) return null;
            return list[idx]?.TryCast<UIFieldFishingBaitListItemData>();
        }

        #endregion
    }
}
