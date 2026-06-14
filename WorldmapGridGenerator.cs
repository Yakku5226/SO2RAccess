using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Generates and saves a terrain height + obstacle grid for a world map
    /// at 0.5m resolution. Uses CalcHeight for terrain detection and
    /// OverlapSphere for solid obstacle detection. Key insight: Col_Obstacle
    /// objects with isTrigger=true (trees, bushes) are passthrough — only
    /// solid colliders (isTrigger=false) block the player. These are cliff
    /// edge barriers and terrain boundaries. Rock faces are detected via
    /// slope checking between adjacent cell heights.
    /// For cells near CharaWalls, stores the sub-cell position with maximum
    /// clearance from all walls so the pathfinder guides the player through
    /// the exact center of narrow gaps.
    /// </summary>
    public static class WorldmapGridGenerator
    {
        /// <summary>File format magic identifier (v10 = WMGG + entrance trigger passability).</summary>
        private const string Magic = "WMGH";

        /// <summary>Grid cell spacing in world units (meters).</summary>
        public const float CellSize = 0.5f;

        /// <summary>
        /// Height for CalcHeight raycast origin. Must be above the highest
        /// terrain point. Map heights range -33m to 98m.
        /// </summary>
        private const float RaycastStartY = 150f;

        /// <summary>Max downward distance for CalcHeight raycast.</summary>
        private const float RaycastMaxDist = 300f;

        /// <summary>
        /// Layer mask for terrain obstacles (layer 22 = Wall).
        /// Checked at player radius (0.50m).
        /// </summary>
        private static readonly int TerrainObstacleMask = 1 << 22;

        /// <summary>
        /// Layer mask for region boundary walls (layer 23 = CharacterWall).
        /// Checked at a reduced radius (0.25m) so that the designed road
        /// gaps between regions (1.8m-5.1m wide) remain walkable in the
        /// grid while solid wall sections are still blocked.
        /// </summary>
        private static readonly int CharaWallMask = 1 << 23;

        /// <summary>
        /// Hard minimum clearance for a cell to be passable. Set to the
        /// player capsule radius (0.50m) so any theoretically passable
        /// gap stays in the grid. The continuous clearance penalty in
        /// the A* pathfinder (up to 20x cost) steers away from tight
        /// cells — the hard threshold just prevents truly impassable ones.
        /// </summary>
        private const float MinPassableClearance = 0.50f;

        /// <summary>
        /// Sub-cell resolution for CharaWall gap detection. Each 0.5m
        /// cell near a CharaWall is checked at 25 sub-positions (5x5
        /// at 0.125m spacing). A cell is blocked only if NONE of the
        /// sub-positions have >= MinPassableClearance from all walls.
        /// The best position is stored as a clearance offset so the
        /// pathfinder guides the player through the widest part of gaps.
        /// </summary>
        private const int SubCellSteps = 2; // -2..+2 = 5 points per axis

        /// <summary>
        /// Search radius for OverlapSphere when finding solid obstacles.
        /// Must cover player collision radius (0.5m) plus margin.
        /// </summary>
        private const float ObstacleSearchRadius = 1.0f;

        /// <summary>
        /// Generates the height grid for the current world map and saves
        /// it to a binary file. Call from the world map with F9 in debug mode.
        /// </summary>
        public static void GenerateAndSave()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say("Grid generation only works on the world map.");
                    return;
                }

                if (!fm.IsExistWorldGridData())
                {
                    ScreenReader.Say("World grid data not available.");
                    return;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say("No player found.");
                    return;
                }

                WorldmapID wmID = fm.WorldmapID;
                string mapName = wmID == WorldmapID.EXPEL ? "expel" : "nede";
                ScreenReader.Say(
                    $"Generating {mapName} world map grid at 0.5 meter " +
                    "resolution. This may take about a minute. Please wait.");
                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Starting 0.5m height grid for {mapName}...");

                // --- Step 1: Fixed world bounds ---
                // Use hardcoded bounds so the grid is identical regardless
                // of where the player is when generating. This ensures
                // consistent cell alignment — critical for CharaWall gap
                // detection. The grid file ships with the mod.
                // Bounds determined from multiple scans across the map.
                float worldMinX, worldMinZ, worldMaxX;
                float worldMaxZ;

                // Same generous bounds for both world maps: Expel covers all
                // terrain with 10m padding; Nede bounds will be refined when tested.
                worldMinX = -1920.0f;
                worldMinZ = -1600.0f;
                worldMaxX = 1870.0f;
                worldMaxZ = 870.0f;

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Fixed bounds for {mapName}. " +
                    $"X=[{worldMinX},{worldMaxX}] Z=[{worldMinZ},{worldMaxZ}]");

                int gridW = (int)((worldMaxX - worldMinX) / CellSize) + 1;
                int gridH = (int)((worldMaxZ - worldMinZ) / CellSize) + 1;
                long totalCells = (long)gridW * gridH;

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] World bounds: X=[{worldMinX:F1},{worldMaxX:F1}]" +
                    $" Z=[{worldMinZ:F1},{worldMaxZ:F1}] " +
                    $"size={gridW}x{gridH} ({totalCells} cells at {CellSize}m)");

                // --- Step 2: Compute ground height + obstacle status ---
                // ushort encoding per cell:
                //   0 = ocean (no terrain from CalcHeight)
                //   1 = solid obstacle (terrain exists but blocked by a
                //       non-trigger Col_Obstacle collider within 0.5m)
                //   2+ = height in offset centimeters: (groundY + 100) * 100
                ushort[,] height = new ushort[gridW, gridH];
                // Sparse table of clearance offsets for cells near CharaWalls.
                // Key = (ax, az), Value = (offsetX, offsetZ) in meters from
                // cell center to the sub-cell position with maximum clearance.
                var clearanceOffsets = new Dictionary<long, (float, float)>();
                // Sparse table of actual clearance values (meters) for cells
                // near walls. The pathfinder uses these for continuous penalty.
                var clearanceValues = new Dictionary<long, float>();
                int terrainCount = 0, oceanCount = 0, solidObstCount = 0;
                float minY = float.MaxValue, maxY = float.MinValue;

                for (int ax = 0; ax < gridW; ax++)
                {
                    if (ax % 200 == 0)
                    {
                        MelonLoader.MelonLogger.Msg(
                            $"[GridGen] Progress: column {ax}/{gridW} " +
                            $"({ax * 100 / gridW}%)");
                    }

                    for (int az = 0; az < gridH; az++)
                    {
                        float worldX = worldMinX + ax * CellSize;
                        float worldZ = worldMinZ + az * CellSize;
                        Vector3 cellWorld = new Vector3(
                            worldX, RaycastStartY, worldZ);

                        float groundY = GameUtility.CalcHeight(
                            cellWorld, out bool hasGround, RaycastMaxDist);

                        if (!hasGround)
                        {
                            height[ax, az] = 0; // Ocean
                            oceanCount++;
                            continue;
                        }

                        terrainCount++;
                        if (groundY < minY) minY = groundY;
                        if (groundY > maxY) maxY = groundY;

                        // Check for solid obstacles on both layers with
                        // different thresholds. Layer 22 (terrain) uses
                        // full player radius. Layer 23 (CharaWall) uses
                        // a smaller radius to preserve road gaps.
                        Vector3 checkPos = new Vector3(
                            worldX, groundY + 0.5f, worldZ);

                        bool hasSolidObstacle = false;

                        // Layer 22: terrain obstacles at player radius.
                        // L22 uses 0.50m threshold (not MinPassableClearance)
                        // because the stuck problem is specifically L23
                        // CharaWall gaps, not terrain obstacles.
                        // Track nearest L22 distance for clearance value.
                        float nearestL22Dist = float.MaxValue;
                        var cols22 = UnityEngine.Physics.OverlapSphere(
                            checkPos, ObstacleSearchRadius, TerrainObstacleMask);
                        if (cols22 != null)
                        {
                            for (int c = 0; c < cols22.Length; c++)
                            {
                                if (cols22[c] == null || cols22[c].isTrigger)
                                    continue;
                                float dist = Vector3.Distance(checkPos,
                                    cols22[c].ClosestPoint(checkPos));
                                if (dist < nearestL22Dist)
                                    nearestL22Dist = dist;
                                if (dist < 0.50f)
                                {
                                    hasSolidObstacle = true;
                                    break;
                                }
                            }
                        }

                        // Layer 23: CharaWall with sub-cell precision.
                        // Check if ANY CharaWall is near this cell first.
                        // If so, scan a 5x5 sub-grid (0.1m spacing) within
                        // the cell. The cell is blocked ONLY if NONE of the
                        // 25 sub-positions have >= 0.50m clearance from walls from all
                        // solid CharaWalls. This gives 0.1m accuracy for
                        // gap detection while keeping the 0.5m grid format.
                        if (!hasSolidObstacle)
                        {
                            var cols23 = UnityEngine.Physics.OverlapSphere(
                                checkPos, ObstacleSearchRadius, CharaWallMask);

                            // Is there any solid CharaWall collider near this cell?
                            bool hasSolidWall = false;
                            if (cols23 != null)
                            {
                                for (int c = 0; c < cols23.Length; c++)
                                {
                                    if (cols23[c] != null && !cols23[c].isTrigger)
                                    {
                                        hasSolidWall = true;
                                        break;
                                    }
                                }
                            }

                            if (hasSolidWall)
                            {
                                // CharaWall nearby — do fine sub-cell check.
                                // Check 9x9 points (0.125m spacing) spanning
                                // the full cell and into neighbors (-0.5m to
                                // +0.5m from center). Track the sub-position
                                // with maximum minimum clearance from all
                                // walls — this becomes the optimal walk-through
                                // point for narrow gaps.
                                float subStep = CellSize / 4f; // 0.125m
                                float bestClearance = -1f;
                                float bestOffX = 0f, bestOffZ = 0f;

                                for (int sx = -SubCellSteps; sx <= SubCellSteps; sx++)
                                {
                                    for (int sz = -SubCellSteps; sz <= SubCellSteps; sz++)
                                    {
                                        float subX = worldX + sx * subStep;
                                        float subZ = worldZ + sz * subStep;
                                        Vector3 subPos = new Vector3(
                                            subX, groundY + 0.5f, subZ);

                                        // Find minimum distance to any solid
                                        // obstacle from this sub-position.
                                        // Check BOTH L23 CharaWalls and L22
                                        // terrain obstacles so the offset
                                        // doesn't push toward rocks.
                                        float minDist = float.MaxValue;
                                        for (int c = 0; c < cols23.Length; c++)
                                        {
                                            if (cols23[c] == null ||
                                                cols23[c].isTrigger)
                                                continue;
                                            float d = Vector3.Distance(subPos,
                                                cols23[c].ClosestPoint(subPos));
                                            if (d < minDist) minDist = d;
                                        }
                                        if (cols22 != null)
                                        {
                                            for (int c = 0; c < cols22.Length; c++)
                                            {
                                                if (cols22[c] == null ||
                                                    cols22[c].isTrigger)
                                                    continue;
                                                float d = Vector3.Distance(
                                                    subPos,
                                                    cols22[c].ClosestPoint(
                                                        subPos));
                                                if (d < minDist) minDist = d;
                                            }
                                        }

                                        // Track best (maximum clearance) point.
                                        if (minDist > bestClearance)
                                        {
                                            bestClearance = minDist;
                                            bestOffX = sx * subStep;
                                            bestOffZ = sz * subStep;
                                        }
                                    }
                                }

                                if (bestClearance < MinPassableClearance)
                                {
                                    // No sub-position has enough clearance.
                                    hasSolidObstacle = true;
                                }
                                else
                                {
                                    long key = (long)ax * gridH + az;
                                    // Store clearance offset if the best
                                    // point differs from cell center.
                                    if (Math.Abs(bestOffX) > 0.01f ||
                                        Math.Abs(bestOffZ) > 0.01f)
                                    {
                                        clearanceOffsets[key] =
                                            (bestOffX, bestOffZ);
                                    }
                                    // Store actual clearance value for the
                                    // pathfinder's continuous penalty.
                                    clearanceValues[key] = bestClearance;
                                }
                            }
                        }

                        if (hasSolidObstacle)
                        {
                            height[ax, az] = 1; // Solid obstacle
                            solidObstCount++;
                            continue;
                        }

                        // For cells near L22 terrain obstacles but still
                        // passable, record the clearance value if it's the
                        // tightest constraint (CharaWall clearance may
                        // already be stored and be tighter).
                        if (nearestL22Dist < 2.0f)
                        {
                            long l22Key = (long)ax * gridH + az;
                            if (!clearanceValues.ContainsKey(l22Key) ||
                                nearestL22Dist < clearanceValues[l22Key])
                            {
                                clearanceValues[l22Key] = nearestL22Dist;
                            }
                        }

                        // Store height with +100m offset in centimeters.
                        int stored = (int)((groundY + 100f) * 100f);
                        if (stored < 2) stored = 2; // Reserve 0=ocean, 1=obstacle
                        if (stored > 65535) stored = 65535;
                        height[ax, az] = (ushort)stored;
                    }
                }

                // --- Step 2b: Flood-fill to seal town model interiors ---
                // Must run BEFORE entrance trigger clearing so the flood fill
                // doesn't leak through the large trigger areas into town interiors.
                int interiorCellsSealed = 0;
                try
                {
                    bool[,] reachable = new bool[gridW, gridH];
                    var floodQueue = new Queue<(int x, int z)>();

                    // 8-directional: cardinal + diagonal. Must include
                    // diagonals so the fill can pass through narrow CharaWall
                    // gaps that are only passable diagonally (1-2 cells wide).
                    int[] fdx = { 0, 1, 0, -1, 1, 1, -1, -1 };
                    int[] fdz = { 1, 0, -1, 0, 1, -1, -1, 1 };

                    // Seed from all edge cells that are terrain.
                    for (int ax = 0; ax < gridW; ax++)
                    {
                        if (height[ax, 0] >= 2) { floodQueue.Enqueue((ax, 0)); reachable[ax, 0] = true; }
                        if (height[ax, gridH - 1] >= 2) { floodQueue.Enqueue((ax, gridH - 1)); reachable[ax, gridH - 1] = true; }
                    }
                    for (int az = 0; az < gridH; az++)
                    {
                        if (height[0, az] >= 2) { floodQueue.Enqueue((0, az)); reachable[0, az] = true; }
                        if (height[gridW - 1, az] >= 2) { floodQueue.Enqueue((gridW - 1, az)); reachable[gridW - 1, az] = true; }
                    }

                    // Also seed from all ocean-adjacent terrain cells.
                    for (int ax = 1; ax < gridW - 1; ax++)
                    {
                        for (int az = 1; az < gridH - 1; az++)
                        {
                            if (height[ax, az] >= 2 && !reachable[ax, az])
                            {
                                for (int d = 0; d < 8; d++)
                                {
                                    int nx = ax + fdx[d];
                                    int nz = az + fdz[d];
                                    if (height[nx, nz] == 0)
                                    {
                                        floodQueue.Enqueue((ax, az));
                                        reachable[ax, az] = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    MelonLoader.MelonLogger.Msg(
                        $"[GridGen] Flood fill: {floodQueue.Count} seed cells.");

                    while (floodQueue.Count > 0)
                    {
                        var (cx, cz) = floodQueue.Dequeue();
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = cx + fdx[d];
                            int nz = cz + fdz[d];
                            if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH)
                                continue;
                            if (reachable[nx, nz]) continue;
                            if (height[nx, nz] < 2) continue;
                            reachable[nx, nz] = true;
                            floodQueue.Enqueue((nx, nz));
                        }
                    }

                    for (int ax = 0; ax < gridW; ax++)
                    {
                        for (int az = 0; az < gridH; az++)
                        {
                            if (height[ax, az] >= 2 && !reachable[ax, az])
                            {
                                height[ax, az] = 1;
                                interiorCellsSealed++;
                            }
                        }
                    }

                    MelonLoader.MelonLogger.Msg(
                        $"[GridGen] Flood fill complete: {interiorCellsSealed} " +
                        "interior cells sealed as obstacles.");
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Warning(
                        $"[GridGen] Flood fill error: {ex.Message}");
                }

                // --- Step 2c: Mark town entrance triggers as passable ---
                // Now that interiors are sealed, punch entrance holes so the
                // A* can route TO town entrances (for mapjump transitions)
                // but never THROUGH the town model.
                int entranceCellsCleared = 0;
                try
                {
                    var mapjumps = UnityEngine.Object
                        .FindObjectsOfType<FieldMapjumpCollision>();
                    if (mapjumps != null)
                    {
                        for (int m = 0; m < mapjumps.Length; m++)
                        {
                            var mj = mapjumps[m];
                            if (mj == null) continue;

                            // Get the trigger's world-space bounds.
                            var colliders = mj.GetComponents<UnityEngine.Collider>();
                            if (colliders == null) continue;

                            for (int ci = 0; ci < colliders.Length; ci++)
                            {
                                var col = colliders[ci];
                                if (col == null || !col.isTrigger) continue;

                                var b = col.bounds;

                                // Only clear SMALL ground-level entrance triggers.
                                // Large triggers (Y extent > 20m) are town-wide
                                // detection zones, not road entrances. Clearing
                                // them would punch huge holes in the sealed town
                                // interior, allowing the A* to route through.
                                if (b.size.y > 20f) continue;
                                // Convert bounds to grid cell range.
                                int minAx = (int)((b.min.x - worldMinX) / CellSize);
                                int maxAx = (int)((b.max.x - worldMinX) / CellSize);
                                int minAz = (int)((b.min.z - worldMinZ) / CellSize);
                                int maxAz = (int)((b.max.z - worldMinZ) / CellSize);

                                // Clamp to grid bounds.
                                minAx = Math.Max(0, minAx);
                                maxAx = Math.Min(gridW - 1, maxAx);
                                minAz = Math.Max(0, minAz);
                                maxAz = Math.Min(gridH - 1, maxAz);

                                // Find the terrain height to use for cleared cells.
                                // Use CalcHeight at the trigger center.
                                float trigGroundY = GameUtility.CalcHeight(
                                    mj.transform.position, out bool trigOk, 50f);
                                ushort trigH = trigOk
                                    ? (ushort)((trigGroundY + 100f) * 100f)
                                    : (ushort)12080; // ~20.8m fallback

                                for (int ex = minAx; ex <= maxAx; ex++)
                                {
                                    for (int ez = minAz; ez <= maxAz; ez++)
                                    {
                                        if (height[ex, ez] == 1) // obstacle
                                        {
                                            height[ex, ez] = trigH;
                                            entranceCellsCleared++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Warning(
                        $"[GridGen] Entrance clearing error: {ex.Message}");
                }

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Complete: {gridW}x{gridH} grid. " +
                    $"terrain={terrainCount} ocean={oceanCount} " +
                    $"solidObstacles={solidObstCount} " +
                    $"entranceCellsCleared={entranceCellsCleared} " +
                    $"interiorSealed={interiorCellsSealed} " +
                    $"clearanceOffsets={clearanceOffsets.Count} " +
                    $"clearanceValues={clearanceValues.Count} " +
                    $"minClearance={MinPassableClearance:F2}m " +
                    $"height range={minY:F2}m to {maxY:F2}m");

                // --- Step 3: Save to binary file ---
                string dir = Path.Combine(
                    Directory.GetCurrentDirectory(), "UserData", "SO2RAccess");
                Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, $"worldmap_{mapName}.grid");

                SaveGrid(filePath, worldMinX, worldMinZ, gridW, gridH,
                    height, clearanceOffsets, clearanceValues);

                long fileSize = new FileInfo(filePath).Length;
                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Saved to: {filePath} ({fileSize} bytes)");
                ScreenReader.Say(
                    $"Grid saved. {gridW} by {gridH} cells at 0.5 meter " +
                    $"spacing. {terrainCount} terrain. {oceanCount} ocean. " +
                    $"{solidObstCount} solid obstacles. " +
                    $"{clearanceValues.Count} clearance values. " +
                    $"Minimum clearance {MinPassableClearance:F2} meters. " +
                    $"Height {minY:F1} to {maxY:F1} meters.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[GridGen] Error: {ex}");
                ScreenReader.Say("Grid generation failed. Check log.");
            }
        }

        /// <summary>Saves the height grid, clearance offsets, and clearance values.</summary>
        public static void SaveGrid(string path, float worldMinX,
            float worldMinZ, int gridW, int gridH, ushort[,] height,
            Dictionary<long, (float, float)> clearanceOffsets,
            Dictionary<long, float> clearanceValues)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            // Header.
            writer.Write(Magic.ToCharArray()); // 4 bytes "WMGG"
            writer.Write(worldMinX);           // float
            writer.Write(worldMinZ);           // float
            writer.Write(CellSize);            // float
            writer.Write(gridW);               // int32
            writer.Write(gridH);               // int32

            // Body: 2 bytes per cell (ushort height), row-major.
            for (int az = 0; az < gridH; az++)
            {
                for (int ax = 0; ax < gridW; ax++)
                {
                    writer.Write(height[ax, az]);
                }
            }

            // Clearance offset table (sparse).
            // Each entry: ushort gridX, ushort gridZ, byte offX, byte offZ.
            writer.Write(clearanceOffsets.Count);
            foreach (var kvp in clearanceOffsets)
            {
                int az2 = (int)(kvp.Key % gridH);
                int ax2 = (int)(kvp.Key / gridH);
                writer.Write((ushort)ax2);
                writer.Write((ushort)az2);
                writer.Write(EncodeClearanceOffset(kvp.Value.Item1));
                writer.Write(EncodeClearanceOffset(kvp.Value.Item2));
            }

            // Clearance value table (sparse).
            // Each entry: ushort gridX, ushort gridZ, ushort clearanceCm.
            // Stores actual clearance distance in centimeters (0-655m range).
            writer.Write(clearanceValues.Count);
            foreach (var kvp in clearanceValues)
            {
                int az2 = (int)(kvp.Key % gridH);
                int ax2 = (int)(kvp.Key / gridH);
                writer.Write((ushort)ax2);
                writer.Write((ushort)az2);
                int cm = (int)(kvp.Value * 100f);
                writer.Write((ushort)Math.Max(0, Math.Min(65535, cm)));
            }
        }

        /// <summary>Encodes a clearance offset (-0.25 to +0.25m) as a byte.</summary>
        private static byte EncodeClearanceOffset(float meters)
        {
            // Map -0.25..+0.25 to 0..250. Precision: ~0.002m per step.
            float normalized = (meters / 0.25f + 1.0f) * 125f;
            return (byte)Math.Max(0, Math.Min(250, (int)normalized));
        }

        /// <summary>Decodes a byte back to a clearance offset in meters.</summary>
        private static float DecodeClearanceOffset(byte encoded)
        {
            return (encoded / 125f - 1.0f) * 0.25f;
        }

        /// <summary>
        /// Loads a cached height grid from a binary file. Supports WMGD
        /// (legacy), WMGE/WMGF (clearance offsets), WMGG (clearance values).
        /// </summary>
        public static CachedGrid LoadGrid(WorldmapID wmID)
        {
            string mapName = wmID == WorldmapID.EXPEL ? "expel" : "nede";
            string dir = Path.Combine(
                Directory.GetCurrentDirectory(), "UserData", "SO2RAccess");
            string filePath = Path.Combine(dir, $"worldmap_{mapName}.grid");

            if (!File.Exists(filePath))
            {
                DebugLogger.LogState($"[GridGen] No cached grid at: {filePath}");
                return null;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open);
                using var reader = new BinaryReader(stream);

                char[] magic = reader.ReadChars(4);
                string magicStr = new string(magic);
                bool isValid = magicStr == Magic;
                if (!isValid)
                {
                    DebugLogger.LogState(
                        $"[GridGen] Invalid grid file (expected {Magic}, " +
                        $"got {magicStr}). Regenerate with F9.");
                    return null;
                }

                float wMinX = reader.ReadSingle();
                float wMinZ = reader.ReadSingle();
                float cSize = reader.ReadSingle();
                int gridW = reader.ReadInt32();
                int gridH = reader.ReadInt32();

                ushort[,] height = new ushort[gridW, gridH];

                for (int az = 0; az < gridH; az++)
                {
                    for (int ax = 0; ax < gridW; ax++)
                    {
                        height[ax, az] = reader.ReadUInt16();
                    }
                }

                // Load clearance offsets (WMGE, WMGF, WMGG formats).
                Dictionary<long, (float, float)> offsets = null;
                if (magicStr != "WMGD" && stream.Position < stream.Length)
                {
                    int count = reader.ReadInt32();
                    offsets = new Dictionary<long, (float, float)>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ushort oax = reader.ReadUInt16();
                        ushort oaz = reader.ReadUInt16();
                        float offX = DecodeClearanceOffset(reader.ReadByte());
                        float offZ = DecodeClearanceOffset(reader.ReadByte());
                        long key = (long)oax * gridH + oaz;
                        offsets[key] = (offX, offZ);
                    }
                    DebugLogger.LogState(
                        $"[GridGen] Loaded {count} clearance offsets.");
                }

                // Load clearance values (WMGG format only).
                Dictionary<long, float> clrValues = null;
                if (magicStr == Magic && stream.Position < stream.Length)
                {
                    int count = reader.ReadInt32();
                    clrValues = new Dictionary<long, float>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ushort oax = reader.ReadUInt16();
                        ushort oaz = reader.ReadUInt16();
                        float val = reader.ReadUInt16() / 100f;
                        long key = (long)oax * gridH + oaz;
                        clrValues[key] = val;
                    }
                    DebugLogger.LogState(
                        $"[GridGen] Loaded {count} clearance values.");
                }

                DebugLogger.LogState(
                    $"[GridGen] Loaded {mapName} grid: {gridW}x{gridH} " +
                    $"at {cSize}m from ({wMinX:F1},{wMinZ:F1})" +
                    (offsets != null ? $" with {offsets.Count} clearance offsets" : "") +
                    (clrValues != null ? $" with {clrValues.Count} clearance values" : ""));

                return new CachedGrid
                {
                    Height = height,
                    WorldMinX = wMinX,
                    WorldMinZ = wMinZ,
                    CellSize = cSize,
                    GridW = gridW,
                    GridH = gridH,
                    ClearanceOffsets = offsets,
                    ClearanceValues = clrValues
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[GridGen] Load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Logs collision diagnostics from the player character.
        /// </summary>
        public static void LogPlayerCollider()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say(
                        "Player collider info only available on world map.");
                    return;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say("No player found.");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== PLAYER COLLISION DIAGNOSTICS ===");
                sb.AppendLine($"Player: {player.name}");
                var pos = player.transform.position;
                sb.AppendLine($"Position: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})");

                // --- Game collision properties ---
                sb.AppendLine("\n--- Game Collision Properties ---");
                try
                {
                    float mcr = player.MoveCollisionRadius;
                    sb.AppendLine($"MoveCollisionRadius: {mcr:F4}m");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"MoveCollisionRadius: ERROR ({ex.Message})");
                }

                // --- CapsuleCollider from FieldObject ---
                sb.AppendLine("\n--- CapsuleCollider (FieldObject) ---");
                try
                {
                    var capsule = player.capsuleCollider;
                    if (capsule != null)
                    {
                        sb.AppendLine($"Radius: {capsule.radius:F4}m");
                        sb.AppendLine($"Height: {capsule.height:F4}m");
                        sb.AppendLine($"Center: ({capsule.center.x:F3}, {capsule.center.y:F3}, {capsule.center.z:F3})");
                        sb.AppendLine($"Direction: {capsule.direction} (0=X, 1=Y, 2=Z)");
                        sb.AppendLine($"IsTrigger: {capsule.isTrigger}");
                        sb.AppendLine($"Enabled: {capsule.enabled}");
                        sb.AppendLine($"ContactOffset: {capsule.contactOffset:F4}m");
                        // World-space effective radius (accounts for scale)
                        var scale = player.transform.lossyScale;
                        float effectiveRadius = capsule.radius *
                            Mathf.Max(scale.x, scale.z);
                        sb.AppendLine($"Transform scale: ({scale.x:F3}, {scale.y:F3}, {scale.z:F3})");
                        sb.AppendLine($"Effective world radius: {effectiveRadius:F4}m");
                    }
                    else
                    {
                        sb.AppendLine("capsuleCollider is NULL");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"CapsuleCollider: ERROR ({ex.Message})");
                }

                // --- All colliders on the player GameObject ---
                sb.AppendLine("\n--- All Colliders on Player ---");
                try
                {
                    var allColliders = player.GetComponentsInChildren<Collider>();
                    if (allColliders != null && allColliders.Length > 0)
                    {
                        for (int i = 0; i < allColliders.Length; i++)
                        {
                            var col = allColliders[i];
                            if (col == null) continue;
                            sb.AppendLine($"  [{i}] {col.GetType().Name} " +
                                $"name=\"{col.name}\" " +
                                $"trigger={col.isTrigger} " +
                                $"enabled={col.enabled} " +
                                $"layer={col.gameObject.layer}");

                            if (col is CapsuleCollider cc)
                            {
                                sb.AppendLine($"       radius={cc.radius:F4} " +
                                    $"height={cc.height:F4} " +
                                    $"center=({cc.center.x:F3},{cc.center.y:F3},{cc.center.z:F3}) " +
                                    $"dir={cc.direction}");
                            }
                            else if (col is SphereCollider sc)
                            {
                                sb.AppendLine($"       radius={sc.radius:F4} " +
                                    $"center=({sc.center.x:F3},{sc.center.y:F3},{sc.center.z:F3})");
                            }
                            else if (col is BoxCollider bc)
                            {
                                sb.AppendLine($"       size=({bc.size.x:F3},{bc.size.y:F3},{bc.size.z:F3}) " +
                                    $"center=({bc.center.x:F3},{bc.center.y:F3},{bc.center.z:F3})");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("  No colliders found.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Collider scan error: {ex.Message}");
                }

                // --- Measure actual gap widths at known corridors ---
                sb.AppendLine("\n--- Gap Width Measurements ---");
                sb.AppendLine("Casting rays from known corridor midpoints to find actual wall distances.");
                MeasureGapWidth(sb, "Salva-Arlia junction (narrow)",
                    new Vector3(-174.7f, 22.9f, -305.4f));
                MeasureGapWidth(sb, "Salva-Arlia junction (east corridor)",
                    new Vector3(-158.0f, 23.0f, -310.0f));
                MeasureGapWidth(sb, "Krosse-Salva corridor",
                    new Vector3(-140.0f, 29.0f, -175.0f));
                MeasureGapWidth(sb, "Player current position", pos);

                sb.AppendLine("\n=== END DIAGNOSTICS ===");

                MelonLoader.MelonLogger.Msg(sb.ToString());
                ScreenReader.Say("Collision diagnostics logged. Check log.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"[GridGen] Diagnostics error: {ex}");
                ScreenReader.Say("Diagnostics failed. Check log.");
            }
        }

        /// <summary>
        /// Measures the actual gap width at a position by casting rays and
        /// OverlapSphere in all directions to find the nearest solid walls.
        /// Logs the minimum clearance (distance to nearest wall) and the
        /// gap widths along each axis.
        /// </summary>
        private static void MeasureGapWidth(System.Text.StringBuilder sb,
            string label, Vector3 pos)
        {
            sb.AppendLine($"\n  [{label}] at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):");

            // Combined mask: L22 (terrain obstacles) + L23 (CharaWalls)
            int solidMask = (1 << 22) | (1 << 23);

            // Find all solid colliders within 10m
            var nearby = Physics.OverlapSphere(pos, 10f, solidMask);
            if (nearby == null || nearby.Length == 0)
            {
                sb.AppendLine("    No solid obstacles within 10m — wide open.");
                return;
            }

            // Find closest solid (non-trigger) collider and measure
            // distances in cardinal directions
            float minDist = float.MaxValue;
            string closestName = "";
            int solidCount = 0;

            // Track nearest wall in each direction
            float nearestNorth = float.MaxValue; // +Z
            float nearestSouth = float.MaxValue; // -Z
            float nearestEast = float.MaxValue;  // +X
            float nearestWest = float.MaxValue;  // -X
            string nearestNorthName = "", nearestSouthName = "";
            string nearestEastName = "", nearestWestName = "";

            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null || nearby[i].isTrigger) continue;
                solidCount++;

                var closest = nearby[i].ClosestPoint(pos);
                float dist = Vector3.Distance(pos, closest);
                string cName = $"{nearby[i].name} L{nearby[i].gameObject.layer}";

                if (dist < minDist)
                {
                    minDist = dist;
                    closestName = cName;
                }

                sb.AppendLine($"    d={dist:F3}m \"{cName}\" " +
                    $"closest=({closest.x:F2},{closest.y:F2},{closest.z:F2})" +
                    (nearby[i].transform.parent != null
                        ? $" parent=\"{nearby[i].transform.parent.name}\""
                        : ""));

                // Determine direction from pos to closest point
                float dx = closest.x - pos.x;
                float dz = closest.z - pos.z;

                // Track nearest in each cardinal direction
                if (dz > 0.01f && dist < nearestNorth)
                {
                    nearestNorth = dist;
                    nearestNorthName = cName;
                }
                if (dz < -0.01f && dist < nearestSouth)
                {
                    nearestSouth = dist;
                    nearestSouthName = cName;
                }
                if (dx > 0.01f && dist < nearestEast)
                {
                    nearestEast = dist;
                    nearestEastName = cName;
                }
                if (dx < -0.01f && dist < nearestWest)
                {
                    nearestWest = dist;
                    nearestWestName = cName;
                }
            }

            sb.AppendLine($"    SUMMARY: {solidCount} solid obstacles within 10m");
            sb.AppendLine($"    Nearest wall: {minDist:F3}m ({closestName})");
            if (nearestEast < float.MaxValue && nearestWest < float.MaxValue)
                sb.AppendLine($"    E-W gap: {nearestEast:F3}m + {nearestWest:F3}m = {nearestEast + nearestWest:F3}m total");
            if (nearestNorth < float.MaxValue && nearestSouth < float.MaxValue)
                sb.AppendLine($"    N-S gap: {nearestNorth:F3}m + {nearestSouth:F3}m = {nearestNorth + nearestSouth:F3}m total");
        }

        /// <summary>Holds a loaded cached height grid with its metadata.</summary>
        public class CachedGrid
        {
            /// <summary>
            /// Per cell: 0 = ocean, 1 = solid obstacle,
            /// 2+ = height in offset cm: realHeight = (stored / 100.0) - 100.0
            /// </summary>
            public ushort[,] Height;

            public float WorldMinX;
            public float WorldMinZ;
            public float CellSize;
            public int GridW;
            public int GridH;

            /// <summary>
            /// Sparse table of sub-cell clearance offsets for cells near
            /// CharaWalls. Key = ax * GridH + az. Value = (offsetX, offsetZ)
            /// in meters from cell center. Null if loaded from legacy format.
            /// </summary>
            public Dictionary<long, (float, float)> ClearanceOffsets;

            /// <summary>
            /// Sparse table of actual clearance distances (meters) for cells
            /// near walls. The pathfinder uses these for continuous penalty —
            /// tighter cells cost more. Null if loaded from older format.
            /// </summary>
            public Dictionary<long, float> ClearanceValues;

            /// <summary>Convert world position to grid indices.</summary>
            public void WorldToGrid(float worldX, float worldZ,
                out int ax, out int az)
            {
                ax = (int)((worldX - WorldMinX) / CellSize);
                az = (int)((worldZ - WorldMinZ) / CellSize);
            }

            /// <summary>Convert grid indices to world position (cell center).</summary>
            public Vector3 GridToWorld(int ax, int az)
            {
                return new Vector3(
                    WorldMinX + ax * CellSize, 0f,
                    WorldMinZ + az * CellSize);
            }

            /// <summary>
            /// Convert grid indices to world position, applying clearance
            /// offset if available. For cells near CharaWalls, this returns
            /// the sub-cell position with maximum clearance from all walls
            /// instead of the cell center.
            /// </summary>
            public Vector3 GridToWorldWithClearance(int ax, int az)
            {
                float x = WorldMinX + ax * CellSize;
                float z = WorldMinZ + az * CellSize;

                if (ClearanceOffsets != null)
                {
                    long key = (long)ax * GridH + az;
                    if (ClearanceOffsets.TryGetValue(key, out var offset))
                    {
                        x += offset.Item1;
                        z += offset.Item2;
                    }
                }

                return new Vector3(x, 0f, z);
            }

            /// <summary>
            /// Gets the actual clearance distance (meters) for a cell.
            /// Returns float.MaxValue for cells with no clearance data
            /// (wide open, far from any wall).
            /// </summary>
            public float GetClearance(int ax, int az)
            {
                if (ClearanceValues == null) return float.MaxValue;
                long key = (long)ax * GridH + az;
                return ClearanceValues.TryGetValue(key, out float val)
                    ? val : float.MaxValue;
            }

            /// <summary>
            /// Get the real height in meters for a cell.
            /// Returns float.MinValue for ocean or obstacle cells.
            /// </summary>
            public float GetHeightM(int ax, int az)
            {
                if (ax < 0 || ax >= GridW || az < 0 || az >= GridH)
                    return float.MinValue;
                ushort v = Height[ax, az];
                if (v < 2) return float.MinValue; // Ocean or obstacle
                return (v / 100f) - 100f;
            }
        }
    }
}
