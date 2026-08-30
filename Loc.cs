using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// Centralized localization for the accessibility mod.
    /// All screen reader strings must go through Loc.Get() — no hardcoded strings.
    ///
    /// The English string table lives in lang\en.json, compiled into the DLL as
    /// an embedded resource. Community translations are plain JSON files in
    /// UserData\SO2RAccess\lang (see TRANSLATING.md). Lookup order per key:
    /// active translation, then embedded English, then the key itself.
    ///
    /// Usage:
    ///   Loc.Get("key")              — get a string
    ///   Loc.Get("key", arg1, arg2)  — get a string with {0}, {1} placeholders
    /// </summary>
    public static class Loc
    {
        #region Fields

        private static bool _initialized = false;
        private static Dictionary<string, string> _english = new Dictionary<string, string>();
        private static Dictionary<string, string> _active = null; // null = English active

        #endregion

        #region Public Methods

        /// <summary>
        /// Language code of the currently active translation ("en" = embedded English).
        /// </summary>
        public static string ActiveCode { get; private set; } = "en";

        /// <summary>
        /// Loads the embedded English table, refreshes the on-disk en.json
        /// template, and applies a manual language override from settings.
        /// Auto-detection of the game language happens later, in
        /// LanguageHandler, because game singletons are not safe to touch
        /// this early. Called once at mod startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _english = LocLoader.LoadEmbeddedEnglish();
            LocLoader.ExtractTemplate();

            string code = ModSettings.Language;
            if (!string.IsNullOrWhiteSpace(code) && code != "auto" && code != "en")
                SetLanguage(code);
        }

        /// <summary>
        /// Returns the localized string for the given key. Falls back to the
        /// embedded English string, then to the key itself (helps spot missing
        /// strings).
        /// </summary>
        public static string Get(string key)
        {
            if (!_initialized) Initialize();
            if (_active != null && _active.TryGetValue(key, out string translated))
                return translated;
            return _english.TryGetValue(key, out string english) ? english : key;
        }

        /// <summary>
        /// Returns the localized string with {0}, {1}, ... placeholders filled in.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Loc.Get format error for key '{key}': {ex.Message}");
                return template;
            }
        }

        /// <summary>
        /// Switches the active language. "en" always succeeds (embedded);
        /// any other code loads UserData\SO2RAccess\lang\[code].json.
        /// On failure the current language is kept and the reason is logged.
        /// Returns true on success so callers can announce the change.
        /// </summary>
        public static bool SetLanguage(string code)
        {
            if (!_initialized) Initialize();
            if (string.IsNullOrWhiteSpace(code)) return false;
            code = code.Trim();

            if (code.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                _active = null;
                ActiveCode = "en";
                MelonLoader.MelonLogger.Msg("[LOC] Language set to English (embedded).");
                return true;
            }

            if (!LocLoader.TryLoadLanguage(code, out Dictionary<string, string> dict))
                return false; // reason already logged by LocLoader

            _active = dict;
            ActiveCode = code;

            int missing = 0, unknown = 0;
            foreach (string key in _english.Keys)
                if (!dict.ContainsKey(key)) missing++;
            foreach (string key in dict.Keys)
                if (!_english.ContainsKey(key)) unknown++;
            MelonLoader.MelonLogger.Msg(
                $"[LOC] Loaded language '{code}': {dict.Count} strings, " +
                $"{missing} missing (English fallback), {unknown} unknown keys.");
            return true;
        }

        /// <summary>
        /// Language codes available for selection: "en" plus every JSON file
        /// in the UserData lang folder.
        /// </summary>
        public static List<string> AvailableCodes()
        {
            return LocLoader.AvailableCodes();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Embedded English value for a key regardless of the active language
        /// (used for language names in the settings menu).
        /// </summary>
        internal static string GetEnglish(string key)
        {
            if (!_initialized) Initialize();
            return _english.TryGetValue(key, out string english) ? english : key;
        }

        #endregion
    }
}
