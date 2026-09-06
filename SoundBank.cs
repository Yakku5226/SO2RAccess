using System;
using System.Collections.Generic;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Load-once cache of looping cues for <see cref="LoopMixer"/>: cue file name to
    /// mono 16-bit samples at the mixer's fixed rate (<see cref="SampleRate"/>).
    ///
    /// <see cref="EmbeddedSounds.Get"/> decompresses a cue every time it is asked, so
    /// this class asks exactly once per cue and keeps the decoded samples for the life
    /// of the mod. Several voices may play the same cue at once; they all share one
    /// buffer by reference, so a cue costs its memory once no matter how many beacons
    /// use it. A cue that is missing or unreadable is remembered as missing, so the
    /// warning is logged a single time rather than every frame.
    ///
    /// Cues recorded at another sample rate are resampled at load with linear
    /// interpolation, so the mixer never has to change speed at run time.
    /// </summary>
    public static class SoundBank
    {
        /// <summary>Sample rate every cue is stored at and the mixer device is opened with.</summary>
        public const int SampleRate = 44100;

        private static readonly Dictionary<string, short[]> _cues =
            new Dictionary<string, short[]>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();

        /// <summary>
        /// Mono samples for a cue, or null when the cue is missing or invalid.
        /// Loads and decodes on first use, cached afterwards.
        /// </summary>
        /// <param name="fileName">Cue file name, e.g. "NavNpc.wav".</param>
        public static short[] Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            lock (_lock)
            {
                if (_cues.TryGetValue(fileName, out short[] cached))
                    return cached;

                short[] samples = Load(fileName);
                _cues[fileName] = samples; // null is cached too: "known missing"
                return samples;
            }
        }

        /// <summary>True when the cue loaded (or can load) successfully.</summary>
        public static bool IsAvailable(string fileName) => Get(fileName) != null;

        /// <summary>
        /// Loads several cues up front so the first beacon does not stall the frame
        /// on a multi-megabyte decompression. Missing cues are reported, not fatal.
        /// </summary>
        public static void Preload(params string[] fileNames)
        {
            foreach (string name in fileNames)
                Get(name);
        }

        /// <summary>Drops every cached cue. Call at shutdown.</summary>
        public static void Clear()
        {
            lock (_lock) { _cues.Clear(); }
        }

        #region Decoding

        private static short[] Load(string fileName)
        {
            try
            {
                byte[] wav = EmbeddedSounds.Get(fileName);
                if (wav == null) return null; // EmbeddedSounds has already said why

                short[] samples = Decode(wav, fileName, out uint rate);
                if (samples == null) return null;

                if (rate != SampleRate)
                {
                    samples = Resample(samples, rate, SampleRate);
                    MelonLogger.Msg($"SoundBank: {fileName} resampled {rate} -> {SampleRate} Hz.");
                }

                return samples;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SoundBank: loading {fileName} failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses PCM WAV bytes into mono samples. Handles 8/16-bit, mono or stereo
        /// (stereo is averaged to mono), and skips unknown chunks.
        /// </summary>
        private static short[] Decode(byte[] fileData, string name, out uint sampleRate)
        {
            sampleRate = 0;
            if (fileData == null || fileData.Length < 44)
            {
                MelonLogger.Error($"SoundBank: {name} is too small to be a WAV file.");
                return null;
            }

            if (fileData[0] != 'R' || fileData[1] != 'I' || fileData[2] != 'F' || fileData[3] != 'F' ||
                fileData[8] != 'W' || fileData[9] != 'A' || fileData[10] != 'V' || fileData[11] != 'E')
            {
                MelonLogger.Error($"SoundBank: {name} is not a RIFF/WAVE file.");
                return null;
            }

            int fmtOffset = -1;
            int dataOffset = -1;
            int dataSize = 0;
            int pos = 12;

            while (pos + 8 <= fileData.Length)
            {
                string chunkId = "" + (char)fileData[pos] + (char)fileData[pos + 1]
                               + (char)fileData[pos + 2] + (char)fileData[pos + 3];
                int chunkSize = BitConverter.ToInt32(fileData, pos + 4);

                if (chunkId == "fmt ") fmtOffset = pos + 8;
                else if (chunkId == "data")
                {
                    dataOffset = pos + 8;
                    dataSize = Math.Min(chunkSize, fileData.Length - dataOffset);
                }

                pos += 8 + chunkSize;
                if (chunkSize % 2 != 0) pos++; // chunks are word-aligned
            }

            if (fmtOffset < 0 || dataOffset < 0 || dataSize <= 0)
            {
                MelonLogger.Error($"SoundBank: {name} has no fmt/data chunks.");
                return null;
            }

            ushort audioFormat = BitConverter.ToUInt16(fileData, fmtOffset);
            ushort channels = BitConverter.ToUInt16(fileData, fmtOffset + 2);
            sampleRate = BitConverter.ToUInt32(fileData, fmtOffset + 4);
            ushort bitsPerSample = BitConverter.ToUInt16(fileData, fmtOffset + 14);

            if (audioFormat != 1)
            {
                MelonLogger.Error($"SoundBank: {name} format tag {audioFormat} unsupported — only integer PCM (1) plays.");
                return null;
            }
            if (bitsPerSample != 8 && bitsPerSample != 16)
            {
                MelonLogger.Error($"SoundBank: {name} is {bitsPerSample}-bit — only 8 or 16 bit PCM is supported.");
                return null;
            }
            if (channels < 1 || sampleRate == 0)
            {
                MelonLogger.Error($"SoundBank: {name} has an invalid format ({channels} ch, {sampleRate} Hz).");
                return null;
            }

            int bytesPerSample = bitsPerSample / 8;
            int frameSize = bytesPerSample * channels;
            int frameCount = dataSize / frameSize;
            var samples = new short[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                int frameStart = dataOffset + i * frameSize;
                int sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = frameStart + c * bytesPerSample;
                    sum += bitsPerSample == 16
                        ? BitConverter.ToInt16(fileData, at)
                        : (fileData[at] - 128) << 8;
                }
                samples[i] = (short)(sum / channels);
            }

            MelonLogger.Msg($"SoundBank: {name} loaded — {frameCount} samples, {sampleRate} Hz, " +
                            $"{channels} ch, {bitsPerSample} bit -> mono.");
            return samples;
        }

        /// <summary>Linear-interpolation resample of a mono buffer between two rates.</summary>
        private static short[] Resample(short[] src, uint fromRate, uint toRate)
        {
            if (src.Length < 2) return src;

            double ratio = (double)fromRate / toRate;
            int outLen = (int)Math.Round(src.Length / ratio);
            var dst = new short[outLen];

            for (int i = 0; i < outLen; i++)
            {
                double srcPos = i * ratio;
                int idx = (int)srcPos;
                double frac = srcPos - idx;
                int next = (idx + 1) % src.Length; // wrap: these are loops
                dst[i] = (short)Math.Round(src[idx] + (src[next] - src[idx]) * frac);
            }

            return dst;
        }

        #endregion
    }
}
