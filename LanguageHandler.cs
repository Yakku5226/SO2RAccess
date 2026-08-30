using HarmonyLib;
using Il2CppCommon;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Auto-detects the game's text language and keeps the mod's speech
    /// language in sync when ModSettings.Language is "auto".
    ///
    /// Two mechanisms:
    /// 1. One-shot startup detection in Update() — reads
    ///    SystemConfigParameter.TextLanguage once the game is ready (game
    ///    singletons must not be touched earlier, see Main.cs).
    /// 2. Harmony postfix on TextManager.OnChangeLanguage — fires when the
    ///    player changes the text language in the game's config menu, so the
    ///    mod switches mid-session without a restart. The parameter is a
    ///    by-value enum, so the never-hook-ref-IL2CPP-value-types rule is
    ///    respected. Should IL2CPP ever inline this method away (patch applies
    ///    but never fires), the fallback would be polling TextLanguage on a
    ///    slow timer here in Update().
    ///
    /// A manual override (any setting other than "auto") disables both — the
    /// override is applied by Loc.Initialize() at startup and by the settings
    /// menu row on change.
    /// </summary>
    public class LanguageHandler
    {
        #region Fields

        private bool _patchesApplied;

        // Static: shared by the static Harmony postfix and DetectNow(), which
        // the settings menu calls without a handler reference.
        private static bool _autoDetectDone;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies the live language-switch hook. Safe to call multiple times.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(TextManager).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(TextManager),
                        nameof(TextManager.OnChangeLanguage)),
                    postfix: new HarmonyMethod(typeof(LanguageHandler),
                        nameof(OnChangeLanguage_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("LanguageHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"LanguageHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Per-frame update. Runs the one-shot startup auto-detection; free
        /// after it completes or when a manual override is set.
        /// </summary>
        public void Update()
        {
            if (_autoDetectDone || ModSettings.Language != "auto") return;
            DetectNow(announce: false);
        }

        /// <summary>
        /// Reads the game's current text language and applies the matching
        /// translation. Called from Update() at startup and by the settings
        /// menu when the user selects Automatic. Silent when the game is not
        /// ready yet (detection retries next frame in that case).
        /// </summary>
        public static void DetectNow(bool announce)
        {
            int gameLanguage;
            try
            {
                SystemConfigParameter config = ParameterManager.Instance?.SystemConfigParameter;
                if (config == null) return; // not ready yet — Update() retries
                gameLanguage = (int)config.TextLanguage;
            }
            catch (Exception ex)
            {
                // Read failed outright (not just "not ready") — stop retrying,
                // stay on the current language, and say why in the log.
                _autoDetectDone = true;
                MelonLogger.Warning($"LanguageHandler: could not read game language, staying on '{Loc.ActiveCode}': {ex.Message}");
                return;
            }

            _autoDetectDone = true;
            ApplyGameLanguage(gameLanguage, announce);
        }

        #endregion

        #region Harmony Patches

        /// <summary>
        /// Postfix for TextManager.OnChangeLanguage — the player switched the
        /// game's text language mid-session. Follows it in auto mode only.
        /// </summary>
        private static void OnChangeLanguage_Postfix(Language language)
        {
            try
            {
                if (ModSettings.Language != "auto") return;
                ApplyGameLanguage((int)language, announce: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"LanguageHandler.OnChangeLanguage_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Maps a game language value to a translation file and loads it if it
        /// differs from the active one. Missing files keep the current
        /// language, with the reason logged (never silent).
        /// </summary>
        private static void ApplyGameLanguage(int gameLanguage, bool announce)
        {
            string code = LocLoader.MapGameLanguage(gameLanguage);
            if (code == null)
            {
                MelonLogger.Warning($"LanguageHandler: unknown game language value {gameLanguage}, staying on '{Loc.ActiveCode}'.");
                return;
            }
            if (code == Loc.ActiveCode) return;

            if (Loc.SetLanguage(code))
            {
                if (announce)
                    ScreenReader.Say(Loc.Get("language_switched", LocLoader.PeekLanguageName(code)));
                return;
            }

            // No translation file for the game's language (reason already
            // logged by LocLoader). Auto mode mirrors the game, so fall back
            // to English rather than keeping an unrelated earlier pick.
            if (Loc.ActiveCode != "en" && Loc.SetLanguage("en") && announce)
                ScreenReader.Say(Loc.Get("language_switched", LocLoader.PeekLanguageName("en")));
        }

        #endregion
    }
}
