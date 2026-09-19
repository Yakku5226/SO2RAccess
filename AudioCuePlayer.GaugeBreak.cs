using MelonLoader;
using System;
using System.Runtime.InteropServices;

namespace SO2RAccess
{
    /// <summary>
    /// Bonus gauge break cue, plus the small reusable <see cref="MemoryCue"/> holder it
    /// runs on. New one-shot cues should use MemoryCue instead of repeating the
    /// per-cue field block of the older cues in AudioCuePlayer.cs.
    /// </summary>
    public static partial class AudioCuePlayer
    {
        /// <summary>
        /// One WAV cue held in memory: parsed once, re-scaled into unmanaged memory only
        /// when the requested volume changes, played through winmm PlaySound.
        /// </summary>
        private sealed class MemoryCue
        {
            private readonly string _label;
            private byte[] _rawWav;
            private int _dataOffset;
            private int _dataLength;
            private short _bitsPerSample;
            private IntPtr _ptr = IntPtr.Zero;
            private float _cachedVolume = -1f;

            /// <param name="label">Name used in log lines only.</param>
            public MemoryCue(string label) { _label = label; }

            /// <summary>True once a valid WAV has been loaded.</summary>
            public bool IsLoaded => _rawWav != null;

            /// <summary>Loads the user's copy from the Sounds folder, else the bundled one.</summary>
            public void Load(string fileName)
            {
                try
                {
                    if (!TryLoadCue(fileName, out byte[] fileBytes, out int dataOffset,
                            out int dataLength, out short bitsPerSample))
                        return;

                    _rawWav = fileBytes;
                    _dataOffset = dataOffset;
                    _dataLength = dataLength;
                    _bitsPerSample = bitsPerSample;
                    _cachedVolume = -1f;
                    MelonLogger.Msg($"AudioCuePlayer: {_label} sound loaded ({fileBytes.Length} bytes, {bitsPerSample}-bit).");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"AudioCuePlayer: loading the {_label} sound failed: {ex.Message}");
                }
            }

            /// <summary>Plays the cue at <paramref name="volume"/> (0..1); silent below 0.001.</summary>
            public void Play(float volume)
            {
                if (_rawWav == null || volume < 0.001f) return;

                try
                {
                    if (Math.Abs(volume - _cachedVolume) > 0.001f)
                    {
                        byte[] adjusted = (byte[])_rawWav.Clone();
                        ScalePcmSamples(adjusted, _dataOffset, _dataLength, _bitsPerSample, volume);

                        if (_ptr != IntPtr.Zero) Marshal.FreeHGlobal(_ptr);
                        _ptr = Marshal.AllocHGlobal(adjusted.Length);
                        Marshal.Copy(adjusted, 0, _ptr, adjusted.Length);
                        _cachedVolume = volume;
                    }

                    PlaySoundPtr(_ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"AudioCuePlayer: playing the {_label} sound failed: {ex.Message}");
                }
            }

            /// <summary>Releases the WAV data and the unmanaged copy.</summary>
            public void Free()
            {
                _rawWav = null;
                if (_ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_ptr);
                    _ptr = IntPtr.Zero;
                }
                _cachedVolume = -1f;
            }
        }

        private static readonly MemoryCue _gaugeBreakCue = new MemoryCue("gauge break");

        /// <summary>
        /// Loads the bonus gauge break cue (played when the gauge is lost). The bundled
        /// GaugeBreak.wav is a synthesized PLACEHOLDER until a real sound is chosen.
        /// </summary>
        public static void LoadGaugeBreakSound(string fileName) => _gaugeBreakCue.Load(fileName);

        /// <summary>Returns true if the gauge break sound was loaded successfully.</summary>
        public static bool IsGaugeBreakSoundLoaded => _gaugeBreakCue.IsLoaded;

        /// <summary>Plays the gauge break cue at the bonus gauge sound volume.</summary>
        public static void PlayGaugeBreakCue() => _gaugeBreakCue.Play(ModSettings.BonusGaugeSoundVolume);
    }
}
