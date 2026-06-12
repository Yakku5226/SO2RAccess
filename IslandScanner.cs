using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    /// <summary>
    /// Scans the NavMesh on a field map to identify disconnected islands and
    /// gaps between them. Returns structured data for use by
    /// <see cref="IslandNavigator"/>. Adapted from NavMeshIslandDiagnostics.
    /// </summary>
    public static class IslandScanner
    {
        /// <summary>Grid cell spacing for NavMesh sampling (meters).</summary>
        private const float CellSize = 2.0f;

        /// <summary>Radius for NavMesh.SamplePosition per grid cell.</summary>
        private const float SampleRadius = 1.5f;

        /// <summary>Search extent from origin in each direction (meters).</summary>
        private const float SearchExtent = 350f;

        /// <summary>
        /// Y-difference threshold for treating two samples in the same cell
        /// as separate vertical layers.
        /// </summary>
        private const float VerticalLayerThreshold = 2.0f;

        /// <summary>
        /// Maximum Y-difference between adjacent samples to consider them
        /// connected (walkable slope/ramp).
        /// </summary>
        private const float MaxConnectedYDelta = 1.5f;

        /// <summary>Minimum sample count for an island to be significant.</summary>
        private const int MinSignificantSamples = 3;

        /// <summary>Maximum gap distance to consider as a potential connection.</summary>
        private const float MaxGapDistance = 15f;

        /// <summary>Maximum Y delta for a gap to be a plausible connection.</summary>
        private const float MaxGapYDelta = 12f;

        /// <summary>Y probe heights for detecting stacked floors.</summary>
        private static readonly float[] ProbeYs =
            { 50f, 30f, 15f, 5f, 0f, -5f, -15f };

        /// <summary>BFS neighbor offsets (4-directional).</summary>
        private static readonly int[] Dx = { -1, 1, 0, 0 };
        private static readonly int[] Dz = { 0, 0, -1, 1 };

        /// <summary>Internal sample during scanning.</summary>
        private struct Sample
        {
            public int Gx, Gz;
            public Vector3 Position;
        }

        /// <summary>
        /// Scans the NavMesh and returns the island graph for the current map.
        /// Uses a fixed grid origin at (0,0) for deterministic island IDs.
        /// </summary>
        /// <param name="mapId">Current field map ID (for logging).</param>
        /// <returns>
        /// A <see cref="MapIslandGraph"/> with islands and filtered gaps,
        /// or null if no NavMesh was found.
        /// </returns>
        public static MapIslandGraph Scan(string mapId)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Fixed grid origin at (0,0) for deterministic results.
            float minX = -SearchExtent;
            float minZ = -SearchExtent;
            int gridW = (int)(SearchExtent * 2 / CellSize) + 1;
            int gridH = gridW;

            MelonLoader.MelonLogger.Msg(
                $"[SO2RAccess] ISLAND: scanning {mapId}, grid {gridW}x{gridH}, " +
                $"cell={CellSize}m, extent={SearchExtent}m");

            // Step 1: Sample NavMesh at each grid cell.
            var cellToIndices = new Dictionary<(int, int), List<int>>();
            var samples = new List<Sample>();

            for (int gx = 0; gx < gridW; gx++)
            {
                float worldX = minX + gx * CellSize;
                for (int gz = 0; gz < gridH; gz++)
                {
                    float worldZ = minZ + gz * CellSize;
                    SampleCell(worldX, worldZ, gx, gz, samples, cellToIndices);
                }
            }

            if (samples.Count == 0)
            {
                DebugLogger.LogState("ISLAND: no NavMesh found, aborting scan.");
                return null;
            }

            DebugLogger.LogState(
                $"ISLAND: {samples.Count} samples ({sw.ElapsedMilliseconds}ms)");

            // Step 2: BFS flood-fill to group connected samples into islands.
            int[] islandIds = FloodFill(samples, cellToIndices);

            // Step 3: Build island data.
            var islandMap = new Dictionary<int, IslandBuildData>();
            for (int i = 0; i < samples.Count; i++)
            {
                int id = islandIds[i];
                if (!islandMap.ContainsKey(id))
                    islandMap[id] = new IslandBuildData(id);
                islandMap[id].Add(samples[i].Position);
            }

            // Assign stable IDs: sort by (roundedMinY, roundedMinX, roundedMinZ)
            // then re-number sequentially. This is deterministic for the same
            // NavMesh geometry regardless of iteration order.
            var buildList = new List<IslandBuildData>(islandMap.Values);
            buildList.Sort((a, b) =>
            {
                int cy = Round1(a.MinY).CompareTo(Round1(b.MinY));
                if (cy != 0) return cy;
                int cx = Round1(a.MinX).CompareTo(Round1(b.MinX));
                if (cx != 0) return cx;
                return Round1(a.MinZ).CompareTo(Round1(b.MinZ));
            });

            // Build final island list and old-to-new ID mapping.
            var oldToNew = new Dictionary<int, int>();
            var islands = new List<IslandData>();
            for (int i = 0; i < buildList.Count; i++)
            {
                var b = buildList[i];
                oldToNew[b.OriginalId] = i;
                islands.Add(new IslandData
                {
                    Id = i,
                    CenterX = (b.MinX + b.MaxX) / 2f,
                    CenterY = (b.MinY + b.MaxY) / 2f,
                    CenterZ = (b.MinZ + b.MaxZ) / 2f,
                    MinX = b.MinX, MaxX = b.MaxX,
                    MinY = b.MinY, MaxY = b.MaxY,
                    MinZ = b.MinZ, MaxZ = b.MaxZ,
                    SampleCount = b.Count
                });
            }

            // Remap sample island IDs.
            for (int i = 0; i < islandIds.Length; i++)
                islandIds[i] = oldToNew[islandIds[i]];

            // Step 4: Find gaps between significant islands.
            var gaps = FindGaps(samples, islandIds, islands);

            // Step 5: Verify gaps by raycasting for ground along the connection.
            // Gaps with continuous ground are real ramps/stairs — promoted to
            // confirmed bridges. Gaps without ground are discarded entirely.
            int preVerifyCount = gaps.Count;
            var verifiedGaps = VerifyGapsWithGround(gaps);

            // Promote verified gaps to bridges — ground verification is as
            // reliable as walking them. This eliminates the "first visit"
            // problem: no manual exploration needed.
            var bridges = new List<BridgeData>();
            foreach (var g in verifiedGaps)
            {
                bridges.Add(new BridgeData
                {
                    IslandA = g.IslandA,
                    IslandB = g.IslandB,
                    CrossPointAX = g.PointAX,
                    CrossPointAY = g.PointAY,
                    CrossPointAZ = g.PointAZ,
                    CrossPointBX = g.PointBX,
                    CrossPointBY = g.PointBY,
                    CrossPointBZ = g.PointBZ,
                    Bidirectional = true
                });
            }

            sw.Stop();
            int sigCount = 0;
            foreach (var isl in islands)
                if (isl.SampleCount >= MinSignificantSamples) sigCount++;

            MelonLoader.MelonLogger.Msg(
                $"[SO2RAccess] ISLAND: scan complete. {islands.Count} islands " +
                $"({sigCount} significant), {verifiedGaps.Count} verified bridges " +
                $"(of {preVerifyCount} gap candidates), {sw.ElapsedMilliseconds}ms");

            return new MapIslandGraph
            {
                MapId = mapId,
                Version = 1,
                ScanTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Islands = islands,
                Bridges = bridges,
                Gaps = new List<GapData>() // All gaps either promoted or discarded
            };
        }

        /// <summary>
        /// Determines which island a world position belongs to, using
        /// NavMesh sampling and bounding-box containment.
        /// Returns the island ID, or -1 if no island found.
        /// </summary>
        public static int FindIsland(Vector3 pos, List<IslandData> islands)
        {
            if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                return -1;

            Vector3 snapped = hit.position;

            // Use NavMesh.CalculatePath to determine which island the point
            // is actually connected to. Test against each island's center —
            // a PathComplete result means same connected island.
            var testPath = new NavMeshPath();
            int bestId = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < islands.Count; i++)
            {
                var isl = islands[i];

                // Quick Y check — skip islands that are clearly on a
                // different vertical level (saves CalculatePath calls).
                float yDiff = Mathf.Abs(snapped.y - isl.CenterY);
                if (yDiff > isl.MaxY - isl.MinY + 3f)
                    continue;

                // Try CalculatePath to island center.
                Vector3 islCenter = new Vector3(isl.CenterX, isl.CenterY, isl.CenterZ);
                if (!NavMesh.SamplePosition(islCenter, out NavMeshHit islHit,
                    5f, NavMesh.AllAreas))
                    continue;

                NavMesh.CalculatePath(snapped, islHit.position,
                    NavMesh.AllAreas, testPath);

                if (testPath.status == NavMeshPathStatus.PathComplete)
                {
                    float dist = Vector3.Distance(snapped, islHit.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestId = isl.Id;
                    }
                }
            }

            // Fallback: if CalculatePath didn't match any island (edge case),
            // use bounding-box proximity with generous margin.
            if (bestId < 0)
            {
                bestDist = float.MaxValue;
                float margin = CellSize * 3f;
                for (int i = 0; i < islands.Count; i++)
                {
                    var isl = islands[i];
                    if (snapped.x < isl.MinX - margin || snapped.x > isl.MaxX + margin)
                        continue;
                    if (snapped.z < isl.MinZ - margin || snapped.z > isl.MaxZ + margin)
                        continue;
                    if (snapped.y < isl.MinY - margin || snapped.y > isl.MaxY + margin)
                        continue;

                    float dist = Vector3.Distance(snapped,
                        new Vector3(isl.CenterX, isl.CenterY, isl.CenterZ));
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestId = isl.Id;
                    }
                }
            }

            return bestId;
        }

        /// <summary>Samples NavMesh at one grid cell, probing multiple Y levels.</summary>
        private static void SampleCell(float worldX, float worldZ, int gx, int gz,
            List<Sample> samples, Dictionary<(int, int), List<int>> cellToIndices)
        {
            var key = (gx, gz);
            List<float> foundYs = null;

            foreach (float probeY in ProbeYs)
            {
                var probePos = new Vector3(worldX, probeY, worldZ);
                if (!NavMesh.SamplePosition(probePos, out NavMeshHit hit,
                    SampleRadius, NavMesh.AllAreas))
                    continue;

                // Check for duplicate Y at this cell.
                bool dup = false;
                if (foundYs != null)
                {
                    for (int i = 0; i < foundYs.Count; i++)
                    {
                        if (Mathf.Abs(foundYs[i] - hit.position.y) <
                            VerticalLayerThreshold)
                        {
                            dup = true;
                            break;
                        }
                    }
                }

                if (dup) continue;

                if (foundYs == null) foundYs = new List<float>(2);
                foundYs.Add(hit.position.y);

                int idx = samples.Count;
                samples.Add(new Sample { Gx = gx, Gz = gz, Position = hit.position });

                if (!cellToIndices.ContainsKey(key))
                    cellToIndices[key] = new List<int>(2);
                cellToIndices[key].Add(idx);
            }
        }

        /// <summary>BFS flood-fill to assign island IDs to all samples.</summary>
        private static int[] FloodFill(List<Sample> samples,
            Dictionary<(int, int), List<int>> cellToIndices)
        {
            int[] ids = new int[samples.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = -1;

            int nextId = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < samples.Count; start++)
            {
                if (ids[start] != -1) continue;

                int id = nextId++;
                ids[start] = id;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    var s = samples[idx];

                    for (int d = 0; d < 4; d++)
                    {
                        var nkey = (s.Gx + Dx[d], s.Gz + Dz[d]);
                        if (!cellToIndices.TryGetValue(nkey, out var neighbors))
                            continue;

                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            int ni = neighbors[n];
                            if (ids[ni] != -1) continue;

                            float yDelta = Mathf.Abs(
                                samples[ni].Position.y - s.Position.y);
                            if (yDelta <= MaxConnectedYDelta)
                            {
                                ids[ni] = id;
                                queue.Enqueue(ni);
                            }
                        }
                    }
                }
            }

            return ids;
        }

        /// <summary>
        /// Finds gaps between significant islands, applying filtering
        /// heuristics to remove likely false connections.
        /// </summary>
        private static List<GapData> FindGaps(List<Sample> samples,
            int[] islandIds, List<IslandData> islands)
        {
            // Build per-island sample position lists for significant islands.
            var islandSamples = new Dictionary<int, List<Vector3>>();
            for (int i = 0; i < samples.Count; i++)
            {
                int id = islandIds[i];
                if (islands[id].SampleCount < MinSignificantSamples)
                    continue;
                if (!islandSamples.ContainsKey(id))
                    islandSamples[id] = new List<Vector3>();
                islandSamples[id].Add(samples[i].Position);
            }

            var sigIds = new List<int>(islandSamples.Keys);
            var gaps = new List<GapData>();

            for (int i = 0; i < sigIds.Count; i++)
            {
                var islandA = islands[sigIds[i]];
                var samplesA = islandSamples[sigIds[i]];

                for (int j = i + 1; j < sigIds.Count; j++)
                {
                    var islandB = islands[sigIds[j]];

                    // Bounding-box pre-check.
                    if (BoundsDistance(islandA, islandB) > MaxGapDistance)
                        continue;

                    var samplesB = islandSamples[sigIds[j]];

                    // Find closest sample pair.
                    float bestDist = float.MaxValue;
                    Vector3 bestA = Vector3.zero, bestB = Vector3.zero;

                    for (int a = 0; a < samplesA.Count; a++)
                    {
                        for (int b = 0; b < samplesB.Count; b++)
                        {
                            float dist = Vector3.Distance(
                                samplesA[a], samplesB[b]);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestA = samplesA[a];
                                bestB = samplesB[b];
                            }
                        }
                    }

                    if (bestDist >= MaxGapDistance)
                        continue;

                    float yDelta = Mathf.Abs(bestA.y - bestB.y);

                    // Filter: Y delta too large for a plausible connection.
                    if (yDelta > MaxGapYDelta)
                        continue;

                    gaps.Add(new GapData
                    {
                        IslandA = islandA.Id,
                        IslandB = islandB.Id,
                        PointAX = bestA.x, PointAY = bestA.y, PointAZ = bestA.z,
                        PointBX = bestB.x, PointBY = bestB.y, PointBZ = bestB.z,
                        Distance = bestDist,
                        YDelta = yDelta,
                        Blocked = false
                    });
                }
            }

            gaps.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return gaps;
        }

        /// <summary>Axis-aligned bounding box distance (0 if overlapping).</summary>
        /// <summary>Height offset above the gap's max Y to start raycasts.</summary>
        private const float RayHeightOffset = 5f;

        /// <summary>Maximum downward raycast distance.</summary>
        private const float RayMaxDist = 30f;

        /// <summary>Number of sample points along the gap line for verification.</summary>
        private const int GapVerifySamples = 5;

        /// <summary>
        /// Maximum allowed Y jump between adjacent ground samples along the
        /// gap line. If ground height jumps more than this between two adjacent
        /// samples, it's a cliff/wall, not a ramp. Real ramps can change 3m+
        /// gradually between samples, so this is deliberately generous.
        /// </summary>
        private const float MaxGroundStepY = 5f;

        /// <summary>
        /// Verifies each gap by raycasting for ground along the line between
        /// the two gap edge points. Uses Physics.Raycast (hits all colliders)
        /// from just above the expected height to avoid hitting upper floors.
        /// Returns only gaps where continuous ground exists.
        /// </summary>
        private static List<GapData> VerifyGapsWithGround(List<GapData> candidates)
        {
            var verified = new List<GapData>();

            foreach (var gap in candidates)
            {
                Vector3 ptA = new Vector3(gap.PointAX, gap.PointAY, gap.PointAZ);
                Vector3 ptB = new Vector3(gap.PointBX, gap.PointBY, gap.PointBZ);

                bool hasGround = true;
                float prevY = float.NaN;
                var sampleYs = new List<float>();

                for (int s = 0; s <= GapVerifySamples; s++)
                {
                    float t = (float)s / GapVerifySamples;
                    Vector3 samplePos = Vector3.Lerp(ptA, ptB, t);

                    // Expected ground height at this point along the gap line:
                    // linear interpolation between the two NavMesh edge points.
                    // A real ramp/stair tracks roughly this line. Multi-level
                    // dungeons overhang ramp areas, so raycasting from high above
                    // and picking the HIGHEST hit grabs the upper floor instead
                    // of the ramp. Instead, start just above the expected height
                    // and pick the hit CLOSEST to the expected height.
                    float expectedY = Mathf.Lerp(ptA.y, ptB.y, t);
                    float rayStartY = expectedY + RayHeightOffset;

                    // Raycast straight down from just above the expected ground.
                    Vector3 rayOrigin = new Vector3(
                        samplePos.x, rayStartY, samplePos.z);

                    // Raycast downward hitting all colliders. Filter by
                    // surface normal: ground faces up (normal.y > 0.4),
                    // walls face sideways. This catches ramp/stair geometry
                    // on any layer while ignoring vertical surfaces.
                    bool hit = false;
                    RaycastHit hitInfo = default;

                    // Cast through multiple hits to skip non-ground surfaces
                    // (e.g. trigger volumes, thin colliders above the floor).
                    var hits = UnityEngine.Physics.RaycastAll(
                        rayOrigin, Vector3.down, RayMaxDist);

                    if (hits != null)
                    {
                        float bestDelta = float.MaxValue;
                        for (int h = 0; h < hits.Length; h++)
                        {
                            var rh = hits[h];
                            // Skip triggers (event zones, etc.)
                            if (rh.collider != null && rh.collider.isTrigger)
                                continue;
                            // Must be an upward-facing surface (ground/ramp).
                            if (rh.normal.y < 0.4f)
                                continue;
                            // Pick the valid ground hit closest to the expected
                            // height — never the highest. This rejects an upper
                            // floor overhanging the ramp.
                            float delta = Mathf.Abs(rh.point.y - expectedY);
                            if (delta < bestDelta)
                            {
                                bestDelta = delta;
                                hitInfo = rh;
                                hit = true;
                            }
                        }
                    }

                    if (!hit)
                    {
                        hasGround = false;
                        MelonLoader.MelonLogger.Msg(
                            $"[SO2RAccess] GAP VERIFY: {gap.IslandA}<->{gap.IslandB} " +
                            $"NO GROUND at t={t:F2} " +
                            $"({samplePos.x:F1},{samplePos.z:F1}) " +
                            $"rayFrom=Y{rayStartY:F1} expectedY={expectedY:F1}");
                        break;
                    }

                    float groundY = hitInfo.point.y;
                    sampleYs.Add(groundY);

                    // Check for cliff/wall: if ground Y jumps too much.
                    if (!float.IsNaN(prevY))
                    {
                        float step = Mathf.Abs(groundY - prevY);
                        if (step > MaxGroundStepY)
                        {
                            hasGround = false;
                            MelonLoader.MelonLogger.Msg(
                                $"[SO2RAccess] GAP VERIFY: {gap.IslandA}<->{gap.IslandB} " +
                                $"CLIFF at t={t:F2} (stepY={step:F1}, " +
                                $"prevY={prevY:F1}, groundY={groundY:F1})");
                            break;
                        }
                    }

                    prevY = groundY;
                }

                string yStr = string.Join(",",
                    sampleYs.ConvertAll(y => y.ToString("F1")));

                if (hasGround)
                {
                    verified.Add(gap);
                    MelonLoader.MelonLogger.Msg(
                        $"[SO2RAccess] GAP VERIFY: {gap.IslandA}<->{gap.IslandB} " +
                        $"VERIFIED dist={gap.Distance:F1} yDelta={gap.YDelta:F1} " +
                        $"groundYs=[{yStr}]");
                }
                else
                {
                    MelonLoader.MelonLogger.Msg(
                        $"[SO2RAccess] GAP VERIFY: {gap.IslandA}<->{gap.IslandB} " +
                        $"REJECTED dist={gap.Distance:F1} yDelta={gap.YDelta:F1} " +
                        $"groundYs=[{yStr}]");
                }
            }

            return verified;
        }

        private static float BoundsDistance(IslandData a, IslandData b)
        {
            float dx = Mathf.Max(0, Mathf.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dz = Mathf.Max(0, Mathf.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float Round1(float v) => Mathf.Round(v * 10f) / 10f;

        /// <summary>Temporary data for building island bounds during scan.</summary>
        private class IslandBuildData
        {
            public int OriginalId;
            public int Count;
            public float MinX = float.MaxValue, MaxX = float.MinValue;
            public float MinY = float.MaxValue, MaxY = float.MinValue;
            public float MinZ = float.MaxValue, MaxZ = float.MinValue;

            public IslandBuildData(int id) { OriginalId = id; }

            public void Add(Vector3 pos)
            {
                Count++;
                if (pos.x < MinX) MinX = pos.x;
                if (pos.x > MaxX) MaxX = pos.x;
                if (pos.y < MinY) MinY = pos.y;
                if (pos.y > MaxY) MaxY = pos.y;
                if (pos.z < MinZ) MinZ = pos.z;
                if (pos.z > MaxZ) MaxZ = pos.z;
            }
        }
    }
}
