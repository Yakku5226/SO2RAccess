using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Plays any number of looping cues at once with live per-voice volume, stereo
    /// pan and muffle: the four wall tones, the object beacons, the enemy-proximity
    /// loop and the menu previews all share it.
    ///
    /// Uses the Windows waveOut API (winmm.dll) on its own device handle, so it never
    /// conflicts with <see cref="AudioCuePlayer"/>'s PlaySound one-shots. Cues come
    /// from <see cref="SoundBank"/>; each active <see cref="MixerVoice"/> is summed in
    /// float and clipped to 16-bit stereo, refilled in 25 ms blocks from the waveOut
    /// callback thread.
    ///
    /// The device opens when the first voice starts and closes after
    /// <see cref="IdleCloseSeconds"/> with nothing playing; <see cref="Update"/> (called
    /// every frame) does that housekeeping on the game thread, because closing from
    /// inside the callback is not allowed. Stop/close follow the same deadlock-safe
    /// order as the previous single-voice player: the playing flag is cleared BEFORE
    /// taking the lock, so callbacks fired by waveOutReset exit immediately.
    /// </summary>
    public static class LoopMixer
    {
        #region P/Invoke

        private delegate void WaveOutCallback(IntPtr hwo, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")]
        private static extern uint waveOutOpen(out IntPtr phwo, uint uDeviceID,
            ref WaveFormatEx pwfx, WaveOutCallback dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        private static extern uint waveOutClose(IntPtr hwo);

        [DllImport("winmm.dll")]
        private static extern uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        private static extern uint waveOutReset(IntPtr hwo);

        [DllImport("winmm.dll")]
        private static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        private static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_FUNCTION = 0x00030000;
        private const uint MMSYSERR_NOERROR = 0;
        private const uint WOM_DONE = 0x3BD;
        private const uint WHDR_DONE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        // WAVEHDR layout on x64: lpData(0,8) dwBufferLength(8,4) dwBytesRecorded(12,4)
        // dwUser(16,8) dwFlags(24,4) dwLoops(28,4) lpNext(32,8) reserved(40,8) = 48 bytes.
        private const int WAVEHDR_SIZE = 48;
        private const int WAVEHDR_LPDATA = 0;
        private const int WAVEHDR_BUFFERLENGTH = 8;
        private const int WAVEHDR_FLAGS = 24;

        #endregion

        #region Tuning

        private const int BufferMs = 25;
        private const int BufferCount = 4;
        private const int FramesPerBuffer = SoundBank.SampleRate * BufferMs / 1000;
        private const int BytesPerBuffer = FramesPerBuffer * 4; // stereo 16-bit

        /// <summary>Seconds with no active voice before the device is closed.</summary>
        private const float IdleCloseSeconds = 10f;

        #endregion

        #region Fields

        private static bool _playing;
        private static IntPtr _hWaveOut;
        private static IntPtr[] _bufferData;
        private static IntPtr[] _bufferHeaders;
        private static WaveOutCallback _callbackDelegate; // kept alive for the GC
        private static readonly object _lock = new object();

        private static readonly List<MixerVoice> _voices = new List<MixerVoice>();
        private static readonly float[] _mixL = new float[FramesPerBuffer];
        private static readonly float[] _mixR = new float[FramesPerBuffer];
        private static readonly short[] _interleaved = new short[FramesPerBuffer * 2];

        private static float _idleSeconds;
        private static readonly Random _random = new Random();

        #endregion

        #region Public API

        /// <summary>Number of voices currently mixed (including ones fading out).</summary>
        public static int ActiveVoiceCount { get { lock (_lock) { return _voices.Count; } } }

        /// <summary>True while the waveOut device is open and streaming.</summary>
        public static bool IsPlaying => _playing;

        /// <summary>True when the cue's WAV is available to play.</summary>
        public static bool IsCueAvailable(string cue) => SoundBank.IsAvailable(cue);

        /// <summary>
        /// Starts a looping voice and returns its handle, or null when the cue is
        /// missing or the device cannot be opened. Keep calling
        /// <see cref="MixerVoice.Set"/> on the handle to steer it; call
        /// <see cref="MixerVoice.Stop"/> to fade it out.
        /// </summary>
        /// <param name="cue">Cue file name, e.g. "Wall_front.wav".</param>
        /// <param name="gain">Initial volume 0..1.</param>
        /// <param name="pan">Initial pan -1..+1.</param>
        /// <param name="phase01">
        /// Where in the loop to start, 0..1. Negative = random, so several beacons of
        /// the same cue do not hit in unison.
        /// </param>
        /// <param name="autoStopSeconds">Greater than zero = preview: stops by itself after this long.</param>
        public static MixerVoice Play(string cue, float gain, float pan, float phase01 = -1f,
            float autoStopSeconds = 0f)
        {
            short[] samples = SoundBank.Get(cue);
            if (samples == null || samples.Length == 0) return null;

            float phase = phase01 < 0f ? (float)_random.NextDouble() : Math.Clamp(phase01, 0f, 0.999f);
            int startPos = (int)(phase * samples.Length);
            var voice = new MixerVoice(cue, samples, startPos, gain, pan, autoStopSeconds);

            int active;
            lock (_lock)
            {
                if (!_playing && !OpenDevice())
                    return null;

                _voices.Add(voice);
                _idleSeconds = 0f;
                active = _voices.Count;
            }

            DebugLogger.LogState($"LoopMixer: voice started ({cue}), {active} active.");
            return voice;
        }

        /// <summary>
        /// Per-frame housekeeping on the game thread: closes the device once nothing
        /// has played for <see cref="IdleCloseSeconds"/>. Cheap when idle.
        /// </summary>
        public static void Update(float deltaTime)
        {
            if (!_playing) return;

            int count;
            lock (_lock) { count = _voices.Count; }

            if (count > 0)
            {
                _idleSeconds = 0f;
                return;
            }

            _idleSeconds += deltaTime;
            if (_idleSeconds >= IdleCloseSeconds)
                CloseDevice();
        }

        /// <summary>Stops every voice immediately (no fade) and closes the device.</summary>
        public static void StopAll()
        {
            lock (_lock)
            {
                foreach (var v in _voices) v.Stop();
                _voices.Clear();
            }
            CloseDevice();
        }

        /// <summary>Releases the device, buffers and cached cues. Call at mod shutdown.</summary>
        public static void Shutdown()
        {
            StopAll();
            FreeBuffers();
            SoundBank.Clear();
            _callbackDelegate = null;
            MelonLogger.Msg("LoopMixer: shutdown.");
        }

        #endregion

        #region Device

        /// <summary>Opens the device and primes all buffers. Caller holds _lock.</summary>
        private static bool OpenDevice()
        {
            try
            {
                if (_bufferData == null) AllocateBuffers();

                _callbackDelegate = WaveOutCallbackHandler;
                var fmt = new WaveFormatEx
                {
                    wFormatTag = 1,
                    nChannels = 2,
                    nSamplesPerSec = SoundBank.SampleRate,
                    wBitsPerSample = 16,
                    nBlockAlign = 4,
                    nAvgBytesPerSec = SoundBank.SampleRate * 4,
                    cbSize = 0
                };

                uint result = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt,
                    _callbackDelegate, IntPtr.Zero, CALLBACK_FUNCTION);
                if (result != MMSYSERR_NOERROR)
                {
                    MelonLogger.Warning($"LoopMixer: waveOutOpen failed (error {result}).");
                    return false;
                }

                _playing = true;
                for (int i = 0; i < BufferCount; i++)
                {
                    FillBuffer(i);
                    SubmitBuffer(i);
                }

                DebugLogger.LogState("LoopMixer: device opened.");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"LoopMixer.OpenDevice failed: {ex.Message}");
                _playing = false;
                return false;
            }
        }

        /// <summary>
        /// Closes the device. _playing is cleared BEFORE locking so the callbacks that
        /// waveOutReset triggers return at once instead of waiting on the lock.
        /// </summary>
        private static void CloseDevice()
        {
            if (!_playing) return;
            _playing = false;

            lock (_lock) { } // let an in-progress fill finish

            try
            {
                waveOutReset(_hWaveOut);
                for (int i = 0; i < BufferCount; i++)
                {
                    if (_bufferHeaders[i] != IntPtr.Zero)
                        waveOutUnprepareHeader(_hWaveOut, _bufferHeaders[i], (uint)WAVEHDR_SIZE);
                }
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
                _idleSeconds = 0f;
                DebugLogger.LogState("LoopMixer: device closed.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"LoopMixer.CloseDevice error: {ex.Message}");
            }
        }

        private static void AllocateBuffers()
        {
            _bufferData = new IntPtr[BufferCount];
            _bufferHeaders = new IntPtr[BufferCount];
            for (int i = 0; i < BufferCount; i++)
            {
                _bufferData[i] = Marshal.AllocHGlobal(BytesPerBuffer);
                _bufferHeaders[i] = Marshal.AllocHGlobal(WAVEHDR_SIZE);
                for (int b = 0; b < WAVEHDR_SIZE; b++)
                    Marshal.WriteByte(_bufferHeaders[i], b, 0);
            }
        }

        private static void FreeBuffers()
        {
            if (_bufferData == null) return;
            for (int i = 0; i < BufferCount; i++)
            {
                if (_bufferData[i] != IntPtr.Zero) Marshal.FreeHGlobal(_bufferData[i]);
                if (_bufferHeaders[i] != IntPtr.Zero) Marshal.FreeHGlobal(_bufferHeaders[i]);
            }
            _bufferData = null;
            _bufferHeaders = null;
        }

        #endregion

        #region Mixing

        /// <summary>
        /// Sums every active voice into one stereo block and writes it to the
        /// unmanaged buffer. Finished voices are dropped here. Caller holds _lock.
        /// </summary>
        private static void FillBuffer(int bufferIndex)
        {
            Array.Clear(_mixL, 0, FramesPerBuffer);
            Array.Clear(_mixR, 0, FramesPerBuffer);

            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                if (!_voices[v].Mix(_mixL, _mixR, FramesPerBuffer))
                    _voices.RemoveAt(v);
            }

            for (int i = 0; i < FramesPerBuffer; i++)
            {
                _interleaved[i * 2] = ToPcm16(_mixL[i]);
                _interleaved[i * 2 + 1] = ToPcm16(_mixR[i]);
            }

            Marshal.Copy(_interleaved, 0, _bufferData[bufferIndex], FramesPerBuffer * 2);
        }

        private static short ToPcm16(float sample)
        {
            if (sample >= 1f) return short.MaxValue;
            if (sample <= -1f) return short.MinValue;
            return (short)(sample * 32767f);
        }

        private static void SubmitBuffer(int bufferIndex)
        {
            IntPtr hdr = _bufferHeaders[bufferIndex];
            Marshal.WriteIntPtr(hdr, WAVEHDR_LPDATA, _bufferData[bufferIndex]);
            Marshal.WriteInt32(hdr, WAVEHDR_BUFFERLENGTH, BytesPerBuffer);
            Marshal.WriteInt32(hdr, WAVEHDR_FLAGS, 0);
            waveOutPrepareHeader(_hWaveOut, hdr, (uint)WAVEHDR_SIZE);
            waveOutWrite(_hWaveOut, hdr, (uint)WAVEHDR_SIZE);
        }

        /// <summary>
        /// Windows calls this when a buffer finishes. Refills and resubmits it.
        /// Runs on a system thread — must not call Unity APIs.
        /// </summary>
        private static void WaveOutCallbackHandler(IntPtr hwo, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg != WOM_DONE) return;
            if (!_playing) return; // checked before the lock: see CloseDevice

            lock (_lock)
            {
                if (!_playing) return;
                try
                {
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (_bufferHeaders[i] == dwParam1)
                        {
                            Refill(i);
                            return;
                        }
                    }

                    // Header not matched by identity: fall back to the DONE flag.
                    for (int i = 0; i < BufferCount; i++)
                    {
                        int flags = Marshal.ReadInt32(_bufferHeaders[i], WAVEHDR_FLAGS);
                        if ((flags & (int)WHDR_DONE) != 0)
                        {
                            Refill(i);
                            return;
                        }
                    }
                }
                catch
                {
                    // Never let an exception escape the audio callback; the game must
                    // keep running even if audio stops.
                }
            }
        }

        private static void Refill(int bufferIndex)
        {
            waveOutUnprepareHeader(_hWaveOut, _bufferHeaders[bufferIndex], (uint)WAVEHDR_SIZE);
            FillBuffer(bufferIndex);
            SubmitBuffer(bufferIndex);
        }

        #endregion
    }
}
