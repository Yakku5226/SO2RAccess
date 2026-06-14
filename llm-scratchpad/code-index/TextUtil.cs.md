# TextUtil.cs (52 lines)

Shared text-cleaning utilities for stripping rich-text markup from game strings. Used by any handler that needs to announce game text via the screen reader.
namespace: SO2RAccess (line 3)
usings (non-System / notable only): System.Text.RegularExpressions

## static class TextUtil (line 11)
Shared text-cleaning utilities for stripping rich-text markup from game strings.

fields/properties (declaration order):
- _spriteNameExtractor : Regex (line 13)  — compiled; extracts name from `<sprite name=X>` tags (e.g. "R1")
- _tagStripper : Regex (line 16)  — compiled; strips any remaining `<...>` tags

methods (declaration order):

- static string StripTags(string) (line 23)
  - note: first replaces `<sprite name=X>` with just X (via _spriteNameExtractor), then strips all remaining tags (via _tagStripper), then trims

- static string ParseCharaNameID(string) (line 37)
  - note: strips "CHARA_" or "MON_" prefix then title-cases; e.g. "CHARA_LIZARDAXE" → "Lizardaxe"
