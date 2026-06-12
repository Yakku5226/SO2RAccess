using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    /// <summary>
    /// Diagnostic tool that scans the NavMesh on field maps (towns, dungeons)
    /// to identify disconnected islands, their bounds, Y-ranges, and the gaps
    /// between them. Triggered by F11 in debug mode on non-world-map fields.
    /// Results are logged and saved to a file for analysis.
    /// </summary>
    public static class NavMeshIslandDiagnostics
    {
        /// <summary>Grid cell spacing for NavMesh sampling (meters).</summary>
        private const float CellSize = 2.0f;

        /// <summary>Radius for NavMesh.SamplePosition per grid cell.</summary>
        private const float SampleRadius = 1.5f;

        /// <summary>
        /// How far to expand the search bounds beyond the player position
        /// when discovering the map extents (meters).
        /// </summary>
        private const float SearchExtent = 300f;

        /// <summary>
        /// Y-difference threshold to consider two adjacent samples as
        /// potentially on different vertical layers (meters).
        /// Samples within one cell that differ by more than this in Y
        /// are treated as separate vertical layers.
        /// </summary>
        private const float VerticalLayerThreshold = 2.0f;

        /// <summary>
        /// Maximum Y-difference between adjacent island samples to
        /// consider them connected (walkable slope/ramp). Samples on
        /// the same island must have gradual Y transitions.
        /// </summary>
        private const float MaxConnectedYDelta = 1.5f;

        /// <summary>Output directory for diagnostic results.</summary>
        private static readonly string OutputDir =
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess");

        /// <summary>
        /// Represents a single NavMesh sample point on the grid.
        /// </summary>
        private struct NavSample
        {
            public int GridX;
            public int GridZ;
            public Vector3 Position; // Actual NavMesh position (snapped)
            public int IslandId;     // -1 = unassigned
        }

        /// <summary>
        /// Holds summary data for one NavMesh island.
        /// </summary>
        private class IslandInfo
        {
            public int Id;
            public List<NavSample> Samples = new List<NavSample>();
            public float MinX = float.MaxValue, MaxX = float.MinValue;
            public float MinY = float.MaxValue, MaxY = float.MinValue;
            public float MinZ = float.MaxValue, MaxZ = float.MinValue;

            public void AddSample(NavSample s)
            {
                Samples.Add(s);
                if (s.Position.x < MinX) MinX = s.Position.x;
                if (s.Position.x > MaxX) MaxX = s.Position.x;
                if (s.Position.y < MinY) MinY = s.Position.y;
                if (s.Position.y > MaxY) MaxY = s.Position.y;
                if (s.Position.z < MinZ) MinZ = s.Position.z;
                if (s.Position.z > MaxZ) MaxZ = s.Position.z;
            }

            public Vector3 Center => new Vector3(
                (MinX + MaxX) / 2f,
                (MinY + MaxY) / 2f,
                (MinZ + MaxZ) / 2f);

            public float YRange => MaxY - MinY;
        }

        /// <summary>
        /// Represents a gap between two islands — the closest pair of edge
        /// samples between them.
        /// </summary>
        private struct IslandGap
        {
            public int IslandA;
            public int IslandB;
            public Vector3 PointA;
            public Vector3 PointB;
            public float Distance;
            public float YDelta;
        }

        /// <summary>
        /// Runs the full NavMesh island diagnostic on the current field map.
        /// </summary>
        public static void Run(Vector3 playerPos, FieldmapID mapId)
        {
            ScreenReader.Say(
                "Scanning NavMesh islands. This may take up to 60 seconds. Please wait.");
            MelonLoader.MelonLogger.Msg(
                $"=== NAVMESH ISLAND DIAGNOSTICS: {mapId} ===");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Step 1: Discover map bounds by probing outward from player.
            float minX = playerPos.x - SearchExtent;
            float maxX = playerPos.x + SearchExtent;
            float minZ = playerPos.z - SearchExtent;
            float maxZ = playerPos.z + SearchExtent;

            int gridW = (int)((maxX - minX) / CellSize) + 1;
            int gridH = (int)((maxZ - minZ) / CellSize) + 1;

            MelonLoader.MelonLogger.Msg(
                $"[ISLAND] Grid: {gridW}x{gridH} = {gridW * gridH} cells " +
                $"({CellSize}m spacing, {SearchExtent}m extent)");
            MelonLoader.MelonLogger.Msg(
                $"[ISLAND] Bounds: X[{minX:F0},{maxX:F0}] Z[{minZ:F0},{maxZ:F0}]");
            MelonLoader.MelonLogger.Msg(
                $"[ISLAND] Player: ({playerPos.x:F1}, {playerPos.y:F1}, {playerPos.z:F1})");

            // Step 2: Sample NavMesh at each grid cell.
            // A cell can have multiple samples at different Y levels.
            // Key: (gridX, gridZ) -> list of samples at that cell.
            var sampleGrid = new Dictionary<(int, int), List<NavSample>>();
            var allSamples = new List<NavSample>();
            int hitCount = 0;
            int missCount = 0;

            for (int gx = 0; gx < gridW; gx++)
            {
                float worldX = minX + gx * CellSize;
                for (int gz = 0; gz < gridH; gz++)
                {
                    float worldZ = minZ + gz * CellSize;

                    // Sample at multiple Y levels to catch stacked floors.
                    // Probe from high to low.
                    var cellSamples = new List<NavSample>();
                    float[] probeYs = { 50f, 30f, 15f, 5f, 0f, -5f, -15f };

                    foreach (float probeY in probeYs)
                    {
                        Vector3 probePos = new Vector3(worldX, probeY, worldZ);
                        if (NavMesh.SamplePosition(probePos, out NavMeshHit hit,
                            SampleRadius, NavMesh.AllAreas))
                        {
                            // Check if we already have a sample at a similar Y.
                            bool duplicate = false;
                            for (int i = 0; i < cellSamples.Count; i++)
                            {
                                if (Mathf.Abs(cellSamples[i].Position.y -
                                    hit.position.y) < VerticalLayerThreshold)
                                {
                                    duplicate = true;
                                    break;
                                }
                            }

                            if (!duplicate)
                            {
                                var sample = new NavSample
                                {
                                    GridX = gx,
                                    GridZ = gz,
                                    Position = hit.position,
                                    IslandId = -1
                                };
                                cellSamples.Add(sample);
                            }
                        }
                    }

                    if (cellSamples.Count > 0)
                    {
                        hitCount += cellSamples.Count;
                        sampleGrid[(gx, gz)] = cellSamples;
                        allSamples.AddRange(cellSamples);
                    }
                    else
                    {
                        missCount++;
                    }
                }
            }

            MelonLoader.MelonLogger.Msg(
                $"[ISLAND] Sampling complete: {hitCount} hits, {missCount} misses " +
                $"({sw.ElapsedMilliseconds}ms)");

            if (hitCount == 0)
            {
                MelonLoader.MelonLogger.Msg("[ISLAND] No NavMesh found. Aborting.");
                ScreenReader.Say("No NavMesh found on this map.");
                return;
            }

            // Step 3: Build index for fast lookup.
            // Map each sample to its index in allSamples.
            // Also build a spatial lookup: (gx, gz) -> list of indices.
            var cellToIndices = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < allSamples.Count; i++)
            {
                var s = allSamples[i];
                var key = (s.GridX, s.GridZ);
                if (!cellToIndices.ContainsKey(key))
                    cellToIndices[key] = new List<int>();
                cellToIndices[key].Add(i);
            }

            // Step 4: Flood-fill BFS to assign island IDs.
            // Two samples are connected if they are in adjacent grid cells
            // (4-directional) AND their Y difference is within MaxConnectedYDelta.
            int nextIslandId = 0;
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            // Work on a mutable copy for island assignment.
            int[] islandIds = new int[allSamples.Count];
            for (int i = 0; i < islandIds.Length; i++)
                islandIds[i] = -1;

            for (int startIdx = 0; startIdx < allSamples.Count; startIdx++)
            {
                if (islandIds[startIdx] != -1)
                    continue;

                // BFS from this unvisited sample.
                int islandId = nextIslandId++;
                var queue = new Queue<int>();
                queue.Enqueue(startIdx);
                islandIds[startIdx] = islandId;

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    var sample = allSamples[idx];

                    // Check 4 adjacent cells.
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = sample.GridX + dx[d];
                        int nz = sample.GridZ + dz[d];
                        var nkey = (nx, nz);

                        if (!cellToIndices.ContainsKey(nkey))
                            continue;

                        foreach (int ni in cellToIndices[nkey])
                        {
                            if (islandIds[ni] != -1)
                                continue;

                            float yDelta = Mathf.Abs(
                                allSamples[ni].Position.y - sample.Position.y);
                            if (yDelta <= MaxConnectedYDelta)
                            {
                                islandIds[ni] = islandId;
                                queue.Enqueue(ni);
                            }
                        }
                    }
                }
            }

            // Step 5: Build island info objects.
            var islands = new Dictionary<int, IslandInfo>();
            for (int i = 0; i < allSamples.Count; i++)
            {
                int id = islandIds[i];
                if (!islands.ContainsKey(id))
                    islands[id] = new IslandInfo { Id = id };

                var s = allSamples[i];
                s.IslandId = id;
                allSamples[i] = s;
                islands[id].AddSample(s);
            }

            // Sort islands by sample count (largest first).
            var sortedIslands = new List<IslandInfo>(islands.Values);
            sortedIslands.Sort((a, b) => b.Samples.Count.CompareTo(a.Samples.Count));

            MelonLoader.MelonLogger.Msg(
                $"[ISLAND] Found {sortedIslands.Count} islands ({sw.ElapsedMilliseconds}ms)");

            // Step 6: Log island details.
            var sb = new StringBuilder();
            sb.AppendLine($"NavMesh Island Diagnostics: {mapId}");
            sb.AppendLine($"Player: ({playerPos.x:F1}, {playerPos.y:F1}, {playerPos.z:F1})");
            sb.AppendLine($"Grid: {gridW}x{gridH}, CellSize={CellSize}m, " +
                $"Extent={SearchExtent}m");
            sb.AppendLine($"Total samples: {hitCount}, Islands: {sortedIslands.Count}");
            sb.AppendLine();

            // Determine which island the player is on.
            int playerIsland = -1;
            float playerClosest = float.MaxValue;
            for (int i = 0; i < allSamples.Count; i++)
            {
                float dist = Vector3.Distance(playerPos, allSamples[i].Position);
                if (dist < playerClosest)
                {
                    playerClosest = dist;
                    playerIsland = islandIds[i];
                }
            }

            foreach (var island in sortedIslands)
            {
                string playerMarker = island.Id == playerIsland ? " *** PLAYER ***" : "";
                string line =
                    $"Island {island.Id}: {island.Samples.Count} samples, " +
                    $"Y=[{island.MinY:F1},{island.MaxY:F1}] (range {island.YRange:F1}m), " +
                    $"X=[{island.MinX:F1},{island.MaxX:F1}], " +
                    $"Z=[{island.MinZ:F1},{island.MaxZ:F1}], " +
                    $"center=({island.Center.x:F1},{island.Center.y:F1},{island.Center.z:F1})" +
                    playerMarker;

                MelonLoader.MelonLogger.Msg($"[ISLAND] {line}");
                sb.AppendLine(line);
            }

            sb.AppendLine();

            // Step 7: Find gaps between islands (closest edge pairs).
            var gaps = FindIslandGaps(allSamples, islandIds, sortedIslands);
            sb.AppendLine($"Gaps between islands ({gaps.Count} pairs):");

            foreach (var gap in gaps)
            {
                string line =
                    $"Gap: Island {gap.IslandA} <-> Island {gap.IslandB}: " +
                    $"dist={gap.Distance:F1}m, yDelta={gap.YDelta:F1}m, " +
                    $"A=({gap.PointA.x:F1},{gap.PointA.y:F1},{gap.PointA.z:F1}), " +
                    $"B=({gap.PointB.x:F1},{gap.PointB.y:F1},{gap.PointB.z:F1})";

                MelonLoader.MelonLogger.Msg($"[ISLAND] {line}");
                sb.AppendLine(line);
            }

            // Step 8: Check FieldMapjumpCollision triggers on this map.
            sb.AppendLine();
            LogMapjumpTriggers(sb);

            // Step 9: Save results.
            sw.Stop();
            sb.AppendLine();
            sb.AppendLine($"Scan completed in {sw.ElapsedMilliseconds}ms");

            string filename = $"island_diag_{mapId}.txt";
            string filepath = Path.Combine(OutputDir, filename);
            try
            {
                if (!Directory.Exists(OutputDir))
                    Directory.CreateDirectory(OutputDir);
                File.WriteAllText(filepath, sb.ToString());
                MelonLoader.MelonLogger.Msg($"[ISLAND] Results saved to {filepath}");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[ISLAND] Failed to save results: {ex.Message}");
            }

            // Summary announcement.
            int bigIslands = 0;
            foreach (var island in sortedIslands)
                if (island.Samples.Count >= 5) bigIslands++;

            string summary =
                $"Found {sortedIslands.Count} NavMesh islands " +
                $"({bigIslands} significant). " +
                $"{gaps.Count} gaps detected. " +
                $"Took {sw.ElapsedMilliseconds / 1000f:F1} seconds. " +
                "Check log for details.";

            MelonLoader.MelonLogger.Msg($"[ISLAND] {summary}");
            ScreenReader.Say(summary);

            MelonLoader.MelonLogger.Msg(
                $"=== END NAVMESH ISLAND DIAGNOSTICS ===");
        }

        /// <summary>
        /// Finds the closest pair of edge samples between each pair of islands.
        /// Only considers islands with at least 3 samples (filters noise).
        /// </summary>
        private static List<IslandGap> FindIslandGaps(
            List<NavSample> allSamples, int[] islandIds,
            List<IslandInfo> islands)
        {
            var gaps = new List<IslandGap>();

            // Filter to significant islands (3+ samples).
            var significant = new List<IslandInfo>();
            foreach (var island in islands)
                if (island.Samples.Count >= 3)
                    significant.Add(island);

            // For each pair of significant islands, find closest samples.
            for (int i = 0; i < significant.Count; i++)
            {
                for (int j = i + 1; j < significant.Count; j++)
                {
                    var a = significant[i];
                    var b = significant[j];

                    // Quick bounding-box pre-check: skip if islands are very far.
                    float boxDist = BoundsDistance(a, b);
                    if (boxDist > 30f)
                        continue;

                    float bestDist = float.MaxValue;
                    Vector3 bestA = Vector3.zero, bestB = Vector3.zero;

                    // Use edge samples only (samples with at least one empty
                    // neighbor) to reduce comparisons.
                    foreach (var sa in a.Samples)
                    {
                        foreach (var sb2 in b.Samples)
                        {
                            float dist = Vector3.Distance(sa.Position, sb2.Position);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestA = sa.Position;
                                bestB = sb2.Position;
                            }
                        }
                    }

                    if (bestDist < 30f)
                    {
                        gaps.Add(new IslandGap
                        {
                            IslandA = a.Id,
                            IslandB = b.Id,
                            PointA = bestA,
                            PointB = bestB,
                            Distance = bestDist,
                            YDelta = Mathf.Abs(bestA.y - bestB.y)
                        });
                    }
                }
            }

            // Sort by distance (closest first).
            gaps.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return gaps;
        }

        /// <summary>
        /// Rough axis-aligned bounding box distance between two islands.
        /// Returns 0 if boxes overlap.
        /// </summary>
        private static float BoundsDistance(IslandInfo a, IslandInfo b)
        {
            float dx = Mathf.Max(0, Mathf.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dz = Mathf.Max(0, Mathf.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Logs all FieldMapjumpCollision triggers found on the current map.
        /// </summary>
        private static void LogMapjumpTriggers(StringBuilder sb)
        {
            try
            {
                var triggers = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();

                sb.AppendLine($"FieldMapjumpCollision triggers: {triggers.Count}");

                if (triggers.Count == 0)
                {
                    sb.AppendLine("  (none — map uses continuous geometry, " +
                        "no trigger-based transitions)");
                    return;
                }

                for (int i = 0; i < triggers.Count; i++)
                {
                    var t = triggers[i];
                    try
                    {
                        string dest = t.fieldmapID.ToString();
                        Vector3 pos = t.transform.position;
                        sb.AppendLine(
                            $"  Trigger {i}: dest={dest} " +
                            $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"  Trigger {i}: error reading — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"FieldMapjumpCollision scan error: {ex.Message}");
            }
        }
    }
}
