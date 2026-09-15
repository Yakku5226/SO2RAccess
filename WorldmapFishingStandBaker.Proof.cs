using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Phase 5 of the bake — the ROUTE PROOF. The grid alone cannot tell a beach
    /// from a ledge: rock bodies (layer 24) are ground in the grid and any height
    /// step up to 5 m per cell counts as walkable, so every mainland stand shares
    /// one comfort region. This phase plans a real route to each stand from the
    /// nearest location entrances (the anchors a player actually starts from) and
    /// sweeps the player's body capsule along it with the streamed rock collision
    /// loaded — the same oracle the pre-walk validation and the F7 auditor trust.
    /// A stand is PROVEN when a whole route passes; unproven stands stay in the
    /// file as evidence and the runtime annotates a place with no proven stand as
    /// "unreachable on foot" (annotated, never hidden).
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>Stop proving a place once this many stands have a route.</summary>
        private const int MaxProvenStandsPerPlace = 2;

        /// <summary>
        /// Plan-and-sweep attempts per place (each is an A* plus a sweep). Enough
        /// for one first-pass attempt on every stand (up to 6) plus re-plans.
        /// </summary>
        private const int MaxProofAttemptsPerPlace = 12;

        /// <summary>Nearest entrances tried per stand.</summary>
        private const int MaxAnchorsPerStand = 3;

        /// <summary>Plan → sweep → stamp → re-plan rounds per anchor (the walk uses 2 + a final check).</summary>
        private const int MaxProofRounds = 3;

        /// <summary>
        /// Segments closer than this to the stand are exempt: the shoreline step
        /// itself. Deliberately NOT the walk's 16 m goal exemption (canyon mouths
        /// at town rings) — for a stand the last metres ARE the cliff question.
        /// </summary>
        private const float ProofGoalExemptMeters = 2f;

        /// <summary>Whole-phase time budget; places past it are left "proof unknown".</summary>
        private const float MaxProofSeconds = 300f;

        /// <summary>Radius (m) of the body-swept flood around a stand in the enclosure test.</summary>
        private const float EnclosureSearchMeters = 14f;

        /// <summary>
        /// A stand is enclosed when the body-swept flood cannot reach any cell
        /// this far (m) from it. The Arlia shore pocket behind the town wall is
        /// about 8 m long; an open shore passes at the first step beyond it.
        /// </summary>
        private const float EnclosureEscapeMeters = 12f;

        /// <summary>A location entrance the proof can start a route from.</summary>
        private sealed class Anchor
        {
            /// <summary>Destination fieldmap ID of the entrance, e.g. MF_0009_01A.</summary>
            public string Label;
            public Vector3 Position;
            /// <summary>Foot region of the entrance cell (0 = unknown, never used to skip).</summary>
            public int Region;
        }

        /// <summary>
        /// Runs the route proof for every place in the file (mutates the stand
        /// entries and re-ranks each place's stands: proven comfort, proven floor,
        /// unproven). Leaves <see cref="FishingStandFile.ProofsBaked"/> false when
        /// no anchors or no chunk data exist — the runtime then treats every stand
        /// as "proof unknown".
        /// </summary>
        private static void ProveStands(FieldPlayer player, WorldmapGridFormat.CachedGrid grid,
            FishingStandFile file)
        {
            var anchors = CollectAnchors();
            file.ProofAnchors = anchors.Count;
            if (anchors.Count == 0)
            {
                Log("Phase 5: no entrance anchors found (no ground-level map jump triggers) — " +
                    "route proofs skipped, every stand stays 'proof unknown'.");
                return;
            }

            int cellsPerTile = (int)(TileSizeMeters / grid.CellSize);
            int tilesX = (grid.GridW + cellsPerTile - 1) / cellsPerTile;
            int tilesZ = (grid.GridH + cellsPerTile - 1) / cellsPerTile;
            var loader = WorldmapChunkLoader.TryCreate(grid.WorldMinX, grid.WorldMinZ,
                tilesX, tilesZ, TileSizeMeters, out string fail);
            if (loader == null)
            {
                Log($"Phase 5: culling chunk data unavailable ({fail}) — route proofs skipped, " +
                    "every stand stays 'proof unknown'.");
                return;
            }

            int mask = NavigationHandler.ResolveBodySweepMask(player, out string maskNote);
            Log($"Phase 5: {anchors.Count} entrance anchors; sweep {maskNote}; start exemption " +
                $"{NavigationHandler.WmSweepEndpointExemptDist:F0} m (start-side wedges still swept and counted), goal exemption " +
                $"{ProofGoalExemptMeters:F0} m; a stand whose route hid a start-side wedge must pass the enclosure test " +
                $"(body flood must reach {EnclosureEscapeMeters:F0} m within {EnclosureSearchMeters:F0} m).");
            foreach (var a in anchors)
                Log($"Phase 5: anchor {a.Label} at ({a.Position.x:F0},{a.Position.y:F0},{a.Position.z:F0}) foot region {a.Region}.");
            file.ProofsBaked = true;
            LogWallCensus();

            var budget = System.Diagnostics.Stopwatch.StartNew();
            int attemptedPlaces = 0, provenPlaces = 0, budgetSkipped = 0;
            try
            {
                foreach (var place in file.Places)
                {
                    if (place.Stands.Count == 0) continue;
                    if (budget.Elapsed.TotalSeconds > MaxProofSeconds)
                    {
                        budgetSkipped++;
                        Log($"Phase 5: place {place.WaterPlaceId}: proof SKIPPED (time budget " +
                            $"{MaxProofSeconds:F0} s spent) — stays 'proof unknown'.");
                        continue;
                    }
                    ProvePlace(place, anchors, grid, loader, cellsPerTile, tilesX, tilesZ, mask);
                    attemptedPlaces++;
                    if (place.ProvenStands > 0) provenPlaces++;
                }
            }
            finally
            {
                loader.Dispose();
            }

            Log($"Phase 5: {provenPlaces} of {attemptedPlaces} places have a proven stand" +
                (budgetSkipped > 0 ? $", {budgetSkipped} skipped by the time budget" : "") +
                $", {budget.ElapsedMilliseconds} ms.");
        }

        /// <summary>The scanned map jumps of the running bake (ring distances in the wedge evidence lines).</summary>
        private static List<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> _proofMapjumps;

        /// <summary>Every map jump with a ground-level trigger ring, labelled by its destination fieldmap.</summary>
        private static List<Anchor> CollectAnchors()
        {
            var anchors = new List<Anchor>();
            _proofMapjumps = WorldmapMapjumps.CollectAll();
            foreach (var (fieldmapID, position, rings) in _proofMapjumps)
            {
                if (rings.Count == 0) continue;
                anchors.Add(new Anchor
                {
                    Label = fieldmapID.ToString(),
                    Position = position,
                    Region = WorldmapPathfinder.GetRegionId(position, WorldmapTravelMode.Foot),
                });
            }
            return anchors;
        }

        /// <summary>
        /// The proof's progress on one stand: which of its nearest anchors is
        /// being tried, the round within that anchor, and the wedge stamps
        /// collected so far for the re-plan.
        /// </summary>
        private sealed class StandProof
        {
            public int Index;
            public FishingStandEntry Stand;
            public List<Anchor> Anchors = new List<Anchor>();
            public int AnchorIndex;
            public int Round = 1;
            public List<Vector3> Blocked = new List<Vector3>();
            public bool SkipComfort;
            public bool Exhausted => AnchorIndex >= Anchors.Count;
            /// <summary>Flat distance (m) from the stand to its nearest anchor — the first-pass order.</summary>
            public float NearestAnchorMeters;
        }

        /// <summary>
        /// Proves the stands of one place within the per-place budget, then
        /// re-ranks them. Scheduling matters more than the budget size (the
        /// 2026-09-08 bake spent all eight attempts on the grid-ranked stand of
        /// the Arlia lake and never tried the stand 42 m from the Arlia gate):
        /// stands closest to an entrance go first, EVERY stand gets one attempt
        /// from its nearest anchor before any stand gets a second, and only then
        /// are re-plan rounds and farther anchors spent on the unproven ones.
        /// </summary>
        private static void ProvePlace(FishingPlaceStands place, List<Anchor> anchors,
            WorldmapGridFormat.CachedGrid grid, WorldmapChunkLoader loader,
            int cellsPerTile, int tilesX, int tilesZ, int mask)
        {
            place.ProofAttempted = true;
            place.ProofAttempts = 0;
            place.ProvenStands = 0;

            var proofs = new List<StandProof>();
            for (int i = 0; i < place.Stands.Count; i++)
            {
                var p = PrepareStandProof(place, i, anchors);
                if (p != null) proofs.Add(p);
            }
            proofs.Sort((a, b) => a.NearestAnchorMeters.CompareTo(b.NearestAnchorMeters));

            // Pass 1: one attempt per stand from its nearest anchor.
            foreach (var p in proofs)
            {
                if (place.ProvenStands >= MaxProvenStandsPerPlace
                    || place.ProofAttempts >= MaxProofAttemptsPerPlace) break;
                if (ProofAttempt(place, p, grid, loader, cellsPerTile, tilesX, tilesZ, mask))
                    place.ProvenStands++;
            }

            // Pass 2: further rounds and anchors for the unproven stands, nearest first.
            bool progressed = true;
            while (progressed && place.ProvenStands < MaxProvenStandsPerPlace
                && place.ProofAttempts < MaxProofAttemptsPerPlace)
            {
                progressed = false;
                foreach (var p in proofs)
                {
                    if (p.Stand.Proven || p.Exhausted) continue;
                    if (place.ProvenStands >= MaxProvenStandsPerPlace
                        || place.ProofAttempts >= MaxProofAttemptsPerPlace) break;
                    progressed = true;
                    if (ProofAttempt(place, p, grid, loader, cellsPerTile, tilesX, tilesZ, mask))
                        place.ProvenStands++;
                }
            }

            place.Stands.Sort(RankProvenStand);
            var d = place.Stands[0];
            Log($"Phase 5: place {place.WaterPlaceId}: {place.ProvenStands} proven of " +
                $"{place.Stands.Count} stands in {place.ProofAttempts} attempts; designated " +
                $"({d.X:F1},{d.Z:F1}) " +
                (d.Proven ? $"proven from {d.ProvenFrom} ({d.ProofTier}, {d.ProofRouteMeters:F0} m)" : "UNPROVEN") + ".");
        }

        /// <summary>
        /// The nearest anchors for one stand (same foot region, or unknown), or
        /// null (logged) when no anchor shares its region.
        /// </summary>
        private static StandProof PrepareStandProof(FishingPlaceStands place, int standIndex,
            List<Anchor> anchors)
        {
            var stand = place.Stands[standIndex];
            Vector3 standPos = stand.Position;
            int standRegion = WorldmapPathfinder.GetRegionId(standPos, WorldmapTravelMode.Foot);

            var candidates = anchors.FindAll(a =>
                a.Region == 0 || standRegion == 0 || a.Region == standRegion);
            candidates.Sort((a, b) =>
                FlatDistanceSq(a.Position, standPos).CompareTo(FlatDistanceSq(b.Position, standPos)));
            if (candidates.Count > MaxAnchorsPerStand)
                candidates.RemoveRange(MaxAnchorsPerStand, candidates.Count - MaxAnchorsPerStand);
            if (candidates.Count == 0)
            {
                Log($"Phase 5: place {place.WaterPlaceId} stand {standIndex} ({standPos.x:F1},{standPos.z:F1}): " +
                    $"no anchor shares its foot region {standRegion} — unproven.");
                return null;
            }
            return new StandProof
            {
                Index = standIndex,
                Stand = stand,
                Anchors = candidates,
                NearestAnchorMeters = Mathf.Sqrt(FlatDistanceSq(candidates[0].Position, standPos)),
            };
        }

        /// <summary>
        /// One plan-and-sweep attempt for a stand: the current anchor and round.
        /// Advances the state (next round after a refusal, next anchor after a
        /// missing grid route or the last round). Returns true when the route
        /// passed and the proof fields are filled.
        /// </summary>
        private static bool ProofAttempt(FishingPlaceStands place, StandProof p,
            WorldmapGridFormat.CachedGrid grid, WorldmapChunkLoader loader,
            int cellsPerTile, int tilesX, int tilesZ, int mask)
        {
            var stand = p.Stand;
            Vector3 standPos = stand.Position;
            var anchor = p.Anchors[p.AnchorIndex];
            int round = p.Round;
            string where = $"place {place.WaterPlaceId} stand {p.Index} ({standPos.x:F1},{standPos.z:F1})";
            string from = $"{anchor.Label} ({anchor.Position.x:F0},{anchor.Position.z:F0})";
            place.ProofAttempts++;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var path = WorldmapPathfinder.FindPath(anchor.Position, standPos,
                WorldmapTravelMode.Foot, p.Blocked.Count > 0 ? p.Blocked : null, p.SkipComfort);
            if (path == null || path.Length < 2)
            {
                Log($"Phase 5: {where} from {from} round {round}: no grid route" +
                    (WorldmapPathfinder.LastNoPathWasDisconnected ? " (disconnected)" : "") +
                    $" in {sw.ElapsedMilliseconds} ms.");
                NextAnchor(p);
                return false;
            }
            bool floor = WorldmapPathfinder.LastPathUsedFloorTier;
            // Stamps only remove cells: a comfort pass that already fell to the
            // floor cannot succeed on a re-plan (same rule as the walk).
            p.SkipComfort = floor;

            int tiles = LoadRouteTiles(loader, path, grid, cellsPerTile, tilesX, tilesZ);
            int wedges, forgiven, swept, hiddenStart;
            string firstWedge, firstHidden;
            string enclosed = null;
            try
            {
                wedges = SweepRoute(path, anchor.Position, standPos, mask, p.Blocked,
                    out firstWedge, out forgiven, out swept, out hiddenStart, out firstHidden);
                if (wedges == 0 && hiddenStart > 0)
                {
                    // The start exemption hid a wall on this route: a real gate
                    // pinch, or a stand walled in beside the gate (Arlia). Only
                    // the body can tell — can it leave the stand at all?
                    tiles += LoadRouteTiles(loader, EnclosureBox(standPos), grid, cellsPerTile, tilesX, tilesZ);
                    if (IsEnclosed(standPos, grid, mask, out float farthest, out int cells, out int sweeps, out string walls))
                        enclosed = $"the body cannot get farther than {farthest:F1} m from the stand " +
                            $"({cells} cells reachable, {sweeps} sweeps; walled by {walls}); " +
                            $"start-side wedge hidden by the exemption: {firstHidden}";
                }
            }
            finally
            {
                loader.UnloadTile();
            }
            if (enclosed != null)
            {
                stand.Enclosed = true;
                Log($"Phase 5: {where} from {from}: refused — ENCLOSED: {enclosed}; {sw.ElapsedMilliseconds} ms.");
                p.AnchorIndex = p.Anchors.Count; // enclosure does not depend on the anchor
                return false;
            }
            string gateNote = forgiven > 0
                ? $", {forgiven} gate pinch segments forgiven (within {WorldmapMapjumps.RingWedgeMeters:F0} m of an entrance ring)"
                : "";

            if (wedges == 0)
            {
                stand.ProvenFrom = anchor.Label;
                stand.ProofTier = floor ? "floor" : "comfort";
                stand.ProofRouteMeters = RouteLength(path);
                stand.ProofRounds = round;
                // Honesty: a route entirely inside the start/goal exemptions
                // sweeps nothing — say so, so a "proof" that rests on the grid
                // alone is visible in the log (2026-09-13: the Arlia pocket).
                string sweptNote = swept == 0
                    ? " — PROVEN BY GRID ONLY (no segment swept)"
                    : $", swept {swept} of {path.Length - 1} segments" +
                      (hiddenStart > 0 ? $", {hiddenStart} start-side wedges exempt (enclosure test passed)" : "");
                Log($"Phase 5: {where} from {from}: PROVEN {stand.ProofTier} {stand.ProofRouteMeters:F0} m, " +
                    $"{path.Length} waypoints, round {round}, {tiles} tiles{gateNote}{sweptNote}, {sw.ElapsedMilliseconds} ms.");
                return true;
            }
            Log($"Phase 5: {where} from {from} round {round}: refused — {wedges} wedges on the " +
                (floor ? "floor" : "comfort") + $"-tier route{gateNote}, first {firstWedge}; " +
                $"{sw.ElapsedMilliseconds} ms.");
            if (round >= MaxProofRounds) NextAnchor(p);
            else p.Round++;
            return false;
        }

        /// <summary>Moves a stand's proof to its next anchor with fresh re-plan state.</summary>
        private static void NextAnchor(StandProof p)
        {
            p.AnchorIndex++;
            p.Round = 1;
            p.Blocked = new List<Vector3>();
            p.SkipComfort = false;
        }
        /// <summary>Proven comfort route, then proven floor route, then unproven; ties by the Phase 4 rank.</summary>
        private static int RankProvenStand(FishingStandEntry a, FishingStandEntry b)
        {
            int byProof = ProofClass(a).CompareTo(ProofClass(b));
            return byProof != 0 ? byProof : RankStand(a, b);
        }

        private static int ProofClass(FishingStandEntry s) =>
            !s.Proven ? 2 : s.ProofTier == "floor" ? 1 : 0;

        private static float RouteLength(Vector3[] path)
        {
            float total = 0f;
            for (int i = 1; i < path.Length; i++)
                total += Mathf.Sqrt(FlatDistanceSq(path[i - 1], path[i]));
            return total;
        }

        private static float FlatDistanceSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
