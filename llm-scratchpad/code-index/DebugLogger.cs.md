# DebugLogger.cs (103 lines)

File-level summary: Centralized debug logging with categories. All output suppressed unless Main.DebugMode is true (toggle F12).
Categories: [SR] screen reader, [INPUT] key presses, [STATE] state changes, [HANDLER] handler decisions, [GAME] game values.
namespace: SO2RAccess (line 3)
usings: MelonLoader

## static class DebugLogger (line 16)
Centralized debug logger; zero overhead when DebugMode is off.

methods (declaration order):
- static void Log(LogCategory, string) (line 21)
- static void Log(LogCategory, string, string) (line 29)  — Overload with source label
- static void LogScreenReader(string) (line 37)  — Called automatically by ScreenReader.Say()
- static void LogInput(string, string) (line 44)
- static void LogState(string) (line 56)
- static void LogGameValue(string, object) (line 63)
- static string GetPrefix(LogCategory) (line 69)  — Returns bracket prefix string for the given category

## enum LogCategory (line 86)
Categories for debug logging.

members:
- ScreenReader (line 88)
- Input (line 91)
- State (line 94)
- Handler (line 97)
- Game (line 100)
