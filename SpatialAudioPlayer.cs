using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Plays a looping WAV file with real-time volume and stereo panning control.
    /// Uses the Windows waveOut API (winmm.dll) for independent audio streaming
    /// that doesn't conflict with AudioCuePlayer's PlaySound calls.
    ///
    /// Designed for ambient spatial cues (e.g. enemy proximity warnings).
    /// The WAV file is loaded from disk — swap the file to change the sound.
    /// </summary>
    public static class SpatialAudioPlayer
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

        #region Fields

        private static bool _initialized;
        private static bool _playing;
        private static IntPtr _hWaveOut;

        // Source audio — mono PCM samples loaded from the WAV file.
        private static short[] _sourceSamples;
        private static uint _sourceSampleRate;
        private static int _readPos;

        // Double-buffered stereo output.
        private const int BufferMs = 100;
        private const int BufferCount = 2;
        private static IntPtr[] _bufferData;     // unmanaged byte arrays for PCM data
        private static IntPtr[] _bufferHeaders;   // unmanaged WAVEHDR structs
        private static int _bufferSampleCount;    // samples per buffer (per channel)
        private static int _bufferByteSize;       // bytes per buffer (stereo 16-bit)

        // Volume and panning — updated from the game thread, read by the callback.
        private static volatile float _volume;
        private static volatile float _pan;

        // Keep the callback delegate alive so GC doesn't collect it.
        private static WaveOutCallback _callbackDelegate;

        // Lock to prevent concurrent buffer operations during stop/shutdown.
        private static readonly object _lock = new object();

        /// <summary>
        /// User-adjustable volume multiplier (0.0–1.0). Applied on top of the
        /// distance-based volume. Intended for a future in-game mod settings menu.
        /// </summary>
        public static float UserVolume { get; set; } = 1.0f;

        /// <summary>Whether the player is currently outputting audio.</summary>
        public static bool IsPlaying => _playing;

        #endregion

        #region Public API

        /// <summary>
        /// Loads a WAV file and prepares the waveOut device. Call once at mod startup.
        /// Returns true on success, false if the file is missing or invalid.
        /// </summary>
        public static bool Initialize(string wavFilePath)
        {
            if (_initialized) return true;

            try
            {
                if (!File.Exists(wavFilePath))
                {
                    MelonLogger.Warning($"SpatialAudioPlayer: WAV file not found: {wavFilePath}");
                    return false;
                }

                if (!LoadWav(wavFilePath))
                    return false;

                _initialized = true;
                MelonLogger.Msg("SpatialAudioPlayer: initialized.");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SpatialAudioPlayer.Initialize failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Starts looped playback. No-op if already playing or not initialized.
        /// </summary>
        public static void Start()
        {
            if (!_initialized || _playing) return;

            lock (_lock)
            {
                try
                {
                    if (!OpenDevice()) return;

                    _readPos = 0;
                    _playing = true;

                    // Fill and submit both buffers to start double-buffered playback.
                    for (int i = 0; i < BufferCount; i++)
                    {
                        FillBuffer(i);
                        SubmitBuffer(i);
                    }

                    DebugLogger.LogState("SpatialAudioPlayer: playback started.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"SpatialAudioPlayer.Start failed: {ex.Message}");
                    _playing = false;
                }
            }
        }

        /// <summary>
        /// Stops playback and closes the waveOut device.
        /// IMPORTANT: _playing is set before acquiring _lock to prevent deadlock.
        /// waveOutReset triggers WOM_DONE callbacks on a system thread — if the
        /// callback tried to acquire _lock while we hold it, we'd deadlock.
        /// Setting _playing=false first makes callbacks exit immediately.
        /// </summary>
        public static void Stop()
        {
            if (!_playing) return;

            // Signal callbacks to stop BEFORE any locking.
            _playing = false;

            // Wait for any in-progress callback to finish its current buffer fill.
            lock (_lock) { }

            // Now no callback will re-enter the lock (they all check _playing first).
            try
            {
                // Reset stops playback and marks all buffers as done.
                // Any WOM_DONE callbacks triggered here see _playing=false and return.
                waveOutReset(_hWaveOut);

                for (int i = 0; i < BufferCount; i++)
                {
                    if (_bufferHeaders[i] != IntPtr.Zero)
                        waveOutUnprepareHeader(_hWaveOut, _bufferHeaders[i], (uint)WAVEHDR_SIZE);
                }

                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;

                DebugLogger.LogState("SpatialAudioPlayer: playback stopped.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SpatialAudioPlayer.Stop error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the volume and stereo pan for the next buffer fill.
        /// Called every frame by EnemyProximityHandler.
        /// </summary>
        /// <param name="volume">0.0 (silent) to 1.0 (full volume).</param>
        /// <param name="pan">-1.0 (full left) to +1.0 (full right), 0.0 = center.</param>
        public static void SetVolumePan(float volume, float pan)
        {
            _volume = Math.Max(0f, Math.Min(1f, volume));
            _pan = Math.Max(-1f, Math.Min(1f, pan));
        }

        /// <summary>
        /// Releases all resources. Call at mod shutdown.
        /// </summary>
        public static void Shutdown()
        {
            if (_playing) Stop();

            FreeBuffers();

            _sourceSamples = null;
            _callbackDelegate = null;
            _initialized = false;

            MelonLogger.Msg("SpatialAudioPlayer: shutdown.");
        }

        #endregion

        #region WAV Loading

        /// <summary>
        /// Parses a PCM WAV file and extracts mono samples.
        /// Handles 8-bit and 16-bit, mono and stereo input.
        /// </summary>
        private static bool LoadWav(string path)
        {
            byte[] fileData = File.ReadAllBytes(path);

            if (fileData.Length < 44)
            {
                MelonLogger.Error("SpatialAudioPlayer: WAV file too small.");
                return false;
            }

            // Verify RIFF/WAVE header.
            if (fileData[0] != 'R' || fileData[1] != 'I' || fileData[2] != 'F' || fileData[3] != 'F' ||
                fileData[8] != 'W' || fileData[9] != 'A' || fileData[10] != 'V' || fileData[11] != 'E')
            {
                MelonLogger.Error("SpatialAudioPlayer: not a valid WAV file.");
                return false;
            }

            // Find the fmt and data chunks by scanning (handles extra chunks).
            int fmtOffset = -1;
            int dataOffset = -1;
            int dataSize = 0;
            int pos = 12; // past RIFF header

            while (pos + 8 <= fileData.Length)
            {
                string chunkId = "" + (char)fileData[pos] + (char)fileData[pos + 1]
                               + (char)fileData[pos + 2] + (char)fileData[pos + 3];
                int chunkSize = BitConverter.ToInt32(fileData, pos + 4);

                if (chunkId == "fmt ")
                {
                    fmtOffset = pos + 8;
                }
                else if (chunkId == "data")
                {
                    dataOffset = pos + 8;
                    dataSize = chunkSize;
                }

                pos += 8 + chunkSize;
                // Chunks are word-aligned
                if (chunkSize % 2 != 0) pos++;
            }

            if (fmtOffset < 0 || dataOffset < 0 || dataSize <= 0)
            {
                MelonLogger.Error("SpatialAudioPlayer: could not find fmt/data chunks.");
                return false;
            }

            ushort audioFormat = BitConverter.ToUInt16(fileData, fmtOffset);
            ushort channels = BitConverter.ToUInt16(fileData, fmtOffset + 2);
            uint sampleRate = BitConverter.ToUInt32(fileData, fmtOffset + 4);
            ushort bitsPerSample = BitConverter.ToUInt16(fileData, fmtOffset + 14);

            if (audioFormat != 1)
            {
                MelonLogger.Error($"SpatialAudioPlayer: unsupported format (tag={audioFormat}). Only PCM (1) is supported.");
                return false;
            }

            if (bitsPerSample != 8 && bitsPerSample != 16)
            {
                MelonLogger.Error($"SpatialAudioPlayer: unsupported bit depth ({bitsPerSample}). Only 8 or 16 bit supported.");
                return false;
            }

            _sourceSampleRate = sampleRate;

            // Extract mono samples.
            int bytesPerSample = bitsPerSample / 8;
            int frameSize = bytesPerSample * channels;
            int frameCount = dataSize / frameSize;
            _sourceSamples = new short[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                int frameStart = dataOffset + i * frameSize;
                if (frameStart + frameSize > fileData.Length) break;

                if (bitsPerSample == 16)
                {
                    if (channels == 1)
                    {
                        _sourceSamples[i] = BitConverter.ToInt16(fileData, frameStart);
                    }
                    else
                    {
                        // Stereo: average left and right to mono.
                        short left = BitConverter.ToInt16(fileData, frameStart);
                        short right = BitConverter.ToInt16(fileData, frameStart + 2);
                        _sourceSamples[i] = (short)((left + right) / 2);
                    }
                }
                else // 8-bit unsigned
                {
                    if (channels == 1)
                    {
                        _sourceSamples[i] = (short)((fileData[frameStart] - 128) << 8);
                    }
                    else
                    {
                        int left = (fileData[frameStart] - 128) << 8;
                        int right = (fileData[frameStart + 1] - 128) << 8;
                        _sourceSamples[i] = (short)((left + right) / 2);
                    }
                }
            }

            MelonLogger.Msg($"SpatialAudioPlayer: loaded {frameCount} samples, {sampleRate}Hz, " +
                            $"{channels}ch {bitsPerSample}bit -> mono.");

            // Allocate output buffers.
            AllocateBuffers();
            return true;
        }

        #endregion

        #region Buffer Management

        /// <summary>
        /// Allocates unmanaged memory for double-buffered stereo output.
        /// </summary>
        private static void AllocateBuffers()
        {
            // Samples per buffer per channel (e.g. 4410 for 100ms at 44100Hz).
            _bufferSampleCount = (int)(_sourceSampleRate * BufferMs / 1000);
            // Stereo 16-bit: 4 bytes per sample frame.
            _bufferByteSize = _bufferSampleCount * 4;

            _bufferData = new IntPtr[BufferCount];
            _bufferHeaders = new IntPtr[BufferCount];

            for (int i = 0; i < BufferCount; i++)
            {
                _bufferData[i] = Marshal.AllocHGlobal(_bufferByteSize);
                _bufferHeaders[i] = Marshal.AllocHGlobal(WAVEHDR_SIZE);
                // Zero out the header.
                for (int b = 0; b < WAVEHDR_SIZE; b++)
                    Marshal.WriteByte(_bufferHeaders[i], b, 0);
            }
        }

        /// <summary>
        /// Frees all unmanaged buffer memory.
        /// </summary>
        private static void FreeBuffers()
        {
            if (_bufferData == null) return;

            for (int i = 0; i < BufferCount; i++)
            {
                if (_bufferData[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_bufferData[i]);
                    _bufferData[i] = IntPtr.Zero;
                }
                if (_bufferHeaders[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_bufferHeaders[i]);
                    _bufferHeaders[i] = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Fills a buffer with stereo PCM data from the mono source,
        /// applying the current volume and panning.
        /// </summary>
        private static void FillBuffer(int bufferIndex)
        {
            float vol = _volume * Math.Max(0f, Math.Min(1f, UserVolume));
            float p = _pan;

            float leftGain = vol * Math.Min(1f, 1f - p);
            float rightGain = vol * Math.Min(1f, 1f + p);

            IntPtr data = _bufferData[bufferIndex];
            int sourceLen = _sourceSamples.Length;

            for (int i = 0; i < _bufferSampleCount; i++)
            {
                int srcIdx = (_readPos + i) % sourceLen;
                float sample = _sourceSamples[srcIdx] / 32768f;

                short left = (short)Math.Max(short.MinValue,
                    Math.Min(short.MaxValue, sample * leftGain * 32767f));
                short right = (short)Math.Max(short.MinValue,
                    Math.Min(short.MaxValue, sample * rightGain * 32767f));

                int offset = i * 4;
                Marshal.WriteInt16(data, offset, left);
                Marshal.WriteInt16(data, offset + 2, right);
            }

            _readPos = (_readPos + _bufferSampleCount) % sourceLen;
        }

        #endregion

        #region WaveOut Device

        /// <summary>
        /// Opens the waveOut device for stereo 16-bit playback.
        /// </summary>
        private static bool OpenDevice()
        {
            _callbackDelegate = WaveOutCallbackHandler;

            var fmt = new WaveFormatEx
            {
                wFormatTag = 1, // PCM
                nChannels = 2, // stereo output
                nSamplesPerSec = _sourceSampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 4, // 2 channels * 2 bytes
                nAvgBytesPerSec = _sourceSampleRate * 4,
                cbSize = 0
            };

            uint result = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt,
                _callbackDelegate, IntPtr.Zero, CALLBACK_FUNCTION);

            if (result != MMSYSERR_NOERROR)
            {
                MelonLogger.Warning($"SpatialAudioPlayer: waveOutOpen failed (error {result}).");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prepares and submits a buffer to the waveOut device.
        /// </summary>
        private static void SubmitBuffer(int bufferIndex)
        {
            IntPtr hdr = _bufferHeaders[bufferIndex];

            // Set up the WAVEHDR fields.
            Marshal.WriteIntPtr(hdr, WAVEHDR_LPDATA, _bufferData[bufferIndex]);
            Marshal.WriteInt32(hdr, WAVEHDR_BUFFERLENGTH, _bufferByteSize);
            // Clear flags.
            Marshal.WriteInt32(hdr, WAVEHDR_FLAGS, 0);

            waveOutPrepareHeader(_hWaveOut, hdr, (uint)WAVEHDR_SIZE);
            waveOutWrite(_hWaveOut, hdr, (uint)WAVEHDR_SIZE);
        }

        /// <summary>
        /// Callback invoked by Windows when a buffer finishes playing.
        /// Refills the buffer and resubmits it for seamless looping.
        /// Runs on a Windows thread pool thread — must not call Unity APIs.
        /// </summary>
        private static void WaveOutCallbackHandler(IntPtr hwo, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg != WOM_DONE) return;

            // Check _playing (volatile) BEFORE the lock to prevent deadlock with Stop().
            // Stop() sets _playing=false then calls waveOutReset which triggers this callback.
            if (!_playing) return;

            lock (_lock)
            {
                if (!_playing) return;

                try
                {
                    // dwParam1 is a pointer to the completed WAVEHDR.
                    // Find which buffer it matches.
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (_bufferHeaders[i] == dwParam1)
                        {
                            waveOutUnprepareHeader(_hWaveOut, _bufferHeaders[i], (uint)WAVEHDR_SIZE);
                            FillBuffer(i);
                            SubmitBuffer(i);
                            return;
                        }
                    }

                    // If dwParam1 didn't match by pointer identity, the WAVEHDR was
                    // allocated by us but passed by value copy. Re-fill buffer 0 as fallback.
                    // This handles the case where Windows copies the header.
                    for (int i = 0; i < BufferCount; i++)
                    {
                        int flags = Marshal.ReadInt32(_bufferHeaders[i], WAVEHDR_FLAGS);
                        if ((flags & (int)WHDR_DONE) != 0)
                        {
                            waveOutUnprepareHeader(_hWaveOut, _bufferHeaders[i], (uint)WAVEHDR_SIZE);
                            FillBuffer(i);
                            SubmitBuffer(i);
                            return;
                        }
                    }
                }
                catch
                {
                    // Swallow exceptions in the callback to prevent crashes.
                    // Audio will stop looping but the game continues.
                }
            }
        }

        #endregion
    }
}
