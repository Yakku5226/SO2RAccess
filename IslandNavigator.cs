using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace SO2RAccess
{
    // ── Serializable data structures ─────────────────────────────────────

    /// <summary>One NavMesh island on a map.</summary>
    public class IslandData
    {
        public int Id { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float CenterZ { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }
        public float MinZ { get; set; }
        public float MaxZ { get; set; }
        public int SampleCount { get; set; }
    }

    /// <summary>A confirmed walkable connection between two islands.</summary>
    public class BridgeData
    {
        public int IslandA { get; set; }
        public int IslandB { get; set; }
        public float CrossPointAX { get; set; }
        public float CrossPointAY { get; set; }
        public float CrossPointAZ { get; set; }
        public float CrossPointBX { get; set; }
        public float CrossPointBY { get; set; }
        public float CrossPointBZ { get; set; }
        public bool Bidirectional { get; set; }
    }

    /// <summary>A potential (unconfirmed) connection between two islands.</summary>
    public class GapData
    {
        public int IslandA { get; set; }
        public int IslandB { get; set; }
        public float PointAX { get; set; }
        public float PointAY { get; set; }
        public float PointAZ { get; set; }
        public float PointBX { get; set; }
        public float PointBY { get; set; }
        public float PointBZ { get; set; }
        public float Distance { get; set; }
        public float YDelta { get; set; }
        public bool Blocked { get; set; }
    }

    /// <summary>Per-map container saved to disk.</summary>
    public class MapIslandGraph
    {
        public string MapId { get; set; }
        public int Version { get; set; }
        public long ScanTimestamp { get; set; }
        public List<IslandData> Islands { get; set; } = new List<IslandData>();
        public List<BridgeData> Bridges { get; set; } = new List<BridgeData>();
        public List<GapData> Gaps { get; set; } = new List<GapData>();
    }

    // ── Route planning structures ────────────────────────────────────────

    /// <summary>One segment in a multi-island route.</summary>
    public struct RouteSegment
    {
        /// <summary>Island the player is on for this segment.</summary>
        public int FromIsland;

        /// <summary>Island the player crosses to after walking this segment.</summary>
        public int ToIsland;

        /// <summary>Point to walk toward on the current island (bridge/gap edge).</summary>
        public Vector3 WalkTarget;

        /// <summary>Point on the destination island after crossing.</summary>
        public Vector3 ArrivalPoint;

        /// <summary>True if this crossing uses a confirmed bridge.</summary>
        public bool IsConfirmed;
    }

    /// <summary>Result of route planning.</summary>
    public class RouteResult
    {
        /// <summary>Ordered segments to walk and cross.</summary>
        public List<RouteSegment> Segments;

        /// <summary>True if the route contains any speculative (gap) crossings.</summary>
        public bool HasSpeculativeSegments;

        /// <summary>Number of speculative crossings in the route.</summary>
        public int SpeculativeCount;
    }

    // ── Navigator (runtime logic) ────────────────────────────────────────

    /// <summary>
    /// Manages the island graph for the current map: loading, saving,
    /// bridge recording, and route planning via BFS.
    /// </summary>
    public class IslandNavigator
    {
        /// <summary>Directory for per-map island JSON files.</summary>
        private static readonly string IslandsDir =
            Path.Combine(Directory.GetCurrentDirectory(),
                "UserData", "SO2RAccess", "islands");

        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { WriteIndented = true };

        private MapIslandGraph _graph;

        /// <summary>The loaded/scanned island graph, or null.</summary>
        public MapIslandGraph Graph => _graph;

        /// <summary>True if a graph is loaded for the current map.</summary>
        public bool HasGraph => _graph != null && _graph.Islands.Count > 0;

        // ── Load / Save ──────────────────────────────────────────────────

        /// <summary>
        /// Loads or scans the island graph for a map. Tries disk cache
        /// first; if not found, runs <see cref="IslandScanner.Scan"/>.
        /// Merges any previously saved bridges into a fresh scan.
        /// </summary>
        public void LoadOrScan(string mapId)
        {
            string path = GetFilePath(mapId);

            // Try loading from disk.
            MapIslandGraph cached = LoadFromDisk(path);

            // Always re-scan to get fresh island/gap data (NavMesh is
            // authoritative). Then merge bridges from cached data.
            MapIslandGraph scanned = IslandScanner.Scan(mapId);

            if (scanned == null)
            {
                // No NavMesh on this map. Use cached if available.
                _graph = cached;
                return;
            }

            if (cached != null && cached.Bridges.Count > 0)
            {
                MergeBridges(scanned, cached);
            }

            _graph = scanned;
            SaveToDisk();

            MelonLoader.MelonLogger.Msg(
                $"[SO2RAccess] ISLAND: graph ready for {mapId}. " +
                $"{_graph.Islands.Count} islands, " +
                $"{_graph.Bridges.Count} bridges, " +
                $"{_graph.Gaps.Count} gaps");

            // Diagnostic: dump FieldStairs data and cross-reference with islands.
            LogFieldStairsDiagnostic();
        }

        /// <summary>Persists the current graph to disk.</summary>
        public void SaveToDisk()
        {
            if (_graph == null) return;
            try
            {
                if (!Directory.Exists(IslandsDir))
                    Directory.CreateDirectory(IslandsDir);

                string path = GetFilePath(_graph.MapId);
                string json = JsonSerializer.Serialize(_graph, JsonOpts);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"ISLAND: save failed: {ex.Message}");
            }
        }

        private static MapIslandGraph LoadFromDisk(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<MapIslandGraph>(json);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"ISLAND: load failed: {ex.Message}");
                return null;
            }
        }

        private static string GetFilePath(string mapId)
        {
            return Path.Combine(IslandsDir, $"{mapId}.json");
        }

        // ── Bridge recording ─────────────────────────────────────────────

        /// <summary>
        /// Records a confirmed bridge between two islands. Deduplicates
        /// and updates bidirectional flag if the reverse already exists.
        /// Saves to disk immediately.
        /// </summary>
        public void RecordBridge(int fromIsland, int toIsland,
            Vector3 crossPointA, Vector3 crossPointB)
        {
            if (_graph == null) return;
            if (fromIsland == toIsland || fromIsland < 0 || toIsland < 0)
                return;

            // Normalize: smaller ID is always IslandA.
            int idA = Math.Min(fromIsland, toIsland);
            int idB = Math.Max(fromIsland, toIsland);
            Vector3 ptA = fromIsland <= toIsland ? crossPointA : crossPointB;
            Vector3 ptB = fromIsland <= toIsland ? crossPointB : crossPointA;

            // Check if bridge already exists.
            for (int i = 0; i < _graph.Bridges.Count; i++)
            {
                var b = _graph.Bridges[i];
                if (b.IslandA == idA && b.IslandB == idB)
                {
                    // Already exists. Check if we need to mark bidirectional.
                    if (!b.Bidirectional)
                    {
                        // Original was recorded in one direction.
                        // If this crossing is in the other direction,
                        // mark bidirectional.
                        bool sameDir =
                            (fromIsland <= toIsland);
                        if (!sameDir)
                        {
                            b.Bidirectional = true;
                            _graph.Bridges[i] = b;
                            SaveToDisk();
                            DebugLogger.LogState(
                                $"ISLAND: bridge {idA}<->{idB} now bidirectional");
                        }
                    }
                    return;
                }
            }

            // New bridge.
            var bridge = new BridgeData
            {
                IslandA = idA,
                IslandB = idB,
                CrossPointAX = ptA.x,
                CrossPointAY = ptA.y,
                CrossPointAZ = ptA.z,
                CrossPointBX = ptB.x,
                CrossPointBY = ptB.y,
                CrossPointBZ = ptB.z,
                Bidirectional = false
            };
            _graph.Bridges.Add(bridge);
            SaveToDisk();

            MelonLoader.MelonLogger.Msg(
                $"[SO2RAccess] ISLAND: bridge recorded {idA}->{idB} at " +
                $"({ptA.x:F1},{ptA.y:F1},{ptA.z:F1})->(" +
                $"{ptB.x:F1},{ptB.y:F1},{ptB.z:F1})");
        }

        /// <summary>
        /// Marks a gap as blocked (crossing attempt failed).
        /// Saves to disk immediately.
        /// </summary>
        public void MarkGapBlocked(int islandA, int islandB)
        {
            if (_graph == null) return;

            int idA = Math.Min(islandA, islandB);
            int idB = Math.Max(islandA, islandB);

            for (int i = 0; i < _graph.Gaps.Count; i++)
            {
                var g = _graph.Gaps[i];
                if (g.IslandA == idA && g.IslandB == idB)
                {
                    g.Blocked = true;
                    _graph.Gaps[i] = g;
                    SaveToDisk();
                    DebugLogger.LogState(
                        $"ISLAND: gap {idA}<->{idB} marked blocked");
                    return;
                }
            }
        }

        // ── Island lookup ────────────────────────────────────────────────

        /// <summary>
        /// Determines which island a world position is on.
        /// Returns -1 if no island found.
        /// </summary>
        public int GetIsland(Vector3 pos)
        {
            if (_graph == null) return -1;
            return IslandScanner.FindIsland(pos, _graph.Islands);
        }

        // ── Route planning ───────────────────────────────────────────────

        /// <summary>
        /// Plans a route from the player's island to the target's island.
        /// Returns null if no route exists (even speculatively).
        /// </summary>
        /// <param name="fromIsland">Player's current island ID.</param>
        /// <param name="toIsland">Target's island ID.</param>
        /// <returns>
        /// A <see cref="RouteResult"/> with segments, or null if unreachable.
        /// </returns>
        public RouteResult PlanRoute(int fromIsland, int toIsland,
            HashSet<int> avoidTransit = null)
        {
            if (_graph == null || fromIsland < 0 || toIsland < 0)
                return null;
            if (fromIsland == toIsland)
                return null; // Same island, no multi-segment needed.

            // Prefer routes that do NOT pass through islands containing a map
            // exit (avoidTransit) — walking across such an island would trip a
            // map transition and warp the player out. Try those first; only if
            // no safe route exists do we fall back to allowing transit islands
            // (the walk-time exit-zone steering is the safety net there).
            for (int pass = 0; pass < 2; pass++)
            {
                HashSet<int> avoid = pass == 0 ? avoidTransit : null;

                // Tier 1: Try confirmed bridges only.
                var confirmedPath = BfsIslandPath(fromIsland, toIsland,
                    useBridges: true, useGaps: false, avoidTransit: avoid);
                if (confirmedPath != null)
                    return BuildRoute(confirmedPath, fromIsland);

                // Tier 2: Try confirmed bridges + non-blocked gaps.
                var specPath = BfsIslandPath(fromIsland, toIsland,
                    useBridges: true, useGaps: true, avoidTransit: avoid);
                if (specPath != null)
                    return BuildRoute(specPath, fromIsland);

                // If no avoidance set was supplied, the second pass is identical
                // to the first — don't repeat it.
                if (avoidTransit == null) break;
            }

            return null;
        }

        /// <summary>
        /// BFS on the island graph. Returns the path as a list of
        /// (islandId, edgeIsConfirmed) pairs, starting with fromIsland.
        /// Returns null if target is unreachable.
        /// </summary>
        private List<(int island, bool confirmed)> BfsIslandPath(
            int from, int to, bool useBridges, bool useGaps,
            HashSet<int> avoidTransit = null)
        {
            // Build adjacency list.
            var adj = new Dictionary<int, List<(int neighbor, bool confirmed)>>();
            for (int i = 0; i < _graph.Islands.Count; i++)
                adj[_graph.Islands[i].Id] = new List<(int, bool)>();

            if (useBridges)
            {
                foreach (var b in _graph.Bridges)
                {
                    if (!adj.ContainsKey(b.IslandA) || !adj.ContainsKey(b.IslandB))
                        continue;
                    adj[b.IslandA].Add((b.IslandB, true));
                    // For now treat all bridges as bidirectional for routing.
                    // The unidirectional flag is informational.
                    adj[b.IslandB].Add((b.IslandA, true));
                }
            }

            if (useGaps)
            {
                foreach (var g in _graph.Gaps)
                {
                    if (g.Blocked) continue;
                    if (!adj.ContainsKey(g.IslandA) || !adj.ContainsKey(g.IslandB))
                        continue;
                    // Only add if no confirmed bridge already exists.
                    if (!HasBridge(g.IslandA, g.IslandB))
                    {
                        adj[g.IslandA].Add((g.IslandB, false));
                        adj[g.IslandB].Add((g.IslandA, false));
                    }
                }
            }

            // BFS.
            var visited = new HashSet<int>();
            var parent = new Dictionary<int, (int prev, bool confirmed)>();
            var queue = new Queue<int>();

            visited.Add(from);
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == to)
                    break;

                if (!adj.ContainsKey(current)) continue;

                foreach (var (neighbor, confirmed) in adj[current])
                {
                    if (visited.Contains(neighbor)) continue;
                    // Don't route THROUGH an exit-bearing island — but allow it
                    // as the final destination (the chest/NPC may be on it).
                    if (avoidTransit != null && neighbor != to &&
                        avoidTransit.Contains(neighbor))
                        continue;
                    visited.Add(neighbor);
                    parent[neighbor] = (current, confirmed);
                    queue.Enqueue(neighbor);
                }
            }

            if (!visited.Contains(to))
                return null;

            // Reconstruct path.
            var path = new List<(int island, bool confirmed)>();
            int node = to;
            while (node != from)
            {
                var (prev, conf) = parent[node];
                path.Add((node, conf));
                node = prev;
            }
            path.Add((from, true)); // Start node, edge type irrelevant.
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Converts a BFS island path into route segments with walk
        /// targets and arrival points from bridge/gap data.
        /// </summary>
        private RouteResult BuildRoute(
            List<(int island, bool confirmed)> path, int fromIsland)
        {
            var segments = new List<RouteSegment>();
            int specCount = 0;

            for (int i = 0; i < path.Count - 1; i++)
            {
                int currentIsland = path[i].island;
                int nextIsland = path[i + 1].island;
                bool isConfirmed = path[i + 1].confirmed;

                Vector3 walkTarget;
                Vector3 arrivalPoint;

                if (isConfirmed)
                {
                    var bridge = FindBridge(currentIsland, nextIsland);
                    if (bridge != null)
                    {
                        GetBridgePoints(bridge, currentIsland,
                            out walkTarget, out arrivalPoint);
                    }
                    else
                    {
                        // Shouldn't happen, but fallback to gap.
                        isConfirmed = false;
                        GetGapPoints(currentIsland, nextIsland,
                            out walkTarget, out arrivalPoint);
                    }
                }
                else
                {
                    specCount++;
                    GetGapPoints(currentIsland, nextIsland,
                        out walkTarget, out arrivalPoint);
                }

                segments.Add(new RouteSegment
                {
                    FromIsland = currentIsland,
                    ToIsland = nextIsland,
                    WalkTarget = walkTarget,
                    ArrivalPoint = arrivalPoint,
                    IsConfirmed = isConfirmed
                });
            }

            return new RouteResult
            {
                Segments = segments,
                HasSpeculativeSegments = specCount > 0,
                SpeculativeCount = specCount
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>Orders an island pair so (a,b) and (b,a) compare equal.</summary>
        private static (int, int) NormalizePair(int idA, int idB)
        {
            return idA <= idB ? (idA, idB) : (idB, idA);
        }

        private bool HasBridge(int idA, int idB)
        {
            int a = Math.Min(idA, idB);
            int b = Math.Max(idA, idB);
            foreach (var br in _graph.Bridges)
                if (br.IslandA == a && br.IslandB == b)
                    return true;
            return false;
        }

        private BridgeData FindBridge(int islandFrom, int islandTo)
        {
            int a = Math.Min(islandFrom, islandTo);
            int b = Math.Max(islandFrom, islandTo);
            foreach (var br in _graph.Bridges)
                if (br.IslandA == a && br.IslandB == b)
                    return br;
            return null;
        }

        /// <summary>
        /// Gets walk target and arrival point from a bridge, oriented
        /// so WalkTarget is on <paramref name="currentIsland"/>.
        /// </summary>
        private void GetBridgePoints(BridgeData bridge, int currentIsland,
            out Vector3 walkTarget, out Vector3 arrivalPoint)
        {
            var ptA = new Vector3(
                bridge.CrossPointAX, bridge.CrossPointAY, bridge.CrossPointAZ);
            var ptB = new Vector3(
                bridge.CrossPointBX, bridge.CrossPointBY, bridge.CrossPointBZ);

            if (currentIsland == bridge.IslandA)
            {
                walkTarget = ptA;
                arrivalPoint = ptB;
            }
            else
            {
                walkTarget = ptB;
                arrivalPoint = ptA;
            }
        }

        /// <summary>
        /// Gets walk target and arrival point from gap data.
        /// </summary>
        private void GetGapPoints(int currentIsland, int targetIsland,
            out Vector3 walkTarget, out Vector3 arrivalPoint)
        {
            int a = Math.Min(currentIsland, targetIsland);
            int b = Math.Max(currentIsland, targetIsland);

            foreach (var g in _graph.Gaps)
            {
                if (g.IslandA == a && g.IslandB == b)
                {
                    var ptA = new Vector3(g.PointAX, g.PointAY, g.PointAZ);
                    var ptB = new Vector3(g.PointBX, g.PointBY, g.PointBZ);

                    if (currentIsland == g.IslandA)
                    {
                        walkTarget = ptA;
                        arrivalPoint = ptB;
                    }
                    else
                    {
                        walkTarget = ptB;
                        arrivalPoint = ptA;
                    }
                    return;
                }
            }

            // No gap data — shouldn't reach here via BFS, but fallback.
            walkTarget = Vector3.zero;
            arrivalPoint = Vector3.zero;
        }

        /// <summary>
        /// Merges bridges from a cached graph into a freshly scanned graph.
        /// Only copies bridges whose island IDs still exist in the new scan.
        /// </summary>
        private static void MergeBridges(MapIslandGraph target, MapIslandGraph cached)
        {
            var validIds = new HashSet<int>();
            foreach (var isl in target.Islands)
                validIds.Add(isl.Id);

            // Seed with the island pairs the fresh scan already produced so we
            // never add a duplicate. Pairs are unordered — scanner-generated
            // bridges are not normalized, so compare both orderings.
            var existingPairs = new HashSet<(int, int)>();
            foreach (var b in target.Bridges)
                existingPairs.Add(NormalizePair(b.IslandA, b.IslandB));

            int merged = 0;
            foreach (var bridge in cached.Bridges)
            {
                if (!validIds.Contains(bridge.IslandA) ||
                    !validIds.Contains(bridge.IslandB))
                    continue;

                var pair = NormalizePair(bridge.IslandA, bridge.IslandB);
                if (existingPairs.Contains(pair))
                    continue; // Already present from the fresh scan — skip dup.

                existingPairs.Add(pair);
                target.Bridges.Add(bridge);
                merged++;
            }

            // Also merge blocked gap state.
            var blockedPairs = new HashSet<(int, int)>();
            foreach (var g in cached.Gaps)
            {
                if (g.Blocked)
                    blockedPairs.Add((g.IslandA, g.IslandB));
            }

            for (int i = 0; i < target.Gaps.Count; i++)
            {
                var g = target.Gaps[i];
                if (blockedPairs.Contains((g.IslandA, g.IslandB)))
                {
                    g.Blocked = true;
                    target.Gaps[i] = g;
                }
            }

            if (merged > 0)
                DebugLogger.LogState(
                    $"ISLAND: merged {merged} bridges from cache");
        }

        /// <summary>
        /// Diagnostic: logs all FieldStairs objects on the current map and
        /// which island each connects to. Helps verify that stair objects
        /// can serve as reliable bridge data.
        /// </summary>
        private void LogFieldStairsDiagnostic()
        {
            if (_graph == null) return;

            try
            {
                var fm = Il2CppGame.FieldManager.Instance;
                if (fm == null) return;

                var stairsList = fm.FieldStairsList;
                if (stairsList == null || stairsList.Count == 0)
                {
                    MelonLoader.MelonLogger.Msg(
                        "[SO2RAccess] STAIRS DIAG: no FieldStairs on this map.");
                    return;
                }

                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] STAIRS DIAG: {stairsList.Count} FieldStairs found:");

                for (int i = 0; i < stairsList.Count; i++)
                {
                    try
                    {
                        var stair = stairsList[i];
                        if (stair == null) continue;

                        Vector3 pos = stair.transform.position;
                        bool isUpper = stair.isUpperStage;
                        float height = stair.height;
                        var icon = stair.iconType;

                        // Determine which island this stair position is on.
                        int island = IslandScanner.FindIsland(pos, _graph.Islands);

                        // Also check the collider bounds if available.
                        string boundsStr = "";
                        try
                        {
                            var col = stair.GetComponent<UnityEngine.Collider>();
                            if (col != null)
                            {
                                var b = col.bounds;
                                boundsStr = $" bounds=({b.size.x:F1},{b.size.y:F1},{b.size.z:F1})";
                            }
                        }
                        catch { }

                        MelonLoader.MelonLogger.Msg(
                            $"[SO2RAccess] STAIRS DIAG: [{i}] " +
                            $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                            $"upper={isUpper} height={height:F1} " +
                            $"icon={icon} island={island}{boundsStr}");
                    }
                    catch (Exception ex)
                    {
                        MelonLoader.MelonLogger.Msg(
                            $"[SO2RAccess] STAIRS DIAG: [{i}] error: {ex.Message}");
                    }
                }

                // Also log island summary for cross-reference.
                MelonLoader.MelonLogger.Msg(
                    "[SO2RAccess] STAIRS DIAG: Island summary:");
                foreach (var isl in _graph.Islands)
                {
                    if (isl.SampleCount < 3) continue;
                    MelonLoader.MelonLogger.Msg(
                        $"[SO2RAccess] STAIRS DIAG: Island {isl.Id}: " +
                        $"{isl.SampleCount} samples, " +
                        $"Y=[{isl.MinY:F1},{isl.MaxY:F1}], " +
                        $"center=({isl.CenterX:F1},{isl.CenterY:F1},{isl.CenterZ:F1})");
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] STAIRS DIAG: error: {ex.Message}");
            }
        }
    }
}
