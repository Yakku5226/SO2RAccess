# SpatialAudioPlayer.cs (561 lines)

Plays a looping WAV file with real-time volume and stereo panning control.
Uses the Windows waveOut API (winmm.dll) for independent audio streaming that doesn't
conflict with AudioCuePlayer's PlaySound calls. Designed for ambient spatial cues
(e.g. enemy proximity warnings). Double-buffered stereo output; waveOutReset deadlock
avoided by setting _playing=false BEFORE acquiring _lock.
namespace: SO2RAccess (line 7)
usings (non-System / notable only): MelonLoader

## static class SpatialAudioPlayer (line 17)
Plays a looping WAV file with real-time volume and stereo panning via winmm.dll waveOut.

### P/Invoke signatures (lines 19-67)
- delegate void WaveOutCallback(IntPtr hwo, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2) (line 21)
- [DllImport("winmm.dll")] uint waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WaveFormatEx pwfx, WaveOutCallback dwCallback, IntPtr dwInstance, uint fdwOpen) (line 25)
- [DllImport("winmm.dll")] uint waveOutClose(IntPtr hwo) (line 29)
- [DllImport("winmm.dll")] uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh) (line 32)
- [DllImport("winmm.dll")] uint waveOutReset(IntPtr hwo) (line 35)
- [DllImport("winmm.dll")] uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh) (line 38)
- [DllImport("winmm.dll")] uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh) (line 41)

### struct WaveFormatEx (line 50) [StructLayout(LayoutKind.Sequential)]
- wFormatTag : ushort
- nChannels : ushort
- nSamplesPerSec : uint
- nAvgBytesPerSec : uint
- nBlockAlign : ushort
- wBitsPerSample : ushort
- cbSize : ushort

### WAVEHDR constants (lines 63-66)
- WAVEHDR_SIZE = 48  — x64 layout: lpData(0,8) dwBufferLength(8,4) dwBytesRecorded(12,4) dwUser(16,8) dwFlags(24,4) dwLoops(28,4) lpNext(32,8) reserved(40,8)
- WAVEHDR_LPDATA = 0
- WAVEHDR_BUFFERLENGTH = 8
- WAVEHDR_FLAGS = 24

fields/properties (declaration order):
- _initialized : static bool (line 72)
- _playing : static bool (line 73)
- _hWaveOut : static IntPtr (line 74)
- _sourceSamples : static short[] (line 77)  — mono PCM samples loaded from WAV
- _sourceSampleRate : static uint (line 78)
- _readPos : static int (line 79)  — current read position into _sourceSamples for looping
- BufferMs : const int = 100 (line 82)  — buffer duration in milliseconds
- BufferCount : const int = 2 (line 83)  — double-buffered
- _bufferData : static IntPtr[] (line 84)  — unmanaged byte arrays for PCM data
- _bufferHeaders : static IntPtr[] (line 85)  — unmanaged WAVEHDR structs
- _bufferSampleCount : static int (line 86)  — samples per buffer per channel
- _bufferByteSize : static int (line 87)  — bytes per buffer (stereo 16-bit = sampleCount * 4)
- _volume : static volatile float (line 90)  — distance-based volume; updated from game thread
- _pan : static volatile float (line 91)  — stereo pan -1.0 to +1.0; updated from game thread
- _callbackDelegate : static WaveOutCallback (line 94)  — held to prevent GC collection
- _lock : static readonly object (line 97)
- UserVolume : static float { get; set; } = 1.0f (line 103)  — user-adjustable multiplier on top of distance volume
- IsPlaying : static bool { get } => _playing (line 106)

methods (declaration order):
- bool Initialize(string wavFilePath) (line 116)
  - note: loads WAV from disk, calls LoadWav then sets _initialized; returns false if file missing or invalid
- void Start() (line 145)
  - note: opens waveOut device, sets _readPos=0, fills and submits both buffers to begin double-buffered playback
- void Stop() (line 182)
  - note: CRITICAL ordering — sets _playing=false BEFORE acquiring _lock to prevent deadlock with WaveOutCallbackHandler; calls waveOutReset (triggers WOM_DONE callbacks that see _playing=false and exit), unprepares headers, closes device
- void SetVolumePan(float volume, float pan) (line 222)
  - note: called every frame by EnemyProximityHandler; updates volatile _volume and _pan; values clamped to valid ranges
- void Shutdown() (line 231)
  - note: stops if playing, frees unmanaged buffers, nulls source samples and callback delegate
- bool LoadWav(string path) (line 252)
  - note: parses RIFF/WAVE by scanning chunks (handles extra chunks); supports 8-bit and 16-bit, mono and stereo input; stereo averaged to mono; calls AllocateBuffers on success
- void AllocateBuffers() (line 377)
  - note: allocates unmanaged HGlobal memory for _bufferData and _bufferHeaders arrays; zeros header memory
- void FreeBuffers() (line 400)
  - note: Marshal.FreeHGlobal on all _bufferData and _bufferHeaders entries
- void FillBuffer(int bufferIndex) (line 423)
  - note: reads mono source samples, applies volume*UserVolume and pan to produce stereo L/R pairs; writes 16-bit samples to unmanaged buffer; advances _readPos with wraparound
- bool OpenDevice() (line 459)
  - note: sets up WaveFormatEx for stereo 16-bit at source sample rate; calls waveOutOpen with CALLBACK_FUNCTION; stores _callbackDelegate to prevent GC
- void SubmitBuffer(int bufferIndex) (line 489)
  - note: writes WAVEHDR fields into unmanaged struct; calls waveOutPrepareHeader then waveOutWrite
- void WaveOutCallbackHandler(IntPtr, uint, IntPtr, IntPtr, IntPtr) (line 508)
  - note: Windows thread pool callback (WOM_DONE); checks _playing volatile BEFORE lock to avoid deadlock; finds completed buffer by pointer identity (falls back to WHDR_DONE flag scan); unprepares, refills, and resubmits for seamless looping; swallows exceptions to prevent game crash
