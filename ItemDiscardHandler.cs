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
    /// Announces the "item storage full — choose items to dump" screen.
    ///
    /// The game opens it when an acquisition (item creation results, chests, rewards)
    /// does not fit into the inventory: after the overflow toast it lists the items
    /// the party holds, the player marks rows with the decision button, and the
    /// Start button asks "Dump selected item?". The screen is hosted on
    /// UISystemWindow (state SystemState.ItemDiscard, selector UIItemDiscardSelector),
    /// not on the camp window, so the camp handlers never see it (log 2026-09-26 09:19).
    ///
    /// Detection: UISystemWindow found via FindObjectOfType (lazy, throttled) and
    /// polled for IsOpened + current state == ItemDiscard, the same pattern as
    /// <see cref="EquipWizardHandler"/>. Cursor movement is native and fires no hooks,
    /// so currentIndex is polled each frame; marking a row does fire
    /// UIItemDiscardSelector.OnDecision / OnSquare, which are hooked so the new
    /// marked state and the item count are spoken.
    /// </summary>
    public class ItemDiscardHandler
    {
        #region Fields

        private bool _patchesApplied;

        private static UISystemWindow _window;
        private static int _findCooldown;

        private static bool _isOpen;
        private static UIItemDiscardSelector _selector;
        private static int _lastIndex = -1;

        /// <summary>Marked-for-dumping state of the row read last, to detect toggles.</summary>
        private static bool _lastRowMarked;

        /// <summary>Item count spoken last, to detect changes after a toggle.</summary>
        private static int _lastCurrentCount = -1;

        /// <summary>True while the opening announcement waits for the list to populate.</summary>
        private static bool _openingPending;

        /// <summary>Time.unscaledTime after which the opening announcement fires regardless.</summary>
        private static float _openingDeadline;

        /// <summary>Set by the OnDecision/OnSquare postfixes; consumed in Update().</summary>
        private static bool _toggleRequested;

        /// <summary>How long the opening announcement may wait for list data.</summary>
        private const float OpeningWaitSeconds = 1.0f;

        #endregion

        #region Patches

        /// <summary>
        /// Initializes the IL2CPP types and hooks the two buttons that change a row's
        /// marked state. Cursor movement stays polling-based.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UISystemWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemDiscardSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemDiscardListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemListItemData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UIItemDiscardSelector),
                        nameof(UIItemDiscardSelector.OnDecision)),
                    postfix: new HarmonyMethod(typeof(ItemDiscardHandler),
                        nameof(Selector_Toggle_Postfix))
                );
                harmony.Patch(
                    AccessTools.Method(typeof(UIItemDiscardSelector),
                        nameof(UIItemDiscardSelector.OnSquare)),
                    postfix: new HarmonyMethod(typeof(ItemDiscardHandler),
                        nameof(Selector_Toggle_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("[ITEMDISCARD] Patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ITEMDISCARD] Patch error: {ex.Message}");
            }
        }

        /// <summary>Called on scene change to reset cached references.</summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            ResetScreenState();
        }

        /// <summary>
        /// Postfix for UIItemDiscardSelector.OnDecision and OnSquare. The game has
        /// already toggled the row by now; the announcement is deferred one frame to
        /// Update() so the list data and the count fields are settled.
        /// </summary>
        private static void Selector_Toggle_Postfix()
        {
            _toggleRequested = true;
        }

        #endregion

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            DetectWindow();
            if (!_isOpen) return;

            try
            {
                if (_openingPending)
                {
                    TryAnnounceOpening();
                    return;
                }

                if (_toggleRequested)
                {
                    _toggleRequested = false;
                    AnnounceToggle();
                }

                PollCursor();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ItemDiscardHandler.Update: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds UISystemWindow lazily (throttled) and detects the discard screen
        /// opening and closing via the window's selector stack state.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _window = UnityEngine.Object.FindObjectOfType<UISystemWindow>();
                    if (_window != null)
                        DebugLogger.LogState("ItemDiscard: cached UISystemWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"ItemDiscard: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                bool open = _window.IsOpened
                    && _window.GetCurrentState() == (int)UIDefine.SystemState.ItemDiscard;

                if (open && !_isOpen)
                {
                    _isOpen = true;
                    _selector = _window.itemDiscardSelector;
                    _lastIndex = -1;
                    _lastCurrentCount = -1;
                    _toggleRequested = false;
                    _openingPending = true;
                    _openingDeadline = UnityEngine.Time.unscaledTime + OpeningWaitSeconds;

                    DebugLogger.LogState("ItemDiscard: opened "
                        + $"(GameManager.IsOpenedItemDiscardWindow={GameManager.IsOpenedItemDiscardWindow}, "
                        + $"selector={(_selector != null ? "ok" : "null")}).");
                }
                else if (!open && _isOpen)
                {
                    ResetScreenState();
                    DebugLogger.LogState("ItemDiscard: closed.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ItemDiscard: detection error: {ex.Message}");
                _window = null;
                _findCooldown = 0;
                ResetScreenState();
            }
        }

        /// <summary>Clears all per-screen state after the discard screen closes.</summary>
        private static void ResetScreenState()
        {
            _isOpen = false;
            _selector = null;
            _lastIndex = -1;
            _lastRowMarked = false;
            _lastCurrentCount = -1;
            _openingPending = false;
            _toggleRequested = false;
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Speaks the heading, the game's own instruction line, the item count and the
        /// focused row once the list has data (or the wait deadline passes). Queued
        /// behind the overflow toast that precedes this screen instead of cutting it off.
        /// </summary>
        private void TryAnnounceOpening()
        {
            if (_selector == null)
            {
                _openingPending = false;
                ScreenReader.Say(Loc.Get("discard_heading"), false);
                DebugLogger.LogState("ItemDiscard: no selector — heading only.");
                return;
            }

            int count = _selector.DataCount;
            if (count <= 0 && UnityEngine.Time.unscaledTime < _openingDeadline) return;
            _openingPending = false;

            var parts = new List<string> { Loc.Get("discard_heading") };

            string operation = TextUtil.StripTags(_selector.operation?.text ?? "");
            if (!string.IsNullOrEmpty(operation))
                parts.Add(operation);

            parts.Add(BuildCountText());

            int idx = _selector.currentIndex;
            string row = BuildRowText(idx, count);
            if (!string.IsNullOrEmpty(row))
                parts.Add(row);
            _lastIndex = idx;

            ScreenReader.Say(string.Join(". ", parts), false);
            DebugLogger.LogState($"ItemDiscard: announced opening, rows={count}, index={idx}.");
        }

        /// <summary>Polls the cursor and speaks the row it lands on.</summary>
        private void PollCursor()
        {
            if (_selector == null) return;

            int idx = _selector.currentIndex;
            if (idx == _lastIndex) return;
            _lastIndex = idx;

            int count = _selector.DataCount;
            string row = BuildRowText(idx, count);
            if (string.IsNullOrEmpty(row))
            {
                DebugLogger.LogState($"ItemDiscard: row {idx} of {count} has no data.");
                return;
            }

            ScreenReader.Say(row);
        }

        /// <summary>
        /// After OnDecision/OnSquare: speaks the row's new marked state and the item
        /// count when either changed. A press that changed nothing (row not selectable)
        /// stays silent and is logged.
        /// </summary>
        private void AnnounceToggle()
        {
            if (_selector == null) return;

            int idx = _selector.currentIndex;
            var item = GetRow(idx);
            if (item == null)
            {
                DebugLogger.LogState($"ItemDiscard: toggle on row {idx} — no data.");
                return;
            }

            bool marked = item.isDecisioned;
            int current = _selector.currentItemCount;
            bool markedChanged = marked != _lastRowMarked;
            bool countChanged = current != _lastCurrentCount;

            DebugLogger.LogGameValue("ItemDiscard.toggle",
                $"row={idx} marked={marked} (was {_lastRowMarked}) count={current}/{_selector.allItemCount} (was {_lastCurrentCount})");

            if (!markedChanged && !countChanged) return;

            _lastRowMarked = marked;
            _lastCurrentCount = current;

            var parts = new List<string>();
            if (markedChanged)
            {
                string name = TextUtil.StripTags(item.itemName ?? "");
                parts.Add(Loc.Get(marked ? "discard_mark_on" : "discard_mark_off", name));
            }
            parts.Add(BuildCountText());

            ScreenReader.Say(string.Join(" ", parts));
        }

        /// <summary>
        /// "Label: current of max." using the game's own label text when it has one.
        /// The count fields are the selector's public ints, not the on-screen text.
        /// </summary>
        private static string BuildCountText()
        {
            int current = _selector.currentItemCount;
            int max = _selector.allItemCount;
            _lastCurrentCount = current;

            string label = TextUtil.StripTags(
                _selector.allPossessionCountPresenter?.LabelText?.text ?? "");
            if (string.IsNullOrEmpty(label))
                label = Loc.Get("discard_count_label");

            return Loc.Get("discard_count", label, current, max);
        }

        /// <summary>
        /// Builds "Name, xN, new, marked to dump. i of count." for the given row.
        /// Returns null when the row has no item data.
        /// </summary>
        private static string BuildRowText(int idx, int count)
        {
            var item = GetRow(idx);
            if (item == null) return null;

            var parts = new List<string>();

            string name = TextUtil.StripTags(item.itemName ?? "");
            parts.Add(string.IsNullOrEmpty(name) ? Loc.Get("ic_unknown_item") : name);

            if (item.itemCount > 1)
                parts.Add(Loc.Get("discard_quantity", item.itemCount));
            if (item.isNew)
                parts.Add(Loc.Get("discard_new"));
            if (item.isDecisioned)
                parts.Add(Loc.Get("discard_marked"));

            _lastRowMarked = item.isDecisioned;

            var sb = new StringBuilder(string.Join(", ", parts));
            TextUtil.AppendPosition(sb, idx, count);

            DebugLogger.LogState($"ItemDiscard: row [{idx}] {name} x{item.itemCount} "
                + $"new={item.isNew} marked={item.isDecisioned} possession={item.isPossession}");

            return sb.ToString();
        }

        /// <summary>Returns the discard row data at the index, or null when out of range.</summary>
        private static UIItemDiscardListItemData GetRow(int idx)
        {
            var list = _selector?.currentDataList;
            if (list == null || idx < 0 || idx >= list.Count) return null;
            return list[idx]?.TryCast<UIItemDiscardListItemData>();
        }

        #endregion
    }
}
