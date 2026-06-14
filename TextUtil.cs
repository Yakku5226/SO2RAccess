using System;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppGame;

namespace SO2RAccess
{
    /// <summary>
    /// Shared text-cleaning utilities for stripping rich-text markup from game strings.
    /// Used by any handler that needs to announce game text via the screen reader.
    /// </summary>
    public static class TextUtil
    {
        /// <summary>Extracts the name from sprite tags (e.g. "&lt;sprite name=R1&gt;" → "R1").</summary>
        private static readonly Regex _spriteNameExtractor = new Regex(
            @"<sprite\s+name\s*=\s*([^>]+?)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>Strips any remaining rich text tags from game strings.</summary>
        private static readonly Regex _tagStripper = new Regex("<[^>]+>", RegexOptions.Compiled);
        /// <summary>Controller-type prefixes stripped from sprite names for readability.</summary>
        private static readonly string[] _spritePrefixes =
            { "PS5_", "PS4_", "Xbox_", "Switch_", "PC_", "Gamepad_" };

        /// <summary>
        /// Removes a controller-type prefix from a sprite name so the screen
        /// reader hears the plain button name (e.g. "PS4_Cross" → "Cross").
        /// </summary>
        public static string StripControllerPrefix(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return spriteName;
            foreach (var prefix in _spritePrefixes)
            {
                if (spriteName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return spriteName.Substring(prefix.Length);
            }
            return spriteName;
        }

        /// <summary>
        /// Cleans rich text from a game string. Sprite tags have their name
        /// extracted (e.g. "&lt;sprite name=R1&gt;" → "R1"), then any remaining
        /// tags are stripped.
        /// </summary>
        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = _spriteNameExtractor.Replace(text, "$1");
            text = _tagStripper.Replace(text, "");
            return text.Trim();
        }

        /// <summary>
        /// Parses a charaNameID key into a readable enemy name.
        /// e.g. "CHARA_LIZARDAXE" → "Lizardaxe", "MON_VOPALBUNNY" → "Vopalbunny".
        /// Strips the "CHARA_" or "MON_" prefix and converts to title case.
        /// </summary>
        public static string ParseCharaNameID(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            string name = key;
            if (name.StartsWith("CHARA_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(6);
            else if (name.StartsWith("MON_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);

            if (string.IsNullOrEmpty(name)) return key;

            return char.ToUpper(name[0]) + name.Substring(1).ToLower();
        }

        /// <summary>
        /// Resolves a charaNameID key to a display name: tries
        /// TextManager.GetMessage for each given message type in order (defaults
        /// to System), falling back to <see cref="ParseCharaNameID"/> when the
        /// game text is unavailable (e.g. in battle, where some keys don't
        /// resolve natively). Returns "" for a null/empty key.
        /// </summary>
        public static string ResolveCharaNameKey(
            string nameKey, params TextManager.MessageType[] types)
        {
            if (string.IsNullOrEmpty(nameKey)) return "";
            if (types == null || types.Length == 0)
                types = new[] { TextManager.MessageType.System };
            try
            {
                var tm = TextManager.Instance;
                if (tm != null)
                {
                    foreach (var t in types)
                    {
                        string resolved = tm.GetMessage(nameKey, t);
                        if (!string.IsNullOrEmpty(resolved))
                            return StripTags(resolved);
                    }
                }
            }
            catch { /* IL2CPP/native unavailable — fall back to key parsing */ }
            return ParseCharaNameID(nameKey);
        }

        /// <summary>
        /// Appends the standard ". N of M." list-position suffix to a screen-reader
        /// message. <paramref name="index"/> is 0-based, so index 2 of count 5
        /// produces ". 3 of 5.".
        /// </summary>
        public static void AppendPosition(StringBuilder sb, int index, int count)
        {
            sb.Append(". ").Append(index + 1).Append(" of ").Append(count).Append('.');
        }
    }
}
