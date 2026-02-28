using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// Announces protagonist selection screen navigation to the screen reader.
    /// This is the screen that appears after choosing "New Game" from the title menu,
    /// where the player chooses between Claude and Rena.
    ///
    /// Patches applied:
    ///   UITitleSelectHeroSelector.Show       — announces the screen heading on open
    ///   UITitleSelectHeroSelector.OnSelected — announces the focused protagonist and
    ///                                          their description on open and navigation
    /// </summary>
    public class HeroSelectHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Static back-reference so Harmony static patch methods can call instance logic.
        private static HeroSelectHandler _instance;

        // Last announced PlayerID — prevents re-announcing if the game fires OnSelected
        // multiple times for the same hero without a real selection change.
        private PlayerID _lastAnnouncedHero = PlayerID.INVALID;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates the handler. Call once in Main.InitializeHandlers().
        /// Does not apply patches — call ApplyPatches() separately.
        /// </summary>
        public HeroSelectHandler()
        {
            _instance = this;
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the protagonist selection screen.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectHeroSelector), nameof(UITitleSelectHeroSelector.Show)),
                    postfix: new HarmonyMethod(typeof(HeroSelectHandler), nameof(HeroSelector_Show_Postfix))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UITitleSelectHeroSelector), nameof(UITitleSelectHeroSelector.OnSelected)),
                    postfix: new HarmonyMethod(typeof(HeroSelectHandler), nameof(HeroSelector_OnSelected_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("HeroSelectHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"HeroSelectHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        // Postfix for UITitleSelectHeroSelector.Show()
        // Announces the screen heading so the user knows where they are.
        private static void HeroSelector_Show_Postfix()
        {
            if (_instance == null) return;

            // Reset so the first OnSelected after Show always announces.
            _instance._lastAnnouncedHero = PlayerID.INVALID;

            ScreenReader.Say(Loc.Get("hero_select_screen"));
            DebugLogger.LogState("HeroSelect: screen shown.");
        }

        // Postfix for UITitleSelectHeroSelector.OnSelected()
        // Fires when the focused protagonist changes (initial show + left/right navigation).
        private static void HeroSelector_OnSelected_Postfix(UITitleSelectHeroSelector __instance, PlayerID playerID)
        {
            _instance?.OnHeroFocused(__instance, playerID);
        }

        #endregion

        #region Internal Logic

        private void OnHeroFocused(UITitleSelectHeroSelector selector, PlayerID playerID)
        {
            if (selector == null) return;
            if (playerID == _lastAnnouncedHero) return;

            _lastAnnouncedHero = playerID;

            string name = playerID == PlayerID.CLAUDE
                ? Loc.Get("hero_name_claude")
                : Loc.Get("hero_name_rena");

            string description = selector.heroDescription?.text ?? "";
            // Collapse newlines so screen reader reads it as one sentence.
            description = description.Replace("\r\n", " ").Replace("\n", " ").Trim();

            DebugLogger.LogGameValue("HeroSelect.hero", $"{name} desc='{description}'");

            if (string.IsNullOrEmpty(description))
                ScreenReader.Say(name);
            else
                ScreenReader.Say(Loc.Get("hero_select_item", name, description));
        }

        #endregion
    }
}
