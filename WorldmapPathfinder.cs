using Il2CppGame;
using System;
using System.Collections.Generic;
using System.Text;
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

                // A* search with slope checking and wall proximity penalty.
                var path = AStarSearch(startAx, startAz, endAx, endAz,
                    workHeight, gridW, gridH, grid);

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
                var hasClearance = new List<bool>();
                foreach (var cell in path)
                {
                    Vector3 wp = grid.GridToWorldWithClearance(cell.x, cell.y);
                    float groundY = GameUtility.CalcHeight(
                        new Vector3(wp.x, 150f, wp.z), out bool ok, 300f);
                    if (ok) wp.y = groundY;
                    waypoints.Add(wp);
                    hasClearance.Add(grid.HasClearanceOffset(cell.x, cell.y));
                }

                // Use raw A* waypoints without simplification.
                // SimplifyPath created long straight segments (up to 5m+)
                // that cut through obstacle-adjacent airspace, causing the
                // player's 0.51m capsule to clip L22 rocks. Raw waypoints
                // (0.5m apart) keep the player on verified passable cells —
                // the game's native Physics2 handles micro-collisions.
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
            WorldmapGridGenerator.CachedGrid grid = null)
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

        /// <summary>
        /// Removes collinear waypoints but keeps periodic density.
        /// At 0.5m cells, keeps a waypoint at least every 5m (10 cells).
        /// Never removes waypoints that have clearance offsets — those are
        /// positioned at the safest point through narrow CharaWall gaps.
        /// </summary>
        private static List<Vector3> SimplifyPath(List<Vector3> waypoints,
            List<bool> hasClearance = null)
        {
            if (waypoints.Count <= 2) return new List<Vector3>(waypoints);

            var result = new List<Vector3> { waypoints[0] };
            int lastKeptIdx = 0;

            for (int i = 1; i < waypoints.Count - 1; i++)
            {
                // Never simplify away clearance-offset waypoints.
                if (hasClearance != null && hasClearance[i])
                {
                    result.Add(waypoints[i]);
                    lastKeptIdx = i;
                    continue;
                }

                Vector3 prev = waypoints[i - 1];
                Vector3 curr = waypoints[i];
                Vector3 next = waypoints[i + 1];

                float dx1 = curr.x - prev.x;
                float dz1 = curr.z - prev.z;
                float dx2 = next.x - curr.x;
                float dz2 = next.z - curr.z;

                float cross = dx1 * dz2 - dz1 * dx2;
                bool isTurn = Mathf.Abs(cross) > 0.01f;
                bool keepPeriodic = (i - lastKeptIdx) >= 10;

                if (isTurn || keepPeriodic)
                {
                    result.Add(curr);
                    lastKeptIdx = i;
                }
            }

            result.Add(waypoints[waypoints.Count - 1]);
            return result;
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

        /// <summary>
        /// When the target is surrounded by obstacles, does a quick BFS
        /// from the start to find the nearest walkable cell to the target
        /// that is actually reachable. Returns null if nothing found
        /// within a reasonable search limit.
        /// </summary>
        private static Vector2Int? FindNearestReachableToTarget(
            int sx, int sz, int tx, int tz,
            ushort[,] height, int gridW, int gridH)
        {
            // BFS from start, tracking the closest cell to target.
            var visited = new bool[gridW, gridH];
            var queue = new Queue<(int x, int z)>();
            queue.Enqueue((sx, sz));
            visited[sx, sz] = true;

            int bestX = -1, bestZ = -1;
            int bestDist = int.MaxValue;
            int maxVisit = 1500000;
            int count = 0;

            while (queue.Count > 0 && count < maxVisit)
            {
                var (cx, cz) = queue.Dequeue();
                count++;

                int dist = Math.Abs(cx - tx) + Math.Abs(cz - tz);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestX = cx;
                    bestZ = cz;
                }

                // Early exit if we're within 2 cells (1m) of target.
                if (dist <= 2) break;

                ushort currentH = height[cx, cz];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Directions[d, 0];
                    int nz = cz + Directions[d, 1];

                    if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH)
                        continue;
                    if (visited[nx, nz]) continue;
                    visited[nx, nz] = true;

                    ushort nh = height[nx, nz];
                    if (nh < 2) continue;

                    if (currentH >= 2)
                    {
                        int hdiff = Math.Abs(currentH - nh);
                        if (hdiff > MaxClimbCm) continue;
                    }

                    queue.Enqueue((nx, nz));
                }
            }

            if (bestX >= 0 && bestDist < int.MaxValue)
            {
                DebugLogger.LogState(
                    $"NAV WM pathfinder: nearest reachable to target " +
                    $"({tx},{tz}): ({bestX},{bestZ}) dist={bestDist}");
                return new Vector2Int(bestX, bestZ);
            }

            return null;
        }

        #region Diagnostics

        public static void RunDiagnostics(Vector3 playerPos)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== WORLDMAP PATHFINDER DIAGNOSTICS ===");
                sb.AppendLine($"Player pos: ({playerPos.x:F1}, " +
                    $"{playerPos.y:F1}, {playerPos.z:F1})");
                sb.AppendLine($"MaxClimbCm: {MaxClimbCm}");
                sb.AppendLine($"SlopePenaltyStartCm: {SlopePenaltyStartCm}");

                var fm = FieldManager.Instance;
                if (fm != null && fm.IsExistWorldGridData())
                {
                    var grid = GetCachedGrid(fm.WorldmapID);
                    if (grid != null)
                    {
                        sb.AppendLine($"\n--- Cached Grid ---");
                        sb.AppendLine($"Size: {grid.GridW}x{grid.GridH}");
                        sb.AppendLine($"CellSize: {grid.CellSize}m");

                        grid.WorldToGrid(playerPos.x, playerPos.z,
                            out int pax, out int paz);
                        sb.AppendLine($"Player in grid: ({pax}, {paz})");

                        if (pax >= 0 && pax < grid.GridW &&
                            paz >= 0 && paz < grid.GridH)
                        {
                            float h = grid.GetHeightM(pax, paz);
                            sb.AppendLine($"Height at player: {h:F2}m");

                            // Show height differences around player.
                            sb.AppendLine($"\nHeight diffs (5x5, cm):");
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                var row = new StringBuilder("  ");
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    int ax = pax + dx;
                                    int az = paz + dz;
                                    float nh = grid.GetHeightM(ax, az);
                                    if (nh == float.MinValue)
                                        row.Append("  ~~~ ");
                                    else
                                    {
                                        int diff = (int)((nh - h) * 100);
                                        row.Append($"{diff,5} ");
                                    }
                                }
                                sb.AppendLine(row.ToString());
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine(
                            "\nNo cached grid. Press F9 to generate.");
                    }
                }

                sb.AppendLine("=== END DIAGNOSTICS ===");
                MelonLoader.MelonLogger.Msg(sb.ToString());
                ScreenReader.Say("Diagnostics complete. Check log.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM diagnostics error: {ex.Message}");
                ScreenReader.Say("Diagnostics failed.");
            }
        }

        public static void ScanPathLine(Vector3 startWorld, Vector3 endWorld)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData())
                {
                    ScreenReader.Say("World grid data not available.");
                    return;
                }

                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null)
                {
                    ScreenReader.Say("No cached grid. Press F9 to generate.");
                    return;
                }

                grid.WorldToGrid(startWorld.x, startWorld.z,
                    out int sx, out int sz);
                grid.WorldToGrid(endWorld.x, endWorld.z,
                    out int ex, out int ez);

                var sb = new StringBuilder();
                sb.AppendLine("=== PATH LINE SCAN ===");
                sb.AppendLine($"Start: world=({startWorld.x:F1}," +
                    $"{startWorld.z:F1}) grid=({sx},{sz})");
                sb.AppendLine($"End:   world=({endWorld.x:F1}," +
                    $"{endWorld.z:F1}) grid=({ex},{ez})");
                sb.AppendLine($"MaxClimbCm: {MaxClimbCm}");

                int dx = Math.Abs(ex - sx);
                int dz = Math.Abs(ez - sz);
                int stepX = sx < ex ? 1 : -1;
                int stepZ = sz < ez ? 1 : -1;
                int err = dx - dz;

                int cx = sx, cz = sz;
                int totalCells = 0, oceanCells = 0, slopeBlocked = 0;
                int terrainCells = 0;
                ushort prevH = 0;
                int maxSlopeSeen = 0;

                bool prevWalkable = true;
                int barrierStart = -1;
                var barriers = new List<string>();

                while (true)
                {
                    bool inBounds = cx >= 0 && cx < grid.GridW &&
                                    cz >= 0 && cz < grid.GridH;
                    bool isWalkable = false;
                    string reason = "out-of-bounds";
                    ushort cellH = 0;

                    if (inBounds)
                    {
                        cellH = grid.Height[cx, cz];
                        if (cellH == 0)
                        {
                            reason = "ocean";
                            oceanCells++;
                        }
                        else
                        {
                            terrainCells++;
                            int slope = prevH > 0
                                ? Math.Abs(cellH - prevH) : 0;
                            if (slope > maxSlopeSeen) maxSlopeSeen = slope;

                            if (slope > MaxClimbCm && prevH > 0)
                            {
                                reason = $"slope({slope}cm)";
                                slopeBlocked++;
                            }
                            else
                            {
                                float hm = grid.GetHeightM(cx, cz);
                                reason = $"ok({hm:F1}m)";
                                isWalkable = true;
                            }
                        }
                    }

                    if (!isWalkable || totalCells % 40 == 0 ||
                        (cx == ex && cz == ez))
                    {
                        sb.AppendLine(
                            $"  [{totalCells}] grid=({cx},{cz}) {reason}");
                    }

                    if (!isWalkable)
                    {
                        if (prevWalkable) barrierStart = totalCells;
                    }
                    else
                    {
                        if (!prevWalkable && barrierStart >= 0)
                        {
                            barriers.Add(
                                $"barrier at cells {barrierStart}-" +
                                $"{totalCells - 1} " +
                                $"({totalCells - barrierStart} cells)");
                        }
                    }

                    prevWalkable = isWalkable;
                    if (cellH > 0) prevH = cellH;
                    totalCells++;

                    if (cx == ex && cz == ez) break;

                    int e2 = 2 * err;
                    if (e2 > -dz) { err -= dz; cx += stepX; }
                    if (e2 < dx) { err += dx; cz += stepZ; }
                }

                if (!prevWalkable && barrierStart >= 0)
                {
                    barriers.Add(
                        $"barrier at cells {barrierStart}-{totalCells - 1} " +
                        $"({totalCells - barrierStart} cells)");
                }

                sb.AppendLine($"\n--- Summary ---");
                sb.AppendLine(
                    $"Total: {totalCells}. Terrain: {terrainCells} " +
                    $"Ocean: {oceanCells} Slope blocked: {slopeBlocked}");
                sb.AppendLine($"Max slope seen: {maxSlopeSeen}cm");
                sb.AppendLine($"Barriers: {barriers.Count}");
                foreach (var b in barriers)
                    sb.AppendLine($"  {b}");

                sb.AppendLine("=== END PATH LINE SCAN ===");

                MelonLoader.MelonLogger.Msg(sb.ToString());
                ScreenReader.Say(
                    $"Line scan: {totalCells} cells, {barriers.Count} " +
                    $"barriers, max slope {maxSlopeSeen}cm. Check log.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"ScanPathLine error: {ex}");
                ScreenReader.Say("Line scan failed. Check log.");
            }
        }

        #endregion
    }
}
