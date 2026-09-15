using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Phase 1 and Phase 4 of the bake: the shoreline candidate scan and the
    /// designated-stand selection. The game's stand test (Phase 2) lives in
    /// <c>WorldmapFishingStandBaker.Verify.cs</c>; the orchestration and the
    /// phase order in <c>WorldmapFishingStandBaker.cs</c>.
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>4-neighbour offsets (x, z).</summary>
        private static readonly int[,] Four = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };

        /// <summary>8 compass directions (unit XZ), tried in order of closeness to the water direction.</summary>
        private static readonly Vector3[] Compass =
        {
            new Vector3(0, 0, 1), new Vector3(0.7071f, 0, 0.7071f), new Vector3(1, 0, 0),
            new Vector3(0.7071f, 0, -0.7071f), new Vector3(0, 0, -1), new Vector3(-0.7071f, 0, -0.7071f),
            new Vector3(-1, 0, 0), new Vector3(-0.7071f, 0, 0.7071f),
        };

        #region Phase 1 — candidates

        /// <summary>
        /// Finds every shoreline cell next to painted fishing water, grouped by the
        /// PAINTED water place ID (a box may overlap a neighbouring place's water).
        /// Grid arrays decide "shoreline"; the game's world grid is asked once per
        /// water neighbour (cached) and, when that is unpainted, once for the cell
        /// itself — the paint covers the shore band too.
        /// </summary>
        private static Dictionary<int, List<Candidate>> ScanCandidates(FieldManager fm,
            WorldmapGridFormat.CachedGrid grid,
            Il2CppSystem.Collections.Generic.List<ConstFishingWaterPlaceParameter> places)
        {
            var byPlace = new Dictionary<int, List<Candidate>>();
            var paramPos = new Dictionary<int, Vector3>();
            var boxes = new Dictionary<int, Bounds>();
            var scanned = new HashSet<long>();
            var paintCache = new Dictionary<long, byte>();
            int paintCalls = 0, shoreCells = 0, ownPaintHits = 0;
            int waterNoGround = 0, waterBlocked = 0;
            byte foot = WorldmapGridFormat.CachedGrid.FlagFootBlocked;

            // Every place's parameter position first: a box may overlap a
            // neighbouring place's water, and a candidate painted with THAT
            // place's ID must measure its distance to that place — not to the
            // box being scanned (2026-09-09: Krosse lake stands read 385 m).
            for (int i = 0; i < places.Count; i++)
            {
                var place = places[i];
                if (place == null) continue;
                paramPos[place.WaterPlaceID] = place.Position;
                boxes[place.WaterPlaceID] = new Bounds(place.Position, place.Size);
            }

            for (int i = 0; i < places.Count; i++)
            {
                var place = places[i];
                if (place == null) continue;
                var box = boxes[place.WaterPlaceID];

                grid.WorldToGrid(box.min.x - BoxMarginMeters, box.min.z - BoxMarginMeters,
                    out int ax0, out int az0);
                grid.WorldToGrid(box.max.x + BoxMarginMeters, box.max.z + BoxMarginMeters,
                    out int ax1, out int az1);
                ax0 = Math.Max(ax0, 1); az0 = Math.Max(az0, 1);
                ax1 = Math.Min(ax1, grid.GridW - 2); az1 = Math.Min(az1, grid.GridH - 2);

                for (int ax = ax0; ax <= ax1; ax++)
                {
                    for (int az = az0; az <= az1; az++)
                    {
                        long key = (long)ax * grid.GridH + az;
                        if (!scanned.Add(key)) continue;
                        if (!grid.IsPassable(ax, az, foot)) continue;

                        bool shore = false;
                        int paintedId = 0;
                        Vector3 toward = Vector3.zero;
                        Vector3 anyBlockedDir = Vector3.zero;
                        float cellY = grid.GetHeightM(ax, az);

                        for (int d = 0; d < 4; d++)
                        {
                            int nx = ax + Four[d, 0], nz = az + Four[d, 1];
                            if (grid.IsPassable(nx, nz, foot)) continue;
                            shore = true;
                            var dir = new Vector3(Four[d, 0], 0f, Four[d, 1]);
                            anyBlockedDir += dir;

                            float ny = grid.GetHeightM(nx, nz);
                            bool noGround = ny == float.MinValue;
                            int paint = PaintAt(fm, grid, nx, nz, noGround ? cellY : ny,
                                paintCache, ref paintCalls);
                            if (paint == 0) continue;
                            if (noGround) waterNoGround++; else waterBlocked++;
                            if (paintedId == 0) paintedId = paint;
                            if (paint == paintedId) toward += dir;
                        }
                        if (!shore) continue;
                        shoreCells++;

                        if (paintedId == 0)
                        {
                            paintedId = PaintAt(fm, grid, ax, az, cellY, paintCache, ref paintCalls);
                            if (paintedId == 0) continue;
                            ownPaintHits++;
                            toward = anyBlockedDir;
                        }
                        if (toward.sqrMagnitude < 0.01f) toward = anyBlockedDir;
                        if (toward.sqrMagnitude < 0.01f) continue;

                        var pos = grid.GridToWorld(ax, az);
                        pos.y = cellY;
                        Vector3 pp = paramPos.TryGetValue(paintedId, out var v) ? v : place.Position;
                        float dx = pos.x - pp.x, dz = pos.z - pp.z;
                        if (!byPlace.TryGetValue(paintedId, out var list))
                            byPlace[paintedId] = list = new List<Candidate>();
                        list.Add(new Candidate
                        {
                            Ax = ax, Az = az, PlaceId = paintedId, Pos = pos,
                            TowardWater = toward.normalized, DistToParamSq = dx * dx + dz * dz,
                        });
                    }
                }
            }

            Log($"Phase 1: {shoreCells} shoreline cells, {paintCalls} paint lookups, " +
                $"{ownPaintHits} matched by the cell's own paint, water neighbours: " +
                $"{waterNoGround} no-ground / {waterBlocked} blocked.");

            foreach (var id in new List<int>(byPlace.Keys))
            {
                var list = byPlace[id];
                int outside = 0;
                if (boxes.TryGetValue(id, out var box))
                    outside = list.FindAll(c => !box.Contains(new Vector3(c.Pos.x, box.center.y, c.Pos.z))).Count;
                string capped = "";
                if (list.Count > MaxCandidatesPerPlace)
                {
                    list.Sort((a, b) => a.DistToParamSq.CompareTo(b.DistToParamSq));
                    list.RemoveRange(MaxCandidatesPerPlace, list.Count - MaxCandidatesPerPlace);
                    capped = $", capped to {MaxCandidatesPerPlace} nearest the parameter position";
                }
                Log($"Phase 1: place {id}: {list.Count} candidates ({outside} outside its box" +
                    (boxes.ContainsKey(id) ? "" : ", ID NOT in this map's parameter list") + capped + ").");
            }
            return byPlace;
        }

        /// <summary>The game's painted fishing water place ID at a grid cell (0 = none), cached per cell.</summary>
        private static int PaintAt(FieldManager fm, WorldmapGridFormat.CachedGrid grid,
            int ax, int az, float y, Dictionary<long, byte> cache, ref int calls)
        {
            long key = (long)ax * grid.GridH + az;
            if (cache.TryGetValue(key, out byte known)) return known;
            byte paint = 0;
            try
            {
                var p = grid.GridToWorld(ax, az);
                p.y = y;
                calls++;
                var data = fm.GetWorldGridData(ref p);
                if (data != null) paint = data.FishingWaterPlaceID;
            }
            catch (Exception ex)
            {
                if (calls < 5) Log($"GetWorldGridData failed at cell ({ax},{az}): {ex.Message}");
            }
            cache[key] = paint;
            return paint;
        }

        #endregion

        #region Phase 4 — selection

        /// <summary>
        /// Builds the file: per water place the WHOLE verified shoreline, thinned
        /// to one stand per <see cref="StandSpacingMeters"/>. The verified cells
        /// are ranked by route class (largest comfort region first), then
        /// clearance, then closeness to the parameter position, and the thinning
        /// walks that order, so within each 5 m the comfortable cell wins and
        /// index 0 is the best-ranked stand of the place. Floor-tier cells are
        /// kept where no comfortable cell is near (the walk falls to the floor
        /// tier itself); a place with no comfortable stand at all is flagged.
        /// </summary>
        private static FishingStandFile SelectStands(WorldmapGridFormat.CachedGrid grid,
            Il2CppSystem.Collections.Generic.List<ConstFishingWaterPlaceParameter> places,
            Dictionary<int, List<Candidate>> byPlace, ushort[] comfortRegions,
            List<int> comfortSizes, WorldmapID wmID, float frontDist)
        {
            var file = new FishingStandFile
            {
                WorldmapId = wmID.ToString(),
                BakedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                FrontDistance = frontDist,
                ComfortClearance = WorldmapPathfinder.PreferredMinClearance,
                GridCellSize = grid.CellSize,
            };

            var mapjumps = WorldmapMapjumps.CollectAll();
            int ringCount = mapjumps.FindAll(m => m.rings.Count > 0).Count;
            Log(ringCount > 0
                ? $"Phase 4: entrance-trigger rule armed with {ringCount} entrance rings."
                : "Phase 4: NO entrance rings found — the entrance-trigger rule is skipped (nothing dropped without evidence).");

            for (int i = 0; i < places.Count; i++)
            {
                var place = places[i];
                if (place == null) continue;
                int id = place.WaterPlaceID;
                var entry = new FishingPlaceStands
                {
                    WaterPlaceId = id, ParamX = place.Position.x, ParamZ = place.Position.z,
                };
                if (byPlace.TryGetValue(id, out var candidates))
                {
                    entry.Candidates = candidates.Count;
                    var verified = candidates.FindAll(c => c.Verified);
                    ApplyEntranceRingRule(verified, mapjumps, id);
                    entry.Verified = verified.Count;
                    var ranked = new List<FishingStandEntry>(verified.Count);
                    foreach (var c in verified) ranked.Add(ToEntry(grid, c, comfortRegions, comfortSizes));

                    entry.FloorTierOnly = ranked.Count > 0
                        && !ranked.Exists(s => s.Clearance >= WorldmapPathfinder.PreferredMinClearance);
                    ranked.Sort(RankStand);
                    float spacingSq = StandSpacingMeters * StandSpacingMeters;
                    foreach (var s in ranked)
                    {
                        if (entry.Stands.Count >= MaxStandsPerPlace) break;
                        bool tooClose = entry.Stands.Exists(k =>
                            (k.X - s.X) * (k.X - s.X) + (k.Z - s.Z) * (k.Z - s.Z) < spacingSq);
                        if (!tooClose) entry.Stands.Add(s);
                    }
                }
                file.Places.Add(entry);

                if (entry.Stands.Count > 0)
                {
                    var d = entry.Stands[0];
                    Log($"Phase 4: place {id}: {entry.Stands.Count} stands kept of {entry.Verified} verified " +
                        $"({entry.Candidates} candidates, {StandSpacingMeters:F0} m spacing" +
                        (entry.Stands.Count >= MaxStandsPerPlace ? ", CAPPED" : "") + "); best " +
                        $"({d.X:F1},{d.Y:F1},{d.Z:F1}) facing ({d.FaceDX:F2},{d.FaceDZ:F2}) " +
                        $"clearance={d.Clearance:F2} comfortCells={d.ComfortCells} bunny={d.BunnyOk} " +
                        $"distToParam={d.DistToParam:F0}" +
                        (entry.FloorTierOnly ? " — FLOOR TIER ONLY (no stand at comfort clearance)" : "") + ".");
                }
                else
                {
                    Log($"Phase 4: place {id} at ({place.Position.x:F0},{place.Position.z:F0}): NO STAND " +
                        $"({entry.Verified}/{entry.Candidates} verified" +
                        (entry.Candidates == 0 ? ", no painted shoreline cells found" : ", the game refused every candidate") + ").");
                }
            }
            file.Places.Sort((a, b) => a.WaterPlaceId.CompareTo(b.WaterPlaceId));
            return file;
        }

        private static FishingStandEntry ToEntry(WorldmapGridFormat.CachedGrid grid, Candidate c,
            ushort[] comfortRegions, List<int> comfortSizes)
        {
            float clearance = grid.GetClearance(c.Ax, c.Az);
            int comfortCells = 0;
            if (comfortRegions != null)
            {
                int label = comfortRegions[(long)c.Ax * grid.GridH + c.Az];
                if (label > 0 && label < comfortSizes.Count) comfortCells = comfortSizes[label];
            }
            return new FishingStandEntry
            {
                X = c.Pos.x, Y = c.Pos.y, Z = c.Pos.z,
                FaceDX = c.Face.x, FaceDZ = c.Face.z,
                Clearance = clearance == float.MaxValue ? 999f : clearance,
                ComfortCells = comfortCells,
                BunnyOk = grid.IsPassable(c.Ax, c.Az, WorldmapGridFormat.CachedGrid.FlagBunnyBlocked),
                DistToParam = Mathf.Sqrt(c.DistToParamSq),
                WallClearance = c.WallClearance,
                RingDistance = c.RingDistance,
            };
        }

        /// <summary>Largest comfort region first, then widest clearance, then nearest the parameter position.</summary>
        private static int RankStand(FishingStandEntry a, FishingStandEntry b)
        {
            int byRegion = b.ComfortCells.CompareTo(a.ComfortCells);
            if (byRegion != 0) return byRegion;
            int byClearance = b.Clearance.CompareTo(a.Clearance);
            if (byClearance != 0) return byClearance;
            return a.DistToParam.CompareTo(b.DistToParam);
        }

        #endregion
    }
}
