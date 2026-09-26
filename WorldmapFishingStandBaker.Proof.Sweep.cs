using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The route proof's physics primitives (Phase 5 of the bake): the body-capsule
    /// sweep over a planned route, the body-swept enclosure flood that tells a
    /// shore pocket behind a town wall from an open beach, the standing-still
    /// wall-overlap test, the streamed-tile loader for a route, and the log-only
    /// town wall census. The proof's orchestration (anchors, attempts, re-plans)
    /// lives in <c>WorldmapFishingStandBaker.Proof.cs</c>.
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>Wall-group name fragments the census always lists, per planet.</summary>
        private static readonly Dictionary<WorldmapID, string[]> KnownWallGroupsByMap =
            new Dictionary<WorldmapID, string[]>
            {
                [WorldmapID.EXPEL] = new[] { "Arlia", "Krosse" },
            };

        /// <summary>
        /// Log-only census of the town wall colliders (layers 22/23 under an
        /// ancestor named Wall*/CharaWall*), grouped by that ancestor, with how
        /// many are active. Answers whether a town's walls are live in physics
        /// from where the bake is run (2026-09-13: the Arlia pocket bakes
        /// differently from Krosse than from Arlia).
        /// </summary>
        private static void LogWallCensus(WorldmapID wmID)
        {
            try
            {
                // Wall groups always listed, per planet (Expel: the two towns of
                // the 2026-09 stand saga). A planet without entries lists only
                // groups with inactive colliders.
                KnownWallGroupsByMap.TryGetValue(wmID, out string[] alwaysListed);
                var groups = new Dictionary<string, (int total, int active)>();
                var all = UnityEngine.Object.FindObjectsOfType<Collider>(true);
                if (all == null) return;
                foreach (var col in all)
                {
                    if (col == null) continue;
                    int layer = col.gameObject.layer;
                    if (layer != 22 && layer != 23) continue;
                    string group = null;
                    var t = col.transform.parent;
                    for (int i = 0; i < 6 && t != null; i++, t = t.parent)
                    {
                        if (t.name.StartsWith("Wall") || t.name.StartsWith("CharaWall") || t.name.StartsWith("Chara"))
                        { group = t.name; break; }
                    }
                    if (group == null) continue;
                    groups.TryGetValue(group, out var g);
                    groups[group] = (g.total + 1, g.active + (col.gameObject.activeInHierarchy && col.enabled ? 1 : 0));
                }
                int inactiveGroups = 0;
                foreach (var kv in groups)
                    if (kv.Value.active < kv.Value.total) inactiveGroups++;
                Log($"Phase 5 wall census: {groups.Count} wall groups on L22/L23; {inactiveGroups} with inactive colliders.");
                int lines = 0;
                foreach (var kv in groups)
                {
                    bool listed = kv.Value.active < kv.Value.total;
                    if (!listed && alwaysListed != null)
                        foreach (string town in alwaysListed)
                            if (kv.Key.Contains(town)) { listed = true; break; }
                    if (listed)
                    {
                        if (lines++ < 24)
                            Log($"Phase 5 wall census: {kv.Key}: {kv.Value.active}/{kv.Value.total} colliders active.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Phase 5 wall census failed: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the walking body, stood at <paramref name="p"/>, overlaps a
        /// wall-mask collider (<see cref="NavigationHandler.BodyWallClearance"/>
        /// = 0). The sweep cannot see a wall its cast starts inside; this can.
        /// </summary>
        private static bool BodyOverlapsWall(Vector3 p, int mask, out Collider blocker)
        {
            try
            {
                return NavigationHandler.BodyWallClearance(p, mask, out blocker) <= 0f && blocker != null;
            }
            catch
            {
                blocker = null;
                return false; // fail open, as in the walk
            }
        }

        /// <summary>Corners and centre of the enclosure search box, as a pseudo-path for the tile loader.</summary>
        private static Vector3[] EnclosureBox(Vector3 c)
        {
            float r = EnclosureSearchMeters;
            return new[]
            {
                c, new Vector3(c.x - r, c.y, c.z - r), new Vector3(c.x + r, c.y, c.z - r),
                new Vector3(c.x - r, c.y, c.z + r), new Vector3(c.x + r, c.y, c.z + r),
            };
        }

        /// <summary>
        /// Body-swept flood from a stand over grid-passable cells (8 neighbours,
        /// the pathfinder's climb rule), every step swept with the walk's body
        /// capsule and the live colliders, gate pinches forgiven as in the walk.
        /// True when no reached cell lies <see cref="EnclosureEscapeMeters"/> or
        /// farther from the stand: the body cannot leave the pocket, so the stand
        /// is unreachable whatever the grid says (2026-09-13: the Arlia shore
        /// strip behind the town wall, which the walk grid holds open). An open
        /// shore passes as soon as one cell that far is reached.
        /// </summary>
        private static bool IsEnclosed(Vector3 stand, WorldmapGridFormat.CachedGrid grid, int mask,
            out float farthest, out int cells, out int sweeps, out string walls)
        {
            farthest = 0f;
            cells = 0;
            sweeps = 0;
            walls = "(nothing)";
            // Which colliders closed the flood — evidence for the log, so an
            // artefact (a rock the player steps over, an overhang) is visible.
            var blockers = new Dictionary<string, int>();
            void Note(Collider c)
            {
                if (c == null) return;
                string k = $"'{c.name}' L{c.gameObject.layer} {NavigationHandler.ColliderChain(c)}";
                blockers.TryGetValue(k, out int n);
                blockers[k] = n + 1;
            }
            string Summary()
            {
                if (blockers.Count == 0) return "(nothing)";
                var top = new List<KeyValuePair<string, int>>(blockers);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var parts = new List<string>();
                for (int i = 0; i < top.Count && i < 3; i++) parts.Add($"{top[i].Key} ×{top[i].Value}");
                return string.Join("; ", parts);
            }
            grid.WorldToGrid(stand.x, stand.z, out int sx, out int sz);
            int r = (int)(EnclosureSearchMeters / grid.CellSize);
            float escapeSq = EnclosureEscapeMeters * EnclosureEscapeMeters;
            byte foot = WorldmapGridFormat.CachedGrid.FlagFootBlocked;
            var seen = new HashSet<long> { (long)sx * grid.GridH + sz };
            var queue = new Queue<(int ax, int az)>();
            queue.Enqueue((sx, sz));
            // The stand itself: a body that overlaps a wall standing on the
            // stand cannot leave it at all.
            Vector3 standFoot = grid.GridToWorld(sx, sz);
            standFoot.y = grid.GetHeightM(sx, sz);
            if (BodyOverlapsWall(standFoot, mask, out Collider onStand)) { cells = 1; Note(onStand); walls = Summary(); return true; }
            while (queue.Count > 0)
            {
                var (ax, az) = queue.Dequeue();
                cells++;
                Vector3 a = grid.GridToWorld(ax, az);
                a.y = grid.GetHeightM(ax, az);
                float dSq = FlatDistanceSq(a, stand);
                if (dSq > farthest * farthest) farthest = Mathf.Sqrt(dSq);
                if (dSq >= escapeSq) { walls = Summary(); return false; } // the body got out
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = ax + dx, nz = az + dz;
                        if ((dx == 0 && dz == 0) || Math.Abs(nx - sx) > r || Math.Abs(nz - sz) > r) continue;
                        long key = (long)nx * grid.GridH + nz;
                        if (seen.Contains(key) || !grid.IsPassable(nx, nz, foot)) continue;
                        Vector3 b = grid.GridToWorld(nx, nz);
                        b.y = grid.GetHeightM(nx, nz);
                        if (Mathf.Abs(b.y - a.y) > WorldmapPathfinder.MaxClimbCm / 100f) continue;
                        seen.Add(key);
                        sweeps++;
                        bool blocked;
                        try
                        {
                            // Sweep the step, then confirm the body can STAND at the
                            // destination: a cast that starts inside a wall reads
                            // as passable, the overlap test does not.
                            blocked = false;
                            if (NavigationHandler.SweepSegmentBlocked(a, b, mask, out Collider blocker, out _, out _)
                                && !WorldmapMapjumps.IsGatePinch(_proofMapjumps, a, blocker, out _))
                            { blocked = true; Note(blocker); }
                            else if (BodyOverlapsWall(b, mask, out Collider standing)
                                && !WorldmapMapjumps.IsGatePinch(_proofMapjumps, b, standing, out _))
                            { blocked = true; Note(standing); }
                        }
                        catch { blocked = false; } // fail open, as in the walk
                        if (!blocked) queue.Enqueue((nx, nz));
                    }
                }
            }
            walls = Summary();
            return true;
        }

        /// <summary>Instantiates the streamed collision of every tile the route crosses (unload with <c>UnloadTile</c>).</summary>
        private static int LoadRouteTiles(WorldmapChunkLoader loader, Vector3[] path,
            WorldmapGridFormat.CachedGrid grid, int cellsPerTile, int tilesX, int tilesZ)
        {
            var tiles = new HashSet<int>();
            foreach (var p in path)
            {
                grid.WorldToGrid(p.x, p.z, out int ax, out int az);
                int tx = Mathf.Clamp(ax / cellsPerTile, 0, tilesX - 1);
                int tz = Mathf.Clamp(az / cellsPerTile, 0, tilesZ - 1);
                if (tiles.Add(tx * tilesZ + tz)) loader.LoadTile(tx, tz);
            }
            return tiles.Count;
        }

        /// <summary>
        /// Body-capsule sweep over a route (the walk's own segment test). Segments
        /// within <see cref="ProofGoalExemptMeters"/> of the stand are not swept
        /// (the shoreline step itself). Segments within the walk's 16 m start
        /// zone of the route start or the anchor are swept AND get the
        /// standing-still overlap test. A blocked one is a wedge like any other
        /// (counted in <paramref name="startSide"/> as evidence) unless
        /// <see cref="ProofStartExemption"/> restores the old rule: then it is
        /// hidden and counted in <paramref name="hiddenStart"/>, and a proven
        /// stand with any of them must pass <see cref="IsEnclosed"/> (a
        /// 2026-09-13 bake from the town symbol centres without that exemption
        /// refused 10 lakes). A blocked segment that
        /// <see cref="WorldmapMapjumps.IsGatePinch"/> recognises as the town's own
        /// entrance collider is forgiven outright (<paramref name="forgiven"/>).
        /// Every other impassable segment's start is added to
        /// <paramref name="blocked"/> for the re-plan. Sweep errors count as
        /// passable (fail open, as in the walk).
        /// </summary>
        private static int SweepRoute(Vector3[] path, Vector3 anchorPos, Vector3 goal, int mask,
            List<Vector3> blocked, out string firstWedge, out int forgiven, out int swept,
            out int hiddenStart, out string firstHidden, out int startSide)
        {
            startSide = 0;
            firstWedge = null;
            firstHidden = null;
            forgiven = 0;
            swept = 0;
            hiddenStart = 0;
            int wedges = 0;
            float startExemptSq = NavigationHandler.WmSweepEndpointExemptDist * NavigationHandler.WmSweepEndpointExemptDist;
            float goalExemptSq = ProofGoalExemptMeters * ProofGoalExemptMeters;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (FlatDistanceSq(path[i], goal) <= goalExemptSq) continue;
                bool startZone = FlatDistanceSq(path[i], path[0]) <= startExemptSq
                    || FlatDistanceSq(path[i], anchorPos) <= startExemptSq;

                Collider blocker;
                swept++;
                try
                {
                    if (!NavigationHandler.SweepSegmentBlocked(path[i], path[i + 1], mask,
                            out blocker, out _, out bool unresolved))
                    {
                        // A cast that STARTS inside a collider comes back unresolved
                        // (null collider) in this runtime and reads as passable —
                        // the 2026-09-13 Krosse-side bake proved the Arlia pocket
                        // that way (its route begins inside the town wall). The
                        // standing-still overlap test does not have that blind spot.
                        if (!startZone || !BodyOverlapsWall(path[i], mask, out blocker)) continue;
                    }
                }
                catch (Exception ex)
                {
                    if (wedges == 0 && firstWedge == null) Log($"sweep error at wp[{i}]: {ex.Message} — segment treated as passable.");
                    continue;
                }

                if (WorldmapMapjumps.IsGatePinch(_proofMapjumps, path[i], blocker, out string why))
                {
                    forgiven++;
                    continue; // gate pinch: the town's own collider beside the road
                }
                if (startZone && ProofStartExemption)
                {
                    hiddenStart++;
                    if (firstHidden == null)
                        firstHidden = $"'{blocker.name}' L{blocker.gameObject.layer} at ({path[i].x:F1},{path[i].z:F1}), {why}";
                    continue; // start-side pinch: exempt as in the walk, but remembered
                }
                wedges++;
                if (startZone) startSide++;
                blocked.Add(path[i]);
                if (firstWedge == null)
                {
                    firstWedge = $"'{blocker.name}' L{blocker.gameObject.layer} at " +
                        $"({path[i].x:F1},{path[i].z:F1}), {Mathf.Sqrt(FlatDistanceSq(path[i], goal)):F0} m from the stand, {why}";
                }
            }
            return wedges;
        }

    }
}
