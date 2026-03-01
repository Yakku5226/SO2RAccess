# Code Index: SpatialAudioPlayer.cs

## Top-Level Comments

Lines 9-16: XML doc summary on the class.
Plays a looping WAV file with real-time volume and stereo panning control using the Windows
waveOut API (winmm.dll). Designed for ambient spatial cues such as enemy proximity warnings.
The WAV file is loaded from disk. Independent of AudioCuePlayer (no conflict with PlaySound).

---

## Class: SpatialAudioPlayer (line 17)

`public static class` in namespace `SO2RAccess`.

---

### P/Invoke Declarations (lines 19-68)

#### Delegate

- `private delegate void WaveOutCallback(IntPtr hwo, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)` (line 21)

#### Extern Methods (DllImport winmm.dll)

- `private static extern uint waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WaveFormatEx pwfx, WaveOutCallback dwCallback, IntPtr dwInstance, uint fdwOpen)` (line 25)
- `private static extern uint waveOutClose(IntPtr hwo)` (line 29)
- `private static extern uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh)` (line 32)
- `private static extern uint waveOutReset(IntPtr hwo)` (line 35)
- `private static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh)` (line 38)
- `private static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh)` (line 41)

#### Constants

- `private const uint WAVE_MAPPER = 0xFFFFFFFF` (line 43)
- `private const uint CALLBACK_FUNCTION = 0x00030000` (line 44)
- `private const uint MMSYSERR_NOERROR = 0` (line 45)
- `private const uint WOM_DONE = 0x3BD` (line 46)
- `private const uint WHDR_DONE = 0x00000001` (line 47)

#### Struct: WaveFormatEx (line 50)

`[StructLayout(LayoutKind.Sequential)]`, `private struct`

- `public ushort wFormatTag` (line 52)
- `public ushort nChannels` (line 53)
- `public uint nSamplesPerSec` (line 54)
- `public uint nAvgBytesPerSec` (line 55)
- `public ushort nBlockAlign` (line 56)
- `public ushort wBitsPerSample` (line 57)
- `public ushort cbSize` (line 58)

#### WAVEHDR Layout Constants (lines 61-67)

Note: Manual x64 struct offsets — no C# struct used; WAVEHDR is read/written via Marshal at fixed byte offsets.

- `private const int WAVEHDR_SIZE = 48` (line 63)
- `private const int WAVEHDR_LPDATA = 0` (line 64)
- `private const int WAVEHDR_BUFFERLENGTH = 8` (line 65)
- `private const int WAVEHDR_FLAGS = 24` (line 66)

---

### Fields (lines 70-107)

- `private static bool _initialized` (line 72)
- `private static bool _playing` (line 73) — Note: declared `volatile` semantics enforced via Stop() ordering pattern, but not marked volatile here; see `_volume`/`_pan` for comparison
- `private static IntPtr _hWaveOut` (line 74)
- `private static short[] _sourceSamples` (line 77) — mono PCM samples extracted from the WAV file
- `private static uint _sourceSampleRate` (line 78)
- `private static int _readPos` (line 79) — current read position into `_sourceSamples`, advances each buffer fill
- `private const int BufferMs = 100` (line 82) — duration of each output buffer in milliseconds
- `private const int BufferCount = 2` (line 83) — number of double-buffered output buffers
- `private static IntPtr[] _bufferData` (line 84) — unmanaged byte arrays for PCM data (one per buffer)
- `private static IntPtr[] _bufferHeaders` (line 85) — unmanaged WAVEHDR structs (one per buffer)
- `private static int _bufferSampleCount` (line 86) — samples per buffer per channel
- `private static int _bufferByteSize` (line 87) — bytes per buffer (stereo 16-bit = 4 bytes per frame)
- `private static volatile float _volume` (line 90) — written by game thread, read by callback thread
- `private static volatile float _pan` (line 91) — written by game thread, read by callback thread
- `private static WaveOutCallback _callbackDelegate` (line 94) — held as a field to prevent GC collection of the delegate
- `private static readonly object _lock = new object()` (line 97)
- `public static float UserVolume { get; set; } = 1.0f` (line 103) — user-adjustable multiplier (0.0-1.0) on top of distance-based volume; intended for a future settings menu
- `public static bool IsPlaying => _playing` (line 106) — read-only property

---

### Methods

#### Public API (lines 110-244)

- `public static bool Initialize(string wavFilePath)` (line 116)
  Note: Calls `LoadWav` internally, which also calls `AllocateBuffers`. Sets `_initialized = true` on success. No-op and returns true if already initialized.

- `public static void Start()` (line 145)
  Note: Opens the waveOut device, fills both buffers, and submits them to begin double-buffered looped playback. No-op if already playing or not initialized.

- `public static void Stop()` (line 182)
  Note: CRITICAL ordering — sets `_playing = false` BEFORE acquiring `_lock`, then acquires and immediately releases the lock to drain any in-progress callback, then calls `waveOutReset` and closes the device. This ordering prevents deadlock with `WaveOutCallbackHandler`.

- `public static void SetVolumePan(float volume, float pan)` (line 222)
  Note: Clamps inputs. Does not immediately affect the currently-playing buffer; takes effect on the next `FillBuffer` call (within ~100ms).

- `public static void Shutdown()` (line 231)
  Note: Calls `Stop()` if playing, then frees unmanaged buffer memory, nulls source samples and the callback delegate, and resets `_initialized`.

#### WAV Loading (lines 246-370)

- `private static bool LoadWav(string path)` (line 252)
  Note: Parses a PCM WAV file by scanning for `fmt ` and `data` chunks (handles extra chunks between them). Supports 8-bit and 16-bit, mono and stereo input; always produces mono `short[]` in `_sourceSamples`. Stereo is averaged to mono. 8-bit unsigned is converted to 16-bit signed. Calls `AllocateBuffers()` on success.

#### Buffer Management (lines 372-452)

- `private static void AllocateBuffers()` (line 377)
  Note: Allocates unmanaged memory (via `Marshal.AllocHGlobal`) for `BufferCount` PCM data arrays and `BufferCount` WAVEHDR structs. Buffer size is derived from `_sourceSampleRate * BufferMs / 1000` samples, times 4 bytes (stereo 16-bit).

- `private static void FreeBuffers()` (line 400)
  Note: Frees all unmanaged memory allocated by `AllocateBuffers`. Safe to call if buffers are null.

- `private static void FillBuffer(int bufferIndex)` (line 423)
  Note: Reads from `_sourceSamples` starting at `_readPos`, applies volume and pan (simple linear pan law: left = 1-p, right = 1+p, clamped to 1.0), writes interleaved stereo 16-bit PCM into unmanaged `_bufferData[bufferIndex]`, then advances `_readPos` (wraps for seamless looping). Reads `_volume`, `_pan`, and `UserVolume` at call time.

#### WaveOut Device (lines 454-559)

- `private static bool OpenDevice()` (line 459)
  Note: Stores the callback delegate in `_callbackDelegate` before calling `waveOutOpen` to keep it alive. Configures stereo 16-bit output at the source sample rate. Uses `WAVE_MAPPER` to let Windows choose the device.

- `private static void SubmitBuffer(int bufferIndex)` (line 489)
  Note: Writes WAVEHDR fields via Marshal at manual offsets, calls `waveOutPrepareHeader` then `waveOutWrite` to queue the buffer for playback.

- `private static void WaveOutCallbackHandler(IntPtr hwo, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)` (line 508)
  Note: Runs on a Windows thread pool thread — must not call Unity APIs. On `WOM_DONE`, checks `_playing` (volatile) before acquiring `_lock` to avoid deadlock with `Stop()`. Identifies the completed buffer by pointer identity against `_bufferHeaders`; falls back to checking `WHDR_DONE` flag if pointer match fails (handles Windows copying the header by value). Unprepares, refills, and resubmits the buffer for seamless looping. Swallows all exceptions to prevent crash.
