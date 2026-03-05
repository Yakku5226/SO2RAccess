using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Types of settings items in the mod menu.
    /// </summary>
    public enum ModMenuItemType
    {
        Toggle,
        Volume,
        Enum
    }

    /// <summary>
    /// Screen-reader-driven mod settings menu. Opened with F4 (keyboard)
    /// or L1+L3 (gamepad). All navigation is purely audio — no game UI involved.
    /// </summary>
    public class ModMenuHandler
    {
        #region Fields

        /// <summary>Whether the mod menu is currently open.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Static flag checked by GameInputManager Harmony prefixes to block
        /// ALL game input while the mod menu is open.
        /// </summary>
        public static bool SuppressAllGameInput { get; private set; }

        private int _currentIndex;
        private List<ModMenuItem> _items;

        // Gamepad D-pad repeat state (mirrors Main's nav overlay pattern).
        private int _dpadRepeatDir;
        private float _dpadRepeatTimer;
        private const float DpadRepeatInitial = 0.4f;
        private const float DpadRepeatInterval = 0.15f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens the mod settings menu and announces the first item.
        /// </summary>
        public void Open()
        {
            BuildItems();
            _currentIndex = 0;
            _dpadRepeatDir = 0;
            _dpadRepeatTimer = 0f;
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
        /// Processes keyboard input while the menu is open.
        /// Called from Main.ProcessHotkeys(). Returns true if input was consumed.
        /// </summary>
        public bool ProcessKeyboard(Keyboard kb)
        {
            if (!IsOpen) return false;

            if (kb[Key.Escape].wasPressedThisFrame || kb[Key.F4].wasPressedThisFrame)
            {
                Close();
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

            // Circle / B button = close
            if (gp.buttonEast.wasPressedThisFrame)
            {
                Close();
                return true;
            }

            // D-pad navigation with auto-repeat
            bool dUp    = gp.dpad.up.isPressed;
            bool dDown  = gp.dpad.down.isPressed;
            bool dLeft  = gp.dpad.left.isPressed;
            bool dRight = gp.dpad.right.isPressed;

            int currentDir = dUp ? 1 : dDown ? 2 : dLeft ? 3 : dRight ? 4 : 0;

            if (currentDir == 0)
            {
                _dpadRepeatDir = 0;
                _dpadRepeatTimer = 0f;
            }
            else if (currentDir != _dpadRepeatDir)
            {
                _dpadRepeatDir = currentDir;
                _dpadRepeatTimer = DpadRepeatInitial;
                FireDpadAction(currentDir);
            }
            else
            {
                _dpadRepeatTimer -= UnityEngine.Time.deltaTime;
                if (_dpadRepeatTimer <= 0f)
                {
                    _dpadRepeatTimer = DpadRepeatInterval;
                    FireDpadAction(currentDir);
                }
            }

            // Consume all gamepad input while menu is open.
            return true;
        }

        #endregion

        #region Private Methods

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
            item.Change(delta);

            string label = Loc.Get(item.LabelKey);
            string value = item.GetValue();
            ScreenReader.Say(Loc.Get("mod_menu_changed", label, value));
        }

        private string FormatItem(int index)
        {
            var item = _items[index];
            string label = Loc.Get(item.LabelKey);
            string value = item.GetValue();
            return Loc.Get("mod_menu_item", label, value, index + 1, _items.Count);
        }

        /// <summary>
        /// Builds the flat list of menu items from current ModSettings.
        /// </summary>
        private void BuildItems()
        {
            _items = new List<ModMenuItem>
            {
                // Save sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_save_sound",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.SaveSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.SaveSoundEnabled = !ModSettings.SaveSoundEnabled; }
                },
                // Save sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_save_volume",
                    Type = ModMenuItemType.Volume,
                    GetValue = () => $"{(int)(ModSettings.SaveSoundVolume * 100)}%",
                    Change = delta => { ModSettings.SaveSoundVolume = ClampVolume(ModSettings.SaveSoundVolume + delta * 0.1f); }
                },
                // Dodge sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dodge_sound",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.DodgeSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.DodgeSoundEnabled = !ModSettings.DodgeSoundEnabled; }
                },
                // Dodge sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dodge_volume",
                    Type = ModMenuItemType.Volume,
                    GetValue = () => $"{(int)(ModSettings.DodgeSoundVolume * 100)}%",
                    Change = delta => { ModSettings.DodgeSoundVolume = ClampVolume(ModSettings.DodgeSoundVolume + delta * 0.1f); }
                },
                // Enemy proximity sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_proximity_sound",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.EnemyProximitySoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.EnemyProximitySoundEnabled = !ModSettings.EnemyProximitySoundEnabled; }
                },
                // Enemy proximity sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_proximity_volume",
                    Type = ModMenuItemType.Volume,
                    GetValue = () => $"{(int)(ModSettings.EnemyProximitySoundVolume * 100)}%",
                    Change = delta => { ModSettings.EnemyProximitySoundVolume = ClampVolume(ModSettings.EnemyProximitySoundVolume + delta * 0.1f); }
                },
                // Dialogue voice mode
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dialogue_mode",
                    Type = ModMenuItemType.Enum,
                    GetValue = () => ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                        ? Loc.Get("mod_menu_dialogue_full")
                        : Loc.Get("mod_menu_dialogue_name_only"),
                    Change = _ =>
                    {
                        ModSettings.DialogueVoiceMode =
                            ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                                ? DialogueVoiceMode.NameOnlyWhenVoiced
                                : DialogueVoiceMode.Full;
                    }
                },
                // Ally health warnings toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_ally_health",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.AllyHealthWarningEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.AllyHealthWarningEnabled = !ModSettings.AllyHealthWarningEnabled; }
                },
                // Ally status ailment toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_ally_ailment",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.AllyStatusAilmentEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.AllyStatusAilmentEnabled = !ModSettings.AllyStatusAilmentEnabled; }
                },
                // Player damage dealt toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_player_damage",
                    Type = ModMenuItemType.Toggle,
                    GetValue = () => Loc.Get(ModSettings.PlayerDamageDealtEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.PlayerDamageDealtEnabled = !ModSettings.PlayerDamageDealtEnabled; }
                }
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
            public ModMenuItemType Type;
            public Func<string> GetValue;
            public Action<int> Change;
        }

        #endregion
    }
}
