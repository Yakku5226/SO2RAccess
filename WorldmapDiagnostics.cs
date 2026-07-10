using Il2CppGame;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Comprehensive world map diagnostics for investigating pathfinding
    /// issues. Triggered by F11 on the world map in debug mode.
    /// Runs flood fill, terrain profile, obstacle census, grid accuracy
    /// check, and slope analysis.
    /// </summary>
    public static class WorldmapDiagnostics
    {
        private const int MaxClimbCm = 100;
        private const float ObstacleSearchRadius = 1.0f;
        private const float PlayerRadius = 0.50f;
        /// <summary>Both wall layers — used for diagnostics (seeing all obstacles).</summary>
        private static readonly int ObstacleLayerMask = (1 << 22) | (1 << 23);

        /// <summary>Grid-matching masks — matches grid generator two-tier check.</summary>
        private static readonly int GridTerrainMask = 1 << 22;
        private static readonly int GridCharaWallMask = 1 << 23;

        /// <summary>
        /// Runs all diagnostics from the player's current position.
        /// Target comes from the last auto-walk target if available.
        /// </summary>
        public static void RunAll(Vector3 playerPos)
        {
            ScreenReader.Say("Running full diagnostics. This may take 30 seconds. Please wait.");
            MelonLoader.MelonLogger.Msg("=== FULL WORLD MAP DIAGNOSTICS ===");

            var fm = FieldManager.Instance;
            if (fm == null || !fm.IsWorldmap())
            {
                ScreenReader.Say("Not on world map.");
                return;
            }

            var target = NavigationHandler.LastAutoWalkTarget;

            // Load cached grid (may be null).
            var grid = LoadGrid(fm);

            var summary = new StringBuilder();

            // 1. Flood fill analysis
            if (grid != null)
            {
                FloodFillAnalysis(grid, playerPos, target, summary);
            }
            else
            {
                MelonLoader.MelonLogger.Msg("[DIAG] Skipping flood fill — no cached grid.");
            }

            // 2. Terrain profile to target
            if (target.HasValue && grid != null)
            {
                TerrainProfile(grid, playerPos, target.Value);
            }
            else
            {
                MelonLoader.MelonLogger.Msg("[DIAG] Skipping terrain profile — no target or grid.");
            }

            // 3. Obstacle census around player
            ObstacleCensus(playerPos, 50f);

            // 4. Grid accuracy check
            if (grid != null)
            {
                GridAccuracyCheck(grid, playerPos);
            }

            // 5. Slope analysis to target
            if (target.HasValue)
            {
                SlopeAnalysis(playerPos, target.Value, summary);
            }

            MelonLoader.MelonLogger.Msg("=== END FULL DIAGNOSTICS ===");

            string sum = summary.Length > 0 ? summary.ToString() : "Diagnostics complete.";
            ScreenReader.Say(sum + " Check log for details.");
        }

        /// <summary>
        /// Flood fill from player position to find connected walkable region
        /// and classify what borders it.
        /// </summary>
        private static void FloodFillAnalysis(
            WorldmapGridFormat.CachedGrid grid,
            Vector3 playerPos, Vector3? target,
            StringBuilder summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== 1. FLOOD FILL ANALYSIS ===");

            grid.WorldToGrid(playerPos.x, playerPos.z,
                out int startAx, out int startAz);
            startAx = Mathf.Clamp(startAx, 0, grid.GridW - 1);
            startAz = Mathf.Clamp(startAz, 0, grid.GridH - 1);

            sb.AppendLine($"Start: grid=({startAx},{startAz}) " +
                $"world=({playerPos.x:F1},{playerPos.z:F1})");

            if (FootCell(grid, startAx, startAz) < 2)
            {
                sb.AppendLine("WARNING: Player cell is NOT walkable in grid! " +
                    $"Value={FootCell(grid, startAx, startAz)}");
            }

            // BFS flood fill.
            var visited = new bool[grid.GridW, grid.GridH];
            var queue = new Queue<(int x, int z)>();
            queue.Enqueue((startAx, startAz));
            visited[startAx, startAz] = true;

            int regionSize = 0;
            int borderOcean = 0, borderObstacle = 0, borderSlope = 0;
            int borderOOB = 0;
            int minRx = startAx, maxRx = startAx;
            int minRz = startAz, maxRz = startAz;
            int maxVisit = 2000000;

            int[,] dirs = {
                { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 },
                { 1, 1 }, { 1, -1 }, { -1, -1 }, { -1, 1 }
            };

            while (queue.Count > 0 && regionSize < maxVisit)
            {
                var (cx, cz) = queue.Dequeue();
                regionSize++;

                if (cx < minRx) minRx = cx;
                if (cx > maxRx) maxRx = cx;
                if (cz < minRz) minRz = cz;
                if (cz > maxRz) maxRz = cz;

                ushort currentH = FootCell(grid, cx, cz);

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dirs[d, 0];
                    int nz = cz + dirs[d, 1];

                    if (nx < 0 || nx >= grid.GridW ||
                        nz < 0 || nz >= grid.GridH)
                    {
                        borderOOB++;
                        continue;
                    }

                    if (visited[nx, nz]) continue;

                    ushort nh = FootCell(grid, nx, nz);

                    if (nh == 0)
                    {
                        borderOcean++;
                        visited[nx, nz] = true;
                        continue;
                    }

                    if (nh == 1)
                    {
                        borderObstacle++;
                        visited[nx, nz] = true;
                        continue;
                    }

                    // Slope check.
                    if (currentH >= 2 && nh >= 2)
                    {
                        int heightDiff = Math.Abs(currentH - nh);
                        if (heightDiff > MaxClimbCm)
                        {
                            borderSlope++;
                            visited[nx, nz] = true;
                            continue;
                        }
                    }

                    visited[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }

            float areaSqM = regionSize * grid.CellSize * grid.CellSize;
            sb.AppendLine($"Region size: {regionSize} cells ({areaSqM:F0} sq m)");
            sb.AppendLine($"Region bounds: grid ({minRx},{minRz})-({maxRx},{maxRz})");
            sb.AppendLine($"  Width: {(maxRx - minRx) * grid.CellSize:F0}m " +
                $"x Height: {(maxRz - minRz) * grid.CellSize:F0}m");
            sb.AppendLine($"Border cells blocked by:");
            sb.AppendLine($"  Ocean: {borderOcean}");
            sb.AppendLine($"  Solid obstacle: {borderObstacle}");
            sb.AppendLine($"  Slope too steep: {borderSlope}");
            sb.AppendLine($"  Out of bounds: {borderOOB}");

            if (regionSize >= maxVisit)
                sb.AppendLine($"WARNING: Flood fill capped at {maxVisit} cells.");

            // Check if target is reachable.
            if (target.HasValue)
            {
                grid.WorldToGrid(target.Value.x, target.Value.z,
                    out int targetAx, out int targetAz);
                targetAx = Mathf.Clamp(targetAx, 0, grid.GridW - 1);
                targetAz = Mathf.Clamp(targetAz, 0, grid.GridH - 1);

                bool targetReachable = visited[targetAx, targetAz];
                sb.AppendLine($"\nTarget: grid=({targetAx},{targetAz}) " +
                    $"Reachable: {targetReachable}");

                if (!targetReachable)
                {
                    // Find nearest reachable cell to target.
                    int bestDist = int.MaxValue;
                    int bestX = -1, bestZ = -1;
                    int searchR = 200;
                    for (int dx = -searchR; dx <= searchR; dx += 2)
                    {
                        for (int dz = -searchR; dz <= searchR; dz += 2)
                        {
                            int tx = targetAx + dx;
                            int tz = targetAz + dz;
                            if (tx >= 0 && tx < grid.GridW &&
                                tz >= 0 && tz < grid.GridH &&
                                visited[tx, tz])
                            {
                                int d2 = dx * dx + dz * dz;
                                if (d2 < bestDist)
                                {
                                    bestDist = d2;
                                    bestX = tx;
                                    bestZ = tz;
                                }
                            }
                        }
                    }

                    if (bestX >= 0)
                    {
                        float distM = Mathf.Sqrt(bestDist) * grid.CellSize;
                        sb.AppendLine($"Nearest reachable to target: " +
                            $"grid=({bestX},{bestZ}) dist={distM:F1}m");

                        // Log what's between nearest reachable and target.
                        sb.AppendLine("Cells between nearest reachable and target:");
                        ScanLineBrief(sb, grid, bestX, bestZ,
                            targetAx, targetAz);
                    }
                    else
                    {
                        sb.AppendLine("No reachable cell found within search range of target.");
                    }
                }
            }

            string log = sb.ToString();
            MelonLoader.MelonLogger.Msg(log);

            summary.Append($"Region: {regionSize} cells. " +
                $"Borders: {borderOcean} ocean, {borderObstacle} obstacle, " +
                $"{borderSlope} slope. ");
        }

        /// <summary>Brief line scan between two grid cells, logging each cell's status.</summary>
        private static void ScanLineBrief(StringBuilder sb,
            WorldmapGridFormat.CachedGrid grid,
            int sx, int sz, int ex, int ez)
        {
            int dx = Math.Abs(ex - sx);
            int dz = Math.Abs(ez - sz);
            int stepX = sx < ex ? 1 : -1;
            int stepZ = sz < ez ? 1 : -1;
            int err = dx - dz;
            int cx = sx, cz = sz;
            ushort prevH = 0;
            int count = 0;

            while (count < 500)
            {
                ushort h = (cx >= 0 && cx < grid.GridW &&
                    cz >= 0 && cz < grid.GridH)
                    ? FootCell(grid, cx, cz) : (ushort)0;

                string status;
                if (h == 0) status = "ocean";
                else if (h == 1) status = "OBSTACLE";
                else
                {
                    float hm = grid.GetHeightM(cx, cz);
                    int slope = prevH >= 2 ? Math.Abs(h - prevH) : 0;
                    bool slopeBlocked = slope > MaxClimbCm;
                    status = $"h={hm:F1}m slope={slope}cm" +
                        (slopeBlocked ? " SLOPE-BLOCKED" : "");
                }
                sb.AppendLine($"    [{count}] ({cx},{cz}) {status}");

                if (h >= 2) prevH = h;
                count++;

                if (cx == ex && cz == ez) break;
                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; cx += stepX; }
                if (e2 < dx) { err += dx; cz += stepZ; }
            }
        }

        /// <summary>
        /// Enhanced terrain profile from player to target.
        /// Logs cached grid value, fresh CalcHeight, and obstacle details
        /// for each cell along a straight line.
        /// </summary>
        private static void TerrainProfile(
            WorldmapGridFormat.CachedGrid grid,
            Vector3 playerPos, Vector3 targetPos)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== 2. TERRAIN PROFILE (Player → Target) ===");

            grid.WorldToGrid(playerPos.x, playerPos.z,
                out int sx, out int sz);
            grid.WorldToGrid(targetPos.x, targetPos.z,
                out int ex, out int ez);

            sx = Mathf.Clamp(sx, 0, grid.GridW - 1);
            sz = Mathf.Clamp(sz, 0, grid.GridH - 1);
            ex = Mathf.Clamp(ex, 0, grid.GridW - 1);
            ez = Mathf.Clamp(ez, 0, grid.GridH - 1);

            sb.AppendLine($"Start: grid=({sx},{sz}) End: grid=({ex},{ez})");

            int dx = Math.Abs(ex - sx);
            int dz = Math.Abs(ez - sz);
            int stepX = sx < ex ? 1 : -1;
            int stepZ = sz < ez ? 1 : -1;
            int err = dx - dz;
            int cx = sx, cz = sz;

            int total = 0, oceanCount = 0, obstCount = 0, slopeCount = 0;
            int walkableCount = 0, mismatchCount = 0;
            ushort prevH = 0;
            int maxSlopeSeen = 0;
            var barriers = new List<string>();
            bool prevWalkable = true;
            int barrierStart = -1;

            while (total < 5000)
            {
                bool inBounds = cx >= 0 && cx < grid.GridW &&
                    cz >= 0 && cz < grid.GridH;
                ushort cachedH = inBounds ? FootCell(grid, cx, cz) : (ushort)0;

                // Fresh CalcHeight.
                Vector3 cellWorld = grid.GridToWorld(cx, cz);
                float freshY = GameUtility.CalcHeight(
                    new Vector3(cellWorld.x, 150f, cellWorld.z),
                    out bool freshOk, 300f);

                // Fresh obstacle check.
                int solidCount = 0, triggerCount = 0;
                string nearestSolidName = null;
                float nearestSolidDist = 99f;
                string nearestSolidType = null;

                if (freshOk)
                {
                    Vector3 checkPos = new Vector3(
                        cellWorld.x, freshY + 0.5f, cellWorld.z);
                    var colliders = Physics.OverlapSphere(
                        checkPos, ObstacleSearchRadius, ObstacleLayerMask);

                    if (colliders != null)
                    {
                        for (int c = 0; c < colliders.Length; c++)
                        {
                            if (colliders[c] == null) continue;
                            bool isTrig = colliders[c].isTrigger;
                            if (isTrig) { triggerCount++; continue; }

                            solidCount++;
                            Vector3 closest =
                                colliders[c].ClosestPoint(checkPos);
                            float dist = Vector3.Distance(checkPos, closest);

                            if (dist < nearestSolidDist)
                            {
                                nearestSolidDist = dist;
                                nearestSolidName =
                                    colliders[c].gameObject?.name ?? "?";
                                nearestSolidType =
                                    colliders[c].GetType().Name;
                            }
                        }
                    }
                }

                // Classify cell.
                bool isWalkable = false;
                string reason;
                if (cachedH == 0) { reason = "ocean"; oceanCount++; }
                else if (cachedH == 1) { reason = "OBSTACLE"; obstCount++; }
                else
                {
                    int slope = prevH >= 2 ? Math.Abs(cachedH - prevH) : 0;
                    if (slope > maxSlopeSeen) maxSlopeSeen = slope;
                    if (slope > MaxClimbCm)
                    {
                        reason = $"SLOPE-BLOCKED({slope}cm)";
                        slopeCount++;
                    }
                    else
                    {
                        reason = "walkable";
                        isWalkable = true;
                        walkableCount++;
                    }
                }

                // Check for grid/fresh mismatch.
                bool mismatch = false;
                string mismatchInfo = "";
                if (cachedH == 0 && freshOk)
                {
                    mismatch = true;
                    mismatchInfo = " MISMATCH:grid=ocean,fresh=terrain";
                }
                else if (cachedH >= 2 && !freshOk)
                {
                    mismatch = true;
                    mismatchInfo = " MISMATCH:grid=terrain,fresh=ocean";
                }
                else if (cachedH == 1 && solidCount == 0)
                {
                    mismatch = true;
                    mismatchInfo = " MISMATCH:grid=obstacle,fresh=noSolid";
                }
                if (mismatch) mismatchCount++;

                // Log non-walkable cells, mismatches, and every 40th cell.
                bool shouldLog = !isWalkable || mismatch ||
                    total % 40 == 0 || (cx == ex && cz == ez);

                if (shouldLog)
                {
                    var line = new StringBuilder();
                    line.Append($"  [{total}] ({cx},{cz}) {reason}");
                    line.Append($" freshY={freshY:F1}({(freshOk ? "ok" : "no")})");
                    if (solidCount > 0 || triggerCount > 0)
                    {
                        line.Append($" solid={solidCount} trig={triggerCount}");
                        if (nearestSolidName != null)
                            line.Append($" nearest={nearestSolidName}" +
                                $"({nearestSolidType}) d={nearestSolidDist:F2}m");
                    }
                    line.Append(mismatchInfo);
                    sb.AppendLine(line.ToString());
                }

                // Barrier tracking.
                if (!isWalkable)
                {
                    if (prevWalkable) barrierStart = total;
                }
                else
                {
                    if (!prevWalkable && barrierStart >= 0)
                    {
                        barriers.Add($"cells {barrierStart}-{total - 1} " +
                            $"({total - barrierStart} wide)");
                    }
                }
                prevWalkable = isWalkable;
                if (cachedH >= 2) prevH = cachedH;
                total++;

                if (cx == ex && cz == ez) break;
                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; cx += stepX; }
                if (e2 < dx) { err += dx; cz += stepZ; }
            }

            if (!prevWalkable && barrierStart >= 0)
                barriers.Add($"cells {barrierStart}-{total - 1} " +
                    $"({total - barrierStart} wide)");

            sb.AppendLine($"\n--- Profile Summary ---");
            sb.AppendLine($"Total: {total} cells. Walkable: {walkableCount} " +
                $"Ocean: {oceanCount} Obstacle: {obstCount} " +
                $"Slope-blocked: {slopeCount}");
            sb.AppendLine($"Max slope: {maxSlopeSeen}cm");
            sb.AppendLine($"Grid mismatches: {mismatchCount}");
            sb.AppendLine($"Barriers: {barriers.Count}");
            foreach (var b in barriers)
                sb.AppendLine($"  {b}");

            MelonLoader.MelonLogger.Msg(sb.ToString());
        }

        /// <summary>
        /// Census of all obstacle colliders near the player.
        /// Separates triggers (passthrough) from solid (blocking).
        /// </summary>
        private static void ObstacleCensus(Vector3 playerPos, float radius)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\n=== 3. OBSTACLE CENSUS (radius={radius}m) ===");

            Vector3 checkPos = new Vector3(playerPos.x,
                playerPos.y + 0.5f, playerPos.z);
            var colliders = Physics.OverlapSphere(
                checkPos, radius, ObstacleLayerMask);

            if (colliders == null || colliders.Length == 0)
            {
                sb.AppendLine("No obstacle colliders found.");
                MelonLoader.MelonLogger.Msg(sb.ToString());
                return;
            }

            int trigCount = 0, solidCount = 0;
            var solidNames = new Dictionary<string, int>();
            var trigNames = new Dictionary<string, int>();

            sb.AppendLine($"Total colliders found: {colliders.Length}");
            sb.AppendLine("");

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null) continue;

                bool isTrig = col.isTrigger;
                string name = col.gameObject?.name ?? "?";
                int layer = col.gameObject?.layer ?? -1;
                string tag = col.gameObject?.tag ?? "?";
                string colType = col.GetType().Name;
                var pos = col.transform.position;
                var bSize = col.bounds.size;
                var bCenter = col.bounds.center;

                Vector3 closest = col.ClosestPoint(checkPos);
                float dist = Vector3.Distance(checkPos, closest);

                if (isTrig)
                {
                    trigCount++;
                    trigNames[name] = trigNames.GetValueOrDefault(name) + 1;
                }
                else
                {
                    solidCount++;
                    solidNames[name] = solidNames.GetValueOrDefault(name) + 1;
                }

                // Log individual colliders within 5m.
                if (dist < 5f)
                {
                    sb.AppendLine(
                        $"  [{(isTrig ? "TRIGGER" : "SOLID")}] " +
                        $"name=\"{name}\" layer={layer} tag=\"{tag}\" " +
                        $"type={colType} " +
                        $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                        $"bounds=({bSize.x:F1},{bSize.y:F1},{bSize.z:F1}) " +
                        $"dist={dist:F2}m");
                }
            }

            sb.AppendLine($"\n--- Census Summary ---");
            sb.AppendLine($"Solid (blocking): {solidCount}");
            foreach (var kvp in solidNames)
                sb.AppendLine($"  \"{kvp.Key}\": {kvp.Value}");
            sb.AppendLine($"Trigger (passthrough): {trigCount}");
            foreach (var kvp in trigNames)
                sb.AppendLine($"  \"{kvp.Key}\": {kvp.Value}");

            MelonLoader.MelonLogger.Msg(sb.ToString());
        }

        /// <summary>
        /// Verifies that the cached grid matches fresh physics/CalcHeight
        /// queries at the player's location.
        /// </summary>
        private static void GridAccuracyCheck(
            WorldmapGridFormat.CachedGrid grid, Vector3 playerPos)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== 4. GRID ACCURACY CHECK (20x20 around player) ===");

            grid.WorldToGrid(playerPos.x, playerPos.z,
                out int pax, out int paz);

            int matches = 0, mismatches = 0;
            int range = 10;

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dz = -range; dz <= range; dz++)
                {
                    int ax = pax + dx;
                    int az = paz + dz;
                    if (ax < 0 || ax >= grid.GridW ||
                        az < 0 || az >= grid.GridH)
                        continue;

                    ushort cached = FootCell(grid, ax, az);
                    Vector3 cellWorld = grid.GridToWorld(ax, az);

                    // Fresh CalcHeight.
                    float freshY = GameUtility.CalcHeight(
                        new Vector3(cellWorld.x, 150f, cellWorld.z),
                        out bool freshOk, 300f);

                    // Fresh obstacle check.
                    bool freshHasSolid = false;
                    if (freshOk)
                    {
                        Vector3 checkPos = new Vector3(
                            cellWorld.x, freshY + 0.5f, cellWorld.z);
                        // Two-tier check matching grid generator.
                        var cols22 = Physics.OverlapSphere(
                            checkPos, ObstacleSearchRadius, GridTerrainMask);
                        if (cols22 != null)
                        {
                            for (int c = 0; c < cols22.Length; c++)
                            {
                                if (cols22[c] == null || cols22[c].isTrigger)
                                    continue;
                                float dist = Vector3.Distance(checkPos,
                                    cols22[c].ClosestPoint(checkPos));
                                if (dist < PlayerRadius)
                                {
                                    freshHasSolid = true;
                                    break;
                                }
                            }
                        }
                        if (!freshHasSolid)
                        {
                            var cols23 = Physics.OverlapSphere(
                                checkPos, ObstacleSearchRadius, GridCharaWallMask);
                            if (cols23 != null)
                            {
                                for (int c = 0; c < cols23.Length; c++)
                                {
                                    if (cols23[c] == null || cols23[c].isTrigger)
                                        continue;
                                    float dist = Vector3.Distance(checkPos,
                                        cols23[c].ClosestPoint(checkPos));
                                    if (dist < 0.25f)
                                    {
                                        freshHasSolid = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Compare.
                    bool ok = true;
                    string issue = "";

                    if (cached == 0 && freshOk)
                    {
                        ok = false;
                        issue = "grid=ocean fresh=terrain";
                    }
                    else if (cached >= 2 && !freshOk)
                    {
                        ok = false;
                        issue = "grid=terrain fresh=ocean";
                    }
                    else if (cached == 1 && !freshHasSolid)
                    {
                        ok = false;
                        issue = "grid=obstacle fresh=NO_SOLID";
                    }
                    else if (cached >= 2 && freshHasSolid)
                    {
                        ok = false;
                        issue = "grid=walkable fresh=HAS_SOLID";
                    }

                    if (ok) matches++;
                    else
                    {
                        mismatches++;
                        sb.AppendLine(
                            $"  MISMATCH ({ax},{az}) cached={cached} " +
                            $"freshY={freshY:F2} freshOk={freshOk} " +
                            $"freshSolid={freshHasSolid} — {issue}");
                    }
                }
            }

            sb.AppendLine($"Checked: {matches + mismatches} cells. " +
                $"Match: {matches} Mismatch: {mismatches}");

            MelonLoader.MelonLogger.Msg(sb.ToString());
        }

        /// <summary>
        /// Samples CalcHeight every 0.5m from player to target, finding
        /// maximum slopes and CalcHeight failures.
        /// </summary>
        private static void SlopeAnalysis(Vector3 playerPos,
            Vector3 targetPos, StringBuilder summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== 5. SLOPE ANALYSIS (Player → Target) ===");

            float cellSize = WorldmapGridGenerator.CellSize;
            Vector3 dir = (targetPos - playerPos);
            dir.y = 0;
            float totalDist = dir.magnitude;
            dir = dir.normalized;

            int steps = (int)(totalDist / cellSize);
            sb.AppendLine($"Distance: {totalDist:F1}m, Steps: {steps}");

            float prevY = float.MinValue;
            bool prevOk = false;
            int maxSlopeCm = 0;
            Vector3 maxSlopePos = Vector3.zero;
            int slopeOver100 = 0, slopeOver50 = 0;
            int calcHeightFails = 0;

            // Track top 10 steepest slopes.
            var steepest = new List<(int slopeCm, Vector3 pos, int step)>();

            for (int i = 0; i <= steps && i < 10000; i++)
            {
                Vector3 samplePos = playerPos + dir * (i * cellSize);
                float y = GameUtility.CalcHeight(
                    new Vector3(samplePos.x, 150f, samplePos.z),
                    out bool ok, 300f);

                if (!ok)
                {
                    calcHeightFails++;
                    if (i % 100 == 0 || !prevOk)
                        sb.AppendLine($"  [{i}] CalcHeight FAIL at " +
                            $"({samplePos.x:F1},{samplePos.z:F1})");
                    prevOk = false;
                    continue;
                }

                if (prevOk && prevY != float.MinValue)
                {
                    int slopeCm = (int)(Math.Abs(y - prevY) * 100);
                    if (slopeCm > maxSlopeCm)
                    {
                        maxSlopeCm = slopeCm;
                        maxSlopePos = samplePos;
                    }
                    if (slopeCm > 100) slopeOver100++;
                    if (slopeCm > 50) slopeOver50++;

                    // Track steepest.
                    if (slopeCm > 30)
                    {
                        steepest.Add((slopeCm, samplePos, i));
                    }

                    // Log steep slopes.
                    if (slopeCm > MaxClimbCm)
                    {
                        sb.AppendLine(
                            $"  [{i}] SLOPE={slopeCm}cm " +
                            $"y={y:F2} prevY={prevY:F2} " +
                            $"at ({samplePos.x:F1},{samplePos.z:F1})");
                    }
                }

                prevY = y;
                prevOk = true;
            }

            // Sort steepest and show top 10.
            steepest.Sort((a, b) => b.slopeCm.CompareTo(a.slopeCm));
            sb.AppendLine($"\n--- Top 10 steepest slopes ---");
            for (int i = 0; i < Math.Min(10, steepest.Count); i++)
            {
                var s = steepest[i];
                sb.AppendLine(
                    $"  {s.slopeCm}cm at step {s.step} " +
                    $"({s.pos.x:F1},{s.pos.z:F1})");
            }

            sb.AppendLine($"\n--- Slope Summary ---");
            sb.AppendLine($"Max slope: {maxSlopeCm}cm at " +
                $"({maxSlopePos.x:F1},{maxSlopePos.z:F1})");
            sb.AppendLine($"Slopes > {MaxClimbCm}cm: {slopeOver100}");
            sb.AppendLine($"Slopes > 50cm: {slopeOver50}");
            sb.AppendLine($"CalcHeight failures: {calcHeightFails}");

            MelonLoader.MelonLogger.Msg(sb.ToString());

            summary.Append($"Max slope: {maxSlopeCm}cm. " +
                $">{MaxClimbCm}cm: {slopeOver100}. ");
        }

        /// <summary>
        /// Scans all CharaWall collider segments on the world map to map
        /// the boundary system and find gaps (road openings) between regions.
        /// Triggered separately from RunAll — use F8 on the world map.
        /// </summary>
        public static void ScanCharaWalls(Vector3 playerPos)
        {
            ScreenReader.Say("Scanning character walls. Please wait.");
            MelonLoader.MelonLogger.Msg("=== CHARAWALL BOUNDARY SCAN ===");

            var sb = new StringBuilder();
            sb.AppendLine($"Player: ({playerPos.x:F1}, {playerPos.z:F1})");

            // Walk small spheres (50m radius) along the path from player
            // to target, collecting layer 23 colliders. Small radius avoids
            // freezing the physics engine.
            int charaWallLayer = 1 << 23;
            var uniqueColliders = new Dictionary<int, Collider>();

            var target0 = NavigationHandler.LastAutoWalkTarget;
            if (target0.HasValue)
            {
                Vector3 dir = target0.Value - playerPos;
                dir.y = 0;
                float pathLen = dir.magnitude;
                Vector3 pathDir = pathLen > 0.01f ? dir / pathLen : Vector3.forward;
                // Perpendicular sweep: also scan ±50m to each side.
                Vector3 perpDir = new Vector3(-pathDir.z, 0, pathDir.x);

                // Step along path every 40m (50m radius overlaps by 10m).
                for (float d = -50f; d <= pathLen + 50f; d += 40f)
                {
                    Vector3 center = playerPos + pathDir * d;
                    AddSphereCols(uniqueColliders, center, 50f, charaWallLayer);
                    // Also sweep sideways to catch walls off the direct path.
                    AddSphereCols(uniqueColliders, center + perpDir * 40f, 50f, charaWallLayer);
                    AddSphereCols(uniqueColliders, center - perpDir * 40f, 50f, charaWallLayer);
                }
            }
            else
            {
                // No target — just scan around player.
                AddSphereCols(uniqueColliders, playerPos, 50f, charaWallLayer);
            }

            if (uniqueColliders.Count == 0)
            {
                sb.AppendLine("No CharacterWall colliders found.");
                MelonLoader.MelonLogger.Msg(sb.ToString());
                ScreenReader.Say("No wall colliders found.");
                return;
            }

            // Group by parent name.
            var wallsByParent = new Dictionary<string, List<WallSegment>>();
            int totalWalls = 0;

            foreach (var kvp2 in uniqueColliders)
            {
                var col = kvp2.Value;
                if (col == null) continue;
                if (col.gameObject == null) continue;

                totalWalls++;
                string parentName = col.transform.parent != null
                    ? col.transform.parent.name : "none";
                string grandparentName = col.transform.parent?.parent != null
                    ? col.transform.parent.parent.name : "none";

                var bounds = col.bounds;
                var seg = new WallSegment
                {
                    Name = col.gameObject.name,
                    Parent = parentName,
                    Grandparent = grandparentName,
                    IsTrigger = col.isTrigger,
                    ColType = col.GetType().Name,
                    BoundsMin = bounds.min,
                    BoundsMax = bounds.max,
                    BoundsCenter = bounds.center,
                    BoundsSize = bounds.size,
                    Position = col.transform.position,
                    DistToPlayer = Vector3.Distance(playerPos, bounds.ClosestPoint(playerPos))
                };

                if (!wallsByParent.ContainsKey(parentName))
                    wallsByParent[parentName] = new List<WallSegment>();
                wallsByParent[parentName].Add(seg);
            }

            sb.AppendLine($"Total layer 23 colliders: {totalWalls}");
            sb.AppendLine($"Parent groups: {wallsByParent.Count}");
            sb.AppendLine("");

            // Log each parent group sorted by distance to player.
            var sortedParents = new List<KeyValuePair<string, List<WallSegment>>>(wallsByParent);
            sortedParents.Sort((a, b) =>
            {
                float minA = float.MaxValue, minB = float.MaxValue;
                foreach (var s in a.Value)
                    if (s.DistToPlayer < minA) minA = s.DistToPlayer;
                foreach (var s in b.Value)
                    if (s.DistToPlayer < minB) minB = s.DistToPlayer;
                return minA.CompareTo(minB);
            });

            foreach (var kvp in sortedParents)
            {
                var segs = kvp.Value;
                segs.Sort((a, b) => a.DistToPlayer.CompareTo(b.DistToPlayer));

                sb.AppendLine($"--- Parent: \"{kvp.Key}\" ({segs.Count} segments) " +
                    $"gp=\"{segs[0].Grandparent}\" ---");

                // Compute overall bounding box for this parent group.
                float groupMinX = float.MaxValue, groupMinZ = float.MaxValue;
                float groupMaxX = float.MinValue, groupMaxZ = float.MinValue;
                foreach (var s in segs)
                {
                    if (s.BoundsMin.x < groupMinX) groupMinX = s.BoundsMin.x;
                    if (s.BoundsMin.z < groupMinZ) groupMinZ = s.BoundsMin.z;
                    if (s.BoundsMax.x > groupMaxX) groupMaxX = s.BoundsMax.x;
                    if (s.BoundsMax.z > groupMaxZ) groupMaxZ = s.BoundsMax.z;
                }
                sb.AppendLine($"  Group XZ extent: X=[{groupMinX:F1},{groupMaxX:F1}] " +
                    $"Z=[{groupMinZ:F1},{groupMaxZ:F1}] " +
                    $"({groupMaxX - groupMinX:F1}m x {groupMaxZ - groupMinZ:F1}m)");

                foreach (var s in segs)
                {
                    sb.AppendLine(
                        $"  [{(s.IsTrigger ? "TRIG" : "SOLID")}] " +
                        $"d={s.DistToPlayer:F1}m " +
                        $"type={s.ColType} " +
                        $"pos=({s.Position.x:F1},{s.Position.z:F1}) " +
                        $"bMin=({s.BoundsMin.x:F1},{s.BoundsMin.z:F1}) " +
                        $"bMax=({s.BoundsMax.x:F1},{s.BoundsMax.z:F1}) " +
                        $"size=({s.BoundsSize.x:F1},{s.BoundsSize.y:F1},{s.BoundsSize.z:F1})");
                }
                sb.AppendLine("");
            }

            // --- Gap analysis for nearby CharaWall groups ---
            // For each parent group within 300m, project wall segments onto
            // a line perpendicular to the player-to-target direction and
            // find uncovered intervals (gaps).
            var target = NavigationHandler.LastAutoWalkTarget;
            if (target.HasValue)
            {
                sb.AppendLine("=== GAP ANALYSIS (near player→target path) ===");
                Vector3 toTarget = target.Value - playerPos;
                toTarget.y = 0;
                float pathLen = toTarget.magnitude;
                Vector3 pathDir = toTarget.normalized;
                // Perpendicular direction (rotate 90 degrees in XZ).
                Vector3 perpDir = new Vector3(-pathDir.z, 0, pathDir.x);

                sb.AppendLine($"Path dir: ({pathDir.x:F2}, {pathDir.z:F2}), " +
                    $"Perp dir: ({perpDir.x:F2}, {perpDir.z:F2})");
                sb.AppendLine($"Path length: {pathLen:F1}m");
                sb.AppendLine("");

                // For each parent group, find segments that cross or are
                // near the player→target line (within 20m perpendicular).
                foreach (var kvp in sortedParents)
                {
                    // Only analyze groups within 300m.
                    float closestDist = float.MaxValue;
                    foreach (var s in kvp.Value)
                        if (s.DistToPlayer < closestDist) closestDist = s.DistToPlayer;
                    if (closestDist > 300f) continue;

                    var nearSegments = new List<(float perpMin, float perpMax,
                        float alongMin, float alongMax, WallSegment seg)>();

                    foreach (var s in kvp.Value)
                    {
                        if (s.IsTrigger) continue;

                        // Project segment bounding box corners onto path and
                        // perp axes relative to player position.
                        float[] perpVals = new float[4];
                        float[] alongVals = new float[4];
                        Vector3[] corners = {
                            new Vector3(s.BoundsMin.x, 0, s.BoundsMin.z),
                            new Vector3(s.BoundsMax.x, 0, s.BoundsMin.z),
                            new Vector3(s.BoundsMin.x, 0, s.BoundsMax.z),
                            new Vector3(s.BoundsMax.x, 0, s.BoundsMax.z)
                        };

                        for (int c = 0; c < 4; c++)
                        {
                            Vector3 rel = corners[c] - new Vector3(playerPos.x, 0, playerPos.z);
                            alongVals[c] = Vector3.Dot(rel, pathDir);
                            perpVals[c] = Vector3.Dot(rel, perpDir);
                        }

                        float pMin = Mathf.Min(Mathf.Min(perpVals[0], perpVals[1]),
                            Mathf.Min(perpVals[2], perpVals[3]));
                        float pMax = Mathf.Max(Mathf.Max(perpVals[0], perpVals[1]),
                            Mathf.Max(perpVals[2], perpVals[3]));
                        float aMin = Mathf.Min(Mathf.Min(alongVals[0], alongVals[1]),
                            Mathf.Min(alongVals[2], alongVals[3]));
                        float aMax = Mathf.Max(Mathf.Max(alongVals[0], alongVals[1]),
                            Mathf.Max(alongVals[2], alongVals[3]));

                        // Keep segments that are within ±20m perpendicular
                        // to the path line.
                        if (pMin < 20f && pMax > -20f)
                        {
                            nearSegments.Add((pMin, pMax, aMin, aMax, s));
                        }
                    }

                    if (nearSegments.Count == 0) continue;

                    sb.AppendLine($"--- \"{kvp.Key}\" ({nearSegments.Count} near-path segments) ---");

                    // Sort by along-path position.
                    nearSegments.Sort((a, b) => a.alongMin.CompareTo(b.alongMin));

                    foreach (var (pMin, pMax, aMin, aMax, s) in nearSegments)
                    {
                        sb.AppendLine(
                            $"  along=[{aMin:F1},{aMax:F1}] " +
                            $"perp=[{pMin:F1},{pMax:F1}] " +
                            $"size=({s.BoundsSize.x:F1},{s.BoundsSize.z:F1}) " +
                            $"pos=({s.Position.x:F1},{s.Position.z:F1})");
                    }

                    // Find gaps in perpendicular coverage at each along-path
                    // slice where walls exist. Sample every 5m along the path.
                    sb.AppendLine("  Perp coverage scan (every 5m along path):");
                    for (float along = 0; along < pathLen; along += 5f)
                    {
                        // Collect all perp intervals at this along position.
                        var intervals = new List<(float min, float max)>();
                        foreach (var (pMin, pMax, aMin, aMax, s) in nearSegments)
                        {
                            if (aMin <= along && aMax >= along)
                                intervals.Add((pMin, pMax));
                        }

                        if (intervals.Count == 0) continue;

                        // Merge overlapping intervals and find gaps.
                        intervals.Sort((a, b) => a.min.CompareTo(b.min));
                        var merged = new List<(float min, float max)>();
                        var cur = intervals[0];
                        for (int j = 1; j < intervals.Count; j++)
                        {
                            if (intervals[j].min <= cur.max + 0.1f)
                            {
                                if (intervals[j].max > cur.max)
                                    cur.max = intervals[j].max;
                            }
                            else
                            {
                                merged.Add(cur);
                                cur = intervals[j];
                            }
                        }
                        merged.Add(cur);

                        // Report gaps between merged intervals.
                        for (int j = 1; j < merged.Count; j++)
                        {
                            float gapStart = merged[j - 1].max;
                            float gapEnd = merged[j].min;
                            float gapWidth = gapEnd - gapStart;
                            if (gapWidth > 0.5f)
                            {
                                // Convert gap center to world coordinates.
                                float gapCenter = (gapStart + gapEnd) / 2f;
                                Vector3 gapWorldPos = playerPos +
                                    pathDir * along + perpDir * gapCenter;
                                sb.AppendLine(
                                    $"  *** GAP at along={along:F0}m " +
                                    $"perp=[{gapStart:F1},{gapEnd:F1}] " +
                                    $"width={gapWidth:F1}m " +
                                    $"world=({gapWorldPos.x:F1},{gapWorldPos.z:F1})");
                            }
                        }

                        // Also report if coverage doesn't span across path
                        // (gap at the edges).
                        if (merged[0].min > 1f)
                        {
                            Vector3 edgePos = playerPos + pathDir * along +
                                perpDir * (merged[0].min - 2f);
                            sb.AppendLine(
                                $"  *** OPEN LEFT at along={along:F0}m " +
                                $"wall starts perp={merged[0].min:F1}m " +
                                $"world=({edgePos.x:F1},{edgePos.z:F1})");
                        }
                        if (merged[merged.Count - 1].max < -1f)
                        {
                            Vector3 edgePos = playerPos + pathDir * along +
                                perpDir * (merged[merged.Count - 1].max + 2f);
                            sb.AppendLine(
                                $"  *** OPEN RIGHT at along={along:F0}m " +
                                $"wall ends perp={merged[merged.Count - 1].max:F1}m " +
                                $"world=({edgePos.x:F1},{edgePos.z:F1})");
                        }
                    }
                    sb.AppendLine("");
                }
            }

            // --- Route trace to gaps ---
            // For each gap wider than 1.5m, trace from the player to the
            // gap center and report every blocked cell with layer-specific
            // obstacle info (layer 22 vs 23). This tells us exactly what
            // blocks the approach route.
            if (target0.HasValue)
            {
                TraceRoutesToGaps(sb, playerPos, target0.Value);
            }

            MelonLoader.MelonLogger.Msg(sb.ToString());

            // Summary for screen reader.
            int nearCount = 0;
            foreach (var kvp in wallsByParent)
                foreach (var s in kvp.Value)
                    if (s.DistToPlayer < 50f) nearCount++;
            ScreenReader.Say($"{totalWalls} wall segments in {wallsByParent.Count} groups. " +
                $"{nearCount} within 50 meters. Check log for gap and route analysis.");
        }

        /// <summary>
        /// Traces routes from the player to key gap positions and to the
        /// target, logging every blocked cell with layer-separated obstacle
        /// details. This reveals whether layer 22 (Col_Obstacle) or layer 23
        /// (CharaWall) blocks the approach to each gap.
        /// </summary>
        private static void TraceRoutesToGaps(StringBuilder sb,
            Vector3 playerPos, Vector3 targetPos)
        {
            sb.AppendLine("=== ROUTE TRACE TO GAPS AND TARGET ===");

            var grid = LoadGrid(FieldManager.Instance);
            if (grid == null)
            {
                sb.AppendLine("No cached grid — cannot trace routes.");
                return;
            }

            // Collect trace destinations: the target plus several points
            // along the western detour (based on discovered gap positions).
            // We trace to multiple waypoints to map the full approach route.
            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0;
            float pathLen = toTarget.magnitude;
            Vector3 pathDir = toTarget.normalized;
            Vector3 perpDir = new Vector3(-pathDir.z, 0, pathDir.x);

            var destinations = new List<(string label, Vector3 pos)>();

            // Add the gap positions discovered in the previous analysis.
            // These are the world coordinates from the gap analysis output.
            // Rather than hardcode them, compute them from the path geometry.
            // Trace to points at key along/perp offsets where gaps were found.
            float[] gapAlongValues = { 110f, 140f, 145f };
            float[] gapPerpValues = { -8.8f, -3.8f, -12.3f };
            string[] gapLabels = {
                "Gap110(1.8m)", "Gap140(3.7m)", "Gap145(5.1m)" };

            for (int g = 0; g < gapAlongValues.Length; g++)
            {
                Vector3 gapPos = playerPos +
                    pathDir * gapAlongValues[g] +
                    perpDir * gapPerpValues[g];
                destinations.Add((gapLabels[g], gapPos));
            }

            // Also trace the western detour approach: go 15m west at
            // along=50m (before the wall), to see if the western flank
            // is clear.
            Vector3 westFlank = playerPos + pathDir * 50f + perpDir * -15f;
            destinations.Add(("WestFlank(50m,-15m)", westFlank));

            // And the target itself.
            destinations.Add(("Target(Salva)", targetPos));

            int layer22Mask = 1 << 22;
            int layer23Mask = 1 << 23;
            int bothMask = layer22Mask | layer23Mask;

            foreach (var (label, destPos) in destinations)
            {
                sb.AppendLine($"\n--- Route: Player → {label} ---");
                sb.AppendLine($"  From: ({playerPos.x:F1},{playerPos.z:F1})");
                sb.AppendLine($"  To:   ({destPos.x:F1},{destPos.z:F1})");

                grid.WorldToGrid(playerPos.x, playerPos.z,
                    out int sx, out int sz);
                grid.WorldToGrid(destPos.x, destPos.z,
                    out int ex, out int ez);
                sx = Mathf.Clamp(sx, 0, grid.GridW - 1);
                sz = Mathf.Clamp(sz, 0, grid.GridH - 1);
                ex = Mathf.Clamp(ex, 0, grid.GridW - 1);
                ez = Mathf.Clamp(ez, 0, grid.GridH - 1);

                float dist = Vector3.Distance(
                    new Vector3(playerPos.x, 0, playerPos.z),
                    new Vector3(destPos.x, 0, destPos.z));
                sb.AppendLine($"  Distance: {dist:F1}m");

                // Bresenham line from start to end.
                int dx = Math.Abs(ex - sx);
                int dz = Math.Abs(ez - sz);
                int stepX = sx < ex ? 1 : -1;
                int stepZ = sz < ez ? 1 : -1;
                int err = dx - dz;
                int cx = sx, cz = sz;

                int totalCells = 0, walkable = 0, blocked = 0;
                int blockedL22 = 0, blockedL23 = 0, blockedBoth = 0;
                int blockedOcean = 0;
                int barrierStart = -1;
                bool prevBlocked = false;
                var barriers = new List<string>();

                while (totalCells < 3000)
                {
                    bool inBounds = cx >= 0 && cx < grid.GridW &&
                        cz >= 0 && cz < grid.GridH;
                    ushort cached = inBounds ? FootCell(grid, cx, cz) : (ushort)0;
                    Vector3 cellWorld = grid.GridToWorld(cx, cz);

                    bool isBlocked = false;
                    string blockInfo = "";

                    if (cached == 0)
                    {
                        isBlocked = true;
                        blockInfo = "OCEAN";
                        blockedOcean++;
                    }
                    else if (cached == 1)
                    {
                        isBlocked = true;

                        // Fresh layer-separated obstacle check.
                        float freshY = GameUtility.CalcHeight(
                            new Vector3(cellWorld.x, 150f, cellWorld.z),
                            out bool freshOk, 300f);

                        if (!freshOk)
                        {
                            blockInfo = "OBSTACLE(grid=1,noGround)";
                        }
                        else
                        {
                            Vector3 checkPos = new Vector3(
                                cellWorld.x, freshY + 0.5f, cellWorld.z);

                            // Check layer 22 only.
                            int l22Count = 0;
                            float l22Nearest = 99f;
                            string l22Name = "";
                            var cols22 = Physics.OverlapSphere(
                                checkPos, 1.0f, layer22Mask);
                            if (cols22 != null)
                            {
                                for (int c = 0; c < cols22.Length; c++)
                                {
                                    if (cols22[c] == null || cols22[c].isTrigger)
                                        continue;
                                    float d22 = Vector3.Distance(checkPos,
                                        cols22[c].ClosestPoint(checkPos));
                                    if (d22 < 0.50f)
                                    {
                                        l22Count++;
                                        if (d22 < l22Nearest)
                                        {
                                            l22Nearest = d22;
                                            l22Name = cols22[c].gameObject?.name ?? "?";
                                        }
                                    }
                                }
                            }

                            // Check layer 23 only.
                            int l23Count = 0;
                            float l23Nearest = 99f;
                            string l23Name = "";
                            string l23Parent = "";
                            var cols23 = Physics.OverlapSphere(
                                checkPos, 1.0f, layer23Mask);
                            if (cols23 != null)
                            {
                                for (int c = 0; c < cols23.Length; c++)
                                {
                                    if (cols23[c] == null || cols23[c].isTrigger)
                                        continue;
                                    float d23 = Vector3.Distance(checkPos,
                                        cols23[c].ClosestPoint(checkPos));
                                    if (d23 < 0.50f)
                                    {
                                        l23Count++;
                                        if (d23 < l23Nearest)
                                        {
                                            l23Nearest = d23;
                                            l23Name = cols23[c].gameObject?.name ?? "?";
                                            l23Parent = cols23[c].transform.parent != null
                                                ? cols23[c].transform.parent.name : "?";
                                        }
                                    }
                                }
                            }

                            bool hasL22 = l22Count > 0;
                            bool hasL23 = l23Count > 0;

                            if (hasL22 && hasL23)
                            {
                                blockInfo = $"L22+L23 " +
                                    $"L22:{l22Name}(d={l22Nearest:F2}) " +
                                    $"L23:{l23Parent}/{l23Name}(d={l23Nearest:F2})";
                                blockedBoth++;
                            }
                            else if (hasL22)
                            {
                                blockInfo = $"L22 {l22Name}(d={l22Nearest:F2})";
                                blockedL22++;
                            }
                            else if (hasL23)
                            {
                                blockInfo = $"L23 {l23Parent}/{l23Name}(d={l23Nearest:F2})";
                                blockedL23++;
                            }
                            else
                            {
                                blockInfo = "OBSTACLE(grid=1,freshClear)";
                                // Grid says obstacle but fresh check finds nothing.
                                // Could be a dynamic object that moved.
                            }
                        }

                        blocked++;
                    }
                    else
                    {
                        walkable++;
                    }

                    // Log blocked cells.
                    if (isBlocked)
                    {
                        sb.AppendLine(
                            $"  [{totalCells}] ({cx},{cz}) " +
                            $"w=({cellWorld.x:F1},{cellWorld.z:F1}) " +
                            $"{blockInfo}");

                        if (!prevBlocked) barrierStart = totalCells;
                    }
                    else
                    {
                        if (prevBlocked && barrierStart >= 0)
                        {
                            barriers.Add(
                                $"cells {barrierStart}-{totalCells - 1} " +
                                $"({totalCells - barrierStart} wide)");
                        }
                    }
                    prevBlocked = isBlocked;
                    totalCells++;

                    if (cx == ex && cz == ez) break;
                    int e2 = 2 * err;
                    if (e2 > -dz) { err -= dz; cx += stepX; }
                    if (e2 < dx) { err += dx; cz += stepZ; }
                }

                if (prevBlocked && barrierStart >= 0)
                    barriers.Add(
                        $"cells {barrierStart}-{totalCells - 1} " +
                        $"({totalCells - barrierStart} wide)");

                sb.AppendLine($"  Summary: {totalCells} cells, " +
                    $"{walkable} walkable, {blocked} obstacle " +
                    $"(L22={blockedL22} L23={blockedL23} both={blockedBoth}) " +
                    $"{blockedOcean} ocean");
                sb.AppendLine($"  Barriers: {barriers.Count}");
                foreach (var b in barriers)
                    sb.AppendLine($"    {b}");
            }
        }

        /// <summary>
        /// Adds colliders from an OverlapSphere to a dictionary, deduplicating
        /// by instance ID.
        /// </summary>
        private static void AddSphereCols(Dictionary<int, Collider> dict,
            Vector3 center, float radius, int layerMask)
        {
            var cols = Physics.OverlapSphere(center, radius, layerMask);
            if (cols == null) return;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                int id = cols[i].GetInstanceID();
                if (!dict.ContainsKey(id))
                    dict[id] = cols[i];
            }
        }

        /// <summary>Data for a single CharaWall collider segment.</summary>
        private struct WallSegment
        {
            public string Name;
            public string Parent;
            public string Grandparent;
            public bool IsTrigger;
            public string ColType;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;
            public Vector3 BoundsCenter;
            public Vector3 BoundsSize;
            public Vector3 Position;
            public float DistToPlayer;
        }

        /// <summary>Loads the cached grid for the current world map.</summary>
        private static WorldmapGridFormat.CachedGrid LoadGrid(
            FieldManager fm)
        {
            return WorldmapGridFormat.LoadGrid(fm.WorldmapID);
        }

        /// <summary>
        /// Effective cell value for FOOT analysis, format-independent: on a
        /// v2 (WMGI) grid, foot-blocked cells carry a real height plus a
        /// flags bit — this maps them back to the legacy obstacle value (1)
        /// so all the classification code in this file (which predates the
        /// flags lane) keeps reporting walls as walls instead of walking
        /// its analyses straight through them.
        /// </summary>
        private static ushort FootCell(WorldmapGridFormat.CachedGrid grid,
            int ax, int az)
        {
            ushort h = grid.Height[ax, az];
            if (h >= 2 && (grid.Flags[(long)ax * grid.GridH + az] &
                WorldmapGridFormat.CachedGrid.FlagFootBlocked) != 0)
                return 1;
            return h;
        }
    }
}
