using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SO2RAccess
{
    /// <summary>
    /// One game-verified fishing stand on the world map: a walkable grid cell
    /// plus the facing direction from which the game's own stand test
    /// (<c>FieldManager.CheckWorldmapFishingPoint</c>) raised the fishing prompt
    /// at bake time. Positions are cell centres with the baked ground height.
    /// </summary>
    public sealed class FishingStandEntry
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        /// <summary>Unit facing direction (XZ) toward the water.</summary>
        public float FaceDX { get; set; }
        public float FaceDZ { get; set; }
        /// <summary>Grid clearance (m) at the cell; 999 = wide open.</summary>
        public float Clearance { get; set; }
        /// <summary>Cells in the 0.60 m-clearance connected region the stand sits in (route comfort class).</summary>
        public int ComfortCells { get; set; }
        /// <summary>True when the cell is also passable for the bunny.</summary>
        public bool BunnyOk { get; set; }
        /// <summary>Flat distance (m) to the water place's parameter position.</summary>
        public float DistToParam { get; set; }
        /// <summary>
        /// Metres the player's body can move from the stand before a wall collider
        /// stops it (8 compass casts, capped at 2 = nothing within 2 m). Stands whose
        /// body already overlapped a wall were dropped at bake time. -1 = older file,
        /// not measured.
        /// </summary>
        public float WallClearance { get; set; } = -1f;
        /// <summary>
        /// Flat distance (m) from the stand to the nearest town entrance trigger
        /// ("Press Cross to Enter" zone). Stands INSIDE a trigger (0 m) are dropped at
        /// bake time: Cross enters the town there, so the bubble could never be used.
        /// -1 = older file or no ring data at bake time.
        /// </summary>
        public float RingDistance { get; set; } = -1f;
        /// <summary>
        /// Entrance the bake walked a body-swept route from (its fieldmap ID, e.g.
        /// MF_0009_01A), or null when no swept route from any tried entrance passed.
        /// Null in a file whose <see cref="FishingStandFile.ProofsBaked"/> is false means
        /// "unknown", never "unreachable".
        /// </summary>
        public string ProvenFrom { get; set; }
        /// <summary>"comfort" (0.60 m route) or "floor" (0.50 m route) for a proven stand, else null.</summary>
        public string ProofTier { get; set; }
        /// <summary>Length (m) of the proven route.</summary>
        public float ProofRouteMeters { get; set; }
        /// <summary>Plan-and-sweep rounds the proof took (1 = first route passed).</summary>
        public int ProofRounds { get; set; }
        /// <summary>
        /// True when the bake's enclosure test found that the player's body cannot
        /// get more than a few metres away from this stand (a shore pocket walled
        /// in beside a town gate, 2026-09-13 Arlia). Never proven; never chosen.
        /// </summary>
        public bool Enclosed { get; set; }

        /// <summary>True when a body-swept route from an entrance reached this stand at bake time.</summary>
        [JsonIgnore]
        public bool Proven => !string.IsNullOrEmpty(ProvenFrom);

        /// <summary>
        /// True when the bake's proof route was no longer than the sweep's start
        /// exemption, so not one segment of it was body-swept: the stand is
        /// "proven" by the grid alone (2026-09-20: Hilton 737,−172.5, a 6.8 m proof
        /// with the gate fences between the player and the stand; Krosse has the
        /// same kind of proof and is fine). The walk sweeps the goal zone itself
        /// for these. 0 m = older file, unknown, treated as a real proof.
        /// </summary>
        [JsonIgnore]
        public bool GridOnlyProof => Proven && ProofRouteMeters > 0f
            && ProofRouteMeters <= NavigationHandler.WmSweepEndpointExemptDist;

        /// <summary>Runtime convenience view of X/Y/Z. Not written to the file: a Vector3 self-references through <c>normalized</c> and cycles the JSON writer.</summary>
        [JsonIgnore]
        public UnityEngine.Vector3 Position => new UnityEngine.Vector3(X, Y, Z);
        /// <summary>Runtime convenience view of FaceDX/FaceDZ. Not written to the file (see <see cref="Position"/>).</summary>
        [JsonIgnore]
        public UnityEngine.Vector3 Facing => new UnityEngine.Vector3(FaceDX, 0f, FaceDZ);
    }

    /// <summary>Stands for one water place (one body of water in the parameter database).</summary>
    public sealed class FishingPlaceStands
    {
        public int WaterPlaceId { get; set; }
        public float ParamX { get; set; }
        public float ParamZ { get; set; }
        /// <summary>Shoreline cells examined for this place at bake time.</summary>
        public int Candidates { get; set; }
        /// <summary>Candidates the game's stand test accepted.</summary>
        public int Verified { get; set; }
        /// <summary>True when even the designated stand sits below the comfort clearance (floor-tier route only).</summary>
        public bool FloorTierOnly { get; set; }
        /// <summary>
        /// True when the bake ran the route proof for this place. False (time
        /// budget exhausted, or an old file) means the proof fields are unknown
        /// and the runtime must not annotate the place.
        /// </summary>
        public bool ProofAttempted { get; set; }
        /// <summary>Stands with a proven route (see <see cref="FishingStandEntry.ProvenFrom"/>).</summary>
        public int ProvenStands { get; set; }
        /// <summary>Plan-and-sweep attempts the proof spent on this place.</summary>
        public int ProofAttempts { get; set; }
        /// <summary>
        /// The verified shoreline, one stand per 5 m (version 3). Proven stands
        /// come first (comfort route before floor route), then the bake's rank;
        /// the runtime chooses by distance to the player, not by index.
        /// </summary>
        public List<FishingStandEntry> Stands { get; set; } = new List<FishingStandEntry>();

        /// <summary>True when the proof ran here and at least one stand has a swept route.</summary>
        [JsonIgnore]
        public bool HasProvenStand => ProofAttempted && Stands.Exists(s => s.Proven);
    }

    /// <summary>The whole stands file for one world map.</summary>
    public sealed class FishingStandFile
    {
        /// <summary>
        /// 1 = stands only (2026-09-07); 2 = adds the entrance route proofs (2026-09-08);
        /// 3 = the whole verified shoreline at 5 m spacing instead of six stands per
        /// place (2026-09-09). Older files still load — the runtime picks the nearest
        /// of whatever stands a place has.
        /// </summary>
        public int Version { get; set; } = 3;
        public string WorldmapId { get; set; }
        public string BakedAt { get; set; }
        /// <summary>
        /// True when the bake's route proof phase ran (Version 2 with entrance
        /// anchors found). A version-1 file deserializes with false, so its
        /// stands are "proof unknown" and never annotated unreachable.
        /// </summary>
        public bool ProofsBaked { get; set; }
        /// <summary>Entrance anchors the proof could start from.</summary>
        public int ProofAnchors { get; set; }
        /// <summary>The game's forward probe distance (m) used at bake time.</summary>
        public float FrontDistance { get; set; }
        /// <summary>Clearance floor (m) of the comfort route tier the selection assumed.</summary>
        public float ComfortClearance { get; set; }
        public float GridCellSize { get; set; }
        public List<FishingPlaceStands> Places { get; set; } = new List<FishingPlaceStands>();
    }

    /// <summary>
    /// Loads and saves the baked world map fishing stands. The user's own bake in
    /// <c>UserData\SO2RAccess\stands\</c> wins; otherwise the copy embedded in the
    /// mod DLL (<c>stands\worldmap_*.json</c>, same pattern as the traversal
    /// graphs). No file at all means the map has no baked stands — callers skip
    /// world map fishing spots there and say so in the log.
    /// </summary>
    public static class WorldmapFishingStands
    {
        private static readonly string Dir =
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess", "stands");

        private static readonly Dictionary<WorldmapID, FishingStandFile> _cache =
            new Dictionary<WorldmapID, FishingStandFile>();
        private static readonly HashSet<WorldmapID> _missingLogged = new HashSet<WorldmapID>();

        /// <summary>File name stem for a world map ("expel" / "nede"), matching the grid files.</summary>
        public static string MapName(WorldmapID wmID) =>
            wmID == WorldmapID.EXPEL ? "expel" : "nede";

        /// <summary>Full path of the user-side stands file for a world map.</summary>
        public static string UserPath(WorldmapID wmID) =>
            Path.Combine(Dir, $"worldmap_{MapName(wmID)}.json");

        /// <summary>
        /// Returns the stands for a world map (cached), or null when neither a
        /// user file nor an embedded copy exists. The "missing" case is logged once.
        /// </summary>
        public static FishingStandFile Load(WorldmapID wmID)
        {
            if (_cache.TryGetValue(wmID, out var cached)) return cached;

            FishingStandFile file = null;
            try
            {
                string path = UserPath(wmID);
                string json = File.Exists(path) ? File.ReadAllText(path) : ReadEmbedded(wmID);
                if (!string.IsNullOrEmpty(json))
                {
                    file = JsonSerializer.Deserialize<FishingStandFile>(json);
                    if (file?.Places == null) file = null;
                    else
                        // Always logged (once per map): the source line is the support
                        // answer to "which stands file is the game actually using?"
                        MelonLoader.MelonLogger.Msg(
                            $"[SO2RAccess] Fishing stands: loaded {file.Places.Count} water places " +
                            $"for {MapName(wmID)} (baked {file.BakedAt}, " +
                            $"source={(File.Exists(path) ? "UserData" : "embedded")}).");
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] Fishing stands load error ({MapName(wmID)}): {ex.Message}");
                file = null;
            }

            if (file == null && _missingLogged.Add(wmID))
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] No fishing stands file for {MapName(wmID)} — world map " +
                    "fishing spots are skipped there until one is baked (Insert in debug mode).");

            _cache[wmID] = file;
            return file;
        }

        /// <summary>Path of the backup the previous user file is copied to before a new bake overwrites it.</summary>
        public static string PreviousPath(WorldmapID wmID) =>
            Path.Combine(Dir, $"worldmap_{MapName(wmID)}.previous.json");

        /// <summary>
        /// Reads the current user-side file straight from disk (no cache, no
        /// embedded fallback) so a bake can compare its result against what the
        /// player had before. Null when there is none or it cannot be parsed.
        /// </summary>
        public static FishingStandFile ReadUserFile(WorldmapID wmID)
        {
            try
            {
                string path = UserPath(wmID);
                if (!File.Exists(path)) return null;
                var file = JsonSerializer.Deserialize<FishingStandFile>(File.ReadAllText(path));
                return file?.Places == null ? null : file;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] Fishing stands previous-file read error ({MapName(wmID)}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes the file to UserData and returns its path. An existing file is
        /// first copied to <see cref="PreviousPath"/> so no bake ever destroys
        /// the last known-good stands.
        /// </summary>
        public static string Save(WorldmapID wmID, FishingStandFile file)
        {
            Directory.CreateDirectory(Dir);
            string path = UserPath(wmID);
            if (File.Exists(path)) File.Copy(path, PreviousPath(wmID), true);
            File.WriteAllText(path, JsonSerializer.Serialize(file,
                new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        /// <summary>Forgets cached files so the next Load re-reads them (after a bake).</summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _missingLogged.Clear();
        }

        /// <summary>Finds a water place's stands by ID, or null.</summary>
        public static FishingPlaceStands TryGetPlace(FishingStandFile file, int waterPlaceId) =>
            file?.Places.Find(p => p.WaterPlaceId == waterPlaceId);

        /// <summary>Reads the stands file embedded in the mod DLL, or null.</summary>
        private static string ReadEmbedded(WorldmapID wmID)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string suffix = $"stands.worldmap_{MapName(wmID)}.json";
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = asm.GetManifestResourceStream(name);
                    if (s == null) return null;
                    using var r = new StreamReader(s);
                    return r.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] Fishing stands embedded read error: {ex.Message}");
            }
            return null;
        }
    }
}
