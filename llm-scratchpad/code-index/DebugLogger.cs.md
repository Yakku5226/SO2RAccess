# Code Index: DebugLogger.cs

## Top-Level Comments

Lines 5-15: XML doc comment on `DebugLogger` class describing its purpose and the five
log categories it supports:
- `[SR]` — What the screen reader announces
- `[INPUT]` — Key presses and input events
- `[STATE]` — Screen and menu state changes
- `[HANDLER]` — Handler decisions and actions
- `[GAME]` — Values read from the game

All output is suppressed unless `Main.DebugMode` is true (toggled with F12).

---

## Class: DebugLogger (line 16)

`public static class` in namespace `SO2RAccess`.

### Fields
(none)

### Methods

- `public static void Log(LogCategory category, string message)` (line 21)
  Logs a categorized message without a source label.

- `public static void Log(LogCategory category, string source, string message)` (line 30)
  Overload that prepends a `[source]` label between the category prefix and the message.

- `public static void LogScreenReader(string text)` (line 39)
  Convenience method hardcoded to the `[SR]` prefix. Called automatically by `ScreenReader.Say()`.
  Note: Bypasses `GetPrefix()` entirely — uses a literal `"[SR]"` string.

- `public static void LogInput(string keyName, string action = null)` (line 48)
  Logs a key press. If `action` is provided, formats as `"keyName -> action"`; otherwise logs just the key name.

- `public static void LogState(string description)` (line 58)
  Logs a state change (screen open, mode switch, etc.) under the `[STATE]` prefix.

- `public static void LogGameValue(string name, object value)` (line 67)
  Logs a name/value pair read from the game, formatted as `"name = value"`.

- `private static string GetPrefix(LogCategory category)` (line 73)
  Maps a `LogCategory` enum value to its bracket-prefixed string (e.g. `Handler` → `"[HANDLER]"`).
  Returns `"[DEBUG]"` for any unrecognized value.

---

## Enum: LogCategory (line 90)

`public enum` in namespace `SO2RAccess`.

### Values

- `ScreenReader` (line 93) — What the screen reader announces
- `Input` (line 96) — Key presses and input events
- `State` (line 99) — Screen and menu state changes
- `Handler` (line 102) — Handler decisions and processing
- `Game` (line 105) — Values read from the game
