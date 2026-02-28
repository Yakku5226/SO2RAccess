using System;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Plays short audio cues for time-critical gameplay feedback.
    /// Uses Windows native audio (winmm.dll) to bypass Unity IL2CPP GC issues.
    /// Generates WAV data in memory at startup — no external files needed.
    /// </summary>
    public static class AudioCuePlayer
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_MEMORY = 0x0004;
        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;

        private static byte[] _dodgeWarningWav;
        private static bool _initialized;

        private const int SampleRate = 44100;
        private const float DodgeWarningFrequency = 600f;
        private const float DodgeWarningDuration = 0.15f;
        private const float DodgeWarningVolume = 0.8f;

        /// <summary>
        /// Generates audio data. Call once at mod startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                _dodgeWarningWav = GenerateWav(DodgeWarningFrequency, DodgeWarningDuration, DodgeWarningVolume);
                _initialized = true;
                MelonLogger.Msg("AudioCuePlayer: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AudioCuePlayer.Initialize failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the dodge warning cue (incoming attack — press X to dodge).
        /// </summary>
        public static void PlayDodgeWarningCue()
        {
            if (!_initialized || _dodgeWarningWav == null)
                return;

            try
            {
                PlaySound(_dodgeWarningWav, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"AudioCuePlayer.PlayDodgeWarningCue failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up audio data.
        /// </summary>
        public static void Shutdown()
        {
            _dodgeWarningWav = null;
            _initialized = false;
        }

        /// <summary>
        /// Generates a PCM WAV byte array with a sine wave at the given frequency.
        /// </summary>
        private static byte[] GenerateWav(float frequency, float duration, float volume)
        {
            int sampleCount = (int)(SampleRate * duration);
            int dataSize = sampleCount * 2; // 16-bit mono = 2 bytes per sample

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // RIFF header
            bw.Write((byte)'R'); bw.Write((byte)'I'); bw.Write((byte)'F'); bw.Write((byte)'F');
            bw.Write(36 + dataSize);
            bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'V'); bw.Write((byte)'E');

            // fmt chunk
            bw.Write((byte)'f'); bw.Write((byte)'m'); bw.Write((byte)'t'); bw.Write((byte)' ');
            bw.Write(16);            // chunk size
            bw.Write((short)1);      // PCM format
            bw.Write((short)1);      // mono
            bw.Write(SampleRate);    // sample rate
            bw.Write(SampleRate * 2); // byte rate
            bw.Write((short)2);      // block align
            bw.Write((short)16);     // bits per sample

            // data chunk
            bw.Write((byte)'d'); bw.Write((byte)'a'); bw.Write((byte)'t'); bw.Write((byte)'a');
            bw.Write(dataSize);

            // Sine wave samples
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float sample = (float)Math.Sin(2.0 * Math.PI * frequency * t) * volume;

                // Fade out last 20% to avoid click at end
                float fadeStart = sampleCount * 0.8f;
                if (i > fadeStart)
                    sample *= 1f - (i - fadeStart) / (sampleCount - fadeStart);

                bw.Write((short)(sample * short.MaxValue));
            }

            return ms.ToArray();
        }
    }
}
