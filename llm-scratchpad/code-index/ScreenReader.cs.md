# Code Index: ScreenReader.cs

## Top-level comments

- Namespace: `SO2RAccess`
- File-level XML doc (lines 8-13): Wrapper for the Tolk screen reader library. Announces
  text via NVDA, JAWS, or other screen readers. Requires `Tolk.dll` and
  `nvdaControllerClient64.dll` in the game folder.

---

## Class: ScreenReader (line 14)

`public static class ScreenReader`

### Native P/Invoke imports (lines 18-38)

- `private static extern void Tolk_Load()` (line 19)
- `private static extern void Tolk_Unload()` (line 22)
- `private static extern bool Tolk_IsLoaded()` (line 25)
- `private static extern bool Tolk_HasSpeech()` (line 28)
- `private static extern bool Tolk_Output(string text, bool interrupt)` (line 31)
- `private static extern bool Tolk_Silence()` (line 34)
- `private static extern IntPtr Tolk_DetectScreenReader()` (line 37)
  Note: Returns a pointer to a Unicode string; caller must marshal via
  `Marshal.PtrToStringUni`.

### Fields

- `private static bool _available` (line 43)
  Note: True only when Tolk loaded AND a speech-capable screen reader was detected.
- `private static bool _initialized` (line 44)
  Note: Guards against calling `Initialize()` more than once.
- `private static string _lastMessage` (line 47)
- `private static float _lastMessageTime` (line 50)
  Note: Stores `Time.time` of the last `Say()` call; initialized to -1 (never spoken).

### Properties

- `public static bool IsAvailable` (line 179) — read-only, returns `_available`

### Methods

- `public static void Initialize()` (line 59)
  Note: Loads Tolk, detects a screen reader, and sets `_available`. Catches
  `DllNotFoundException` separately to give a clear missing-DLL error. Safe to call
  multiple times; returns immediately if already initialized.

- `public static void Say(string text, bool interrupt = true)` (line 101)
  Note: Primary announcement method. Always logs via `DebugLogger.LogScreenReader` even
  when no screen reader is available (so debug output still shows announced text). Updates
  `_lastMessage` / `_lastMessageTime` before the availability guard, so `GetRecentMessage`
  works even without a real screen reader.

- `public static void SayQueued(string text)` (line 127)
  Note: Thin wrapper around `Say(text, false)`. Passes `interrupt = false` so the new
  text is appended after current speech rather than cutting it off.

- `public static string GetRecentMessage(float withinSeconds)` (line 138)
  Note: Returns the last spoken string if it was said within `withinSeconds` seconds of
  now, otherwise null. Used by callers that need to re-announce text that may have been
  interrupted by a higher-priority message.

- `public static void Stop()` (line 148)
  Note: Calls `Tolk_Silence()`. Swallows all exceptions silently (empty catch).

- `public static void Shutdown()` (line 162)
  Note: Calls `Tolk_Unload()` and resets both `_initialized` and `_available` to false.
  Swallows all exceptions silently (empty catch). Safe to call if never initialized.
