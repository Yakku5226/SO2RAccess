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
    }
}
