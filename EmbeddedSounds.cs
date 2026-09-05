using MelonLoader;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace SO2RAccess
{
    /// <summary>
    /// Supplies the mod's audio cues as raw WAV bytes, so installing the mod is a
    /// single DLL with no Sounds folder to copy alongside it.
    ///
    /// Every cue is compiled into the assembly as a gzip-compressed embedded resource
    /// (project folder <c>soundcues\</c>, one <c>&lt;name&gt;.wav.gz</c> per cue), the
    /// same way the world map grids are carried. Nothing is written to disk: the bytes
    /// are decompressed straight into the array the WAV parsers already expect.
    ///
    /// A file of the same name in <c>UserData\SO2RAccess\Sounds\</c> still wins, so
    /// anyone who prefers their own cue can drop a WAV in that folder and keep it
    /// across updates. The folder is now optional rather than required.
    ///
    /// To change a bundled cue: gzip the new WAV into <c>soundcues\</c> under the same
    /// name and rebuild. Cues must be integer PCM (16-bit for volume control to apply);
    /// float32 WAVs do not play through the winmm players.
    /// </summary>
    public static class EmbeddedSounds
    {
        /// <summary>Folder users can drop replacement cues into, relative to the game root.</summary>
        private const string UserSoundsFolder = @"UserData\SO2RAccess\Sounds";

        /// <summary>
        /// Full path of the folder a user's own replacement cues live in.
        /// The folder does not have to exist.
        /// </summary>
        public static string UserSoundsPath =>
            Path.Combine(Directory.GetCurrentDirectory(), UserSoundsFolder);

        /// <summary>
        /// Raw WAV bytes for a cue, or null when neither source has it.
        /// Checks the user's Sounds folder first, then the copy inside the DLL.
        /// Logs which source answered — a cue playing from an unexpected place is the
        /// first thing to check when one sounds wrong after an update.
        /// </summary>
        /// <param name="fileName">Cue file name, e.g. "Dodge.wav".</param>
        public static byte[] Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            byte[] fromDisk = TryReadUserFile(fileName);
            if (fromDisk != null)
            {
                MelonLogger.Msg($"EmbeddedSounds: {fileName} loaded from the user's Sounds folder " +
                                $"({fromDisk.Length} bytes), overriding the bundled copy.");
                return fromDisk;
            }

            byte[] fromDll = TryReadEmbedded(fileName);
            if (fromDll != null)
            {
                DebugLogger.LogState($"EmbeddedSounds: {fileName} loaded from the DLL ({fromDll.Length} bytes).");
                return fromDll;
            }

            MelonLogger.Warning(
                $"EmbeddedSounds: {fileName} is in neither the DLL nor {UserSoundsFolder} — that cue will be silent.");
            return null;
        }

        /// <summary>
        /// Reads a user-supplied replacement from the Sounds folder, or null when there
        /// is none. A file that exists but cannot be read is reported and treated as
        /// absent, so a permissions problem falls back to the bundled cue instead of
        /// losing the sound entirely.
        /// </summary>
        private static byte[] TryReadUserFile(string fileName)
        {
            try
            {
                string path = Path.Combine(UserSoundsPath, fileName);
                if (!File.Exists(path)) return null;

                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"EmbeddedSounds: could not read {fileName} from the Sounds folder ({ex.Message}) — " +
                    "using the bundled copy.");
                return null;
            }
        }

        /// <summary>
        /// Decompresses the cue's embedded resource, or null when the assembly has no
        /// such resource. Resource names are matched case-insensitively on the file name
        /// alone, so the cue's spelling in code does not have to track the file on disk.
        /// </summary>
        private static byte[] TryReadEmbedded(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string suffix = "." + fileName + ".gz";
                string resourceName = null;

                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName == null) return null;

                using (Stream compressed = assembly.GetManifestResourceStream(resourceName))
                {
                    if (compressed == null) return null;

                    using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                    using (var buffer = new MemoryStream())
                    {
                        gzip.CopyTo(buffer);
                        return buffer.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"EmbeddedSounds: unpacking {fileName} from the DLL failed: {ex.Message}");
                return null;
            }
        }
    }
}
