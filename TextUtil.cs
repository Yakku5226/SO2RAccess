using System;
using System.Collections.Generic;
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
        /// Resolves an item's display name from its numeric itemID via ParameterManager
        /// and TextManager. Returns null when the item parameter or its name key is
        /// missing. Falls back to parsing the raw key (e.g. "ITEM_BLUEBERRY" →
        /// "Blueberry") when TextManager can't resolve it (some keys only resolve in
        /// native code).
        /// </summary>
        public static string ResolveItemName(int itemID)
        {
            try
            {
                var param = ParameterManager.Instance?.GetItemParameter(itemID);
                if (param == null) return null;

                string nameID = param.itemNameID;
                if (string.IsNullOrEmpty(nameID)) return null;

                var tm = TextManager.Instance;
                if (tm != null)
                {
                    string resolved = tm.GetMessage(nameID, TextManager.MessageType.Item);
                    if (!string.IsNullOrEmpty(resolved)) return StripTags(resolved);
                }

                // Fallback: parse key (e.g. "ITEM_BLUEBERRY" → "Blueberry").
                string fallback = nameID;
                if (fallback.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase))
                    fallback = fallback.Substring(5);
                fallback = fallback.Replace('_', ' ');
                if (fallback.Length > 0)
                    fallback = char.ToUpper(fallback[0]) + fallback.Substring(1).ToLower();
                return fallback;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"TextUtil.ResolveItemName({itemID}) error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves a factor's (talent / passive ability) display name from its
        /// <see cref="FactorID"/>. Factors are the game's internal representation of
        /// talents such as "Nimble Fingers"; reward popups carry them as a factorID
        /// with no plain name. Chain: GetFactorParameter(id).messageID →
        /// GetFactorMessage(messageID) → display name. Returns null when the id is
        /// INVALID or no message exists.
        /// </summary>
        public static string ResolveFactorName(FactorID factorID)
        {
            try
            {
                if (factorID == FactorID.INVALID) return null;

                var pm = ParameterManager.Instance;
                if (pm == null) return null;

                var fp = pm.GetFactorParameter(factorID);
                if (fp == null) return null;

                string messageID = fp.messageID;
                if (string.IsNullOrEmpty(messageID)) return null;

                string name = pm.GetFactorMessage(messageID);
                return string.IsNullOrEmpty(name) ? null : StripTags(name);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"TextUtil.ResolveFactorName({factorID}) error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Joins sentence fragments into one readout separated by ". ", collapsing a
        /// trailing period already on a fragment so the result never contains a double
        /// period (e.g. "A straight blade." + "None" → "A straight blade. None", not
        /// "A straight blade.. None"). Null/whitespace fragments are skipped. The
        /// returned string carries no trailing punctuation; callers append their own.
        /// </summary>
        public static string JoinSentences(IEnumerable<string> fragments)
        {
            if (fragments == null) return "";
            var cleaned = new List<string>();
            foreach (var fragment in fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment)) continue;
                string p = fragment.Trim();
                if (p.EndsWith(".")) p = p.Substring(0, p.Length - 1).TrimEnd();
                if (p.Length > 0) cleaned.Add(p);
            }
            return string.Join(". ", cleaned);
        }

        /// <summary>
        /// Appends the standard ". N of M." list-position suffix to a screen-reader
        /// message. <paramref name="index"/> is 0-based, so index 2 of count 5
        /// produces ". 3 of 5.". Any trailing whitespace and a single sentence period
        /// already on the buffer are collapsed first, so a preceding segment that ends
        /// in "." doesn't produce a double period before the suffix.
        /// </summary>
        public static void AppendPosition(StringBuilder sb, int index, int count)
        {
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            if (sb.Length > 0 && sb[sb.Length - 1] == '.') sb.Length--;
            sb.Append(". ").Append(index + 1).Append(" of ").Append(count).Append('.');
        }
    }
}
