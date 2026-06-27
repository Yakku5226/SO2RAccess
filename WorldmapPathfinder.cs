using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// A* pathfinder for the world map using a pre-computed height grid
    /// at 0.5m resolution. Rejects moves between cells where the height
    /// difference is too steep (rock faces, cliffs). Trees (Col_Obstacle)
    /// are passthrough on the world map — only terrain geometry blocks.
    /// </summary>
    public static class WorldmapPathfinder
    {
        /// <summary>Cost for cardinal movement (1 cell = 0.5m).</summary>
        private const float CardinalCost = 1.0f;

        /// <summary>Cost for diagonal movement.</summary>
        private const float DiagonalCost = 1.414f;

        /// <summary>
        /// Max height difference in centimeters between adjacent cells.
        /// At 0.5m cell spacing, this controls max climbable slope.
        /// Set high (500cm) because the world map uses CharaWalls (layer 23)
        /// for movement barriers, not terrain slope. The player can walk
        /// up steep world map terrain — only CharaWalls physically block.
        /// The slope penalty (SlopePenaltyStartCm) still steers the A*
        /// toward flat roads without hard-blocking steep paths.
        /// </summary>
        private const int MaxClimbCm = 500;

        /// <summary>
        /// Height difference above which movement gets a cost penalty.
        /// Encourages A* to prefer flat paths (roads) over slopes.
        /// </summary>
        private const int SlopePenaltyStartCm = 30;

        /// <summary>
        /// Radius around a stuck position to mark as blocked (in grid cells).
        /// At 0.5m resolution, 4 cells = 2m radius.
        /// </summary>
        private const int BlockedRadiusCells = 4;

        /// <summary>
        /// Clearance threshold below which cells receive a continuous
        /// penalty. Cells with clearance at or above this value get no
        /// penalty. Based on: comfortable passage = 2x player radius.
        /// </summary>
        private const float ComfortableClearance = 1.5f;

        /// <summary>
        /// Maximum penalty applied to the tightest passable cells
        /// (those just above the hard minimum). The penalty scales
        /// linearly: tighter cells get higher penalties. Kept low
        /// (3.0) so A* prefers direct routes through gaps — the
        /// real-time wall avoidance in NavigationHandler handles
        /// steering through tight passages safely.
        /// </summary>
        private const float MaxClearancePenalty = 3.0f;

        /// <summary>
        /// Preferred minimum clearance (meters) for the first pathfinding
        /// pass. The grid bakes a hard 0.50m floor at generation time, which
        /// equals the player's capsule radius — gaps that tight wedge the
        /// player (they cannot fit even when aimed dead-center). We first
        /// search for a route where every cell has at least this much
        /// clearance (a real safety margin). Only if NO such route exists do
        /// we fall back to the 0.50m-floor route. This steers the player onto
        /// wider roads when one exists, WITHOUT ever making a destination less
        /// reachable than before: the fallback pass is identical to the
        /// original behavior.
        /// </summary>
        private const float PreferredMinClearance = 0.60f;

        /// <summary>8-directional movement offsets.</summary>
        private static readonly int[,] Directions = {
            { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 },
            { 1, 1 }, { 1, -1 }, { -1, -1 }, { -1, 1 }
        };

        private static WorldmapGridGenerator.CachedGrid _cachedExpel;
        private static WorldmapGridGenerator.CachedGrid _cachedNede;

        private static WorldmapGridGenerator.CachedGrid GetCachedGrid(
            WorldmapID wmID)
        {
            if (wmID == WorldmapID.EXPEL)
            {
                if (_cachedExpel == null)
                    _cachedExpel = WorldmapGridGenerator.LoadGrid(
                        WorldmapID.EXPEL);
                return _cachedExpel;
            }
            else
            {
                if (_cachedNede == null)
                    _cachedNede = WorldmapGridGenerator.LoadGrid(
                        WorldmapID.NEDE);
                return _cachedNede;
            }
        }

        /// <summary>Clears cached grids (call if grid files are regenerated).</summary>
        public static void ClearCache()
        {
            _cachedExpel = null;
            _cachedNede = null;
        }

        /// <summary>
        /// Finds a path on the world map using the pre-computed height grid.
        /// Returns world-space waypoints or null if no path exists.
        /// </summary>
        public static Vector3[] FindPath(Vector3 start, Vector3 end,
            List<Vector3> blockedPositions = null)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData())
                {
                    DebugLogger.LogState(
                        "NAV WM pathfinder: WorldGridData not available.");
                    return null;
                }

                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null)
                {
                    DebugLogger.LogState(
                        "NAV WM pathfinder: no cached grid. " +
                        "Press F9 in debug mode on the world map to generate.");
                    ScreenReader.Say(
                        "World map grid not found. Enable debug mode " +
                        "with F12, then press F9 to generate the map grid.");
                    return null;
                }

                int gridW = grid.GridW;
                int gridH = grid.GridH;

                // Working copy for stuck-blocking.
                ushort[,] workHeight = (ushort[,])grid.Height.Clone();

                // Convert world positions to grid indices.
                grid.WorldToGrid(start.x, start.z,
                    out int startAx, out int startAz);
                grid.WorldToGrid(end.x, end.z,
                    out int endAx, out int endAz);

                startAx = Mathf.Clamp(startAx, 0, gridW - 1);
                startAz = Mathf.Clamp(startAz, 0, gridH - 1);
                endAx = Mathf.Clamp(endAx, 0, gridW - 1);
                endAz = Mathf.Clamp(endAz, 0, gridH - 1);

                // Apply stuck-position blocks (set to ocean/0).
                if (blockedPositions != null)
                {
                    for (int b = 0; b < blockedPositions.Count; b++)
                    {
                        Vector3 bp = blockedPositions[b];
                        grid.WorldToGrid(bp.x, bp.z,
                            out int bax, out int baz);

                        for (int dx = -BlockedRadiusCells;
                            dx <= BlockedRadiusCells; dx++)
                        {
                            for (int dz = -BlockedRadiusCells;
                                dz <= BlockedRadiusCells; dz++)
                            {
                                int nx = bax + dx;
                                int nz = baz + dz;
                                if (nx >= 0 && nx < gridW &&
                                    nz >= 0 && nz < gridH &&
                                    dx * dx + dz * dz <=
                                    BlockedRadiusCells * BlockedRadiusCells)
                                {
                                    workHeight[nx, nz] = 0;
                                }
                            }
                        }
                    }
                }

                // Clear a small area around the start so the player's
                // immediate cell is passable. The grid already has real
                // entrance paths baked in (from FieldMapjumpCollision
                // triggers), so we don't need the old 15m blanket clearance
                // that created phantom passable cells through obstacle rings.
                // Start: 3m radius — just enough for the player's cell.
                // Target: 10m radius — covers the arrival zone for
                // TryEnterWorldmapLocation (arrival at 10m).
                int startClearRadius = 6; // 3m at 0.5m cells
                int endClearRadius = 20;  // 10m at 0.5m cells
                int[] clearCentersX = { startAx, endAx };
                int[] clearCentersZ = { startAz, endAz };
                int[] clearRadii = { startClearRadius, endClearRadius };
                for (int c = 0; c < clearCentersX.Length; c++)
                {
                    int cx = clearCentersX[c];
                    int cz = clearCentersZ[c];
                    int clearRadius = clearRadii[c];
                    // Use the center cell's height as fallback.
                    ushort centerH = workHeight[cx, cz] >= 2
                        ? workHeight[cx, cz] : (ushort)12080;
                    for (int dx = -clearRadius; dx <= clearRadius; dx++)
                    {
                        for (int dz = -clearRadius; dz <= clearRadius; dz++)
                        {
                            if (dx * dx + dz * dz > clearRadius * clearRadius)
                                continue;
                            int nx = cx + dx;
                            int nz = cz + dz;
                            if (nx >= 0 && nx < gridW && nz >= 0 && nz < gridH
                                && workHeight[nx, nz] == 1)
                            {
                                ushort origH = grid.Height[nx, nz];
                                if (origH == 1)
                                    workHeight[nx, nz] = centerH;
                                else
                                    workHeight[nx, nz] = origH;
                            }
                        }
                    }
                }

                // Snap start/end to nearest terrain cell.
                if (workHeight[startAx, startAz] < 2)
                    SnapToTerrain(ref startAx, ref startAz,
                        workHeight, gridW, gridH);
                if (workHeight[endAx, endAz] < 2)
                    SnapToTerrain(ref endAx, ref endAz,
                        workHeight, gridW, gridH);

                if (workHeight[startAx, startAz] < 2 ||
                    workHeight[endAx, endAz] < 2)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: start or end not on terrain. " +
                        $"grid={gridW}x{gridH} start=({startAx},{startAz}) " +
                        $"end=({endAx},{endAz})");
                    return null;
                }

                // Tiered A* search: first try a route where every cell has a
                // real clearance margin (PreferredMinClearance) so the player
                // is never threaded through a body-width gap. Only if no such
                // route exists do we fall back to the grid's baked 0.50m floor
                // (the original behavior). This prefers wide roads when one
                // exists but never removes a reachable destination.
                var path = AStarSearch(startAx, startAz, endAx, endAz,
                    workHeight, gridW, gridH, grid, PreferredMinClearance);

                if (path == null)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: no route at " +
                        $"{PreferredMinClearance:F2}m clearance — falling back " +
                        $"to the 0.50m floor.");
                    path = AStarSearch(startAx, startAz, endAx, endAz,
                        workHeight, gridW, gridH, grid, 0f);
                }

                if (path == null)
                {
                    DebugLogger.LogState(
                        $"NAV WM pathfinder: no path found. " +
                        $"grid={gridW}x{gridH} " +
                        $"start=({startAx},{startAz}) " +
                        $"end=({endAx},{endAz}) " +
                        $"maxClimb={MaxClimbCm}cm");
                    return null;
                }

                // Convert path to world-space waypoints with ground Y.
                // Uses clearance-adjusted positions for cells near CharaWalls
                // so the player walks through the exact center of narrow gaps.
                var waypoints = new List<Vector3>();
                foreach (var cell in path)
                {
                    Vector3 wp = grid.GridToWorldWithClearance(cell.x, cell.y);
                    float groundY = GameUtility.CalcHeight(
                        new Vector3(wp.x, 150f, wp.z), out bool ok, 300f);
                    if (ok) wp.y = groundY;
                    waypoints.Add(wp);
                }

                // Use raw A* waypoints (0.5m apart) without simplification:
                // path simplification cut long straight segments through
                // obstacle-adjacent airspace, clipping the player's 0.51m
                // capsule on L22 rocks. Raw waypoints keep the player on
                // verified passable cells; native Physics2 handles micro-collisions.
                if (waypoints.Count > 0)
                    waypoints[waypoints.Count - 1] = end;
                else
                    waypoints.Add(end);

                DebugLogger.LogState(
                    $"NAV WM pathfinder: found path with " +
                    $"{waypoints.Count} waypoints " +
                    $"(raw {path.Count} cells, maxClimb={MaxClimbCm}cm).");

                return waypoints.ToArray();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM pathfinder error: {ex.Message}");
                return null;
            }
        }

        #region A* with Binary Heap

        /// <summary>
        /// A* search with slope checking and wall proximity penalty.
        /// Uses direction-based parent tracking to save memory on the
        /// 0.5m resolution grid. Cells near walls (with clearance offsets)
        /// get a higher movement cost so the A* prefers wide corridors.
        /// </summary>
        private static List<Vector2Int> AStarSearch(
            int sx, int sz, int ex, int ez,
            ushort[,] height, int gridW, int gridH,
            WorldmapGridGenerator.CachedGrid grid = null,
            float minClearance = 0f)
        {
            var gCost = new float[gridW, gridH];
            var parentDir = new byte[gridW, gridH];
            var closed = new bool[gridW, gridH];

            for (int x = 0; x < gridW; x++)
                for (int z = 0; z < gridH; z++)
                {
                    gCost[x, z] = float.MaxValue;
                    parentDir[x, z] = 255;
                }

            gCost[sx, sz] = 0f;

            var heap = new List<(float f, int x, int z)>();
            HeapPush(heap, (Heuristic(sx, sz, ex, ez), sx, sz));

            while (heap.Count > 0)
            {
                var (_, cx, cz) = HeapPop(heap);

                if (closed[cx, cz]) continue;
                closed[cx, cz] = true;

                if (cx == ex && cz == ez)
                    return ReconstructPath(parentDir, sx, sz, ex, ez);

                ushort currentH = height[cx, cz];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Directions[d, 0];
                    int nz = cz + Directions[d, 1];

                    if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH)
                        continue;
                    if (closed[nx, nz]) continue;

                    ushort neighborH = height[nx, nz];
                    if (neighborH < 2) continue; // Ocean (0) or obstacle (1).

                    // Slope check.
                    int heightDiff = Math.Abs(currentH - neighborH);
                    if (heightDiff > MaxClimbCm) continue; // Too steep.

                    float moveCost = d < 4 ? CardinalCost : DiagonalCost;

                    // Slope penalty: prefer flat paths (roads).
                    if (heightDiff > SlopePenaltyStartCm)
                        moveCost += heightDiff * 0.02f;

                    // Continuous clearance penalty: prefer wide corridors.
                    // Penalty scales linearly from MaxClearancePenalty at
                    // minimum clearance (0.55m) down to 0 at ComfortableClearance.
                    if (grid != null)
                    {
                        float clr = grid.GetClearance(nx, nz);

                        // Hard clearance floor (first pass only). Cells too
                        // narrow for the player to fit through are skipped
                        // entirely. minClearance == 0 in the fallback pass
                        // disables this, preserving original reachability.
                        if (minClearance > 0f && clr < minClearance) continue;

                        if (clr < ComfortableClearance)
                        {
                            float ratio = (ComfortableClearance - clr) /
                                (ComfortableClearance - 0.55f);
                            if (ratio > 1f) ratio = 1f;
                            moveCost += ratio * MaxClearancePenalty;
                        }
                    }

                    float newG = gCost[cx, cz] + moveCost;

                    if (newG < gCost[nx, nz])
                    {
                        gCost[nx, nz] = newG;
                        parentDir[nx, nz] = (byte)d;

                        float f = newG + Heuristic(nx, nz, ex, ez);
                        HeapPush(heap, (f, nx, nz));
                    }
                }
            }

            return null;
        }

        private static void HeapPush(List<(float f, int x, int z)> heap,
            (float f, int x, int z) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].f <= heap[i].f) break;
                var tmp = heap[parent];
                heap[parent] = heap[i];
                heap[i] = tmp;
                i = parent;
            }
        }

        private static (float f, int x, int z) HeapPop(
            List<(float f, int x, int z)> heap)
        {
            var min = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            last--;

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int smallest = i;

                if (left <= last && heap[left].f < heap[smallest].f)
                    smallest = left;
                if (right <= last && heap[right].f < heap[smallest].f)
                    smallest = right;

                if (smallest == i) break;

                var tmp = heap[i];
                heap[i] = heap[smallest];
                heap[smallest] = tmp;
                i = smallest;
            }

            return min;
        }

        #endregion

        private static float Heuristic(int ax, int az, int bx, int bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static List<Vector2Int> ReconstructPath(
            byte[,] parentDir, int sx, int sz, int ex, int ez)
        {
            var path = new List<Vector2Int>();
            int cx = ex, cz = ez;

            while (cx != sx || cz != sz)
            {
                path.Add(new Vector2Int(cx, cz));
                byte d = parentDir[cx, cz];
                if (d == 255 || d > 7) break;
                cx -= Directions[d, 0];
                cz -= Directions[d, 1];
                if (path.Count > 500000) break;
            }
            path.Add(new Vector2Int(sx, sz));
            path.Reverse();
            return path;
        }

        /// <summary>Finds nearest terrain cell (height > 0).</summary>
        private static void SnapToTerrain(ref int gx, ref int gz,
            ushort[,] height, int gridW, int gridH)
        {
            for (int r = 1; r <= 100; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                            continue;
                        int nx = gx + dx;
                        int nz = gz + dz;
                        if (nx >= 0 && nx < gridW && nz >= 0 && nz < gridH
                            && height[nx, nz] >= 2)
                        {
                            gx = nx;
                            gz = nz;
                            return;
                        }
                    }
                }
            }
        }

    }
}
