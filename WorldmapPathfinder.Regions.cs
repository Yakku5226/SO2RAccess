using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    // Partial class fragment of WorldmapPathfinder: per-travel-mode
    // CONNECTED-REGION maps. Two cells share a region label if and only if
    // the mode's floor A* could route between them, so label equality is an
    // instant "any route at all?" test. Foot regions are built eagerly at
    // grid load (as always); bunny regions are built LAZILY on the first
    // bunny query (~75MB + ~1-2s, only paid when the user actually rides).
    // Region 0 always means "unknown — never reject" (fail open).
    public static partial class WorldmapPathfinder
    {
        /// <summary>
        /// True when the cached grid can answer reachability questions for
        /// the given travel mode. Foot needs any grid; bunny additionally
        /// needs the v2 (WMGI) bake with a real bunny lane. Psynard has no
        /// grid support by design — callers must treat everything as
        /// reachable while flying, never consult the grid.
        /// </summary>
        public static bool GridSupportsMode(WorldmapTravelMode mode)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return false;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return false;
                return mode switch
                {
                    WorldmapTravelMode.Foot => true,
                    WorldmapTravelMode.Bunny => grid.BunnyDataAvailable,
                    _ => false,
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM GridSupportsMode error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Connected-region id of the grid cell at a world position for the
        /// given travel mode, or 0 when unknown (no grid/regions cached, out
        /// of bounds, a cell not passable for the mode, or — for the bunny —
        /// a legacy grid without bunny data). Two positions with the same
        /// non-zero id are connected for that mode's A*. Used to pick an
        /// entrance point on the PLAYER'S side of a boundary town (e.g.
        /// Salva) and for the honest reachability annotations in the nav
        /// list. Callers must treat 0 as "unknown → reachable", never as
        /// "unreachable".
        /// </summary>
        public static int GetRegionId(Vector3 world,
            WorldmapTravelMode mode = WorldmapTravelMode.Foot)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return 0;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return 0;

                ushort[] regions = mode switch
                {
                    WorldmapTravelMode.Foot => grid.RegionsFoot,
                    WorldmapTravelMode.Bunny => grid.BunnyDataAvailable
                        ? EnsureBunnyRegions(grid)
                        : null,
                    _ => null, // psynard: no grid truth — unknown
                };
                if (regions == null) return 0;

                grid.WorldToGrid(world.x, world.z, out int ax, out int az);
                if (ax < 0 || ax >= grid.GridW || az < 0 || az >= grid.GridH)
                    return 0;
                return regions[(long)ax * grid.GridH + az];
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM GetRegionId error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Collects EVERY distinct connected-region id the player's start
        /// position can bridge onto, mirroring FindPath's real start
        /// semantics: the snapped start cell PLUS all cells inside the
        /// start-clearing disc (+1 cell rim), because the clearing converts
        /// blocked cells there to passable and can bridge the player out of
        /// a baked-obstacle pocket. Reachability verdicts MUST use this set,
        /// not the single start-cell region — a player wedged in a rocky
        /// pocket has a tiny pocket region on their exact cell while the A*
        /// happily walks them onto the mainland (the single-cell compare
        /// would falsely report everything unreachable, violating the hard
        /// rule). Empty result = unknown (no grid/regions for the mode) —
        /// callers must fail open.
        /// </summary>
        public static void GetStartRegionIds(Vector3 start,
            WorldmapTravelMode mode, List<int> results)
        {
            results.Clear();
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsExistWorldGridData()) return;
                var grid = GetCachedGrid(fm.WorldmapID);
                if (grid == null) return;
                var regions = GetRegionsForSearch(grid, mode);
                if (regions == null) return;

                int gridH = grid.GridH;
                grid.WorldToGrid(start.x, start.z, out int ax, out int az);
                ax = Mathf.Clamp(ax, 0, grid.GridW - 1);
                az = Mathf.Clamp(az, 0, gridH - 1);

                // The snapped start cell counts too (FindPath snaps a
                // non-passable start to the nearest passable cell, which
                // can lie outside the disc).
                byte modeBit = ModeSearchBit(grid, mode);
                int sx = ax, sz = az;
                if (!grid.IsPassable(sx, sz, modeBit))
                    SnapToPassable(ref sx, ref sz, grid, modeBit);
                if (grid.IsPassable(sx, sz, modeBit))
                {
                    int sr = regions[(long)sx * gridH + sz];
                    if (sr != 0) results.Add(sr);
                }

                int rim = StartClearRadiusCells + 1;
                for (int dx = -rim; dx <= rim; dx++)
                {
                    for (int dz = -rim; dz <= rim; dz++)
                    {
                        if (dx * dx + dz * dz > rim * rim) continue;
                        int nx = ax + dx;
                        int nz = az + dz;
                        if (nx < 0 || nx >= grid.GridW ||
                            nz < 0 || nz >= gridH) continue;
                        int r = regions[(long)nx * gridH + nz];
                        if (r != 0 && !results.Contains(r))
                            results.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                results.Clear(); // unknown — callers fail open
                DebugLogger.LogState(
                    $"NAV WM GetStartRegionIds error: {ex.Message}");
            }
        }

        /// <summary>
        /// Region map to use for a search's fast-reject, or null to skip the
        /// reject entirely (fail open): psynard always skips; bunny on a
        /// legacy grid uses the FOOT map (its search predicate IS the foot
        /// predicate there, so the foot map is sound for it).
        /// </summary>
        private static ushort[] GetRegionsForSearch(
            WorldmapGridFormat.CachedGrid grid, WorldmapTravelMode mode)
        {
            switch (mode)
            {
                case WorldmapTravelMode.Foot:
                    return grid.RegionsFoot;
                case WorldmapTravelMode.Bunny:
                    return grid.BunnyDataAvailable
                        ? EnsureBunnyRegions(grid)
                        : grid.RegionsFoot;
                default:
                    return null;
            }
        }

        /// <summary>Builds the FOOT region map at grid load.</summary>
        private static void BuildFootRegions(
            WorldmapGridFormat.CachedGrid grid)
        {
            grid.RegionsFoot = BuildRegions(grid,
                WorldmapGridFormat.CachedGrid.FlagFootBlocked, "foot");
        }

        /// <summary>
        /// Returns the bunny region map, building it on first use. Only
        /// called for grids with real bunny data. Returns null (fail open)
        /// if the build failed.
        /// </summary>
        private static ushort[] EnsureBunnyRegions(
            WorldmapGridFormat.CachedGrid grid)
        {
            if (grid.RegionsBunny == null && !grid.RegionsBunnyBuildFailed)
            {
                DebugLogger.LogState(
                    "NAV WM regions: first bunny query — building bunny " +
                    "region map (one-time).");
                grid.RegionsBunny = BuildRegions(grid,
                    WorldmapGridFormat.CachedGrid.FlagBunnyBlocked, "bunny");
                // A failed build (e.g. OOM) must not be retried on every
                // query — latch it and stay fail-open.
                grid.RegionsBunnyBuildFailed = grid.RegionsBunny == null;
            }
            return grid.RegionsBunny;
        }

        /// <summary>
        /// Labels every cell passable for the mode with a connected-region
        /// number using the exact same neighbor rule as the authoritative A*
        /// floor pass (8-directional, both cells passable for the mode,
        /// height step ≤ MaxClimbCm). Runs once per grid load / first bunny
        /// query (~1-2s); region 0 means unlabeled (no ground, blocked, or
        /// label overflow) and is always treated as "unknown — do not
        /// reject". Returns null on failure (fail open — no fast reject).
        /// </summary>
        private static ushort[] BuildRegions(
            WorldmapGridFormat.CachedGrid grid, byte modeBit, string modeName)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int gridW = grid.GridW;
                int gridH = grid.GridH;
                var height = grid.Height;
                var flags = grid.Flags;
                var regions = new ushort[(long)gridW * gridH];
                var queue = new Queue<int>();
                ushort nextLabel = 0;
                bool overflow = false;

                for (int ax = 0; ax < gridW && !overflow; ax++)
                {
                    for (int az = 0; az < gridH; az++)
                    {
                        if (height[ax, az] < 2) continue;
                        int rootIdx = ax * gridH + az;
                        if ((flags[rootIdx] & modeBit) != 0) continue;
                        if (regions[rootIdx] != 0) continue;

                        if (nextLabel == ushort.MaxValue)
                        {
                            overflow = true;
                            break;
                        }
                        nextLabel++;

                        regions[rootIdx] = nextLabel;
                        queue.Enqueue(rootIdx);
                        while (queue.Count > 0)
                        {
                            int idx = queue.Dequeue();
                            int cx = idx / gridH;
                            int cz = idx % gridH;
                            ushort ch = height[cx, cz];

                            for (int d = 0; d < 8; d++)
                            {
                                int nx = cx + Directions[d, 0];
                                int nz = cz + Directions[d, 1];
                                if (nx < 0 || nx >= gridW ||
                                    nz < 0 || nz >= gridH) continue;
                                int nIdx = nx * gridH + nz;
                                if (regions[nIdx] != 0) continue;
                                ushort nh = height[nx, nz];
                                if (nh < 2) continue;
                                if ((flags[nIdx] & modeBit) != 0) continue;
                                if (Math.Abs(ch - nh) > MaxClimbCm) continue;
                                regions[nIdx] = nextLabel;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                DebugLogger.LogState(
                    $"NAV WM regions ({modeName}): {nextLabel} connected " +
                    $"regions labeled in {sw.ElapsedMilliseconds}ms" +
                    (overflow
                        ? " (label overflow — remainder unlabeled)" : "") +
                    ".");
                return regions;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM regions ({modeName}) error: {ex.Message}");
                return null; // fail open — no fast reject
            }
        }

        /// <summary>
        /// True if the snapped start cell, or ANY base-grid cell within the
        /// start-clearing disc (+1 cell rim), belongs to
        /// <paramref name="targetRegion"/> in <paramref name="regions"/>.
        /// The start clearing converts blocked cells inside the disc to
        /// passable, which can bridge the player onto any region the disc
        /// touches — so all of them count as reachable start regions.
        /// </summary>
        private static bool StartTouchesRegion(
            WorldmapGridFormat.CachedGrid grid, ushort[] regions,
            int origAx, int origAz, int snapAx, int snapAz,
            ushort targetRegion)
        {
            int gridH = grid.GridH;

            ushort sr = regions[(long)snapAx * gridH + snapAz];
            if (sr == 0) return true; // unknown — fail open
            if (sr == targetRegion) return true;

            int rim = StartClearRadiusCells + 1;
            for (int dx = -rim; dx <= rim; dx++)
            {
                for (int dz = -rim; dz <= rim; dz++)
                {
                    if (dx * dx + dz * dz > rim * rim) continue;
                    int nx = origAx + dx;
                    int nz = origAz + dz;
                    if (nx < 0 || nx >= grid.GridW ||
                        nz < 0 || nz >= grid.GridH) continue;
                    if (regions[(long)nx * gridH + nz] == targetRegion)
                        return true;
                }
            }
            return false;
        }
    }
}
