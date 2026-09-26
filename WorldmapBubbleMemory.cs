using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// One world map position where the game really showed the fishing bubble,
    /// with the facing the player had at that moment.
    /// </summary>
    public sealed class BubblePoint
    {
        /// <summary>Painted water place under the player when the bubble showed (0 = unknown).</summary>
        public int WaterPlaceId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        /// <summary>Unit facing (XZ) of the player when the bubble showed.</summary>
        public float DirX { get; set; }
        public float DirZ { get; set; }
        /// <summary>How many times the bubble was seen here.</summary>
        public int Seen { get; set; } = 1;

        /// <summary>Runtime view of X/Y/Z (not written: a Vector3 cycles the JSON writer).</summary>
        [JsonIgnore]
        public Vector3 Position => new Vector3(X, Y, Z);
        /// <summary>Runtime view of DirX/DirZ.</summary>
        [JsonIgnore]
        public Vector3 Facing => new Vector3(DirX, 0f, DirZ);
    }

    /// <summary>On-disk container of the remembered bubble points of one world map.</summary>
    public sealed class BubblePointFile
    {
        public List<BubblePoint> Points { get; set; } = new List<BubblePoint>();
    }

    /// <summary>
    /// Remembers where the fishing bubble really appeared on the world map. The
    /// bake's stand test cannot call the game's player check
    /// (<c>FieldManager.CheckFishingPoint</c> needs the live player), and at the Lacuer
    /// east lake the real bubble zone lay 7 m beside the baked stand's creep line
    /// (log 2026-09-19 16:17). Every real bubble — reached by auto-walk, by the shore
    /// search or by hand — is therefore saved, and later walks creep from the stand
    /// straight to the nearest remembered point. Points = the embedded seed file
    /// (<c>stands\bubbles_*.json</c>) plus the user's own
    /// <c>UserData\SO2RAccess\stands\bubbles_*.json</c>.
    /// </summary>
    public static class WorldmapBubbleMemory
    {
        /// <summary>Bubbles closer than this to a known point count as the same point.</summary>
        private const float MergeMeters = 2f;

        private static readonly Dictionary<WorldmapID, List<BubblePoint>> _cache =
            new Dictionary<WorldmapID, List<BubblePoint>>();
        /// <summary>The user's own points per map — only these are written back.</summary>
        private static readonly Dictionary<WorldmapID, BubblePointFile> _userFiles =
            new Dictionary<WorldmapID, BubblePointFile>();

        /// <summary>Full path of the user-side memory file for a world map.</summary>
        public static string UserPath(WorldmapID wmID) =>
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess", "stands",
                $"bubbles_{WorldmapFishingStands.MapName(wmID)}.json");

        /// <summary>
        /// Saves a bubble sighting. A sighting within <see cref="MergeMeters"/> of a known
        /// point only raises that point's count.
        /// </summary>
        public static void Record(WorldmapID wmID, int waterPlaceId, Vector3 pos, Vector3 forward)
        {
            var all = Points(wmID);
            var known = Nearest(all, pos, MergeMeters, p => true);
            var user = _userFiles[wmID];
            if (known != null)
            {
                known.Seen++;
                DebugLogger.LogState(
                    $"BubbleMemory: known point ({known.X:F1},{known.Z:F1}) seen again ({known.Seen}×).");
                // Seed points live in the DLL; only the user's own counts are worth a write.
                if (!user.Points.Contains(known)) return;
            }
            else
            {
                forward.y = 0f;
                forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
                var point = new BubblePoint
                {
                    WaterPlaceId = waterPlaceId,
                    X = pos.x, Y = pos.y, Z = pos.z,
                    DirX = forward.x, DirZ = forward.z,
                };
                all.Add(point);
                user.Points.Add(point);
                DebugLogger.LogState(
                    $"BubbleMemory: NEW point ({pos.x:F1},{pos.y:F1},{pos.z:F1}) place {waterPlaceId} " +
                    $"facing ({forward.x:F2},{forward.z:F2}); {all.Count} known on this map.");
            }
            Save(wmID, user);
        }

        /// <summary>Every remembered point of a map (embedded seed plus the user's own) — the bake's ground truth.</summary>
        public static IReadOnlyList<BubblePoint> All(WorldmapID wmID) => Points(wmID);

        /// <summary>Nearest remembered point of a water place to a position, or null.</summary>
        public static BubblePoint NearestForPlace(WorldmapID wmID, int waterPlaceId, Vector3 pos) =>
            Nearest(Points(wmID), pos, float.MaxValue, p => p.WaterPlaceId == waterPlaceId);

        /// <summary>Nearest remembered point within <paramref name="maxMeters"/> of a position, or null.</summary>
        public static BubblePoint NearestWithin(WorldmapID wmID, Vector3 pos, float maxMeters) =>
            Nearest(Points(wmID), pos, maxMeters, p => true);

        private static BubblePoint Nearest(List<BubblePoint> points, Vector3 pos, float maxMeters,
            Predicate<BubblePoint> filter)
        {
            BubblePoint best = null;
            float bestSqr = maxMeters >= float.MaxValue ? float.MaxValue : maxMeters * maxMeters;
            foreach (var p in points)
            {
                if (!filter(p)) continue;
                float dx = p.X - pos.x, dz = p.Z - pos.z;
                float sqr = dx * dx + dz * dz;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = p;
            }
            return best;
        }

        /// <summary>All known points of a map: embedded seed first, then the user's file (cached).</summary>
        private static List<BubblePoint> Points(WorldmapID wmID)
        {
            if (_cache.TryGetValue(wmID, out var cached)) return cached;

            var all = new List<BubblePoint>();
            if (WorldmapFishingStands.MapName(wmID) == null)
            {
                // No planet (INVALID): nothing to read and nothing to write back.
                _userFiles[wmID] = new BubblePointFile();
                _cache[wmID] = all;
                return all;
            }
            var seed = Parse(ReadEmbedded(wmID), "embedded");
            if (seed != null) all.AddRange(seed.Points);

            BubblePointFile user = null;
            try
            {
                string path = UserPath(wmID);
                if (File.Exists(path)) user = Parse(File.ReadAllText(path), path);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] Bubble memory read error: {ex.Message}");
            }
            user ??= new BubblePointFile();
            all.AddRange(user.Points);

            _userFiles[wmID] = user;
            _cache[wmID] = all;
            DebugLogger.LogState(
                $"BubbleMemory: {all.Count} point(s) for {WorldmapFishingStands.MapName(wmID)} " +
                $"({all.Count - user.Points.Count} embedded, {user.Points.Count} own).");
            return all;
        }

        private static BubblePointFile Parse(string json, string source)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var file = JsonSerializer.Deserialize<BubblePointFile>(json);
                return file?.Points == null ? null : file;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] Bubble memory parse error ({source}): {ex.Message}");
                return null;
            }
        }

        private static void Save(WorldmapID wmID, BubblePointFile user)
        {
            try
            {
                string path = UserPath(wmID);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(user,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] Bubble memory write error: {ex.Message}");
            }
        }

        /// <summary>Reads the seed file embedded in the mod DLL, or null.</summary>
        private static string ReadEmbedded(WorldmapID wmID)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string suffix = $"stands.bubbles_{WorldmapFishingStands.MapName(wmID)}.json";
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = asm.GetManifestResourceStream(name);
                    if (s == null) return null;
                    using var reader = new StreamReader(s);
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] Bubble memory embedded read error: {ex.Message}");
            }
            return null;
        }
    }
}
