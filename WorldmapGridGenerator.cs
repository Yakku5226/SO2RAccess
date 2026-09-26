using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Generates and saves the per-travel-mode terrain grid for a world map
    /// at 0.5m resolution (format WMGI, see <see cref="WorldmapGridFormat"/>).
    /// Uses CalcHeight for terrain detection and OverlapSphere for solid
    /// obstacle detection (the per-cell probe itself lives in
    /// <see cref="WorldmapGridProbe"/> so F10 can replay it live), probing
    /// each cell separately for FOOT and BUNNY
    /// travel with the game's own wall layer masks
    /// (GameRenderManager.LayerMaskWall / LayerMaskBunnyWall) read LIVE at
    /// bake time — never hardcoded layer guesses. Key insights:
    /// - Col_Obstacle objects with isTrigger=true (trees, bushes) are
    ///   passthrough — only solid colliders (isTrigger=false) block.
    /// - Ocean is the absence of CalcHeight ground, which blocks BOTH modes
    ///   (the bunny still needs ground — confirmed by the Phase A ride trace).
    /// - For cells near CharaWalls (foot-only barriers with designed road
    ///   gaps), stores the sub-cell position with maximum clearance so the
    ///   pathfinder guides the player through the exact center of narrow gaps.
    /// </summary>
    public static class WorldmapGridGenerator
    {
        /// <summary>Grid cell spacing in world units (meters).</summary>
        public const float CellSize = 0.5f;

        /// <summary>
        /// Bake tile edge length in metres. The bake probes the grid tile by
        /// tile, instantiating each tile's streamed collision chunks first
        /// (see <see cref="WorldmapChunkLoader"/>) — the game's CullingManager
        /// only keeps ground-detail collision loaded within ~100m of the
        /// camera, so probing without this loads bakes fiction beyond that
        /// radius (the 2026-07-06 B7 audit failure). Must be an exact
        /// multiple of <see cref="CellSize"/>.
        /// </summary>
        internal const float TileSizeMeters = 64f;

        /// <summary>
        /// Expel bake bounds — FIXED, because the shipped Expel grid (bake of
        /// 2026-07-10, validated in play) uses exactly this cell alignment and
        /// the user declined whole-map rebakes. Determined from multiple scans
        /// across the map: all terrain with 10 m padding. Any other world map
        /// derives its bounds from the game's own data at bake time
        /// (<see cref="WorldmapGridSurvey"/>).
        /// </summary>
        private const float ExpelMinX = -1920.0f, ExpelMinZ = -1600.0f;
        private const float ExpelMaxX = 1870.0f, ExpelMaxZ = 870.0f;

        /// <summary>
        /// Generates the per-mode grid for the current world map and saves
        /// it to a binary file. Call from the world map with F9 in debug mode.
        /// </summary>
        public static void GenerateAndSave()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say(Loc.Get("gridgen_only_worldmap"));
                    return;
                }

                if (!fm.IsExistWorldGridData())
                {
                    ScreenReader.Say(Loc.Get("gridgen_no_griddata"));
                    return;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say(Loc.Get("gridgen_no_player"));
                    return;
                }

                WorldmapID wmID = fm.WorldmapID;
                string mapName = WorldmapFishingStands.MapName(wmID);
                if (mapName == null)
                {
                    MelonLoader.MelonLogger.Error(
                        $"[GridGen] World map id {wmID} is not a known planet — bake refused.");
                    ScreenReader.Say(Loc.Get("gridgen_no_map"));
                    return;
                }

                // The Expel grid is validated in play and its cell alignment
                // is what every Expel test result refers to; a stray F9 must
                // not replace it. Rename or delete the file to rebake on purpose.
                if (wmID == WorldmapID.EXPEL &&
                    File.Exists(WorldmapGridFormat.UserGridPath(mapName)))
                {
                    MelonLoader.MelonLogger.Msg(
                        "[GridGen] Expel grid locked — rebake refused (rename or delete " +
                        "worldmap_expel.grid to rebake it on purpose).");
                    ScreenReader.Say(Loc.Get("gridgen_expel_locked"));
                    return;
                }

                // --- Step 0: Read the game's wall masks LIVE ---
                // The grid is only as honest as its bake inputs. If the
                // masks cannot be read we ABORT — silently falling back to
                // hardcoded layer bits is exactly how the old grid missed
                // 4 of the 6 real foot wall layers (the Lasgus/Mountain
                // Palace false connection).
                int footMask, bunnyMask;
                try
                {
                    footMask = GameRenderManager.LayerMaskWall;
                    bunnyMask = GameRenderManager.LayerMaskBunnyWall;
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Error(
                        $"[GridGen] Cannot read wall masks: {ex.Message}");
                    ScreenReader.Say(Loc.Get("gridgen_abort_masks"));
                    return;
                }
                if (footMask == 0 || bunnyMask == 0)
                {
                    MelonLoader.MelonLogger.Error(
                        $"[GridGen] Wall mask empty: foot=0x{footMask:X8} " +
                        $"bunny=0x{bunnyMask:X8} — refusing to bake.");
                    ScreenReader.Say(Loc.Get("gridgen_abort_mask_empty"));
                    return;
                }

                int footSolidMask = footMask & ~(1 << WorldmapGridProbe.CharaWallLayer);
                int charaWallMask = footMask & (1 << WorldmapGridProbe.CharaWallLayer);

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Bake masks (read live): " +
                    $"foot=0x{footMask:X8} → {WorldmapGridDiagnostics.DescribeMask(footMask)} | " +
                    $"bunny=0x{bunnyMask:X8} → {WorldmapGridDiagnostics.DescribeMask(bunnyMask)}");

                // --- Step 0b: survey the map data the bake depends on ---
                // Same honesty rule as the masks: a red flag (no painted rect,
                // no streamed unit list, lowered terrain copies with colliders,
                // a grid too large to be right) refuses the bake.
                var survey = WorldmapGridSurvey.Run(fm, wmID,
                    player.transform.position, writeProbeFile: true);
                if (!survey.CanBake)
                {
                    MelonLoader.MelonLogger.Error(
                        $"[GridGen] ABORT: survey red flags — {survey.AbortSummary}");
                    ScreenReader.Say(Loc.Get("gridgen_survey_abort", survey.AbortSummary));
                    return;
                }

                ScreenReader.Say(Loc.Get("gridgen_start", mapName));
                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Starting 0.5m per-mode grid for {mapName}...");

                // --- Step 1: World bounds ---
                // Position-independent by construction, so the grid is
                // identical regardless of where the player stands when
                // generating (consistent cell alignment — critical for
                // CharaWall gap detection). Expel: the fixed constants of the
                // validated grid. Other maps: derived from the game's painted
                // rect and streamed unit coverage (static assets), snapped to
                // the tile lattice. The loader reads bounds from the file
                // header, never from these values.
                GetBakeBounds(wmID, survey, out float worldMinX, out float worldMinZ,
                    out float worldMaxX, out float worldMaxZ, out string boundsSource);
                MelonLoader.MelonLogger.Msg($"[GridGen] Bounds source: {boundsSource}");

                int gridW = (int)((worldMaxX - worldMinX) / CellSize) + 1;
                int gridH = (int)((worldMaxZ - worldMinZ) / CellSize) + 1;
                long totalCells = (long)gridW * gridH;

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] World bounds: X=[{worldMinX:F1},{worldMaxX:F1}]" +
                    $" Z=[{worldMinZ:F1},{worldMaxZ:F1}] " +
                    $"size={gridW}x{gridH} ({totalCells} cells at {CellSize}m)");

                // --- Step 1b: streamed-chunk loader (collision streaming fix)
                // Tiling derived from the GRID so the last row/column of
                // cells is always covered by a tile.
                int cellsPerTile = (int)(TileSizeMeters / CellSize);
                int tilesX = (gridW + cellsPerTile - 1) / cellsPerTile;
                int tilesZ = (gridH + cellsPerTile - 1) / cellsPerTile;
                var chunkLoader = WorldmapChunkLoader.TryCreate(
                    worldMinX, worldMinZ, tilesX, tilesZ, TileSizeMeters,
                    out string chunkFail);
                if (chunkLoader == null)
                {
                    // Same honesty rule as the mask read: without the
                    // streamed chunks the bake is only correct ~100m around
                    // the player (proven by the B7 audit) — refuse to
                    // produce fiction.
                    MelonLoader.MelonLogger.Error(
                        $"[GridGen] ABORT: culling chunk data unavailable " +
                        $"({chunkFail}). Refusing to bake a grid that would " +
                        "be wrong beyond ~100m of the player.");
                    ScreenReader.Say(Loc.Get("gridgen_abort_chunks"));
                    return;
                }

                // --- Step 2: Ground height + per-mode obstacle status ---
                // Height lane is PURE terrain: 0 = no ground, else
                // (groundY + 100) * 100 cm (clamped to >= 2). Blocked state
                // lives in the flags lane, one bit per travel mode.
                ushort[,] height = new ushort[gridW, gridH];
                byte[] flags = new byte[totalCells];
                // Sparse table of clearance offsets for cells near CharaWalls.
                // Key = (ax * gridH + az), value = (offsetX, offsetZ) meters
                // from cell center to the best sub-cell position. Foot-only.
                var clearanceOffsets = new Dictionary<long, (float, float)>();
                // Sparse table of actual clearance values (meters) for cells
                // near walls. The foot A* uses these for continuous penalty.
                var clearanceValues = new Dictionary<long, float>();
                int terrainCount = 0, oceanCount = 0;
                int footBlockedCount = 0, bunnyBlockedCount = 0;
                float minY = float.MaxValue, maxY = float.MinValue;

                // Probes ONE cell against whatever geometry is currently in
                // the physics world. Only called from the tile loop below,
                // which guarantees the cell's streamed chunks are loaded.
                void ProbeCell(int ax, int az)
                {
                    float worldX = worldMinX + ax * CellSize;
                    float worldZ = worldMinZ + az * CellSize;
                    float groundY = WorldmapGridProbe.BakeGroundHeight(
                        worldX, worldZ, out bool hasGround);

                    if (!hasGround)
                    {
                        height[ax, az] = 0; // No ground — blocks all modes.
                        oceanCount++;
                        return;
                    }

                    terrainCount++;
                    if (groundY < minY) minY = groundY;
                    if (groundY > maxY) maxY = groundY;

                    // Store the pure height regardless of blocked state.
                    int stored = (int)((groundY + 100f) * 100f);
                    if (stored < 2) stored = 2; // 0 reserved for "no ground"
                    if (stored > 65535) stored = 65535;
                    height[ax, az] = (ushort)stored;

                    // The obstacle test lives in WorldmapGridProbe so the F10
                    // truth probe can replay exactly what the bake did here.
                    var r = WorldmapGridProbe.ProbeObstacles(worldX, worldZ, groundY,
                        footSolidMask, charaWallMask, bunnyMask);
                    long key = (long)ax * gridH + az;
                    if (r.HasClearanceOffset) clearanceOffsets[key] = (r.BestOffX, r.BestOffZ);
                    if (r.ClearanceValue < float.MaxValue) clearanceValues[key] = r.ClearanceValue;

                    byte f = 0;
                    if (r.FootBlocked)
                    {
                        f |= WorldmapGridFormat.CachedGrid.FlagFootBlocked;
                        footBlockedCount++;
                    }
                    if (r.BunnyBlocked)
                    {
                        f |= WorldmapGridFormat.CachedGrid.FlagBunnyBlocked;
                        bunnyBlockedCount++;
                    }
                    flags[key] = f;
                }

                // Tile loop: load each tile's streamed chunks, probe its
                // cells with the full local geometry present, unload. The
                // loader is disposed in finally — an exception mid-bake must
                // never leave thousands of chunk clones in the scene.
                int totalTiles = tilesX * tilesZ;
                int tilesDone = 0;
                // Spoken every quarter: the game is frozen for minutes and a
                // blind user has no other sign that the bake is still alive.
                int nextSpokenPct = 25;
                var bakeWatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    for (int tx = 0; tx < tilesX; tx++)
                    {
                        for (int tz = 0; tz < tilesZ; tz++)
                        {
                            try
                            {
                                chunkLoader.LoadTile(tx, tz);
                                int axEnd = Math.Min(
                                    (tx + 1) * cellsPerTile, gridW);
                                int azEnd = Math.Min(
                                    (tz + 1) * cellsPerTile, gridH);
                                for (int ax = tx * cellsPerTile; ax < axEnd; ax++)
                                {
                                    for (int az = tz * cellsPerTile; az < azEnd; az++)
                                    {
                                        ProbeCell(ax, az);
                                    }
                                }
                            }
                            finally
                            {
                                chunkLoader.UnloadTile();
                            }

                            tilesDone++;
                            int pct = tilesDone * 100 / totalTiles;
                            if (pct >= nextSpokenPct && nextSpokenPct < 100)
                            {
                                ScreenReader.Say(Loc.Get("gridgen_progress",
                                    nextSpokenPct, bakeWatch.ElapsedMilliseconds / 1000));
                                nextSpokenPct += 25;
                            }
                            if (tilesDone % 200 == 0)
                            {
                                MelonLoader.MelonLogger.Msg(
                                    $"[GridGen] Progress: tile {tilesDone}/" +
                                    $"{totalTiles} ({tilesDone * 100 / totalTiles}%), " +
                                    $"{chunkLoader.InstantiationsTotal} chunk loads, " +
                                    $"{bakeWatch.ElapsedMilliseconds / 1000}s elapsed");
                            }
                        }
                    }
                }
                finally
                {
                    chunkLoader.Dispose();
                }

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Chunk stats: unitsIndexed={chunkLoader.UnitsIndexed} " +
                    $"instantiations={chunkLoader.InstantiationsTotal} " +
                    $"skippedNoColliders={chunkLoader.SkippedNoColliders} " +
                    $"activationFixes={chunkLoader.ActivationFixes} | " +
                    $"probe pass took {bakeWatch.ElapsedMilliseconds / 1000}s");

                // --- Step 2b: Flood-fill to seal town model interiors ---
                // Per travel mode: a cell the mode could stand on but that
                // is not connected to the open world gets the mode's blocked
                // bit (plus the sealed-interior diagnostic bit). Must run
                // BEFORE entrance trigger clearing so the fill doesn't leak
                // through the large trigger areas into town interiors.
                int footSealed = FloodFillSeal(height, flags, gridW, gridH,
                    WorldmapGridFormat.CachedGrid.FlagFootBlocked, "foot");
                int bunnySealed = FloodFillSeal(height, flags, gridW, gridH,
                    WorldmapGridFormat.CachedGrid.FlagBunnyBlocked, "bunny");

                // --- Step 2c: Mark town entrance triggers as passable ---
                // Now that interiors are sealed, punch entrance holes so the
                // A* can route TO town entrances (for mapjump transitions)
                // but never THROUGH the town model.
                int entranceCellsCleared = ClearEntranceTriggers(
                    height, flags, gridW, gridH, worldMinX, worldMinZ);

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Complete: {gridW}x{gridH} grid. " +
                    $"terrain={terrainCount} noGround={oceanCount} " +
                    $"footBlocked={footBlockedCount} (+{footSealed} sealed) " +
                    $"bunnyBlocked={bunnyBlockedCount} (+{bunnySealed} sealed) " +
                    $"entranceCellsCleared={entranceCellsCleared} " +
                    $"clearanceOffsets={clearanceOffsets.Count} " +
                    $"clearanceValues={clearanceValues.Count} " +
                    $"footFloor={WorldmapGridProbe.MinPassableClearance:F2}m " +
                    $"bunnyFloor={WorldmapGridProbe.BunnyMinClearance:F2}m " +
                    $"height range={minY:F2}m to {maxY:F2}m");

                // --- Step 3: Save to binary file (WMGI v2) ---
                string filePath = WorldmapGridFormat.UserGridPath(mapName);

                WorldmapGridFormat.SaveGrid(filePath, worldMinX, worldMinZ,
                    CellSize, gridW, gridH, height, flags,
                    clearanceOffsets, clearanceValues,
                    footMask, bunnyMask, WorldmapGridProbe.MinPassableClearance,
                    WorldmapGridProbe.BunnyMinClearance);

                long fileSize = new FileInfo(filePath).Length;
                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Saved to: {filePath} ({fileSize} bytes)");
                ScreenReader.Say(Loc.Get("gridgen_saved", gridW, gridH,
                    terrainCount, oceanCount, footBlockedCount, footSealed,
                    bunnyBlockedCount, bunnySealed, minY.ToString("F1"),
                    maxY.ToString("F1")));
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[GridGen] Error: {ex}");
                ScreenReader.Say(Loc.Get("gridgen_failed"));
            }
        }

        /// <summary>
        /// Seals cells that are passable for a travel mode but unreachable
        /// from the open world (town model interiors): flood-fills from the
        /// map edges and all ocean-adjacent cells over the mode's passable
        /// cells, then sets the mode's blocked bit (plus the sealed-interior
        /// diagnostic bit) on every passable cell the fill never reached.
        /// The fill is 8-directional — diagonals matter, because narrow
        /// CharaWall gaps can be passable only diagonally (1-2 cells wide).
        /// Returns the number of cells sealed.
        /// </summary>
        private static int FloodFillSeal(ushort[,] height, byte[] flags,
            int gridW, int gridH, byte modeBit, string modeName)
        {
            try
            {
                bool[,] reachable = new bool[gridW, gridH];
                var floodQueue = new Queue<(int x, int z)>();

                // 8-directional: cardinal + diagonal.
                int[] fdx = { 0, 1, 0, -1, 1, 1, -1, -1 };
                int[] fdz = { 1, 0, -1, 0, 1, -1, -1, 1 };

                bool Passable(int x, int z) =>
                    height[x, z] >= 2 &&
                    (flags[(long)x * gridH + z] & modeBit) == 0;

                // Seed from all edge cells that are passable.
                for (int ax = 0; ax < gridW; ax++)
                {
                    if (Passable(ax, 0)) { floodQueue.Enqueue((ax, 0)); reachable[ax, 0] = true; }
                    if (Passable(ax, gridH - 1)) { floodQueue.Enqueue((ax, gridH - 1)); reachable[ax, gridH - 1] = true; }
                }
                for (int az = 0; az < gridH; az++)
                {
                    if (Passable(0, az)) { floodQueue.Enqueue((0, az)); reachable[0, az] = true; }
                    if (Passable(gridW - 1, az)) { floodQueue.Enqueue((gridW - 1, az)); reachable[gridW - 1, az] = true; }
                }

                // Also seed from all ocean-adjacent passable cells.
                for (int ax = 1; ax < gridW - 1; ax++)
                {
                    for (int az = 1; az < gridH - 1; az++)
                    {
                        if (!Passable(ax, az) || reachable[ax, az]) continue;
                        for (int d = 0; d < 8; d++)
                        {
                            if (height[ax + fdx[d], az + fdz[d]] == 0)
                            {
                                floodQueue.Enqueue((ax, az));
                                reachable[ax, az] = true;
                                break;
                            }
                        }
                    }
                }

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Flood fill ({modeName}): " +
                    $"{floodQueue.Count} seed cells.");

                while (floodQueue.Count > 0)
                {
                    var (cx, cz) = floodQueue.Dequeue();
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + fdx[d];
                        int nz = cz + fdz[d];
                        if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH)
                            continue;
                        if (reachable[nx, nz]) continue;
                        if (!Passable(nx, nz)) continue;
                        reachable[nx, nz] = true;
                        floodQueue.Enqueue((nx, nz));
                    }
                }

                int sealedCount = 0;
                for (int ax = 0; ax < gridW; ax++)
                {
                    for (int az = 0; az < gridH; az++)
                    {
                        if (!Passable(ax, az) || reachable[ax, az]) continue;
                        flags[(long)ax * gridH + az] |= (byte)(modeBit |
                            WorldmapGridFormat.CachedGrid.SealedBitFor(modeBit));
                        sealedCount++;
                    }
                }

                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Flood fill ({modeName}) complete: " +
                    $"{sealedCount} interior cells sealed.");
                return sealedCount;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning(
                    $"[GridGen] Flood fill ({modeName}) error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Punches entrance holes: on every ground cell inside a SMALL
        /// ground-level FieldMapjumpCollision trigger, lifts the town-interior
        /// SEAL (the flood fill's blocked bit, per travel mode) so the A* can
        /// route to town entrances. A cell whose blocked bit came from the
        /// probe — a real wall collider — is left blocked: until 2026-09-13
        /// this pass cleared every blocked bit in the trigger's box, which
        /// re-opened Arlia's own wall strips and a slice of the sealed shore
        /// behind them (the "fishing stand inside the wall"). Large triggers
        /// (Y extent &gt; 20m) are town-wide detection zones and are skipped.
        /// If an un-sealed cell's baked height is far off the trigger's ground
        /// level (CalcHeight hit a model roof above the road), the height is
        /// corrected to the trigger's, otherwise the climb rule would
        /// disconnect the entrance from the road. Returns the number of cells
        /// un-sealed; walls kept blocked inside triggers are logged per trigger.
        /// </summary>
        private static int ClearEntranceTriggers(ushort[,] height, byte[] flags,
            int gridW, int gridH, float worldMinX, float worldMinZ)
        {
            const byte footSeal = WorldmapGridFormat.CachedGrid.FlagSealedInterior;
            const byte bunnySeal = WorldmapGridFormat.CachedGrid.FlagBunnySealed;
            const byte footBit = WorldmapGridFormat.CachedGrid.FlagFootBlocked;
            const byte bunnyBit = WorldmapGridFormat.CachedGrid.FlagBunnyBlocked;
            // Matches the pathfinder's MaxClimbCm — a larger baked step at
            // an entrance cell would break connectivity to the road.
            const int MaxEntranceHeightStepCm = 500;
            const int MaxWallLogLines = 12;
            const int MaxEntranceRefLogLines = 60;

            int cleared = 0, wallsKept = 0, triggers = 0, wallLines = 0;
            try
            {
                var mapjumps = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (mapjumps == null) return 0;

                for (int m = 0; m < mapjumps.Length; m++)
                {
                    var mj = mapjumps[m];
                    if (mj == null) continue;

                    var colliders = mj.GetComponents<UnityEngine.Collider>();
                    if (colliders == null) continue;

                    for (int ci = 0; ci < colliders.Length; ci++)
                    {
                        var col = colliders[ci];
                        if (col == null || !col.isTrigger) continue;

                        var b = col.bounds;
                        if (b.size.y > 20f) continue; // town-wide zone
                        triggers++;

                        int minAx = Math.Max(0,
                            (int)((b.min.x - worldMinX) / CellSize));
                        int maxAx = Math.Min(gridW - 1,
                            (int)((b.max.x - worldMinX) / CellSize));
                        int minAz = Math.Max(0,
                            (int)((b.min.z - worldMinZ) / CellSize));
                        int maxAz = Math.Min(gridH - 1,
                            (int)((b.max.z - worldMinZ) / CellSize));

                        // Ground reference for the roof repair: the baked
                        // road cells inside the box (chunk-loaded, honest on
                        // every planet), else CalcHeight on the resident
                        // geometry, else no repair. Never a constant — the old
                        // 20.8 m Expel fallback would have fabricated steps at
                        // Nede's gates (its towns sit at 1–5 m).
                        bool haveRef = TriggerGroundReference(height, flags, gridH,
                            minAx, maxAx, minAz, maxAz, mj, out ushort trigH,
                            out string refSource);
                        if (triggers <= MaxEntranceRefLogLines)
                            MelonLoader.MelonLogger.Msg(
                                $"[GridGen] entrance {mj.fieldmapID} at ({mj.transform.position.x:F0}," +
                                $"{mj.transform.position.z:F0}): ground {refSource}");

                        int trigCleared = 0, trigWalls = 0;
                        for (int ex = minAx; ex <= maxAx; ex++)
                        {
                            for (int ez = minAz; ez <= maxAz; ez++)
                            {
                                if (height[ex, ez] < 2) continue; // no ground
                                long idx = (long)ex * gridH + ez;
                                byte f = flags[idx];
                                byte lift = 0;
                                if ((f & footSeal) != 0) lift |= footBit | footSeal;
                                if ((f & bunnySeal) != 0) lift |= bunnyBit | bunnySeal;
                                // A blocked bit WITHOUT its seal bit is the
                                // probe's own verdict: a wall. Keep it.
                                bool wall = ((f & footBit) != 0 && (f & footSeal) == 0)
                                    || ((f & bunnyBit) != 0 && (f & bunnySeal) == 0);
                                if (wall) trigWalls++;
                                if (lift == 0) continue;

                                flags[idx] = (byte)(f & ~lift);
                                if (haveRef && Math.Abs(height[ex, ez] - trigH) >
                                    MaxEntranceHeightStepCm)
                                {
                                    height[ex, ez] = trigH;
                                }
                                trigCleared++;
                            }
                        }
                        cleared += trigCleared;
                        wallsKept += trigWalls;
                        if (trigWalls > 0 && wallLines++ < MaxWallLogLines)
                            MelonLoader.MelonLogger.Msg(
                                $"[GridGen] entrance {mj.fieldmapID} at ({mj.transform.position.x:F0}," +
                                $"{mj.transform.position.z:F0}): {trigCleared} cells un-sealed, " +
                                $"{trigWalls} wall cells kept blocked inside the trigger box.");
                    }
                }
                MelonLoader.MelonLogger.Msg(
                    $"[GridGen] Entrance clearing: {triggers} triggers, {cleared} cells un-sealed, " +
                    $"{wallsKept} wall cells kept blocked (probe verdicts are never lifted).");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning(
                    $"[GridGen] Entrance clearing error: {ex.Message}");
            }
            return cleared;
        }

        /// <summary>
        /// Bake bounds for a world map. Expel keeps the constants of the
        /// validated shipped grid; every other map takes the survey's derived
        /// bounds (painted rect ∪ streamed unit coverage, margin, snapped to
        /// the tile lattice). <paramref name="source"/> names the choice for
        /// the log. Falls back to the Expel constants only when the survey
        /// could not derive bounds — which the survey already reports as a
        /// red flag, so the bake never gets here in that state.
        /// </summary>
        private static void GetBakeBounds(WorldmapID wmID, WorldmapGridSurvey.Result survey,
            out float minX, out float minZ, out float maxX, out float maxZ, out string source)
        {
            if (wmID != WorldmapID.EXPEL && survey != null && survey.HasDerivedBounds)
            {
                minX = survey.MinX; minZ = survey.MinZ;
                maxX = survey.MaxX; maxZ = survey.MaxZ;
                source = $"derived from the game data (rect ∪ units + {WorldmapGridSurvey.BoundsMarginMeters:F0} m, " +
                         $"snapped to {TileSizeMeters:F0} m) for {wmID}";
                return;
            }
            minX = ExpelMinX; minZ = ExpelMinZ;
            maxX = ExpelMaxX; maxZ = ExpelMaxZ;
            source = wmID == WorldmapID.EXPEL
                ? "Expel constants (validated grid alignment)"
                : $"Expel constants as FALLBACK for {wmID} — the survey derived no bounds";
        }

        /// <summary>
        /// Ground reference for an entrance trigger's roof repair, in the
        /// grid's height encoding. Preference: median of the baked FOOT-
        /// passable cells inside the trigger box (the road the flood fill
        /// reached — chunk-loaded, so honest on every planet), else the
        /// median of all baked ground cells there, else CalcHeight on the
        /// resident geometry, else none (returns false: no repair). The
        /// chosen source, and any disagreement with CalcHeight above the
        /// climb rule, go to <paramref name="source"/> for the log.
        /// </summary>
        private static bool TriggerGroundReference(ushort[,] height, byte[] flags, int gridH,
            int minAx, int maxAx, int minAz, int maxAz, FieldMapjumpCollision mj,
            out ushort trigH, out string source)
        {
            const byte footBit = WorldmapGridFormat.CachedGrid.FlagFootBlocked;
            const int DisagreeCm = 500;
            var road = new List<ushort>();
            var ground = new List<ushort>();
            for (int ex = minAx; ex <= maxAx; ex++)
            {
                for (int ez = minAz; ez <= maxAz; ez++)
                {
                    ushort h = height[ex, ez];
                    if (h < 2) continue;
                    ground.Add(h);
                    if ((flags[(long)ex * gridH + ez] & footBit) == 0) road.Add(h);
                }
            }

            float calcY = GameUtility.CalcHeight(mj.transform.position, out bool calcOk, 50f);
            int calcEncoded = calcOk ? Math.Clamp((int)((calcY + 100f) * 100f), 2, 65535) : 0;

            var pick = road.Count > 0 ? road : ground;
            if (pick.Count > 0)
            {
                pick.Sort();
                trigH = pick[pick.Count / 2];
                source = $"grid median of {pick.Count} {(road.Count > 0 ? "road" : "ground")} cells = " +
                         $"{trigH / 100f - 100f:F1} m";
                if (calcOk && Math.Abs(trigH - calcEncoded) > DisagreeCm)
                    source += $" — DISAGREES with CalcHeight {calcY:F1} m (gate on a roof, or resident geometry differs)";
                else if (calcOk)
                    source += $" (CalcHeight {calcY:F1} m agrees)";
                return true;
            }
            if (calcOk)
            {
                trigH = (ushort)calcEncoded;
                source = $"CalcHeight {calcY:F1} m (no baked ground inside the box)";
                return true;
            }
            trigH = 0;
            source = "no reference (no baked ground, CalcHeight failed) — height repair skipped";
            return false;
        }
    }
}
