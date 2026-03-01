# Code Index: Loc.cs

## Top-level Comments

- XML doc on the class (lines 5-12): explains that all screen reader strings must go through `Loc.Get()`.
  Provides usage examples for `Loc.Get("key")` and `Loc.Get("key", arg1, arg2)`.

---

## Class: Loc (line 13)

`public static class` in namespace `SO2RAccess`.

### Fields

- `private static bool _initialized` (line 17)
- `private static readonly Dictionary<string, string> _strings` (line 18)

### Methods

- `public static void Initialize()` (line 27)
  Note: Guards against double-initialization with `_initialized`. Calls `InitializeStrings()` once.

- `public static string Get(string key)` (line 38)
  Note: Returns the localized string for `key`. Falls back to returning `key` itself when the key is missing, so missing strings are visible rather than silent.

- `public static string Get(string key, params object[] args)` (line 47)
  Note: Overload of `Get` that fills `{0}`, `{1}`, ... placeholders via `string.Format`. On format error, returns the unformatted template string rather than throwing.

- `private static void Add(string key, string english)` (line 64)
  Note: Thin wrapper — writes one key/value pair into `_strings`. Exists to keep `InitializeStrings` readable.

- `private static void InitializeStrings()` (line 73)
  Note: Defines every localized string used by the mod. Convention for key names is `[handler]_[action]` (e.g. `"inventory_opened"`, `"battle_hp"`). String categories (by comment block):
  - General (mod load, debug toggle, help text) — lines 75-79
  - Title menu — lines 81-84
  - Config menu — lines 86-90
  - Keyboard binding menu — lines 92-95
  - Gamepad binding menu — lines 97-100
  - Protagonist selection screen — lines 102-106
  - New game settings screen — lines 108-119
  - Load game menu — lines 121-125
  - Dialogue / NPC text boxes — lines 127-129
  - Tutorial boxes — lines 131-134
  - Dialog and description popups — lines 136-141
  - Field navigation list — lines 143-179
  - Navigation enemies — lines 181-188
  - Camp menu root — lines 190-193
  - Camp item sub-screen — lines 195-198
  - Camp status sub-screen — lines 200-209
  - Camp equip sub-screen — lines 212-225
  - Camp battle skill leveling sub-screen — lines 227-232
  - Camp battle skill assignment sub-screen — lines 234-240
  - Camp formation sub-screen — lines 242-245
  - Camp skills sub-screen — lines 247-251
  - Camp party formation sub-screen — lines 253-255
  - Camp assist formation sub-screen — lines 257-262
  - Camp tactics sub-screen — lines 264-268
  - Save game screen — lines 270-271
  - Shop menu — lines 273-280
  - Item acquisition popups — lines 282-284
  - Battle results — lines 286-292
  - Battle counter cue — lines 294-295
  - Enemy proximity audio — lines 297-298
  - Location discovery notifications — lines 300-302
  - Reward notifications — lines 304-310
  - Game over menu — lines 312-314
  - Placeholder comments for future strings — lines 316-317
