using HarmonyLib;
using Il2CppCommon;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Auto-detects the game's text language and keeps the mod's speech
    /// language in sync when ModSettings.Language is "auto".
    ///
    /// Three mechanisms:
    /// 1. One-shot startup detection in Update() — reads
    ///    SystemConfigParameter.TextLanguage once the game is ready (game
    ///    singletons must not be touched earlier, see Main.cs).
    /// 2. Harmony postfix on TextManager.OnChangeLanguage — fires when the
    ///    player changes the text language in the game's config menu, so the
    ///    mod switches mid-session without a restart. The parameter is a
    ///    by-value enum, so the never-hook-ref-IL2CPP-value-types rule is
    ///    respected.
    /// 3. Polling backup in Update() (every 2 s, auto mode only) — the game
    ///    applies language changes through a native ChangeLanguageTask, so
    ///    the managed hook may never fire; the poll catches any change the
    ///    hook misses. Hook and poll share _lastSeenGameLanguage so a switch
    ///    is only ever announced once.
    ///
    /// A manual override (any setting other than "auto") disables both — the
    /// override is applied by Loc.Initialize() at startup and by the settings
    /// menu row on change.
    /// </summary>
    public class LanguageHandler
    {
        #region Fields

        private bool _patchesApplied;
        private float _pollTimer;

        private const float PollIntervalSeconds = 2f;

        // Static: shared by the static Harmony postfix and DetectNow(), which
        // the settings menu calls without a handler reference.
        private static bool _autoDetectDone;
        private static int _lastSeenGameLanguage = -1;
        private static bool _readFailureLogged;

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
        /// Per-frame update. Runs the one-shot startup auto-detection, then
        /// polls for live language changes every couple of seconds while the
        /// setting is Automatic. Free when a manual override is set.
        /// </summary>
        public void Update()
        {
            if (ModSettings.Language != "auto") return;

            if (!_autoDetectDone)
            {
                DetectNow(announce: false);
                return;
            }

            _pollTimer += Time.deltaTime;
            if (_pollTimer < PollIntervalSeconds) return;
            _pollTimer = 0f;

            int gameLanguage = TryReadGameLanguage();
            if (gameLanguage < 0 || gameLanguage == _lastSeenGameLanguage) return;

            MelonLogger.Msg($"LanguageHandler: game text language change detected by polling ({_lastSeenGameLanguage} -> {gameLanguage}).");
            _lastSeenGameLanguage = gameLanguage;
            ApplyGameLanguage(gameLanguage, announce: true);
        }

        /// <summary>
        /// Reads the game's current text language and applies the matching
        /// translation. Called from Update() at startup and by the settings
        /// menu when the user selects Automatic. Silent when the game is not
        /// readable yet (detection retries via Update()).
        /// </summary>
        public static void DetectNow(bool announce)
        {
            int gameLanguage = TryReadGameLanguage();
            if (gameLanguage < 0) return; // not ready — Update() retries

            _autoDetectDone = true;
            _lastSeenGameLanguage = gameLanguage;
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
                // Always log, even when nothing is done with it — this line is
                // the proof that the hook actually fires (vs. the polling
                // backup catching the change).
                MelonLogger.Msg($"LanguageHandler: OnChangeLanguage hook fired, game text language = {(int)language}.");
                _lastSeenGameLanguage = (int)language;
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
        /// Reads the game's current text language, or -1 when it is not
        /// readable (game still loading, or a transient IL2CPP hiccup — both
        /// resolve on a later poll). A hard failure is logged once.
        /// </summary>
        private static int TryReadGameLanguage()
        {
            try
            {
                SystemConfigParameter config = ParameterManager.Instance?.SystemConfigParameter;
                if (config == null) return -1;
                return (int)config.TextLanguage;
            }
            catch (Exception ex)
            {
                if (!_readFailureLogged)
                {
                    _readFailureLogged = true;
                    MelonLogger.Warning($"LanguageHandler: could not read game language (will keep retrying quietly): {ex.Message}");
                }
                return -1;
            }
        }

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
