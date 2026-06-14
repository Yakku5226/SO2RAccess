# AudioCuePlayer.cs (744 lines)

Plays short audio cues for time-critical gameplay feedback using Windows native audio (winmm.dll) to bypass Unity IL2CPP GC issues. Supports dodge warning, jump-prompt, save, private action, bonus gauge fill, and bonus gauge break cues. File-based WAV only (no synthesized audio). Volume-adjusted copies are pinned in unmanaged memory via Marshal.AllocHGlobal so the GC cannot relocate them during async playback.
namespace: SO2RAccess (line 6)
usings (non-System / notable only): MelonLoader, System.Runtime.InteropServices

## class AudioCuePlayer (line 13) [public static]
Plays short audio cues using winmm.dll P/Invoke.

P/Invoke signatures:
- [DllImport("winmm.dll")] static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound) (line 16)
- [DllImport("winmm.dll", EntryPoint = "PlaySound")] static extern bool PlaySoundPtr(IntPtr pszSound, IntPtr hmod, uint fdwSound) (line 21)  — overload taking IntPtr for pinned unmanaged buffers

fields/properties (declaration order):
- SND_MEMORY : const uint = 0x0004 (line 23)
- SND_ASYNC : const uint = 0x0001 (line 24)
- SND_NODEFAULT : const uint = 0x0002 (line 25)
- _initialized : static bool (line 27)
- _dodgeSoundRawWav : static byte[] (line 30)
- _dodgeSoundDataOffset : static int (line 31)
- _dodgeSoundDataLength : static int (line 32)
- _dodgeSoundBitsPerSample : static short (line 33)
- _dodgeSoundPtr : static IntPtr (line 34)  — pinned unmanaged copy used for async playback
- _dodgeSoundPtrSize : static int (line 35)
- _dodgeSoundCachedVolume : static float = -1f (line 36)  — last built volume; triggers rebuild when changed
- _dodgeSoundLoaded : static bool (line 37)
- _paSoundRawWav : static byte[] (line 40)
- _paSoundDataOffset : static int (line 41)
- _paSoundDataLength : static int (line 42)
- _paSoundBitsPerSample : static short (line 43)
- _paSoundPtr : static IntPtr (line 44)
- _paSoundPtrSize : static int (line 45)
- _paSoundCachedVolume : static float = -1f (line 46)
- _paSoundLoaded : static bool (line 47)
- _gaugeFillSoundRawWav : static byte[] (line 50)
- _gaugeFillSoundDataOffset : static int (line 51)
- _gaugeFillSoundDataLength : static int (line 52)
- _gaugeFillSoundBitsPerSample : static short (line 53)
- _gaugeFillSoundPtr : static IntPtr (line 54)
- _gaugeFillSoundPtrSize : static int (line 55)
- _gaugeFillSoundCachedVolume : static float = -1f (line 56)
- _gaugeFillSoundLoaded : static bool (line 57)
- _gaugeBreakSoundRawWav : static byte[] (line 60)
- _gaugeBreakSoundDataOffset : static int (line 61)
- _gaugeBreakSoundDataLength : static int (line 62)
- _gaugeBreakSoundBitsPerSample : static short (line 63)
- _gaugeBreakSoundPtr : static IntPtr (line 64)
- _gaugeBreakSoundPtrSize : static int (line 65)
- _gaugeBreakSoundCachedVolume : static float = -1f (line 66)
- _gaugeBreakSoundLoaded : static bool (line 67)
- _jumpSoundRawWav : static byte[] (line 71)
- _jumpSoundDataOffset : static int (line 72)
- _jumpSoundDataLength : static int (line 73)
- _jumpSoundBitsPerSample : static short (line 74)
- _jumpSoundPtr : static IntPtr (line 75)
- _jumpSoundPtrSize : static int (line 76)
- _jumpSoundCachedVolume : static float = -1f (line 77)
- _jumpSoundLoaded : static bool (line 78)
- _saveSoundRawWav : static byte[] (line 86)
- _saveSoundDataOffset : static int (line 87)
- _saveSoundDataLength : static int (line 88)
- _saveSoundBitsPerSample : static short (line 89)
- _saveSoundPtr : static IntPtr (line 90)
- _saveSoundPtrSize : static int (line 91)
- _saveSoundCachedVolume : static float = -1f (line 92)
- _saveSoundLoaded : static bool (line 93)

methods (declaration order):
- void Initialize() (line 98)  [public static]
- void LoadDodgeSound(string path) (line 108)  [public static]
- bool IsDodgeSoundLoaded (line 141)  [public static property]
- void PlayDodgeWarningCue() (line 147)  [public static]
  - note: rebuilds unmanaged WAV buffer only when ModSettings.DodgeSoundVolume differs from cached; plays via PlaySoundPtr with SND_MEMORY|SND_ASYNC|SND_NODEFAULT.
- void LoadJumpSound(string path) (line 189)  [public static]
- bool IsJumpSoundLoaded (line 221)  [public static property]
- void PlayJumpCue() (line 226)  [public static]
  - note: same pattern as PlayDodgeWarningCue; reads ModSettings.JumpPromptSoundVolume; skips if volume < 0.001.
- void LoadSaveSound(string path) (line 268)  [public static]
- void PlaySaveCue() (line 303)  [public static]
  - note: same volume-cache-rebuild pattern; reads ModSettings.SaveSoundVolume; logs PlaySound return value via DebugLogger.
- bool IsSaveSoundLoaded (line 348)  [public static property]
- void LoadPrivateActionSound(string path) (line 353)  [public static]
- bool IsPrivateActionSoundLoaded (line 386)  [public static property]
- void PlayPrivateActionCue() (line 391)  [public static]
  - note: reads ModSettings.PrivateActionSoundVolume; skips if volume < 0.001.
- void LoadGaugeFillSound(string path) (line 433)  [public static]
- bool IsGaugeFillSoundLoaded (line 466)  [public static property]
- void PlayGaugeFillCue() (line 471)  [public static]
  - note: reads ModSettings.BonusGaugeSoundVolume; skips if volume < 0.01.
- void LoadGaugeBreakSound(string path) (line 514)  [public static]
  - note: missing file is not a warning (break sound is optional) — uses DebugLogger instead of MelonLogger.Warning.
- bool IsGaugeBreakSoundLoaded (line 548)  [public static property]
- void PlayGaugeBreakCue(float volume) (line 554)  [public static]
  - note: takes explicit volume parameter (not from ModSettings); provision for future use.
- void Shutdown() (line 593)  [public static]
  - note: cancels in-flight async sound via PlaySound(null,...), then frees all unmanaged Marshal.AllocHGlobal buffers and nulls all raw WAV arrays.
- bool TryParseWav(string path, out byte[] fileBytes, out int dataOffset, out int dataLength, out short bitsPerSample) (line 656)  [private static]
  - note: validates RIFF/WAVE header; walks chunks to find "fmt " (validates PCM format=1, reads bitsPerSample) and "data" (records offset+length). Returns false and logs warning on any failure.
- void ScalePcmSamples(byte[] wav, int dataOffset, int dataLength, short bitsPerSample, float volume) (line 716)  [private static]
  - note: in-place PCM amplitude scaling; supports 16-bit (little-endian signed) and 8-bit (unsigned, center=128) formats. Clamps to avoid clipping.
