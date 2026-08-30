using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// File and resource IO for Loc: the embedded English table, the on-disk
    /// en.json reference template, and community translation files in
    /// UserData\SO2RAccess\lang. See TRANSLATING.md for the file format.
    /// </summary>
    internal static class LocLoader
    {
        #region Fields

        // Cache for PeekLanguageName so the settings menu does not re-read
        // files while cycling. Invalidated per file via its last-write time.
        private static readonly Dictionary<string, (DateTime WriteTime, string Name)> _nameCache =
            new Dictionary<string, (DateTime, string)>();

        #endregion

        #region Internal Methods

        /// <summary>
        /// Folder holding en.json (regenerated template) and community
        /// translation files. UserData is always {game root}\UserData — the
        /// game's working directory.
        /// </summary>
        internal static string LangDir =>
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess", "lang");

        /// <summary>
        /// Loads the English table embedded in the DLL. Returns an empty
        /// dictionary (and logs an error) if the resource is missing, in which
        /// case Loc.Get falls back to speaking raw keys — audible, not silent.
        /// </summary>
        internal static Dictionary<string, string> LoadEmbeddedEnglish()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = FindEmbeddedResourceName(assembly);
                if (resourceName == null)
                {
                    MelonLogger.Error("[LOC] Embedded lang.en.json resource not found in DLL.");
                    return new Dictionary<string, string>();
                }
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream); // detects UTF-8 BOM
                Dictionary<string, string> dict = ParseLanguageJson(reader.ReadToEnd());
                MelonLogger.Msg($"[LOC] Embedded English loaded: {dict.Count} strings.");
                return dict;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LOC] Failed to load embedded English: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Writes the embedded en.json to the UserData lang folder as a
        /// reference template for translators. Deliberately overwrites en.json
        /// on every launch so the template stays current across mod updates —
        /// unlike the world map grids, this file is never user-edited (edits
        /// belong in a copy, e.g. de.json, which is never touched).
        /// </summary>
        internal static void ExtractTemplate()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = FindEmbeddedResourceName(assembly);
                if (resourceName == null) return; // already logged by LoadEmbeddedEnglish

                Directory.CreateDirectory(LangDir);
                string targetPath = Path.Combine(LangDir, "en.json");
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                using FileStream file = File.Create(targetPath);
                stream.CopyTo(file);
                MelonLogger.Msg($"[LOC] Refreshed language template: {targetPath}");
            }
            catch (Exception ex)
            {
                // A locked or read-only file must not break startup.
                MelonLogger.Warning($"[LOC] Could not refresh en.json template: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a translation file from the UserData lang folder. Returns
        /// false with a logged reason (bad code, missing file, parse error) so
        /// the caller keeps the current language.
        /// </summary>
        internal static bool TryLoadLanguage(string code, out Dictionary<string, string> dict)
        {
            dict = null;
            if (string.IsNullOrWhiteSpace(code) ||
                code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MelonLogger.Warning($"[LOC] Invalid language code '{code}'.");
                return false;
            }

            string path = Path.Combine(LangDir, code + ".json");
            if (!File.Exists(path))
            {
                MelonLogger.Warning($"[LOC] No translation file for '{code}': {path} not found.");
                return false;
            }

            try
            {
                dict = ParseLanguageJson(File.ReadAllText(path)); // ReadAllText detects UTF-8/UTF-16 BOMs
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LOC] Failed to parse {path}: {ex.Message}. Keeping current language.");
                dict = null;
                return false;
            }
        }

        /// <summary>
        /// Maps the game's Common.Language enum value to a translation file
        /// code. Taken by int so this file needs no IL2CPP references.
        /// </summary>
        internal static string MapGameLanguage(int gameLanguage)
        {
            switch (gameLanguage)
            {
                case 0: return "ja";
                case 1: return "en";
                case 2: return "ko";
                case 3: return "zh-Hant";
                case 4: return "zh-Hans";
                case 5: return "fr";
                case 6: return "it";
                case 7: return "de";
                case 8: return "es";
                default: return null;
            }
        }

        /// <summary>
        /// Codes selectable in the settings menu: "en" (embedded) first, then
        /// every other JSON file in the lang folder, sorted by code.
        /// </summary>
        internal static List<string> AvailableCodes()
        {
            var codes = new List<string> { "en" };
            try
            {
                if (Directory.Exists(LangDir))
                {
                    var found = new List<string>();
                    foreach (string path in Directory.GetFiles(LangDir, "*.json"))
                    {
                        string code = Path.GetFileNameWithoutExtension(path);
                        if (!code.Equals("en", StringComparison.OrdinalIgnoreCase))
                            found.Add(code);
                    }
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    codes.AddRange(found);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LOC] Could not scan {LangDir}: {ex.Message}");
            }
            return codes;
        }

        /// <summary>
        /// The spoken name of a language file — its "language_name" value
        /// (e.g. "Deutsch") — without switching to it. Falls back to the code
        /// itself if the file is unreadable or lacks the key.
        /// </summary>
        internal static string PeekLanguageName(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            if (code.Equals("en", StringComparison.OrdinalIgnoreCase))
                return Loc.GetEnglish("language_name");

            string path = Path.Combine(LangDir, code + ".json");
            try
            {
                if (!File.Exists(path)) return code;
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (_nameCache.TryGetValue(code, out var cached) && cached.WriteTime == writeTime)
                    return cached.Name;

                Dictionary<string, string> dict = ParseLanguageJson(File.ReadAllText(path));
                string name = dict.TryGetValue("language_name", out string value) ? value : code;
                _nameCache[code] = (writeTime, name);
                return name;
            }
            catch (Exception)
            {
                return code; // broken file: the code is still a speakable label
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Finds the embedded en.json by suffix so the resource name never
        /// hardcodes the assembly namespace (same pattern as the world map
        /// grid extraction).
        /// </summary>
        private static string FindEmbeddedResourceName(Assembly assembly)
        {
            foreach (string name in assembly.GetManifestResourceNames())
                if (name.EndsWith("lang.en.json", StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }

        /// <summary>
        /// Parses a language JSON object into a key/value dictionary, dropping
        /// "_"-prefixed marker keys (section comments for translators) and
        /// empty values (treated as untranslated, so English wins per key).
        /// </summary>
        private static Dictionary<string, string> ParseLanguageJson(string json)
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            var dict = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> entry in raw)
            {
                if (entry.Key.StartsWith("_")) continue;
                if (string.IsNullOrEmpty(entry.Value)) continue;
                dict[entry.Key] = entry.Value;
            }
            return dict;
        }

        #endregion
    }
}
