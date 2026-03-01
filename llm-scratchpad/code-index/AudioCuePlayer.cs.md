# AudioCuePlayer.cs

## Top-level comments
- Plays short audio cues for time-critical gameplay feedback
- Uses Windows native audio (winmm.dll) to bypass Unity IL2CPP garbage collection issues
- Generates WAV data in memory at startup — no external files needed

## Namespace: SO2RAccess

---

## Class: AudioCuePlayer (line 13)
Static class.

### P/Invoke
- private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound) (line 16)
  Note: Imported from winmm.dll. Plays a WAV from a byte array in memory.

### Constants
- private const uint SND_MEMORY = 0x0004 (line 18)
- private const uint SND_ASYNC = 0x0001 (line 19)
- private const uint SND_NODEFAULT = 0x0002 (line 20)
- private const int SampleRate = 44100 (line 25)
- private const float DodgeWarningFrequency = 600f (line 26)
- private const float DodgeWarningDuration = 0.15f (line 27)
- private const float DodgeWarningVolume = 0.8f (line 28)

### Fields
- private static byte[] _dodgeWarningWav (line 22)
  Note: Pre-generated WAV byte array for the dodge warning cue, built at Initialize().
- private static bool _initialized (line 23)

### Methods
- public static void Initialize() (line 33)
  Note: Generates _dodgeWarningWav once at startup. Guards against double-init. Logs error on failure.

- public static void PlayDodgeWarningCue() (line 52)
  Note: Fires the 600 Hz, 150 ms sine-wave cue when an incoming attack targets the player. Guards on _initialized and non-null wav data.

- public static void Shutdown() (line 70)
  Note: Nulls out wav data and resets _initialized flag. Does not call PlaySound stop — cue is async and short-lived.

- private static byte[] GenerateWav(float frequency, float duration, float volume) (line 79)
  Note: Builds a complete RIFF/WAV byte array in memory (16-bit mono PCM) with a sine wave at the given frequency. Applies a 20% fade-out at the end of the sample to prevent an audible click.
