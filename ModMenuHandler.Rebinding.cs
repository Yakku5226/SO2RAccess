using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Key-bindings submenu of the mod settings menu. Lists every mod action
    /// with its current key; Enter on an action captures the next key pressed
    /// as its new binding. All edits are pending until "Save and go back" is
    /// activated; Escape discards them. Clash warnings (against live game keys
    /// and other mod bindings in an overlapping context) are passive — the key
    /// is accepted, the user just hears the warning.
    /// </summary>
    public partial class ModMenuHandler
    {
        #region Rebind Fields

        /// <summary>Pending (unsaved) bindings, edited by the submenu.</summary>
        private Dictionary<ModAction, Key> _pendingKeys;

        /// <summary>Rows of the rebind screen: one per action, then commands.</summary>
        private List<RebindRow> _rebindRows;

        private int _rebindIndex;

        /// <summary>The action whose key is being captured, when in Capture.</summary>
        private ModAction _captureAction;

        #endregion

        #region Screen Entry / Exit

        /// <summary>
        /// Opens the key-bindings submenu: copies the live bindings into the
        /// pending set and announces the first row.
        /// </summary>
        private void OpenRebindScreen()
        {
            _pendingKeys = new Dictionary<ModAction, Key>();
            foreach (var pair in ModKeys.AllKeyboard)
                _pendingKeys[pair.Key] = pair.Value;

            BuildRebindRows();
            _rebindIndex = 0;
            _screen = MenuScreen.Rebind;
            _dpadRepeater.Reset();

            ScreenReader.Say($"{Loc.Get("keybind_menu_open")} {FormatRebindRow(_rebindIndex)}");
            DebugLogger.LogState("ModMenu: rebind screen opened.");
        }

        /// <summary>
        /// Applies the pending bindings to <see cref="ModKeys"/>, saves them to
        /// the settings file, and returns to the root settings list.
        /// </summary>
        private void SaveRebindChanges()
        {
            ModKeys.Apply(_pendingKeys);
            ModSettings.Save();
            LeaveRebindScreen(Loc.Get("keybind_saved"));
            DebugLogger.LogState("ModMenu: key bindings saved.");
        }

        /// <summary>Discards all pending binding changes (Escape).</summary>
        private void DiscardRebindChanges()
        {
            LeaveRebindScreen(Loc.Get("keybind_discarded"));
            DebugLogger.LogState("ModMenu: key binding changes discarded.");
        }

        /// <summary>Returns to the root list and announces how the submenu ended.</summary>
        private void LeaveRebindScreen(string message)
        {
            _pendingKeys = null;
            _rebindRows = null;
            _screen = MenuScreen.Root;
            _dpadRepeater.Reset();
            ScreenReader.Say($"{message} {FormatItem(_currentIndex)}");
        }

        #endregion

        #region Rebind List Input

        /// <summary>Keyboard handling for the rebind list screen.</summary>
        private void ProcessRebindKeyboard(Keyboard kb)
        {
            if (kb[Key.Escape].wasPressedThisFrame)
            {
                DiscardRebindChanges();
                return;
            }
            if (kb[Key.UpArrow].wasPressedThisFrame)
            {
                NavigateRebind(-1);
                return;
            }
            if (kb[Key.DownArrow].wasPressedThisFrame)
            {
                NavigateRebind(1);
                return;
            }
            if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
            {
                _rebindRows[_rebindIndex].Activate();
            }
        }

        /// <summary>Gamepad handling for the rebind list screen.</summary>
        private void ProcessRebindGamepad(Gamepad gp)
        {
            // Circle / B = back, discarding changes (mirrors Escape).
            if (gp.buttonEast.wasPressedThisFrame)
            {
                DiscardRebindChanges();
                return;
            }
            // Cross / A = activate row.
            if (gp.buttonSouth.wasPressedThisFrame)
            {
                _rebindRows[_rebindIndex].Activate();
                return;
            }

            bool dUp   = gp.dpad.up.isPressed;
            bool dDown = gp.dpad.down.isPressed;
            int currentDir = dUp ? 1 : dDown ? 2 : 0;
            _dpadRepeater.Update(currentDir, UnityEngine.Time.deltaTime, FireRebindDpadAction);
        }

        private void FireRebindDpadAction(int dir)
        {
            switch (dir)
            {
                case 1: NavigateRebind(-1); break;
                case 2: NavigateRebind(1); break;
            }
        }

        private void NavigateRebind(int delta)
        {
            _rebindIndex += delta;
            if (_rebindIndex < 0) _rebindIndex = _rebindRows.Count - 1;
            else if (_rebindIndex >= _rebindRows.Count) _rebindIndex = 0;

            ScreenReader.Say(FormatRebindRow(_rebindIndex));
        }

        #endregion

        #region Key Capture

        /// <summary>
        /// Enters capture mode for one action: the next key pressed becomes its
        /// pending binding.
        /// </summary>
        private void StartCapture(ModAction action)
        {
            _captureAction = action;
            _screen = MenuScreen.Capture;
            ScreenReader.Say(Loc.Get("keybind_capture_prompt", ActionLabel(action)));
            DebugLogger.LogState($"ModMenu: capturing new key for {action}.");
        }

        /// <summary>
        /// Capture-mode keyboard handling: Escape cancels, any other key is
        /// taken as the new binding. Runs a frame after the Enter that started
        /// the capture, so that Enter itself is never captured.
        /// </summary>
        private void ProcessCaptureKeyboard(Keyboard kb)
        {
            if (kb[Key.Escape].wasPressedThisFrame)
            {
                _screen = MenuScreen.Rebind;
                ScreenReader.Say(Loc.Get("keybind_capture_cancelled",
                    ActionLabel(_captureAction),
                    ModKeys.DisplayName(_pendingKeys[_captureAction])));
                return;
            }

            var allKeys = kb.allKeys;
            for (int i = 0; i < allKeys.Count; i++)
            {
                var control = allKeys[i];
                if (control == null || !control.wasPressedThisFrame) continue;
                Key key = control.keyCode;
                if (key == Key.None || key == Key.Escape) continue;

                AssignCapturedKey(key);
                return;
            }
        }

        /// <summary>
        /// Capture-mode gamepad handling. Rebinding is keyboard-only, so pad
        /// buttons are ignored except Circle / B, which cancels like Escape.
        /// </summary>
        private void ProcessCaptureGamepad(Gamepad gp)
        {
            if (gp.buttonEast.wasPressedThisFrame)
            {
                _screen = MenuScreen.Rebind;
                ScreenReader.Say(Loc.Get("keybind_capture_cancelled",
                    ActionLabel(_captureAction),
                    ModKeys.DisplayName(_pendingKeys[_captureAction])));
            }
        }

        /// <summary>
        /// Stores the captured key in the pending set and announces it together
        /// with any passive clash warnings.
        /// </summary>
        private void AssignCapturedKey(Key key)
        {
            _pendingKeys[_captureAction] = key;
            _screen = MenuScreen.Rebind;

            var parts = new List<string>
            {
                Loc.Get("keybind_set", ActionLabel(_captureAction), ModKeys.DisplayName(key))
            };
            parts.AddRange(BuildClashWarnings(_captureAction, key));

            ScreenReader.Say(string.Join(" ", parts));
            DebugLogger.LogState($"ModMenu: pending {_captureAction} = {key}.");
        }

        /// <summary>
        /// Passive clash warnings for a newly captured key: against the game's
        /// live keyboard bindings, and against other pending mod bindings whose
        /// context can be active at the same time (same context, or either is
        /// Global). Nav and battle-pause sharing keys is deliberate, not a clash.
        /// </summary>
        private List<string> BuildClashWarnings(ModAction action, Key key)
        {
            var warnings = new List<string>();

            var gameActions = InputBindingDump.GameActionsForKey(key);
            if (gameActions.Count > 0)
            {
                warnings.Add(Loc.Get("keybind_clash_game",
                    ModKeys.DisplayName(key),
                    string.Join(", ", gameActions)));
            }

            var context = ModKeys.ContextOf(action);
            foreach (var pair in _pendingKeys)
            {
                if (pair.Key == action || pair.Value != key) continue;
                var otherContext = ModKeys.ContextOf(pair.Key);
                if (otherContext == ModKeyContext.DebugOnly)
                {
                    // F5 to F11 stay owned by the debug hotkeys while debug
                    // mode is on; the new binding still works in normal play.
                    warnings.Add(Loc.Get("keybind_clash_debug",
                        ModKeys.DisplayName(key)));
                    continue;
                }
                bool overlaps = otherContext == context ||
                                otherContext == ModKeyContext.Global ||
                                context == ModKeyContext.Global;
                if (overlaps)
                {
                    warnings.Add(Loc.Get("keybind_clash_mod",
                        ModKeys.DisplayName(key),
                        ActionLabel(pair.Key)));
                }
            }

            return warnings;
        }

        #endregion

        #region Rows

        /// <summary>
        /// Builds the rebind screen rows: every mod action in enum order,
        /// then the reset and save commands. Debug-only investigation hotkeys
        /// (F5 to F11) are not rebindable and are left off the list; capturing
        /// one of their keys for another action gives a passive warning instead.
        /// </summary>
        private void BuildRebindRows()
        {
            _rebindRows = new List<RebindRow>();

            foreach (ModAction action in Enum.GetValues(typeof(ModAction)))
            {
                if (ModKeys.ContextOf(action) == ModKeyContext.DebugOnly) continue;
                ModAction a = action; // capture per iteration
                _rebindRows.Add(new RebindRow
                {
                    GetLabel = () => ActionLabel(a),
                    GetValue = () => ModKeys.DisplayName(_pendingKeys[a]),
                    Activate = () => StartCapture(a)
                });
            }

            _rebindRows.Add(new RebindRow
            {
                GetLabel = () => Loc.Get("keybind_reset_item"),
                Activate = ResetPendingToDefaults
            });
            _rebindRows.Add(new RebindRow
            {
                GetLabel = () => Loc.Get("keybind_save_item"),
                Activate = SaveRebindChanges
            });
        }

        /// <summary>Sets every pending binding back to the shipped default.</summary>
        private void ResetPendingToDefaults()
        {
            foreach (ModAction action in Enum.GetValues(typeof(ModAction)))
                _pendingKeys[action] = ModKeys.GetDefault(action);
            ScreenReader.Say(Loc.Get("keybind_reset_done"));
        }

        private string FormatRebindRow(int index)
        {
            var row = _rebindRows[index];
            return row.GetValue != null
                ? Loc.Get("mod_menu_item", row.GetLabel(), row.GetValue(), index + 1, _rebindRows.Count)
                : Loc.Get("keybind_command_item", row.GetLabel(), index + 1, _rebindRows.Count);
        }

        /// <summary>Spoken label of a mod action, from localization.</summary>
        private static string ActionLabel(ModAction action)
        {
            return Loc.Get("keybind_action_" + action);
        }

        /// <summary>One row of the rebind screen: an action or a command.</summary>
        private class RebindRow
        {
            public Func<string> GetLabel;
            /// <summary>Current key name. Null for command rows (reset/save).</summary>
            public Func<string> GetValue;
            public Action Activate;
        }

        #endregion
    }
}
