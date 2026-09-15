using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Phase 2 of the bake — the game's own stand test per shoreline candidate,
    /// tile by tile with the streamed collision loaded; the body-fit test that
    /// drops cells the walking body cannot stand in; the log-only flicker
    /// diagnostic; and the Phase 4 entrance-trigger ring rule. The candidate
    /// scan (Phase 1) and the stand selection (Phase 4) live in
    /// <c>WorldmapFishingStandBaker.Scan.cs</c>.
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        #region Phase 2 — the game's stand test

        /// <summary>
        /// Runs <c>CheckWorldmapFishingPoint</c> for every candidate, tile by tile with
        /// the tile's streamed collision instantiated (the test raycasts). Returns
        /// false (after speaking) when the chunk data is unavailable — the test would
        /// fail open far from the player and bake stands the game never accepts.
        /// </summary>
        private static bool VerifyCandidates(FieldManager fm, FieldPlayer player,
            WorldmapGridFormat.CachedGrid grid, Dictionary<int, List<Candidate>> byPlace,
            float frontDist, Vector3 playerPos)
        {
            int bodyMask = NavigationHandler.ResolveBodySweepMask(player, out string maskNote);
            Log($"Phase 2: stand test = game bubble check, then body fit ({maskNote}, " +
                $"clearance cap {NavigationHandler.BodyClearanceCap:F0} m).");
            var fit = new Dictionary<int, BodyFitStats>();
            var flicker = new FlickerStats();
            int cellsPerTile = (int)(TileSizeMeters / grid.CellSize);
            int tilesX = (grid.GridW + cellsPerTile - 1) / cellsPerTile;
            int tilesZ = (grid.GridH + cellsPerTile - 1) / cellsPerTile;
            var loader = WorldmapChunkLoader.TryCreate(grid.WorldMinX, grid.WorldMinZ,
                tilesX, tilesZ, TileSizeMeters, out string fail);
            if (loader == null)
            {
                MelonLoader.MelonLogger.Error(
                    $"[FishBake] ABORT: culling chunk data unavailable ({fail}). The game's stand " +
                    "test needs the streamed collision; refusing to bake fiction.");
                ScreenReader.Say("Fishing stand bake aborted. The game's terrain chunk data could not be read. Check log.");
                return false;
            }

            var byTile = new Dictionary<int, List<Candidate>>();
            foreach (var list in byPlace.Values)
                foreach (var c in list)
                {
                    int tile = (c.Ax / cellsPerTile) * tilesZ + (c.Az / cellsPerTile);
                    if (!byTile.TryGetValue(tile, out var tl)) byTile[tile] = tl = new List<Candidate>();
                    tl.Add(c);
                }

            int checks = 0, verifiedTotal = 0;
            try
            {
                foreach (var kv in byTile)
                {
                    int tx = kv.Key / tilesZ, tz = kv.Key % tilesZ;
                    int verified = 0;
                    try
                    {
                        loader.LoadTile(tx, tz);
                        foreach (var c in kv.Value)
                        {
                            bool first = TryVerify(fm, grid, c, frontDist, ref checks);
                            MeasureFlicker(fm, grid, c, first, frontDist, playerPos, flicker, ref checks);
                            if (!first) continue;
                            if (!fit.TryGetValue(c.PlaceId, out var stats))
                                fit[c.PlaceId] = stats = new BodyFitStats();
                            if (BodyFits(c, bodyMask, stats)) verified++;
                        }
                    }
                    finally
                    {
                        loader.UnloadTile();
                    }
                    verifiedTotal += verified;
                    var centre = grid.GridToWorld(tx * cellsPerTile + cellsPerTile / 2,
                        tz * cellsPerTile + cellsPerTile / 2);
                    float dist = Vector2.Distance(new Vector2(centre.x, centre.z),
                        new Vector2(playerPos.x, playerPos.z));
                    // Distance-tagged so a fail-open (all far tiles pass) or a
                    // fail-closed (all far tiles refuse) test shows in the log.
                    Log($"Phase 2: tile ({tx},{tz}) {dist:F0} m from player: " +
                        $"{verified}/{kv.Value.Count} verified.");
                }
            }
            finally
            {
                loader.Dispose();
            }

            foreach (var kv in byPlace)
            {
                fit.TryGetValue(kv.Key, out var stats);
                Log($"Phase 2: place {kv.Key}: {kv.Value.FindAll(c => c.Verified).Count}/{kv.Value.Count} verified" +
                    (stats == null ? " (no cell passed the bubble check)." : $"; {stats.Describe()}."));
            }
            Log($"Phase 2: {verifiedTotal} stands verified with {checks} game checks over {byTile.Count} tiles, " +
                $"{loader.InstantiationsTotal} chunk loads.");
            Log($"Phase 2 flicker: {flicker.Summary()}");
            return true;
        }

        /// <summary>
        /// Asks the game whether a stand at the candidate cell, facing the water,
        /// raises the fishing prompt. Tries the water direction first, then the
        /// compass directions nearest to it; a direction whose 5 m-ahead cell is
        /// walkable land is skipped. Copies are passed by ref — the native side
        /// may write them back.
        /// </summary>
        private static bool TryVerify(FieldManager fm, WorldmapGridFormat.CachedGrid grid,
            Candidate c, float frontDist, ref int checks)
        {
            if (!GameAcceptsStand(fm, grid, c, frontDist, ref checks, out Vector3 face)) return false;
            c.Face = face;
            c.Verified = true;
            return true;
        }

        /// <summary>The game's stand test without touching the candidate — the flicker diagnostic repeats it.</summary>
        private static bool GameAcceptsStand(FieldManager fm, WorldmapGridFormat.CachedGrid grid,
            Candidate c, float frontDist, ref int checks, out Vector3 face)
        {
            face = Vector3.zero;
            var dirs = new List<Vector3>(9) { c.TowardWater };
            var ordered = new List<Vector3>(Compass);
            ordered.Sort((a, b) =>
                Vector3.Dot(b, c.TowardWater).CompareTo(Vector3.Dot(a, c.TowardWater)));
            dirs.AddRange(ordered);

            for (int i = 0; i < dirs.Count; i++)
            {
                Vector3 dir = dirs[i];
                if (i > 0)
                {
                    Vector3 ahead = c.Pos + dir * frontDist;
                    grid.WorldToGrid(ahead.x, ahead.z, out int gx, out int gz);
                    if (grid.IsPassable(gx, gz, WorldmapGridFormat.CachedGrid.FlagFootBlocked))
                        continue; // land ahead, not water
                }
                Vector3 pos = c.Pos;
                Vector3 d = dir;
                bool ok;
                try
                {
                    checks++;
                    ok = fm.CheckWorldmapFishingPoint(ref pos, ref d);
                }
                catch (Exception ex)
                {
                    if (checks < 5) Log($"CheckWorldmapFishingPoint failed at ({c.Pos.x:F1},{c.Pos.z:F1}): {ex.Message}");
                    return false;
                }
                if (ok)
                {
                    face = dir;
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Phase 2 — flicker diagnostic (log only), body fit; Phase 4 — entrance-trigger rule

        /// <summary>Cells within this many metres of the player get the repeated check.</summary>
        private const float FlickerRadiusMeters = 20f;

        /// <summary>Extra runs of the game's stand test per near cell.</summary>
        private const int FlickerRepeats = 4;

        /// <summary>Most flickering cells logged one by one; the rest only count.</summary>
        private const int FlickerLogLines = 20;

        /// <summary>
        /// Log-only evidence for the 2026-09-13 observation that the game's stand
        /// test answers differently between bakes for a few cells near the bake
        /// position (place 1: 1448 → 1465 → 1448 passes; the Arlia pocket cell
        /// came and went with no rule involved). Repeats the test for every cell
        /// within <see cref="FlickerRadiusMeters"/> of the player and logs each
        /// cell whose answers disagree, with its distance from the player. The
        /// verdict is NOT changed — this only measures.
        /// </summary>
        private sealed class FlickerStats
        {
            public int Checked, Flickered, Logged;
            public float NearestFlicker = float.MaxValue, FarthestFlicker;

            public void Note(Candidate c, bool first, int passes, float distToPlayer)
            {
                Checked++;
                if (passes == (first ? FlickerRepeats : 0)) return;
                Flickered++;
                NearestFlicker = Mathf.Min(NearestFlicker, distToPlayer);
                FarthestFlicker = Mathf.Max(FarthestFlicker, distToPlayer);
                if (Logged++ < FlickerLogLines)
                    Log($"Phase 2 flicker: place {c.PlaceId} cell ({c.Pos.x:F1},{c.Pos.z:F1}) {distToPlayer:F1} m from the player: " +
                        $"first answer {(first ? "PASS" : "fail")}, then {passes}/{FlickerRepeats} passes.");
            }

            public string Summary() => Checked == 0
                ? $"no cells within {FlickerRadiusMeters:F0} m of the player."
                : $"{Checked} cells within {FlickerRadiusMeters:F0} m of the player re-checked {FlickerRepeats}× — " +
                  $"{Flickered} flickered" +
                  (Flickered > 0 ? $" (from {NearestFlicker:F1} m to {FarthestFlicker:F1} m away)" : "") + ".";
        }

        /// <summary>Repeats the game's stand test for a near cell and records any disagreement.</summary>
        private static void MeasureFlicker(FieldManager fm, WorldmapGridFormat.CachedGrid grid,
            Candidate c, bool first, float frontDist, Vector3 playerPos, FlickerStats stats, ref int checks)
        {
            float dx = c.Pos.x - playerPos.x, dz = c.Pos.z - playerPos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist > FlickerRadiusMeters) return;
            int passes = 0;
            for (int i = 0; i < FlickerRepeats; i++)
                if (GameAcceptsStand(fm, grid, c, frontDist, ref checks, out _)) passes++;
            stats.Note(c, first, passes, dist);
        }

        /// <summary>
        /// The second half of the stand test: can the player's body occupy the
        /// cell at all? The bubble check is a ray from the cell toward the water,
        /// so it passes from inside a town wall (2026-09-13, Arlia). A cell whose
        /// body capsule overlaps a wall collider is dropped with the blocker
        /// logged (first few per place); every kept cell records how far the body
        /// can move before a wall stops it, as evidence in the file and the log.
        /// </summary>
        private static bool BodyFits(Candidate c, int bodyMask, BodyFitStats stats)
        {
            float clearance;
            Collider blocker;
            try
            {
                clearance = NavigationHandler.BodyWallClearance(c.Pos, bodyMask, out blocker);
            }
            catch (Exception ex)
            {
                // A failed physics query is not evidence either way: keep the stand, mark it unmeasured.
                if (stats.Errors++ < 3)
                    Log($"Phase 2: body fit query failed at ({c.Pos.x:F1},{c.Pos.z:F1}): {ex.Message} — kept unmeasured.");
                return true;
            }
            c.WallClearance = clearance;
            stats.Count(clearance);
            if (clearance > 0f) return true;

            c.Verified = false;
            string why = $"body overlaps '{blocker?.name}' L{blocker?.gameObject.layer} " +
                $"({NavigationHandler.ColliderChain(blocker)})";
            _ruleDrops.Add((c.Pos, c.PlaceId, why));
            if (stats.Dropped <= 3)
                Log($"Phase 2: place {c.PlaceId} cell ({c.Pos.x:F1},{c.Pos.z:F1}) dropped — {why}.");
            return false;
        }

        /// <summary>
        /// Drops verified cells that lie INSIDE a town entrance trigger (the
        /// "Press Cross to Enter" zone): Cross enters the town there, so a bubble
        /// could never be used. Every kept cell records its ring distance as
        /// evidence. Without ring data (no map jumps found) nothing is dropped —
        /// an exclusion needs evidence, never a guess.
        /// </summary>
        private static void ApplyEntranceRingRule(List<Candidate> verified,
            List<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps, int placeId)
        {
            if (mapjumps == null || mapjumps.Count == 0) return;
            int dropped = 0;
            foreach (var c in verified)
            {
                float? d = WorldmapMapjumps.NearestRingDistance(mapjumps, c.Pos, out string label);
                if (d == null) continue;
                c.RingDistance = d.Value;
                if (d.Value > InsideRingMeters) continue;

                c.Verified = false;
                string why = $"inside the entrance trigger of {label} (Cross = Enter there)";
                _ruleDrops.Add((c.Pos, placeId, why));
                if (++dropped <= 3)
                    Log($"Phase 4: place {placeId} cell ({c.Pos.x:F1},{c.Pos.z:F1}) dropped — {why}.");
            }
            if (dropped > 0)
                Log($"Phase 4: place {placeId}: {dropped} verified cells dropped by the entrance-trigger rule.");
            verified.RemoveAll(c => !c.Verified);
        }

        /// <summary>A ring distance at or below this counts as "inside the trigger" (ClosestPoint returns the point itself).</summary>
        private const float InsideRingMeters = 0.01f;

        /// <summary>Per-place body-fit evidence for the Phase 2 log: dropped cells and a clearance histogram.</summary>
        private sealed class BodyFitStats
        {
            public int Dropped, Within1, Within2, Clear, Errors;

            public void Count(float clearance)
            {
                if (clearance <= 0f) Dropped++;
                else if (clearance < 1f) Within1++;
                else if (clearance < NavigationHandler.BodyClearanceCap) Within2++;
                else Clear++;
            }

            public string Describe() =>
                $"{Dropped} dropped (body inside a wall); wall clearance under 1 m: {Within1}, " +
                $"1–2 m: {Within2}, 2 m or more: {Clear}" + (Errors > 0 ? $", {Errors} unmeasured" : "");
        }

        #endregion
    }
}
