# ScreenReader.cs (187 lines)

Wrapper for the Tolk screen reader library. Announces text via NVDA, JAWS, or other screen readers. Requires Tolk.dll and nvdaControllerClient64.dll in the game folder.

namespace: SO2RAccess (line 6)
usings (non-System / notable only): MelonLoader, UnityEngine

## static class ScreenReader (line 14)
Wrapper for the Tolk screen reader library; announces text via NVDA, JAWS, or other screen readers.

fields/properties (declaration order):
- Tolk_Load : extern void (line 19)  — P/Invoke: Tolk.dll
- Tolk_Unload : extern void (line 22)  — P/Invoke: Tolk.dll
- Tolk_IsLoaded : extern bool (line 25)  — P/Invoke: Tolk.dll
- Tolk_HasSpeech : extern bool (line 28)  — P/Invoke: Tolk.dll
- Tolk_Output : extern bool (line 31)  — P/Invoke: Tolk.dll, CharSet.Unicode; params: text, interrupt
- Tolk_Silence : extern bool (line 34)  — P/Invoke: Tolk.dll
- Tolk_DetectScreenReader : extern IntPtr (line 37)  — P/Invoke: Tolk.dll, CharSet.Unicode; returns pointer to screen reader name string
- _available : bool (line 43)
- _initialized : bool (line 44)
- _lastMessage : string (line 47)  — most recently spoken message text
- _lastMessageTime : float (line 50)  — Time.time when last message was spoken
- IsAvailable : bool (line 182)  — property; returns _available

methods (declaration order):
- static void Initialize() (line 59)
  - note: Calls Tolk_Load, checks IsLoaded+HasSpeech, detects and logs screen reader name. Catches DllNotFoundException separately. Idempotent.
- static void Say(string text, bool interrupt = true) (line 101)
  - note: Main announcement path. Also calls DebugLogger.LogScreenReader and records _lastMessage/_lastMessageTime regardless of _available.
- static void SayQueued(string text) (line 127)
  - note: Calls Say(text, false) — waits for current speech to finish.
- static string GetRecentMessage(float withinSeconds) (line 138)
  - note: Returns _lastMessage if spoken within withinSeconds of now; null otherwise. Used by callers to replay interrupted speech.
- static void Stop() (line 148)
  - note: Calls Tolk_Silence to interrupt current speech.
- static void Shutdown() (line 165)
  - note: Calls Tolk_Unload; resets _initialized and _available. Idempotent.
