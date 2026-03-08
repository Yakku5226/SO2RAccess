using System;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Plays short audio cues for time-critical gameplay feedback.
    /// Uses Windows native audio (winmm.dll) to bypass Unity IL2CPP GC issues.
    /// Supports both synthesized cues (dodge warning) and file-based cues (save sound).
    /// </summary>
    public static class AudioCuePlayer
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

        // Overload that takes IntPtr — used for unmanaged memory buffers
        // that won't be moved by the GC during async playback.
        [DllImport("winmm.dll", SetLastError = true, EntryPoint = "PlaySound")]
        private static extern bool PlaySoundPtr(IntPtr pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_MEMORY = 0x0004;
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;

        private static bool _initialized;

        // File-based dodge warning sound — same pattern as save sound.
        private static byte[] _dodgeSoundRawWav;
        private static int _dodgeSoundDataOffset;
        private static int _dodgeSoundDataLength;
        private static short _dodgeSoundBitsPerSample;
        private static IntPtr _dodgeSoundPtr = IntPtr.Zero;
        private static int _dodgeSoundPtrSize;
        private static float _dodgeSoundCachedVolume = -1f;
        private static bool _dodgeSoundLoaded;

        // File-based private action notification sound.
        private static byte[] _paSoundRawWav;
        private static int _paSoundDataOffset;
        private static int _paSoundDataLength;
        private static short _paSoundBitsPerSample;
        private static IntPtr _paSoundPtr = IntPtr.Zero;
        private static int _paSoundPtrSize;
        private static float _paSoundCachedVolume = -1f;
        private static bool _paSoundLoaded;

        // File-based bonus gauge fill sound.
        private static byte[] _gaugeFillSoundRawWav;
        private static int _gaugeFillSoundDataOffset;
        private static int _gaugeFillSoundDataLength;
        private static short _gaugeFillSoundBitsPerSample;
        private static IntPtr _gaugeFillSoundPtr = IntPtr.Zero;
        private static int _gaugeFillSoundPtrSize;
        private static float _gaugeFillSoundCachedVolume = -1f;
        private static bool _gaugeFillSoundLoaded;

        // File-based bonus gauge break sound (provision for future use).
        private static byte[] _gaugeBreakSoundRawWav;
        private static int _gaugeBreakSoundDataOffset;
        private static int _gaugeBreakSoundDataLength;
        private static short _gaugeBreakSoundBitsPerSample;
        private static IntPtr _gaugeBreakSoundPtr = IntPtr.Zero;
        private static int _gaugeBreakSoundPtrSize;
        private static float _gaugeBreakSoundCachedVolume = -1f;
        private static bool _gaugeBreakSoundLoaded;

        // File-based save sound — stores the raw WAV file bytes and the
        // byte offset of the PCM data region within it. Volume adjustment
        // creates a copy with scaled samples rather than rebuilding the
        // entire WAV from scratch. The final WAV is stored in unmanaged
        // memory (Marshal.AllocHGlobal) so the GC cannot move it during
        // async PlaySound playback.
        private static byte[] _saveSoundRawWav;
        private static int _saveSoundDataOffset;
        private static int _saveSoundDataLength;
        private static short _saveSoundBitsPerSample;
        private static IntPtr _saveSoundPtr = IntPtr.Zero;
        private static int _saveSoundPtrSize;
        private static float _saveSoundCachedVolume = -1f;
        private static bool _saveSoundLoaded;

        /// <summary>
        /// Marks the player as initialized. Call once at mod startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            MelonLogger.Msg("AudioCuePlayer: initialized.");
        }

        /// <summary>
        /// Loads a WAV file from disk for the dodge warning cue.
        /// </summary>
        public static void LoadDodgeSound(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MelonLogger.Warning($"AudioCuePlayer: dodge sound not found: {path}");
                    return;
                }

                if (!TryParseWav(path, out byte[] fileBytes, out int dataOffset,
                        out int dataLength, out short bitsPerSample))
                    return;

                _dodgeSoundRawWav = fileBytes;
                _dodgeSoundDataOffset = dataOffset;
                _dodgeSoundDataLength = dataLength;
                _dodgeSoundBitsPerSample = bitsPerSample;
                _dodgeSoundCachedVolume = -1f;
                _dodgeSoundLoaded = true;

                int sampleCount = dataLength / (bitsPerSample / 8);
                MelonLogger.Msg($"AudioCuePlayer: dodge sound loaded ({fileBytes.Length} bytes, {sampleCount} samples, {bitsPerSample}-bit).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.LoadDodgeSound failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the dodge sound WAV was loaded successfully.
        /// </summary>
        public static bool IsDodgeSoundLoaded => _dodgeSoundLoaded;

        /// <summary>
        /// Plays the dodge warning cue (incoming attack — press X to dodge)
        /// at the current ModSettings volume.
        /// </summary>
        public static void PlayDodgeWarningCue()
        {
            if (!_dodgeSoundLoaded || _dodgeSoundRawWav == null)
                return;

            try
            {
                float volume = ModSettings.DodgeSoundVolume;

                if (Math.Abs(volume - _dodgeSoundCachedVolume) > 0.001f)
                {
                    byte[] adjusted = (byte[])_dodgeSoundRawWav.Clone();
                    ScalePcmSamples(adjusted, _dodgeSoundDataOffset,
                        _dodgeSoundDataLength, _dodgeSoundBitsPerSample, volume);

                    if (_dodgeSoundPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(_dodgeSoundPtr);

                    _dodgeSoundPtrSize = adjusted.Length;
                    _dodgeSoundPtr = Marshal.AllocHGlobal(_dodgeSoundPtrSize);
                    Marshal.Copy(adjusted, 0, _dodgeSoundPtr, _dodgeSoundPtrSize);
                    _dodgeSoundCachedVolume = volume;

                    DebugLogger.LogState($"Dodge WAV built: {_dodgeSoundPtrSize} bytes at volume {volume:F2}");
                }

                if (_dodgeSoundPtr != IntPtr.Zero)
                {
                    PlaySoundPtr(_dodgeSoundPtr, IntPtr.Zero,
                        SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"AudioCuePlayer.PlayDodgeWarningCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a WAV file from disk for the save sound cue.
        /// </summary>
        public static void LoadSaveSound(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MelonLogger.Warning($"AudioCuePlayer: save sound not found: {path}");
                    return;
                }

                if (!TryParseWav(path, out byte[] fileBytes, out int dataOffset,
                        out int dataLength, out short bitsPerSample))
                    return;

                _saveSoundRawWav = fileBytes;
                _saveSoundDataOffset = dataOffset;
                _saveSoundDataLength = dataLength;
                _saveSoundBitsPerSample = bitsPerSample;
                _saveSoundCachedVolume = -1f;
                _saveSoundLoaded = true;

                int sampleCount = dataLength / (bitsPerSample / 8);
                MelonLogger.Msg($"AudioCuePlayer: save sound loaded ({fileBytes.Length} bytes, {sampleCount} samples, {bitsPerSample}-bit).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.LoadSaveSound failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the save sound cue at the current ModSettings volume.
        /// Creates a volume-adjusted copy of the WAV in unmanaged memory
        /// only when the volume setting changes.
        /// </summary>
        public static void PlaySaveCue()
        {
            if (!_saveSoundLoaded || _saveSoundRawWav == null)
            {
                DebugLogger.LogState($"PlaySaveCue skipped: loaded={_saveSoundLoaded}");
                return;
            }

            try
            {
                float volume = ModSettings.SaveSoundVolume;

                if (Math.Abs(volume - _saveSoundCachedVolume) > 0.001f)
                {
                    byte[] adjusted = (byte[])_saveSoundRawWav.Clone();
                    ScalePcmSamples(adjusted, _saveSoundDataOffset,
                        _saveSoundDataLength, _saveSoundBitsPerSample, volume);

                    if (_saveSoundPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(_saveSoundPtr);

                    _saveSoundPtrSize = adjusted.Length;
                    _saveSoundPtr = Marshal.AllocHGlobal(_saveSoundPtrSize);
                    Marshal.Copy(adjusted, 0, _saveSoundPtr, _saveSoundPtrSize);
                    _saveSoundCachedVolume = volume;

                    DebugLogger.LogState($"Save WAV built: {_saveSoundPtrSize} bytes at volume {volume:F2}");
                }

                if (_saveSoundPtr != IntPtr.Zero)
                {
                    bool result = PlaySoundPtr(_saveSoundPtr, IntPtr.Zero,
                        SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                    DebugLogger.LogState($"Save PlaySound returned: {result}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.PlaySaveCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the save sound WAV was loaded successfully.
        /// </summary>
        public static bool IsSaveSoundLoaded => _saveSoundLoaded;

        /// <summary>
        /// Loads a WAV file from disk for the private action notification cue.
        /// </summary>
        public static void LoadPrivateActionSound(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MelonLogger.Warning($"AudioCuePlayer: private action sound not found: {path}");
                    return;
                }

                if (!TryParseWav(path, out byte[] fileBytes, out int dataOffset,
                        out int dataLength, out short bitsPerSample))
                    return;

                _paSoundRawWav = fileBytes;
                _paSoundDataOffset = dataOffset;
                _paSoundDataLength = dataLength;
                _paSoundBitsPerSample = bitsPerSample;
                _paSoundCachedVolume = -1f;
                _paSoundLoaded = true;

                int sampleCount = dataLength / (bitsPerSample / 8);
                MelonLogger.Msg($"AudioCuePlayer: private action sound loaded ({fileBytes.Length} bytes, {sampleCount} samples, {bitsPerSample}-bit).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.LoadPrivateActionSound failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the private action sound WAV was loaded successfully.
        /// </summary>
        public static bool IsPrivateActionSoundLoaded => _paSoundLoaded;

        /// <summary>
        /// Plays the private action notification cue at the current ModSettings volume.
        /// </summary>
        public static void PlayPrivateActionCue()
        {
            if (!_paSoundLoaded || _paSoundRawWav == null)
                return;

            try
            {
                float volume = ModSettings.PrivateActionSoundVolume;
                if (volume < 0.001f) return;

                if (Math.Abs(volume - _paSoundCachedVolume) > 0.001f)
                {
                    byte[] adjusted = (byte[])_paSoundRawWav.Clone();
                    ScalePcmSamples(adjusted, _paSoundDataOffset,
                        _paSoundDataLength, _paSoundBitsPerSample, volume);

                    if (_paSoundPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(_paSoundPtr);

                    _paSoundPtrSize = adjusted.Length;
                    _paSoundPtr = Marshal.AllocHGlobal(_paSoundPtrSize);
                    Marshal.Copy(adjusted, 0, _paSoundPtr, _paSoundPtrSize);
                    _paSoundCachedVolume = volume;

                    DebugLogger.LogState($"PA WAV built: {_paSoundPtrSize} bytes at volume {volume:F2}");
                }

                if (_paSoundPtr != IntPtr.Zero)
                {
                    PlaySoundPtr(_paSoundPtr, IntPtr.Zero,
                        SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"AudioCuePlayer.PlayPrivateActionCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a WAV file from disk for the bonus gauge fill cue.
        /// </summary>
        public static void LoadGaugeFillSound(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MelonLogger.Warning($"AudioCuePlayer: gauge fill sound not found: {path}");
                    return;
                }

                if (!TryParseWav(path, out byte[] fileBytes, out int dataOffset,
                        out int dataLength, out short bitsPerSample))
                    return;

                _gaugeFillSoundRawWav = fileBytes;
                _gaugeFillSoundDataOffset = dataOffset;
                _gaugeFillSoundDataLength = dataLength;
                _gaugeFillSoundBitsPerSample = bitsPerSample;
                _gaugeFillSoundCachedVolume = -1f;
                _gaugeFillSoundLoaded = true;

                int sampleCount = dataLength / (bitsPerSample / 8);
                MelonLogger.Msg($"AudioCuePlayer: gauge fill sound loaded ({fileBytes.Length} bytes, {sampleCount} samples, {bitsPerSample}-bit).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.LoadGaugeFillSound failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the gauge fill sound WAV was loaded successfully.
        /// </summary>
        public static bool IsGaugeFillSoundLoaded => _gaugeFillSoundLoaded;

        /// <summary>
        /// Plays the gauge fill cue at the current ModSettings volume.
        /// </summary>
        public static void PlayGaugeFillCue()
        {
            if (!_gaugeFillSoundLoaded || _gaugeFillSoundRawWav == null)
                return;

            try
            {
                float volume = ModSettings.BonusGaugeSoundVolume;
                if (volume < 0.01f) return;

                if (Math.Abs(volume - _gaugeFillSoundCachedVolume) > 0.001f)
                {
                    byte[] adjusted = (byte[])_gaugeFillSoundRawWav.Clone();
                    ScalePcmSamples(adjusted, _gaugeFillSoundDataOffset,
                        _gaugeFillSoundDataLength, _gaugeFillSoundBitsPerSample, volume);

                    if (_gaugeFillSoundPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(_gaugeFillSoundPtr);

                    _gaugeFillSoundPtrSize = adjusted.Length;
                    _gaugeFillSoundPtr = Marshal.AllocHGlobal(_gaugeFillSoundPtrSize);
                    Marshal.Copy(adjusted, 0, _gaugeFillSoundPtr, _gaugeFillSoundPtrSize);
                    _gaugeFillSoundCachedVolume = volume;

                    DebugLogger.LogState($"GaugeFill WAV built: {_gaugeFillSoundPtrSize} bytes at volume {volume:F2}");
                }

                if (_gaugeFillSoundPtr != IntPtr.Zero)
                {
                    PlaySoundPtr(_gaugeFillSoundPtr, IntPtr.Zero,
                        SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"AudioCuePlayer.PlayGaugeFillCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a WAV file from disk for the bonus gauge break cue.
        /// Provision for future use — no WAV assigned yet.
        /// </summary>
        public static void LoadGaugeBreakSound(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    // Not a warning — break sound is optional.
                    DebugLogger.LogState($"AudioCuePlayer: gauge break sound not found (optional): {path}");
                    return;
                }

                if (!TryParseWav(path, out byte[] fileBytes, out int dataOffset,
                        out int dataLength, out short bitsPerSample))
                    return;

                _gaugeBreakSoundRawWav = fileBytes;
                _gaugeBreakSoundDataOffset = dataOffset;
                _gaugeBreakSoundDataLength = dataLength;
                _gaugeBreakSoundBitsPerSample = bitsPerSample;
                _gaugeBreakSoundCachedVolume = -1f;
                _gaugeBreakSoundLoaded = true;

                int sampleCount = dataLength / (bitsPerSample / 8);
                MelonLogger.Msg($"AudioCuePlayer: gauge break sound loaded ({fileBytes.Length} bytes, {sampleCount} samples, {bitsPerSample}-bit).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.LoadGaugeBreakSound failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the gauge break sound WAV was loaded successfully.
        /// </summary>
        public static bool IsGaugeBreakSoundLoaded => _gaugeBreakSoundLoaded;

        /// <summary>
        /// Plays the gauge break cue at the specified volume.
        /// Provision for future use.
        /// </summary>
        public static void PlayGaugeBreakCue(float volume)
        {
            if (!_gaugeBreakSoundLoaded || _gaugeBreakSoundRawWav == null)
                return;

            try
            {
                if (volume < 0.01f) return;

                if (Math.Abs(volume - _gaugeBreakSoundCachedVolume) > 0.001f)
                {
                    byte[] adjusted = (byte[])_gaugeBreakSoundRawWav.Clone();
                    ScalePcmSamples(adjusted, _gaugeBreakSoundDataOffset,
                        _gaugeBreakSoundDataLength, _gaugeBreakSoundBitsPerSample, volume);

                    if (_gaugeBreakSoundPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(_gaugeBreakSoundPtr);

                    _gaugeBreakSoundPtrSize = adjusted.Length;
                    _gaugeBreakSoundPtr = Marshal.AllocHGlobal(_gaugeBreakSoundPtrSize);
                    Marshal.Copy(adjusted, 0, _gaugeBreakSoundPtr, _gaugeBreakSoundPtrSize);
                    _gaugeBreakSoundCachedVolume = volume;
                }

                if (_gaugeBreakSoundPtr != IntPtr.Zero)
                {
                    PlaySoundPtr(_gaugeBreakSoundPtr, IntPtr.Zero,
                        SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"AudioCuePlayer.PlayGaugeBreakCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up audio data.
        /// </summary>
        public static void Shutdown()
        {
            // Cancel any in-flight async sound before releasing the WAV data.
            try { PlaySound(null, IntPtr.Zero, SND_ASYNC); }
            catch { /* Best-effort cleanup on shutdown */ }

            _dodgeSoundRawWav = null;
            if (_dodgeSoundPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_dodgeSoundPtr);
                _dodgeSoundPtr = IntPtr.Zero;
            }
            _dodgeSoundLoaded = false;

            _saveSoundRawWav = null;
            if (_saveSoundPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_saveSoundPtr);
                _saveSoundPtr = IntPtr.Zero;
            }
            _saveSoundLoaded = false;

            _paSoundRawWav = null;
            if (_paSoundPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_paSoundPtr);
                _paSoundPtr = IntPtr.Zero;
            }
            _paSoundLoaded = false;

            _gaugeFillSoundRawWav = null;
            if (_gaugeFillSoundPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_gaugeFillSoundPtr);
                _gaugeFillSoundPtr = IntPtr.Zero;
            }
            _gaugeFillSoundLoaded = false;

            _gaugeBreakSoundRawWav = null;
            if (_gaugeBreakSoundPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_gaugeBreakSoundPtr);
                _gaugeBreakSoundPtr = IntPtr.Zero;
            }
            _gaugeBreakSoundLoaded = false;

            _initialized = false;
        }

        #region WAV Helpers

        /// <summary>
        /// Reads and validates a PCM WAV file, returning the raw bytes and
        /// the location/size of the PCM data region within it.
        /// </summary>
        private static bool TryParseWav(string path, out byte[] fileBytes,
            out int dataOffset, out int dataLength, out short bitsPerSample)
        {
            fileBytes = null;
            dataOffset = -1;
            dataLength = 0;
            bitsPerSample = 0;

            byte[] raw = File.ReadAllBytes(path);

            if (raw.Length < 44 ||
                raw[0] != 'R' || raw[1] != 'I' ||
                raw[2] != 'F' || raw[3] != 'F' ||
                raw[8] != 'W' || raw[9] != 'A' ||
                raw[10] != 'V' || raw[11] != 'E')
            {
                MelonLogger.Warning($"AudioCuePlayer: {Path.GetFileName(path)} is not a valid WAV file.");
                return false;
            }

            int pos = 12;
            while (pos < raw.Length - 8)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
                int chunkSize = BitConverter.ToInt32(raw, pos + 4);

                if (chunkId == "fmt ")
                {
                    short format = BitConverter.ToInt16(raw, pos + 8);
                    if (format != 1)
                    {
                        MelonLogger.Warning($"AudioCuePlayer: {Path.GetFileName(path)} must be PCM format.");
                        return false;
                    }
                    bitsPerSample = BitConverter.ToInt16(raw, pos + 22);
                }
                else if (chunkId == "data")
                {
                    dataOffset = pos + 8;
                    dataLength = chunkSize;
                }

                pos += 8 + chunkSize;
                if (chunkSize % 2 != 0) pos++;
            }

            if (dataOffset < 0 || bitsPerSample == 0)
            {
                MelonLogger.Warning($"AudioCuePlayer: could not parse {Path.GetFileName(path)} WAV data.");
                return false;
            }

            fileBytes = raw;
            return true;
        }

        /// <summary>
        /// Scales PCM samples in-place within the given byte array.
        /// Supports 16-bit and 8-bit PCM WAV data.
        /// </summary>
        private static void ScalePcmSamples(byte[] wav, int dataOffset,
            int dataLength, short bitsPerSample, float volume)
        {
            int end = dataOffset + dataLength;

            if (bitsPerSample == 16)
            {
                for (int i = dataOffset; i + 1 < end; i += 2)
                {
                    short sample = BitConverter.ToInt16(wav, i);
                    int scaled = Math.Clamp((int)(sample * volume), short.MinValue, short.MaxValue);
                    wav[i] = (byte)(scaled & 0xFF);
                    wav[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = dataOffset; i < end; i++)
                {
                    int sample = wav[i] - 128;
                    int scaled = Math.Clamp((int)(sample * volume) + 128, 0, 255);
                    wav[i] = (byte)scaled;
                }
            }
        }

        #endregion
    }
}
