using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Debug-mode bake of the world map fishing stands (Insert key on the world
    /// map). Runs once per map, saves <c>UserData\SO2RAccess\stands\worldmap_*.json</c>,
    /// and the runtime (<see cref="WorldmapFishingStands"/>) only ever looks the
    /// result up — no shoreline scans or stand loops while playing.
    ///
    /// Phases (each timed in the log as <c>[FishBake]</c>):
    /// 1. Candidates — shoreline cells of the mod's walkability grid (a foot-passable
    ///    cell with a non-passable 4-neighbour) inside each water place's box plus a
    ///    margin, whose water neighbour the game's own world grid paints with a
    ///    fishing water place ID (<c>WorldGridData.FishingWaterPlaceID</c>).
    /// 2. Verify — the game's per-frame bubble test
    ///    <c>FieldManager.CheckWorldmapFishingPoint</c> from each candidate cell, with
    ///    the tile's streamed collision loaded (the test contains a physics ray).
    /// 3. Comfort regions — connected regions of cells at or above the comfort
    ///    clearance, so a stand's route class is known without an A*.
    /// 4. Select — the whole verified shoreline of each water place, thinned to one
    ///    stand every <see cref="StandSpacingMeters"/>; the runtime picks the one
    ///    nearest the player. Index 0 is the best-ranked stand (the proof's first pick).
    /// 5. Proof — a body-swept route from the nearest entrances (see the Proof partial).
    ///
    /// Synchronous: the game freezes for the duration (spoken beforehand).
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>Bake tile edge (m) for the streamed-chunk loader; same tiling idea as the grid bake.</summary>
        private const float TileSizeMeters = 64f;

        /// <summary>How far (m) outside a water place's box shoreline cells are still examined.</summary>
        private const float BoxMarginMeters = 16f;

        /// <summary>
        /// Per-place cap on candidates sent to the game test (coastal boxes are
        /// 1000 m+ across). Phase 2 costs ~30 µs per check, so a high cap is cheap;
        /// a low one drops the far shore of a coast and the list then names a stand
        /// much farther than the nearest fishable water (2026-09-09).
        /// </summary>
        private const int MaxCandidatesPerPlace = 8000;

        /// <summary>
        /// Minimum spacing (m) between the stands kept for one place. The runtime
        /// picks the stand nearest the player, so the whole verified shoreline is
        /// kept at this density (2026-09-09: six stands per lake left the Krosse
        /// shore by the gate without one while the player fished there).
        /// </summary>
        private const float StandSpacingMeters = 5f;

        /// <summary>Safety cap on stands kept per place (a 2 km coast at 5 m spacing).</summary>
        private const int MaxStandsPerPlace = 400;

        /// <summary>Documented value of the game's forward probe distance when the field is unreadable.</summary>
        private const float FallbackFrontDistance = 5f;

        /// <summary>A shoreline cell awaiting (or past) the game's stand test.</summary>
        private sealed class Candidate
        {
            public int Ax, Az;
            /// <summary>Painted water place ID of the adjacent water.</summary>
            public int PlaceId;
            /// <summary>Cell centre with the baked ground height.</summary>
            public Vector3 Pos;
            /// <summary>Unit XZ direction toward the painted water neighbour(s).</summary>
            public Vector3 TowardWater;
            public float DistToParamSq;
            /// <summary>Facing direction the game accepted (valid when <see cref="Verified"/>).</summary>
            public Vector3 Face;
            public bool Verified;
            /// <summary>Body wall clearance (m) at the cell, see <see cref="FishingStandEntry.WallClearance"/>.</summary>
            public float WallClearance = -1f;
            /// <summary>Distance (m) to the nearest entrance trigger, see <see cref="FishingStandEntry.RingDistance"/>.</summary>
            public float RingDistance = -1f;
        }

        /// <summary>
        /// Every cell a bake RULE removed after the game's bubble check had passed
        /// it, with the rule's reason — the audit trail the before/after comparison
        /// (<see cref="LogBakeDiff"/>) answers "why is this stand gone?" from.
        /// Cleared at the start of each bake.
        /// </summary>
        private static readonly List<(Vector3 pos, int placeId, string reason)> _ruleDrops =
            new List<(Vector3, int, string)>();

        /// <summary>
        /// Bakes the stands for the current world map and saves them. Refuses to
        /// run without the walkability grid, the game's world grid data or the
        /// streamed-chunk data — a bake without any of them would be fiction.
        /// </summary>
        public static void BakeAndSave()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say("Fishing stand bake only works on the world map.");
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
                Vector3 playerPos = player.transform.position;

                WorldmapID wmID = fm.WorldmapID;
                string mapName = WorldmapFishingStands.MapName(wmID);
                var grid = WorldmapPathfinder.GetCachedGrid(wmID);
                if (grid == null)
                {
                    ScreenReader.Say(
                        $"No walkability grid for the {mapName} world map. Bake it first with F9.");
                    return;
                }

                var pm = ParameterManager.Instance;
                var places = pm?.GetFishingWaterPlaceParameterList(fm.currentFieldmapID);
                if (places == null || places.Count == 0)
                {
                    ScreenReader.Say("No fishing water places in the parameter data for this map.");
                    return;
                }

                float frontDist = ReadFrontDistance();
                bool checkDisabled = false;
                try { checkDisabled = fm.IsFieldFlag(FieldBitFlag.DisableFishingCheck); }
                catch (Exception ex) { Log($"DisableFishingCheck flag unreadable: {ex.Message}"); }
                Log($"Starting {mapName}: {places.Count} water places, frontDist={frontDist:F1}, " +
                    $"DisableFishingCheck={checkDisabled}, player=({playerPos.x:F0},{playerPos.z:F0}).");

                ScreenReader.Say(
                    $"Baking {mapName} fishing stands: {places.Count} water places. " +
                    "This may take a few minutes and the game will freeze. Please wait.");

                var total = System.Diagnostics.Stopwatch.StartNew();
                _ruleDrops.Clear();

                // Anchors first: a bake whose proof starts from the wrong points is
                // worthless, and this check costs a second, not minutes.
                int sweepMask = NavigationHandler.ResolveBodySweepMask(player, out string sweepMaskNote);
                var anchors = CollectAnchors(fm.FieldmapID, grid, sweepMask);
                if (!AnchorsMatchKnownExits(wmID, fm.FieldmapID, anchors))
                {
                    Log("ABORT: the exit anchors do not match the logged exits — nothing baked, nothing saved.");
                    ScreenReader.Say("Fishing stand bake stopped. The exit anchors do not match the logged town exits. " +
                        "Nothing was saved. Check log.");
                    return;
                }
                var previous = WorldmapFishingStands.ReadUserFile(wmID);
                Log(previous == null
                    ? "No previous user-side stands file — the before/after comparison is skipped."
                    : $"Previous user-side file (baked {previous.BakedAt}) read for the before/after comparison.");

                // Phase 1 — shoreline candidates from the grid + the game's paint.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var byPlace = ScanCandidates(fm, grid, places);
                Log($"Phase 1 candidates: {CountAll(byPlace)} cells for {byPlace.Count} painted " +
                    $"place IDs in {sw.ElapsedMilliseconds} ms.");

                // Phase 2 — the game's own stand test, per tile with collision loaded.
                sw.Restart();
                if (!VerifyCandidates(fm, player, grid, byPlace, frontDist, playerPos)) return;
                Log($"Phase 2 verify done in {sw.ElapsedMilliseconds} ms.");

                // Phase 3 — comfort-tier connectivity (route class without an A*).
                sw.Restart();
                var comfortSizes = new List<int>();
                var comfortRegions = WorldmapPathfinder.BuildRegions(grid,
                    WorldmapGridFormat.CachedGrid.FlagFootBlocked, "comfort",
                    WorldmapPathfinder.PreferredMinClearance, comfortSizes);
                Log($"Phase 3 comfort regions: {comfortSizes.Count - 1} regions in {sw.ElapsedMilliseconds} ms" +
                    (comfortRegions == null ? " (FAILED — route class unknown for all stands)" : "") + ".");

                // Phase 4 — pick the designated stand and alternates per place.
                sw.Restart();
                var file = SelectStands(grid, places, byPlace, comfortRegions, comfortSizes,
                    wmID, frontDist);
                Log($"Phase 4 select done in {sw.ElapsedMilliseconds} ms.");

                // Phase 5 — route proof: a body-swept route from an entrance to
                // each stand, with the streamed rock collision loaded.
                sw.Restart();
                ProveStands(grid, file, anchors, sweepMask, sweepMaskNote);
                Log($"Phase 5 proof done in {sw.ElapsedMilliseconds} ms.");

                if (previous != null) LogBakeDiff(previous, file);

                if (!VerdictPasses(wmID, file, previous))
                {
                    string rejected = WorldmapFishingStands.SaveRejected(wmID, file);
                    Log($"Bake REJECTED by its own verdict — written to {rejected}; the stands file in use is unchanged, " +
                        $"total {total.ElapsedMilliseconds / 1000} s.");
                    ScreenReader.Say("Fishing stand bake rejected. It disagrees with the known fishing spots. " +
                        "The stands in use were not changed. Check log.");
                    return;
                }

                string path = WorldmapFishingStands.Save(wmID, file);
                WorldmapFishingStands.ClearCache();

                int withStands = file.Places.FindAll(p => p.Stands.Count > 0).Count;
                int without = file.Places.Count - withStands;
                int proven = file.Places.FindAll(p => p.HasProvenStand).Count;
                var missing = file.Places.FindAll(p => p.Stands.Count == 0)
                    .ConvertAll(p => p.WaterPlaceId.ToString());
                var unproven = file.Places.FindAll(p => p.ProofAttempted && !p.HasProvenStand)
                    .ConvertAll(p => p.WaterPlaceId.ToString());
                Log($"Saved {path}: {withStands} of {file.Places.Count} places have a stand, " +
                    $"{without} without [{string.Join(",", missing)}], " +
                    (file.ProofsBaked
                        ? $"{proven} with a proven route, unproven [{string.Join(",", unproven)}], "
                        : "route proofs NOT baked, ") +
                    $"total {total.ElapsedMilliseconds / 1000} s.");
                ScreenReader.Say(
                    $"Fishing stands saved. {withStands} of {file.Places.Count} water places " +
                    $"have a stand, {without} without. " +
                    (file.ProofsBaked
                        ? $"{proven} have a proven route from an entrance. "
                        : "Route proofs were not baked. ") +
                    $"Took {total.ElapsedMilliseconds / 1000} seconds. Check log.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[FishBake] Error: {ex}");
                ScreenReader.Say("Fishing stand bake failed. Check log.");
            }
        }

        /// <summary>The game's forward probe distance, or the documented fallback when unreadable.</summary>
        private static float ReadFrontDistance()
        {
            try
            {
                float v = FieldManager.worldmapFishingFrontDistance;
                if (v > 0.01f) return v;
                Log($"worldmapFishingFrontDistance read as {v}; using {FallbackFrontDistance}.");
            }
            catch (Exception ex)
            {
                Log($"worldmapFishingFrontDistance unreadable ({ex.Message}); using {FallbackFrontDistance}.");
            }
            return FallbackFrontDistance;
        }

        private static int CountAll(Dictionary<int, List<Candidate>> byPlace)
        {
            int n = 0;
            foreach (var list in byPlace.Values) n += list.Count;
            return n;
        }

        /// <summary>
        /// Before/after comparison against the previous user-side file, so a
        /// changed rule can be judged by what it took away: per place the old
        /// and new stand counts, and every old stand that has no new stand within
        /// the 5 m spacing ("GONE") or only a shifted neighbour ("moved"), each
        /// with the rule reason when a rule dropped that cell, else "no rule —
        /// game check or thinning". Old proven stands that are gone are the ones
        /// to read first: those were reachable before.
        /// </summary>
        private static void LogBakeDiff(FishingStandFile previous, FishingStandFile fresh)
        {
            float keepSq = StandSpacingMeters * StandSpacingMeters;
            int oldTotal = 0, newTotal = 0, kept = 0, moved = 0, gone = 0, goneProven = 0, added = 0, proofLost = 0, proofGained = 0;
            Log($"Bake diff: comparing with the previous file (baked {previous.BakedAt}); " +
                $"an old stand counts as kept when a new stand lies within {StandSpacingMeters:F0} m.");

            foreach (var oldPlace in previous.Places)
            {
                var newPlace = fresh.Places.Find(p => p.WaterPlaceId == oldPlace.WaterPlaceId);
                var newStands = newPlace?.Stands ?? new List<FishingStandEntry>();
                oldTotal += oldPlace.Stands.Count;
                int placeGone = 0, placeMoved = 0;

                foreach (var old in oldPlace.Stands)
                {
                    float nearestSq = float.MaxValue;
                    foreach (var s in newStands)
                    {
                        float dx = s.X - old.X, dz = s.Z - old.Z;
                        float dSq = dx * dx + dz * dz;
                        if (dSq < nearestSq) nearestSq = dSq;
                    }
                    if (nearestSq < 1f)
                    {
                        kept++;
                        // Same stand, different proof verdict: the list the user
                        // reads first when a proof rule changes (2026-09-13).
                        var same = newStands.Find(s =>
                            (s.X - old.X) * (s.X - old.X) + (s.Z - old.Z) * (s.Z - old.Z) < 1f);
                        if (same != null && old.Proven && !same.Proven)
                        {
                            proofLost++;
                            Log($"Bake diff: place {oldPlace.WaterPlaceId}: stand ({old.X:F1},{old.Z:F1}) PROOF LOST — " +
                                $"was proven from {old.ProvenFrom} ({old.ProofTier}, {old.ProofRouteMeters:F0} m), now unproven" +
                                (same.Enclosed ? " (ENCLOSED: the body cannot leave the stand's pocket)."
                                    : "; see this place's Phase 5 refusal lines for the wedge."));
                        }
                        else if (same != null && !old.Proven && same.Proven) proofGained++;
                        continue;
                    }

                    string reason = DropReasonNear(old.X, old.Z);
                    string proof = old.Proven ? $"PROVEN from {old.ProvenFrom}" : "unproven";
                    if (nearestSq <= keepSq)
                    {
                        moved++; placeMoved++;
                        // A move WITHOUT a rule reason is the interesting case: the
                        // game's own check answered differently this time (2026-09-13:
                        // the Arlia pocket cell vanished that way, not by a rule).
                        Log($"Bake diff: place {oldPlace.WaterPlaceId}: old stand ({old.X:F1},{old.Z:F1}) [{proof}" +
                            (old.WallClearance >= 0f ? $", wallClear {old.WallClearance:F2} m" : "") +
                            $"] moved — nearest new stand {Mathf.Sqrt(nearestSq):F1} m; " +
                            (reason != null ? $"the old cell was dropped: {reason}."
                                : "NO rule dropped the old cell (game check answered differently, or thinning picked a neighbour)."));
                        continue;
                    }
                    gone++; placeGone++;
                    if (old.Proven) goneProven++;
                    string nearest = nearestSq == float.MaxValue ? "no stand left in this place"
                        : $"nearest new stand {Mathf.Sqrt(nearestSq):F1} m";
                    Log($"Bake diff: place {oldPlace.WaterPlaceId}: old stand ({old.X:F1},{old.Z:F1}) [{proof}" +
                        (old.WallClearance >= 0f ? $", wallClear {old.WallClearance:F2} m" : "") +
                        $"] GONE — {nearest}; reason: " +
                        (reason ?? "no rule dropped this cell (game check changed or thinning picked elsewhere)") + ".");
                }

                foreach (var s in newStands)
                {
                    bool hadOld = oldPlace.Stands.Exists(o =>
                        (o.X - s.X) * (o.X - s.X) + (o.Z - s.Z) * (o.Z - s.Z) <= keepSq);
                    if (!hadOld) added++;
                }
                newTotal += newStands.Count;
                if (placeGone > 0 || placeMoved > 0 || oldPlace.Stands.Count != newStands.Count)
                    Log($"Bake diff: place {oldPlace.WaterPlaceId}: {oldPlace.Stands.Count} stands before " +
                        $"({(oldPlace.HasProvenStand ? "had" : "no")} proven), {newStands.Count} after " +
                        $"({(newPlace != null && newPlace.HasProvenStand ? "has" : "no")} proven); " +
                        $"{placeGone} gone, {placeMoved} moved.");
            }

            Log($"Bake diff: {oldTotal} stands before, {newTotal} after — {kept} kept in place, {moved} moved within " +
                $"{StandSpacingMeters:F0} m, {gone} GONE ({goneProven} of them were proven), {added} new; " +
                $"proof LOST on {proofLost} kept stands, gained on {proofGained}; rules dropped {_ruleDrops.Count} cells in total.");
        }

        /// <summary>The recorded rule reason for the dropped cell nearest a stand position (within 1 m), or null.</summary>
        private static string DropReasonNear(float x, float z)
        {
            string best = null;
            float bestSq = 1f;
            foreach (var (pos, _, reason) in _ruleDrops)
            {
                float dx = pos.x - x, dz = pos.z - z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestSq) { bestSq = dSq; best = reason; }
            }
            return best;
        }

        private static void Log(string message) =>
            MelonLoader.MelonLogger.Msg($"[FishBake] {message}");
    }
}
