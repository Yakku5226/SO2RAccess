using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Debug action (Delete key, world map): restores the town-gate walls to the
    /// ACTIVE grid without rebaking it. Until 2026-09-13 the F9 bake's
    /// entrance-clearing pass lifted every blocked bit inside each entrance
    /// trigger's box, real walls included (Arlia's wall strips became open
    /// ground and a slice of the town's shore behind them became a "fishing
    /// stand"). A whole-map rebake would also fold in the position-dependent
    /// streaming differences the validated July grid does not have, so this
    /// patch touches ONLY cells inside the ground-level entrance trigger boxes,
    /// re-runs the bake's own probe there at the grid's stored height, and sets
    /// the blocked bit where the probe finds a wall. It never clears anything
    /// and never changes a height. The grid is backed up before it is written;
    /// every gate logs how many cells it blocked, and an offline diff must
    /// show newly blocked cells inside gate boxes and nowhere else.
    /// </summary>
    public static class WorldmapGridGatePatch
    {
        /// <summary>Triggers taller than this are town-wide zones, not gate rings (same rule as the bake).</summary>
        private const float GroundLevelTriggerMaxHeight = 20f;

        /// <summary>Cells logged one by one per gate; the rest only count.</summary>
        private const int MaxCellLinesPerGate = 3;

        /// <summary>Runs the patch on the current world map's active grid and saves it.</summary>
        public static void PatchAndSave()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say("The gate wall patch only works on the world map.");
                    return;
                }
                var grid = WorldmapPathfinder.GetCachedGrid(fm.WorldmapID);
                if (grid == null || !grid.IsV2)
                {
                    ScreenReader.Say("No version 2 grid is loaded. Nothing patched.");
                    return;
                }

                int footMask = GameRenderManager.LayerMaskWall;
                int bunnyMask = GameRenderManager.LayerMaskBunnyWall;
                if (footMask != grid.FootMask || bunnyMask != grid.BunnyMask)
                {
                    // The probe must run with the masks the grid was baked with,
                    // or its verdicts would not be comparable to the file's.
                    MelonLogger.Error($"[GatePatch] ABORT: live masks foot=0x{footMask:X8} bunny=0x{bunnyMask:X8} " +
                        $"differ from the grid's foot=0x{grid.FootMask:X8} bunny=0x{grid.BunnyMask:X8}.");
                    ScreenReader.Say("Gate wall patch aborted. The wall masks differ from the grid's. Check log.");
                    return;
                }
                int footSolidMask = footMask & ~(1 << WorldmapGridProbe.CharaWallLayer);
                int charaWallMask = footMask & (1 << WorldmapGridProbe.CharaWallLayer);

                var mapjumps = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                if (mapjumps == null || mapjumps.Length == 0)
                {
                    MelonLogger.Error("[GatePatch] ABORT: no map jump triggers in the scene (town colliders not spawned yet?).");
                    ScreenReader.Say("Gate wall patch aborted. No town entrance triggers were found. Check log.");
                    return;
                }

                // Patch a COPY of the flags; the live grid is replaced by a reload.
                byte[] flags = (byte[])grid.Flags.Clone();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[GatePatch] === GATE WALL PATCH === grid {grid.GridW}x{grid.GridH}, masks foot=0x{footMask:X8} bunny=0x{bunnyMask:X8}");
                int gates = 0, cellsProbed = 0, footSet = 0, bunnySet = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                foreach (var mj in mapjumps)
                {
                    if (mj == null) continue;
                    var colliders = mj.GetComponents<Collider>();
                    if (colliders == null) continue;
                    foreach (var col in colliders)
                    {
                        if (col == null || !col.isTrigger) continue;
                        var b = col.bounds;
                        if (b.size.y > GroundLevelTriggerMaxHeight) continue;
                        gates++;

                        grid.WorldToGrid(b.min.x, b.min.z, out int minAx, out int minAz);
                        grid.WorldToGrid(b.max.x, b.max.z, out int maxAx, out int maxAz);
                        minAx = Math.Max(0, minAx); minAz = Math.Max(0, minAz);
                        maxAx = Math.Min(grid.GridW - 1, maxAx); maxAz = Math.Min(grid.GridH - 1, maxAz);

                        int gateFoot = 0, gateBunny = 0, gateProbed = 0, lines = 0;
                        for (int ax = minAx; ax <= maxAx; ax++)
                        {
                            for (int az = minAz; az <= maxAz; az++)
                            {
                                if (grid.Height[ax, az] < 2) continue; // no ground
                                long idx = (long)ax * grid.GridH + az;
                                byte f = flags[idx];
                                bool footOpen = (f & WorldmapGridFormat.CachedGrid.FlagFootBlocked) == 0;
                                bool bunnyOpen = (f & WorldmapGridFormat.CachedGrid.FlagBunnyBlocked) == 0;
                                if (!footOpen && !bunnyOpen) continue;

                                Vector3 w = grid.GridToWorld(ax, az);
                                float gridY = grid.GetHeightM(ax, az);
                                var r = WorldmapGridProbe.ProbeObstacles(w.x, w.z, gridY,
                                    footSolidMask, charaWallMask, bunnyMask);
                                gateProbed++;
                                byte add = 0;
                                if (footOpen && r.FootBlocked) { add |= WorldmapGridFormat.CachedGrid.FlagFootBlocked; gateFoot++; }
                                if (bunnyOpen && r.BunnyBlocked) { add |= WorldmapGridFormat.CachedGrid.FlagBunnyBlocked; gateBunny++; }
                                if (add == 0) continue;
                                flags[idx] = (byte)(f | add);
                                if (lines++ < MaxCellLinesPerGate)
                                    sb.AppendLine($"[GatePatch]   cell ({ax},{az}) w=({w.x:F1},{w.z:F1}) gridY={gridY:F2} " +
                                        $"now {(r.FootBlocked ? "foot-blocked " : "")}{(r.BunnyBlocked ? "bunny-blocked " : "")}by " +
                                        WorldmapGridProbe.DescribeCollider(r.NearestCollider) +
                                        $" (nearest {r.NearestDist:F2} m)");
                            }
                        }
                        cellsProbed += gateProbed; footSet += gateFoot; bunnySet += gateBunny;
                        sb.AppendLine($"[GatePatch] gate {mj.fieldmapID} at ({mj.transform.position.x:F0},{mj.transform.position.z:F0}): " +
                            $"{gateProbed} open cells probed, {gateFoot} foot + {gateBunny} bunny cells blocked by a wall.");
                    }
                }

                sb.AppendLine($"[GatePatch] {gates} gates, {cellsProbed} cells probed, {footSet} foot + {bunnySet} bunny cells " +
                    $"newly blocked, nothing cleared, no height changed; {sw.ElapsedMilliseconds} ms.");
                MelonLogger.Msg(sb.ToString());

                if (footSet + bunnySet == 0)
                {
                    ScreenReader.Say("Gate wall patch: no wall cell was open. The grid is unchanged.");
                    return;
                }

                string mapName = WorldmapFishingStands.MapName(fm.WorldmapID);
                if (mapName == null)
                {
                    MelonLogger.Msg($"[GatePatch] world map id {fm.WorldmapID} is not a known planet — not saved.");
                    ScreenReader.Say(Loc.Get("gridgen_no_map"));
                    return;
                }
                string path = WorldmapGridFormat.UserGridPath(mapName);
                WorldmapGridFormat.SaveGrid(path, grid.WorldMinX, grid.WorldMinZ, grid.CellSize,
                    grid.GridW, grid.GridH, grid.Height, flags, grid.ClearanceOffsets, grid.ClearanceValues,
                    grid.FootMask, grid.BunnyMask, grid.FootFloor, grid.BunnyFloor);
                MelonLogger.Msg($"[GatePatch] Saved to: {path} ({new FileInfo(path).Length} bytes). Grid cache cleared — regions rebuild on the next use.");
                WorldmapPathfinder.ClearCache();
                ScreenReader.Say($"Gate wall patch saved. {footSet} foot cells at {gates} gates are now blocked by their walls. " +
                    "Nothing was opened. Check log.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GatePatch] Error: {ex}");
                ScreenReader.Say("Gate wall patch failed. Check log.");
            }
        }
    }
}
