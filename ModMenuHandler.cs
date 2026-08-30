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
    /// (arrows, Enter, Escape) and never affected by rebinding.
    /// The key-rebinding submenu lives in ModMenuHandler.Rebinding.cs.
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
            /// <summary>The flat settings list.</summary>
            Root,
            /// <summary>The key-bindings submenu list.</summary>
            Rebind,
            /// <summary>Waiting for the user to press the new key for one action.</summary>
            Capture
        }

        private MenuScreen _screen = MenuScreen.Root;
        private int _currentIndex;
        private List<ModMenuItem> _items;

        // Gamepad D-pad repeat (shared with Main's nav overlay).
        private readonly DpadRepeater _dpadRepeater = new DpadRepeater();

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens the mod settings menu and announces the first item.
        /// </summary>
        public void Open()
        {
            BuildItems();
            _currentIndex = 0;
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

            if (kb[Key.Escape].wasPressedThisFrame || kb[ModKeys.ModMenu].wasPressedThisFrame)
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
            if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
            {
                ActivateCurrent();
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

            // Circle / B button = close
            if (gp.buttonEast.wasPressedThisFrame)
            {
                Close();
                return true;
            }

            // Cross / A button = activate (submenus)
            if (gp.buttonSouth.wasPressedThisFrame)
            {
                ActivateCurrent();
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
                    GetValue = () => Loc.Get(ModSettings.SaveSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.SaveSoundEnabled = !ModSettings.SaveSoundEnabled; }
                },
                // Save sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_save_volume",
                    GetValue = () => $"{(int)(ModSettings.SaveSoundVolume * 100)}%",
                    Change = delta => { ModSettings.SaveSoundVolume = ClampVolume(ModSettings.SaveSoundVolume + delta * 0.1f); }
                },
                // Dodge sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dodge_sound",
                    GetValue = () => Loc.Get(ModSettings.DodgeSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.DodgeSoundEnabled = !ModSettings.DodgeSoundEnabled; }
                },
                // Dodge sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dodge_volume",
                    GetValue = () => $"{(int)(ModSettings.DodgeSoundVolume * 100)}%",
                    Change = delta => { ModSettings.DodgeSoundVolume = ClampVolume(ModSettings.DodgeSoundVolume + delta * 0.1f); }
                },
                // Enemy proximity sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_proximity_sound",
                    GetValue = () => Loc.Get(ModSettings.EnemyProximitySoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.EnemyProximitySoundEnabled = !ModSettings.EnemyProximitySoundEnabled; }
                },
                // Enemy proximity sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_proximity_volume",
                    GetValue = () => $"{(int)(ModSettings.EnemyProximitySoundVolume * 100)}%",
                    Change = delta => { ModSettings.EnemyProximitySoundVolume = ClampVolume(ModSettings.EnemyProximitySoundVolume + delta * 0.1f); }
                },
                // Private action sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_pa_volume",
                    GetValue = () => $"{(int)(ModSettings.PrivateActionSoundVolume * 100)}%",
                    Change = delta => { ModSettings.PrivateActionSoundVolume = ClampVolume(ModSettings.PrivateActionSoundVolume + delta * 0.1f); }
                },
                // Dialogue voice mode
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dialogue_mode",
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
                    GetValue = () => Loc.Get(ModSettings.AllyHealthWarningEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.AllyHealthWarningEnabled = !ModSettings.AllyHealthWarningEnabled; }
                },
                // Ally status ailment toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_ally_ailment",
                    GetValue = () => Loc.Get(ModSettings.AllyStatusAilmentEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.AllyStatusAilmentEnabled = !ModSettings.AllyStatusAilmentEnabled; }
                },
                // Player damage dealt toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_player_damage",
                    GetValue = () => Loc.Get(ModSettings.PlayerDamageDealtEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.PlayerDamageDealtEnabled = !ModSettings.PlayerDamageDealtEnabled; }
                },
                // Bonus gauge sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_gauge_volume",
                    GetValue = () => $"{(int)(ModSettings.BonusGaugeSoundVolume * 100)}%",
                    Change = delta => { ModSettings.BonusGaugeSoundVolume = ClampVolume(ModSettings.BonusGaugeSoundVolume + delta * 0.1f); }
                },
                // Bonus gauge break announcement toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_gauge_break_announce",
                    GetValue = () => Loc.Get(ModSettings.BonusGaugeBreakAnnouncementEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.BonusGaugeBreakAnnouncementEnabled = !ModSettings.BonusGaugeBreakAnnouncementEnabled; }
                },
                // Bonus gauge percentage announcement toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_gauge_percent",
                    GetValue = () => Loc.Get(ModSettings.BonusGaugePercentAnnounceEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.BonusGaugePercentAnnounceEnabled = !ModSettings.BonusGaugePercentAnnounceEnabled; }
                },
                // Jump prompt sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_jump_sound",
                    GetValue = () => Loc.Get(ModSettings.JumpPromptSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.JumpPromptSoundEnabled = !ModSettings.JumpPromptSoundEnabled; }
                },
                // Jump prompt speech toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_jump_speech",
                    GetValue = () => Loc.Get(ModSettings.JumpPromptSpeechEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.JumpPromptSpeechEnabled = !ModSettings.JumpPromptSpeechEnabled; }
                },
                // Enter prompt speech toggle (world-map "Press X to enter")
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_enter_speech",
                    GetValue = () => Loc.Get(ModSettings.EnterPromptSpeechEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.EnterPromptSpeechEnabled = !ModSettings.EnterPromptSpeechEnabled; }
                },
                // Fishing prompt bubble sound toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_fish_sound",
                    GetValue = () => Loc.Get(ModSettings.FishPromptSoundEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.FishPromptSoundEnabled = !ModSettings.FishPromptSoundEnabled; }
                },
                // Fishing prompt bubble sound volume
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_fish_volume",
                    GetValue = () => $"{(int)(ModSettings.FishPromptSoundVolume * 100)}%",
                    Change = delta => { ModSettings.FishPromptSoundVolume = ClampVolume(ModSettings.FishPromptSoundVolume + delta * 0.1f); }
                },
                // Walk assist (soft obstacle steering during auto-walk) toggle
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_walk_assist",
                    GetValue = () => Loc.Get(ModSettings.WalkAssistEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.WalkAssistEnabled = !ModSettings.WalkAssistEnabled; }
                },
                // NPC-aware field pathfinding (carve nearby NPCs so the path routes around them)
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_npc_aware_path",
                    GetValue = () => Loc.Get(ModSettings.NpcAwarePathfindingEnabled ? "mod_menu_on" : "mod_menu_off"),
                    Change = _ => { ModSettings.NpcAwarePathfindingEnabled = !ModSettings.NpcAwarePathfindingEnabled; }
                },
                // Where event NPCs (the "!") appear in nav: NPC list / Events list / both
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_event_npc_display",
                    GetValue = () => Loc.Get(EventNpcDisplayValueKey(ModSettings.EventNpcDisplay)),
                    Change = delta =>
                    {
                        int v = ((int)ModSettings.EventNpcDisplay + delta) % 3;
                        if (v < 0) v += 3;
                        ModSettings.EventNpcDisplay = (EventNpcDisplayMode)v;
                    }
                },
                // Speech language: Automatic (follows the game's text language)
                // or a specific translation file from UserData\SO2RAccess\lang
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_language",
                    GetValue = LanguageValueText,
                    Change = ChangeLanguage
                },
                // Key bindings submenu (ModMenuHandler.Rebinding.cs)
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_keybinds",
                    GetValue = () => Loc.Get("mod_menu_submenu"),
                    Activate = OpenRebindScreen
                }
            };
        }

        private static float ClampVolume(float v)
        {
            return (float)Math.Round(Math.Clamp(v, 0f, 1f), 1);
        }

        /// <summary>Spoken value of the language row, e.g. "Automatic (English)" or "Deutsch".</summary>
        private static string LanguageValueText()
        {
            if (ModSettings.Language == "auto")
                return Loc.Get("mod_menu_language_auto", LocLoader.PeekLanguageName(Loc.ActiveCode));
            return LocLoader.PeekLanguageName(ModSettings.Language);
        }

        /// <summary>
        /// Cycles the language setting: Automatic, English, then every
        /// translation file in the lang folder. Applies immediately, so the
        /// menu re-announces this row in the newly loaded language.
        /// </summary>
        private static void ChangeLanguage(int delta)
        {
            var options = new List<string> { "auto" };
            options.AddRange(Loc.AvailableCodes());

            int index = options.IndexOf(ModSettings.Language);
            if (index < 0) index = 0; // stale setting (file deleted): restart the cycle
            index = (index + delta) % options.Count;
            if (index < 0) index += options.Count;

            string choice = options[index];
            ModSettings.Language = choice;
            if (choice == "auto")
                LanguageHandler.DetectNow(announce: false); // this row's re-announce covers it
            else
                Loc.SetLanguage(choice);
        }

        /// <summary>Loc key for the spoken value of the event-NPC display setting.</summary>
        private static string EventNpcDisplayValueKey(EventNpcDisplayMode mode)
        {
            switch (mode)
            {
                case EventNpcDisplayMode.EventsList: return "mod_menu_event_npc_events";
                case EventNpcDisplayMode.Both:       return "mod_menu_event_npc_both";
                default:                             return "mod_menu_event_npc_npclist";
            }
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
        }

        #endregion
    }
}
