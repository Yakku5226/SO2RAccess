using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// Announces title menu navigation to the screen reader.
    /// Covers the "press any button" screen and the main title menu.
    ///
    /// Patches applied:
    ///   UITitlePressAnyButtonSelector.Show — announces the press-any-button screen
    ///   UITitleMenuSelector.Show           — announces the menu on open
    ///   UITitleMenuSelector.OnInput        — announces the focused item when it changes
    /// </summary>
    public class TitleMenuHandler
    {
        #region Fields

        private int _lastAnnouncedIndex = -1;
        private bool _patchesApplied = false;

        // Static back-reference so Harmony static patch methods can call instance logic.
        private static TitleMenuHandler _instance;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates the handler. Call once in Main.InitializeHandlers().
        /// Does not apply patches — call ApplyPatches() separately.
        /// </summary>
        public TitleMenuHandler()
        {
            _instance = this;
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the title menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(UITitlePressAnyButtonSelector), nameof(UITitlePressAnyButtonSelector.Show)),
                    postfix: new HarmonyMethod(typeof(TitleMenuHandler), nameof(PressAnyButton_Show_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleMenuSelector), nameof(UITitleMenuSelector.Show)),
                    postfix: new HarmonyMethod(typeof(TitleMenuHandler), nameof(TitleMenu_Show_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleMenuSelector), nameof(UITitleMenuSelector.OnInput)),
                    postfix: new HarmonyMethod(typeof(TitleMenuHandler), nameof(TitleMenu_OnInput_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("TitleMenuHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"TitleMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        // Postfix for UITitlePressAnyButtonSelector.Show()
        private static void PressAnyButton_Show_Postfix()
        {
            ScreenReader.Say(Loc.Get("title_press_any_button"));
            DebugLogger.LogState("Title: press any button screen shown.");
        }

        // Postfix for UITitleMenuSelector.Show()
        private static void TitleMenu_Show_Postfix(UITitleMenuSelector __instance)
        {
            if (_instance == null) return;

            // Reset so the first item is always announced when the menu opens.
            _instance._lastAnnouncedIndex = -1;
            _instance.AnnounceCurrentItem(__instance);
        }

        // Postfix for UITitleMenuSelector.OnInput()
        private static void TitleMenu_OnInput_Postfix(UITitleMenuSelector __instance)
        {
            _instance?.OnInputProcessed(__instance);
        }

        #endregion

        #region Internal Logic

        private void OnInputProcessed(UITitleMenuSelector selector)
        {
            if (selector == null) return;

            int current = selector.CurrentIndex;
            if (current == _lastAnnouncedIndex) return;

            _lastAnnouncedIndex = current;
            AnnounceCurrentItem(selector);
        }

        private void AnnounceCurrentItem(UITitleMenuSelector selector)
        {
            if (selector?.menuItemList == null) return;

            int idx = selector.CurrentIndex;
            int count = selector.menuItemList.Count;

            if (idx < 0 || idx >= count) return;

            var item = selector.menuItemList[idx];
            if (item == null) return;

            string name = item.itemName ?? "";
            if (string.IsNullOrEmpty(name)) return;

            bool available = item.canDecision;
            DebugLogger.LogGameValue("TitleMenu.item", $"{name} ({idx + 1}/{count}) available={available}");

            if (available)
                ScreenReader.Say(Loc.Get("title_menu_item", name, idx + 1, count));
            else
                ScreenReader.Say(Loc.Get("title_menu_item_unavailable", name, idx + 1, count));
        }

        #endregion
    }
}
