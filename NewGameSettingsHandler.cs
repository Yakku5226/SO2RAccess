using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces new game settings screen navigation to the screen reader.
    /// This screen follows protagonist selection and contains:
    /// Name, Voice Language, Voice Type, Difficulty, BGM Version,
    /// Display Event Art, Return to defaults, and Confirm.
    ///
    /// Patches applied:
    ///   UITitleSelectVoiceSelector.Show                   — announces screen heading on open
    ///   UITitleSelectVoiceSelector.OnUp                   — announces newly focused item
    ///   UITitleSelectVoiceSelector.OnDown                 — announces newly focused item
    ///   UITitleSelectVoiceSelector.UpdateCurrentPresenter — announces new value on left/right
    ///   UITitleSelectVoiceSelector.OnDecision             — announces when name editing begins
    /// </summary>
    public class NewGameSettingsHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Static back-reference so Harmony static patch methods can call instance logic.
        private static NewGameSettingsHandler _instance;

        private int _lastMenuIndex = -1;

        // True once Show() has finished its own initialization calls to UpdateCurrentPresenter.
        // Prevents announcing the flood of initialization calls as setting changes.
        private bool _showComplete = false;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates the handler. Call once in Main.InitializeHandlers().
        /// </summary>
        public NewGameSettingsHandler()
        {
            _instance = this;
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the new game settings screen.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                // Pre-initialize IL2CPP type tables so TryCast works in postfixes.
                RuntimeHelpers.RunClassConstructor(typeof(UITitleSelectVoiceMenuSelectItemPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommonSelectTextPresenter).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectVoiceSelector), nameof(UITitleSelectVoiceSelector.Show)),
                    postfix: new HarmonyMethod(typeof(NewGameSettingsHandler), nameof(VoiceSelector_Show_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectVoiceSelector), nameof(UITitleSelectVoiceSelector.OnUp)),
                    postfix: new HarmonyMethod(typeof(NewGameSettingsHandler), nameof(VoiceSelector_OnUp_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectVoiceSelector), nameof(UITitleSelectVoiceSelector.OnDown)),
                    postfix: new HarmonyMethod(typeof(NewGameSettingsHandler), nameof(VoiceSelector_OnDown_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectVoiceSelector), nameof(UITitleSelectVoiceSelector.UpdateCurrentPresenter)),
                    postfix: new HarmonyMethod(typeof(NewGameSettingsHandler), nameof(VoiceSelector_UpdateCurrentPresenter_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectVoiceSelector), nameof(UITitleSelectVoiceSelector.OnDecision)),
                    postfix: new HarmonyMethod(typeof(NewGameSettingsHandler), nameof(VoiceSelector_OnDecision_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("NewGameSettingsHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"NewGameSettingsHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        // Postfix for UITitleSelectVoiceSelector.Show()
        private static void VoiceSelector_Show_Postfix(UITitleSelectVoiceSelector __instance)
        {
            if (_instance == null) return;

            _instance._showComplete = false;
            _instance._lastMenuIndex = -1;

            ScreenReader.Say(Loc.Get("newgame_screen"));
            _instance.AnnounceCurrentItem(__instance);

            // Mark initialization complete so UpdateCurrentPresenter postfix fires for user input.
            _instance._showComplete = true;
        }

        // Postfix for UITitleSelectVoiceSelector.OnUp()
        private static void VoiceSelector_OnUp_Postfix(UITitleSelectVoiceSelector __instance)
        {
            _instance?.OnNavigated(__instance);
        }

        // Postfix for UITitleSelectVoiceSelector.OnDown()
        private static void VoiceSelector_OnDown_Postfix(UITitleSelectVoiceSelector __instance)
        {
            _instance?.OnNavigated(__instance);
        }

        // Postfix for UITitleSelectVoiceSelector.UpdateCurrentPresenter(Menu menu, int index, ...)
        // Fires when the player presses left/right to change a setting value.
        private static void VoiceSelector_UpdateCurrentPresenter_Postfix(
            UITitleSelectVoiceSelector __instance,
            UITitleSelectVoiceSelector.Menu menu)
        {
            if (_instance == null || !_instance._showComplete) return;

            try
            {
                var presenter = GetPresenter(__instance, menu);
                string value = GetSettingValueText(__instance, menu, presenter, preferNewValue: true);
                if (!string.IsNullOrEmpty(value))
                {
                    DebugLogger.LogGameValue("NewGameSettings.value", $"menu={menu} value='{value}'");
                    ScreenReader.Say(value);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogGameValue("NewGameSettings.UpdatePresenter.error", ex.Message);
            }
        }

        // Postfix for UITitleSelectVoiceSelector.OnDecision()
        // When the player confirms on the Name row, name editing mode begins.
        private static void VoiceSelector_OnDecision_Postfix(UITitleSelectVoiceSelector __instance)
        {
            if (_instance == null) return;

            if (__instance.isEditName)
            {
                ScreenReader.Say(Loc.Get("newgame_editing_name"));
                DebugLogger.LogState("NewGameSettings: entered name edit mode.");
            }
        }

        #endregion

        #region Internal Logic

        private void OnNavigated(UITitleSelectVoiceSelector selector)
        {
            if (selector == null) return;

            int current = selector.menuIndex;
            if (current == _lastMenuIndex) return;

            _lastMenuIndex = current;
            AnnounceCurrentItem(selector);
        }

        private void AnnounceCurrentItem(UITitleSelectVoiceSelector selector)
        {
            if (selector == null) return;

            try
            {
                var menu = (UITitleSelectVoiceSelector.Menu)selector.menuIndex;
                var presenter = GetPresenter(selector, menu);

                string label = presenter?.label?.text ?? "";
                if (string.IsNullOrEmpty(label))
                    label = GetFallbackLabel(menu);

                string value = GetSettingValueText(selector, menu, presenter, preferNewValue: false);

                DebugLogger.LogGameValue("NewGameSettings.item",
                    $"menu={menu} label='{label}' value='{value}'");

                if (!string.IsNullOrEmpty(value))
                    ScreenReader.Say(Loc.Get("newgame_item_with_value", label, value));
                else
                    ScreenReader.Say(label);
            }
            catch (Exception ex)
            {
                DebugLogger.LogGameValue("NewGameSettings.navigate.error", ex.Message);
            }
        }

        /// <summary>
        /// Returns the presenter for the given menu item, or null if not available.
        /// </summary>
        private static UITitleSelectVoiceMenuSelectItemPresenter GetPresenter(
            UITitleSelectVoiceSelector selector,
            UITitleSelectVoiceSelector.Menu menu)
        {
            var list = selector?.menuPresneterList;
            int idx = (int)menu;
            if (list == null || idx < 0 || idx >= list.Count) return null;
            return list[idx]?.TryCast<UITitleSelectVoiceMenuSelectItemPresenter>();
        }

        /// <summary>
        /// Returns the value text for a menu item.
        /// When preferNewValue is true (left/right change), reads nextText first
        /// because currentText still holds the old value during animation.
        /// When false (navigating to a row), reads the settled currentText.
        /// Returns empty string for button-only rows (Initialize, Decision).
        /// </summary>
        private static string GetSettingValueText(
            UITitleSelectVoiceSelector selector,
            UITitleSelectVoiceSelector.Menu menu,
            UITitleSelectVoiceMenuSelectItemPresenter presenter,
            bool preferNewValue)
        {
            switch (menu)
            {
                case UITitleSelectVoiceSelector.Menu.EditName:
                    return selector.fullNamePresenter?.fullName?.text ?? "";

                case UITitleSelectVoiceSelector.Menu.Initialize:
                case UITitleSelectVoiceSelector.Menu.Decision:
                    return "";

                default:
                    var tp = presenter?.textPresenter;
                    if (tp == null) return "";
                    if (preferNewValue)
                    {
                        // nextText is the new value animating in; currentText is the old value fading out.
                        string next = tp.nextText?.text ?? "";
                        return string.IsNullOrEmpty(next) ? (tp.currentText?.text ?? "") : next;
                    }
                    return tp.currentText?.text ?? "";
            }
        }

        /// <summary>
        /// Fallback label strings used if the presenter's label GameText is empty.
        /// These match the in-game English text for each row.
        /// </summary>
        private static string GetFallbackLabel(UITitleSelectVoiceSelector.Menu menu)
        {
            switch (menu)
            {
                case UITitleSelectVoiceSelector.Menu.EditName:                   return Loc.Get("newgame_label_name");
                case UITitleSelectVoiceSelector.Menu.VoiceLanguage:              return Loc.Get("newgame_label_voice_language");
                case UITitleSelectVoiceSelector.Menu.VoiceVersion:               return Loc.Get("newgame_label_voice_type");
                case UITitleSelectVoiceSelector.Menu.Difficulty:                 return Loc.Get("newgame_label_difficulty");
                case UITitleSelectVoiceSelector.Menu.BGMVersion:                 return Loc.Get("newgame_label_bgm");
                case UITitleSelectVoiceSelector.Menu.EnableEventStandingPicture: return Loc.Get("newgame_label_event_art");
                case UITitleSelectVoiceSelector.Menu.Initialize:                 return Loc.Get("newgame_label_initialize");
                case UITitleSelectVoiceSelector.Menu.Decision:                   return Loc.Get("newgame_label_decision");
                default:                                                         return "";
            }
        }

        #endregion
    }
}
