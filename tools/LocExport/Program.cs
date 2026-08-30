using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocExport
{
    /// <summary>
    /// Exports the string table from Loc.cs to lang\en.json.
    ///
    /// Values come from the compiled Loc dictionary (reflection), so every
    /// escape sequence and multi-line concatenation is byte-exact. Loc.cs is
    /// scanned as text only for ordering and the // comment headers, which
    /// become "_"-prefixed marker keys that the mod's loader skips.
    /// </summary>
    internal static class Program
    {
        private const string LocSourcePath = @"E:\StarOcean\Loc.cs";
        private const string OutputPath = @"E:\StarOcean\lang\en.json";

        private static int Main()
        {
            // 1. Reflected truth: run Loc.Initialize() and read the private dictionary.
            SO2RAccess.Loc.Initialize();
            FieldInfo stringsField = typeof(SO2RAccess.Loc).GetField(
                "_strings", BindingFlags.NonPublic | BindingFlags.Static);
            if (stringsField == null)
            {
                Console.WriteLine("FAIL: could not find Loc._strings via reflection.");
                return 1;
            }
            var reflected = (Dictionary<string, string>)stringsField.GetValue(null);
            Console.WriteLine($"Reflected dictionary: {reflected.Count} keys.");

            // 2. Text scan of InitializeStrings() for ordering + comments.
            List<KeyValuePair<string, string>> entries = BuildOrderedEntries(reflected);
            if (entries == null) return 1;

            // 3. Write en.json (UTF-8, indented, flat object, insertion order).
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            WriteJson(OutputPath, entries);
            Console.WriteLine($"Wrote {OutputPath} ({entries.Count} entries incl. markers).");

            // 4. Round-trip verification: every reflected key present exactly once
            //    with an identical value; no stray real keys in the file.
            return Verify(reflected) ? 0 : 1;
        }

        private static List<KeyValuePair<string, string>> BuildOrderedEntries(
            Dictionary<string, string> reflected)
        {
            string[] lines = File.ReadAllLines(LocSourcePath);
            int start = Array.FindIndex(lines, l => l.Contains("private static void InitializeStrings()"));
            if (start < 0)
            {
                Console.WriteLine("FAIL: InitializeStrings() not found in Loc.cs.");
                return null;
            }

            var entries = new List<KeyValuePair<string, string>>();
            var emittedKeys = new HashSet<string>();
            var markerCounts = new Dictionary<string, int>();
            var commentRun = new List<string>();
            var addKeyRegex = new Regex("^Add\\(\\s*\"([^\"]+)\"");

            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == "}" && lines[i].StartsWith("        }")) break; // end of method body

                if (line.StartsWith("//"))
                {
                    commentRun.Add(line.TrimStart('/', ' '));
                    continue;
                }

                // A non-comment line ends any pending comment run: emit it as a
                // marker key at this position so it survives into the JSON.
                if (commentRun.Count > 0)
                {
                    string markerKey = MakeMarkerKey(commentRun[0], markerCounts);
                    entries.Add(new KeyValuePair<string, string>(
                        markerKey, string.Join(" ", commentRun)));
                    commentRun.Clear();
                }

                Match m = addKeyRegex.Match(line);
                if (!m.Success) continue;
                string key = m.Groups[1].Value;

                if (!reflected.TryGetValue(key, out string value))
                {
                    Console.WriteLine($"FAIL: key '{key}' parsed from source but not in dictionary.");
                    return null;
                }
                if (!emittedKeys.Add(key))
                {
                    Console.WriteLine($"FAIL: key '{key}' emitted twice.");
                    return null;
                }
                entries.Add(new KeyValuePair<string, string>(key, value));
            }

            if (emittedKeys.Count != reflected.Count)
            {
                Console.WriteLine($"FAIL: parsed {emittedKeys.Count} keys from source, " +
                                  $"dictionary has {reflected.Count}.");
                foreach (string key in reflected.Keys)
                    if (!emittedKeys.Contains(key))
                        Console.WriteLine($"  missing from source scan: {key}");
                return null;
            }
            Console.WriteLine($"Source scan: {emittedKeys.Count} keys, " +
                              $"{entries.Count - emittedKeys.Count} comment markers, order preserved.");
            return entries;
        }

        /// <summary>Turns the first line of a comment run into a unique "_section_..." key.</summary>
        private static string MakeMarkerKey(string firstLine, Dictionary<string, int> counts)
        {
            string slug = Regex.Replace(firstLine.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            string[] words = slug.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 6) slug = string.Join("_", words, 0, 6);
            if (slug.Length == 0) slug = "comment";

            string key = "_section_" + slug;
            if (counts.TryGetValue(key, out int n))
            {
                counts[key] = n + 1;
                return $"{key}_{n + 1}";
            }
            counts[key] = 1;
            return key;
        }

        private static void WriteJson(string path, List<KeyValuePair<string, string>> entries)
        {
            var options = new JsonWriterOptions
            {
                Indented = true,
                // Keeps quotes as \" and non-ASCII readable — this file is for humans.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            using var stream = File.Create(path);
            using var writer = new Utf8JsonWriter(stream, options);
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> entry in entries)
                writer.WriteString(entry.Key, entry.Value);
            writer.WriteEndObject();
        }

        private static bool Verify(Dictionary<string, string> reflected)
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(OutputPath, Encoding.UTF8));

            int realKeys = 0;
            foreach (KeyValuePair<string, string> entry in loaded)
            {
                if (entry.Key.StartsWith("_")) continue;
                realKeys++;
                if (!reflected.TryGetValue(entry.Key, out string expected))
                {
                    Console.WriteLine($"FAIL: file has unknown key '{entry.Key}'.");
                    return false;
                }
                if (!string.Equals(expected, entry.Value, StringComparison.Ordinal))
                {
                    Console.WriteLine($"FAIL: value mismatch for '{entry.Key}'.");
                    return false;
                }
            }
            if (realKeys != reflected.Count)
            {
                Console.WriteLine($"FAIL: file has {realKeys} real keys, dictionary has {reflected.Count}.");
                return false;
            }
            Console.WriteLine($"PASS: round-trip verified, {realKeys} keys match byte-exactly.");
            return true;
        }
    }
}
