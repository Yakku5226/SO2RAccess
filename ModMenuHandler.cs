using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Screen-reader-driven mod settings menu. Opened with the mod-menu key
    /// (ModKeys.ModMenu, default F4) or L2+L3 (gamepad). All navigation is
    /// purely audio — no game UI involved. Menu navigation keys are fixed
    /// (arrows, Enter, Space, Escape) and never affected by rebinding.
    ///
    /// The root list holds submenu rows only; the settings themselves live one
    /// level down:
    /// <list type="bullet">
    /// <item>Sound and announcements — ModMenuHandler.Sound.cs</item>
    /// <item>Language and speech — ModMenuHandler.Language.cs</item>
    /// <item>Key bindings — ModMenuHandler.Rebinding.cs</item>
    /// </list>
    /// </summary>
    public partial class ModMenuHandler
    {
        #region Fields

        /// <summary>Whether the mod menu is currently open.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Static flag checked by GameInputManager Harmony prefixes to block
        /// ALL game input while the mod menu is open.
        /// </summary>
        public static bool SuppressAllGameInput { get; private set; }

        /// <summary>Which screen of the menu is active.</summary>
        private enum MenuScreen
        {
            /// <summary>The root list of submenus.</summary>
            Root,
            /// <summary>A settings submenu — same behaviour as Root, but Escape goes back.</summary>
            SubSettings,
            /// <summary>The key-bindings submenu list.</summary>
            Rebind,
            /// <summary>Waiting for the user to press the new key for one action.</summary>
            Capture
        }

        private MenuScreen _screen = MenuScreen.Root;
        private int _currentIndex;
        private List<ModMenuItem> _items;

        /// <summary>Focused root row, restored when a settings submenu is left.</summary>
        private int _rootIndex;

        // Gamepad D-pad repeat (shared with Main's nav overlay).
        private readonly DpadRepeater _dpadRepeater = new DpadRepeater();

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens the mod settings menu and announces the first item.
        /// </summary>
        public void Open()
        {
            BuildRootItems();
            _currentIndex = 0;
            _rootIndex = 0;
            _screen = MenuScreen.Root;
            _dpadRepeater.Reset();
            IsOpen = true;
            SuppressAllGameInput = true;

            string heading = Loc.Get("mod_menu_open");
            string firstItem = FormatItem(_currentIndex);
            ScreenReader.Say($"{heading} {firstItem}");
            DebugLogger.LogState("ModMenu opened.");
        }

        /// <summary>
        /// Closes the mod settings menu, saves settings, and announces.
        /// </summary>
        public void Close()
        {
            StopSoundPreview();
            IsOpen = false;
            SuppressAllGameInput = false;
            ModSettings.Save();
            ScreenReader.Say(Loc.Get("mod_menu_close"));
            DebugLogger.LogState("ModMenu closed, settings saved.");
        }

        /// <summary>
        /// Toggles the menu open or closed.
        /// </summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Per-frame housekeeping, called from Main.OnUpdate() whether the menu
        /// is open or not. Preview loops stop on the mixer's own timer now, so
        /// there is nothing to count down; kept as the menu's per-frame hook.
        /// </summary>
        public void Tick()
        {
        }

        /// <summary>
        /// Processes keyboard input while the menu is open.
        /// Called from Main.ProcessHotkeys(). Returns true if input was consumed.
        /// </summary>
        public bool ProcessKeyboard(Keyboard kb)
        {
            if (!IsOpen) return false;

            // Sub-screens have their own key handling (Rebinding partial).
            if (_screen == MenuScreen.Capture)
            {
                ProcessCaptureKeyboard(kb);
                return true;
            }
            if (_screen == MenuScreen.Rebind)
            {
                ProcessRebindKeyboard(kb);
                return true;
            }

            // The mod-menu key always closes the whole menu, at any depth.
            if (kb[ModKeys.ModMenu].wasPressedThisFrame)
            {
                Close();
                return true;
            }
            // Escape leaves a submenu, or closes when the root list is showing.
            if (kb[Key.Escape].wasPressedThisFrame)
            {
                GoBack();
                return true;
            }

            if (kb[Key.UpArrow].wasPressedThisFrame)
            {
                Navigate(-1);
                return true;
            }
            if (kb[Key.DownArrow].wasPressedThisFrame)
            {
                Navigate(1);
                return true;
            }
            if (kb[Key.LeftArrow].wasPressedThisFrame)
            {
                ChangeValue(-1);
                return true;
            }
            if (kb[Key.RightArrow].wasPressedThisFrame)
            {
                ChangeValue(1);
                return true;
            }
            if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
            {
                ActivateCurrent();
                return true;
            }
            // Space — hear the sound the focused row controls (Square on the pad).
            if (kb[Key.Space].wasPressedThisFrame)
            {
                PreviewCurrent();
                return true;
            }

            // Consume all other keys while menu is open to prevent pass-through.
            return true;
        }

        /// <summary>
        /// Processes gamepad input while the menu is open.
        /// Called from Main.ProcessGamepad(). Returns true if input was consumed.
        /// </summary>
        public bool ProcessGamepad(Gamepad gp)
        {
            if (!IsOpen) return false;

            // Sub-screens have their own pad handling (Rebinding partial).
            if (_screen == MenuScreen.Capture)
            {
                ProcessCaptureGamepad(gp);
                return true;
            }
            if (_screen == MenuScreen.Rebind)
            {
                ProcessRebindGamepad(gp);
                return true;
            }

            // Circle / B button = back one level, or close at the root
            if (gp.buttonEast.wasPressedThisFrame)
            {
                GoBack();
                return true;
            }

            // Cross / A button = activate (opens a submenu)
            if (gp.buttonSouth.wasPressedThisFrame)
            {
                ActivateCurrent();
                return true;
            }

            // Square / X button = preview the focused row's sound
            if (gp.buttonWest.wasPressedThisFrame)
            {
                PreviewCurrent();
                return true;
            }

            // D-pad navigation with auto-repeat
            bool dUp    = gp.dpad.up.isPressed;
            bool dDown  = gp.dpad.down.isPressed;
            bool dLeft  = gp.dpad.left.isPressed;
            bool dRight = gp.dpad.right.isPressed;

            int currentDir = dUp ? 1 : dDown ? 2 : dLeft ? 3 : dRight ? 4 : 0;
            _dpadRepeater.Update(currentDir, UnityEngine.Time.deltaTime, FireDpadAction);

            // Consume all gamepad input while menu is open.
            return true;
        }

        #endregion

        #region Navigation

        private void FireDpadAction(int dir)
        {
            switch (dir)
            {
                case 1: Navigate(-1); break; // Up
                case 2: Navigate(1); break;  // Down
                case 3: ChangeValue(-1); break; // Left
                case 4: ChangeValue(1); break;  // Right
            }
        }

        private void Navigate(int delta)
        {
            if (_items == null || _items.Count == 0) return;

            _currentIndex += delta;
            if (_currentIndex < 0) _currentIndex = _items.Count - 1;
            else if (_currentIndex >= _items.Count) _currentIndex = 0;

            ScreenReader.Say(FormatItem(_currentIndex));
        }

        private void ChangeValue(int delta)
        {
            if (_items == null || _items.Count == 0) return;

            var item = _items[_currentIndex];
            if (item.Change == null)
            {
                // Submenu rows have no left/right value; point at Enter instead.
                ScreenReader.Say(Loc.Get("mod_menu_use_enter"));
                return;
            }
            item.Change(delta);

            string label = Loc.Get(item.LabelKey);
            string value = item.GetValue();
            ScreenReader.Say(Loc.Get("mod_menu_changed", label, value));
        }

        /// <summary>
        /// Runs the Enter action of the focused item, if it has one.
        /// Items without one (plain settings) ignore Enter.
        /// </summary>
        private void ActivateCurrent()
        {
            if (_items == null || _items.Count == 0) return;
            _items[_currentIndex].Activate?.Invoke();
        }

        /// <summary>
        /// Escape / Circle: leaves a settings submenu, or closes the menu when
        /// the root list is already showing.
        /// </summary>
        private void GoBack()
        {
            if (_screen == MenuScreen.SubSettings) LeaveSettingsSubmenu();
            else Close();
        }

        private string FormatItem(int index)
        {
            var item = _items[index];
            string label = Loc.Get(item.LabelKey);
            string value = item.GetValue();
            return Loc.Get("mod_menu_item", label, value, index + 1, _items.Count);
        }

        #endregion

        #region Screens

        /// <summary>
        /// Builds the root list. It holds submenu rows only — every actual
        /// setting lives one level down.
        /// </summary>
        private void BuildRootItems()
        {
            _items = new List<ModMenuItem>
            {
                Submenu("mod_menu_label_sound_group", OpenSoundScreen),
                Submenu("mod_menu_label_wall_sounds_group", OpenWallSoundsScreen),
                Submenu("mod_menu_label_beacon_sounds_group", OpenBeaconSoundsScreen),
                Submenu("mod_menu_label_language_group", OpenLanguageScreen),
                Submenu("mod_menu_label_keybinds", OpenRebindScreen)
            };
        }

        /// <summary>A root row that opens a submenu when Enter is pressed.</summary>
        private static ModMenuItem Submenu(string labelKey, Action activate)
        {
            return new ModMenuItem
            {
                LabelKey = labelKey,
                GetValue = () => Loc.Get("mod_menu_submenu"),
                Activate = activate
            };
        }

        /// <summary>
        /// Shows a settings submenu: remembers the root row, swaps in the
        /// submenu's items, and announces its heading plus the first row.
        /// </summary>
        private void OpenSettingsSubmenu(string headingKey, List<ModMenuItem> items)
        {
            _rootIndex = _currentIndex;
            _items = items;
            _currentIndex = 0;
            _screen = MenuScreen.SubSettings;
            _dpadRepeater.Reset();

            ScreenReader.Say($"{Loc.Get(headingKey)} {FormatItem(_currentIndex)}");
            DebugLogger.LogState($"ModMenu: submenu {headingKey} opened.");
        }

        /// <summary>
        /// Returns from a settings submenu to the root list, saving on the way
        /// out so a later crash cannot lose the changes just made.
        /// </summary>
        private void LeaveSettingsSubmenu()
        {
            StopSoundPreview();
            ModSettings.Save();
            BuildRootItems();
            _currentIndex = Math.Clamp(_rootIndex, 0, _items.Count - 1);
            _screen = MenuScreen.Root;
            _dpadRepeater.Reset();

            ScreenReader.Say($"{Loc.Get("mod_menu_back")} {FormatItem(_currentIndex)}");
            DebugLogger.LogState("ModMenu: submenu left, settings saved.");
        }

        #endregion

        #region Row Builders

        /// <summary>A plain on/off setting row.</summary>
        private static ModMenuItem Toggle(string labelKey, Func<bool> get, Action<bool> set,
            Func<bool> preview = null)
        {
            return new ModMenuItem
            {
                LabelKey = labelKey,
                GetValue = () => Loc.Get(get() ? "mod_menu_on" : "mod_menu_off"),
                Change = _ => set(!get()),
                Preview = preview
            };
        }

        /// <summary>A 0 to 100 per cent volume row, stepped by 10 per left/right press.</summary>
        private static ModMenuItem Volume(string labelKey, Func<float> get, Action<float> set,
            Func<bool> preview = null)
        {
            return new ModMenuItem
            {
                LabelKey = labelKey,
                GetValue = () => $"{(int)(get() * 100)}%",
                Change = delta => set(ClampVolume(get() + delta * 0.1f)),
                Preview = preview
            };
        }

        /// <summary>A distance row in whole metres, stepped by 1 per left/right press, stopping at the ends.</summary>
        private static ModMenuItem Metres(string labelKey, Func<int> get, Action<int> set, int min, int max)
        {
            return new ModMenuItem
            {
                LabelKey = labelKey,
                GetValue = () => Loc.Get("mod_menu_metres", get()),
                Change = delta => set(Math.Clamp(get() + delta, min, max))
            };
        }

        private static float ClampVolume(float v)
        {
            return (float)Math.Round(Math.Clamp(v, 0f, 1f), 1);
        }

        #endregion

        #region Inner Types

        /// <summary>
        /// A single item in the mod settings menu.
        /// </summary>
        private class ModMenuItem
        {
            public string LabelKey;
            public Func<string> GetValue;
            /// <summary>Left/right value change. Null for submenu rows.</summary>
            public Action<int> Change;
            /// <summary>Enter action (opens a submenu). Null for plain settings.</summary>
            public Action Activate;
            /// <summary>
            /// Space / Square action: plays the sound this row controls, so the
            /// user can judge a volume without leaving the menu. Returns false
            /// when the WAV is missing, so the menu can say so instead of just
            /// going quiet. Null on rows that have no sound of their own.
            /// </summary>
            public Func<bool> Preview;
        }

        #endregion
    }
}
