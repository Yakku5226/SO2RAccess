using System;
using System.Text.RegularExpressions;

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
    }
}
