using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: world-map PATHFINDING —
    // safe-approach/exit point computation and the A* path builder
    // (WorldmapCalculateAndStorePath), all aware of the current travel mode
    // (foot/bunny/psynard). The per-frame walk loop that consumes these
    // paths lives in NavigationHandler.Worldmap.cs; the reachability
    // verdicts for the nav list live in
    // NavigationHandler.Worldmap.Reachability.cs.
    public partial class NavigationHandler
    {
        #region World Map Pathfinding

        /// <summary>
        /// Number of waypoints to pre-validate against real L22 obstacles
        /// before the player starts walking. Covers ~15m of path at 0.5m spacing.
        /// </summary>
        private const int WmPreValidateCount = 30;

        /// <summary>
        /// Maximum pre-validation rounds before accepting the best path found.
        /// Each round detects blocked waypoints and re-pathfinds around them.
        /// </summary>
        private const int WmPreValidateMaxRounds = 10;

        /// <summary>
        /// Radius for OverlapSphere when pre-validating waypoints.
        /// Slightly larger than player collision (0.50m) to account for
        /// physics contact offset and ensure clearance.
        /// </summary>
        private const float WmPreValidateRadius = 0.55f;

        /// <summary>
        /// Layer mask for L22 (Col_Obstacle) — the physical rocks/obstacles
        /// that block world map movement. Used for pre-validation only.
        /// </summary>
        private static readonly int WmObstacleLayerMask = 1 << 22;

        /// <summary>
        /// Pre-walk sweep: wedges closer than this to either route endpoint
        /// (start or goal ring point) are exempt. Town-gate and canyon-mouth
        /// pinches live at endpoints and the walk survives them in practice
        /// (slow-follow + enter-prompt arrival, proven at Salva and Krosse
        /// Cave; the player standing at a gate demonstrably traversed it).
        /// 16m is data-grounded (2026-07-11 Marze↔Krosse Cave false-refusal
        /// logs): farthest observed goal-side pinch 14.0m from the ring
        /// point, farthest start-side pinch 6m from the player — while
        /// genuinely unwalkable routes (Mountain Palace, Arlia) wedge 44m+
        /// from any endpoint. Do not raise this without route-audit data.
        /// </summary>
        private const float WmSweepEndpointExemptDist = 16f;

        /// <summary>
        /// Maximum distance (meters) at which a straight-line fallback is
        /// still used when the grid pathfinder finds no path. Close-range
        /// failures are usually grid-snap artifacts; beyond this, "no path"
        /// means the route genuinely doesn't exist and auto-walk must report
        /// unreachable instead of grinding the player into obstacles.
        /// </summary>
        private const float WmStraightLineFallbackMaxDist = 15f;

        /// <summary>
        /// Resolves the world-map auto-walk destination for a location: the nearest
        /// navigable point on that location's enter-trigger RING — its
        /// FieldMapjumpCollision trigger collider, the same volume that raises the
        /// "Press X to enter" prompt. The ring is navigable; the location's model
        /// wall is not. Targeting the ring (not the centre point, and not a guessed
        /// hole through the model) lets the pathfinder keep the whole model impassable
        /// and still deliver the player to exactly where they can enter.
        /// Returns <paramref name="locationPos"/> unchanged if no trigger is found.
        /// </summary>
        private Vector3 ComputeEnterTriggerTarget(Vector3 locationPos, Vector3 playerPos)
        {
            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                {
                    DebugLogger.LogState(
                        "NAV WM enter-trigger: no FieldMapjumpCollision found.");
                    return locationPos;
                }

                // Nearest mapjump to the location, and its destination fieldmap.
                FieldMapjumpCollision nearest = null;
                float nearestDist = float.MaxValue;
                FieldmapID nearestFieldmapID = default;
                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    float d = Vector3.Distance(c.transform.position, locationPos);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = c;
                        nearestFieldmapID = c.fieldmapID;
                    }
                }
                if (nearest == null) return locationPos;

                // Collect ALL ground-level entrance trigger colliders for that
                // destination (small Y extent = road entrances, not town-wide
                // detection volumes). Boundary towns like Salva have SEVERAL
                // entrances facing different regions (Krosse side vs Arlia
                // valley side) — sampling only one can target the wrong side
                // of the town entirely. Fall back to any trigger on the nearest.
                var rings = new List<Collider>();
                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null || c.fieldmapID != nearestFieldmapID) continue;
                    var cols = c.GetComponents<Collider>();
                    if (cols == null) continue;
                    for (int k = 0; k < cols.Length; k++)
                    {
                        var col = cols[k];
                        if (col == null || !col.isTrigger) continue;
                        if (col.bounds.size.y > 20f) continue; // skip town-wide zones
                        rings.Add(col);
                    }
                }
                if (rings.Count == 0)
                {
                    var cols = nearest.GetComponents<Collider>();
                    if (cols != null)
                        for (int k = 0; k < cols.Length; k++)
                            if (cols[k] != null && cols[k].isTrigger)
                            { rings.Add(cols[k]); break; }
                }

                Vector3 dest;
                if (rings.Count > 0)
                {
                    // The triggers hug the model wall, so raw points often land
                    // on baked-obstacle cells (→ "no path") or on a walkable
                    // cell CONNECTED TO THE WRONG REGION (Salva's valley-side
                    // sliver — the "unreachable Salva from Krosse" bug).
                    // Sample all entrances for a walkable cell, preferring one
                    // in the SAME connected region as the player — walkability
                    // and regions both judged for the CURRENT travel mode.
                    dest = PickReachableRingPoint(rings, playerPos, locationPos,
                        WorldmapTravel.CurrentMode(), out bool usedCenter);
                    if (usedCenter)
                        DebugLogger.LogState(
                            "NAV WM enter-trigger: no walkable cell on any " +
                            "entrance trigger; routing to the location centre " +
                            "instead (arrival still waits for this location's " +
                            "enter prompt).");
                }
                else
                {
                    dest = nearest.transform.position;
                }

                float h = GameUtility.CalcHeight(
                    new Vector3(dest.x, 150f, dest.z), out bool ok, 300f);
                if (ok) dest.y = h;

                DebugLogger.LogState(
                    $"NAV WM enter-trigger: routing to ring point " +
                    $"({dest.x:F1},{dest.z:F1}) for location " +
                    $"({locationPos.x:F1},{locationPos.z:F1}) fieldmap={nearestFieldmapID}");
                return dest;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM enter-trigger error: {ex.Message}");
                return locationPos;
            }
        }

        /// <summary>
        /// Returns a point on one of the location's entrance triggers that sits
        /// on a grid cell WALKABLE for the given travel mode (the road into
        /// the location). Candidates in the SAME connected region as the
        /// player win over merely-walkable ones: boundary towns (Salva) have
        /// walkable cells at BOTH entrances, and a cell on the far side's
        /// region is a guaranteed "no route" — that was the "unreachable
        /// Salva from Krosse" bug. Within a tier the nearest candidate to the
        /// player wins. Candidate points come from the shared
        /// <see cref="ForEachRingCandidate"/> sampler (also used by the
        /// nav-list reachability check, so the two never disagree). If
        /// nothing tests walkable (triggers fully buried in the model wall,
        /// or no grid cached), sets <paramref name="usedCenter"/> and returns
        /// the location centre so the caller can route there instead — the
        /// enter prompt still gates arrival.
        /// </summary>
        private Vector3 PickReachableRingPoint(List<Collider> rings,
            Vector3 playerPos, Vector3 locationPos, WorldmapTravelMode mode,
            out bool usedCenter)
        {
            usedCenter = false;

            // Player side = the START REGION SET (same disc bridging as
            // FindPath), not just the exact cell — a player in a rocky
            // pocket still gets tier-1 candidates on the mainland.
            var playerRegions = new List<int>();
            WorldmapPathfinder.GetStartRegionIds(playerPos, mode, playerRegions);

            // Tier 1: walkable AND in one of the player's start regions.
            Vector3 bestConn = Vector3.zero;
            float bestConnSq = float.MaxValue;
            // Tier 2: walkable (region unknown or different) — old behavior.
            Vector3 bestWalk = Vector3.zero;
            float bestWalkSq = float.MaxValue;

            ForEachRingCandidate(rings, playerPos, cand =>
            {
                if (!WorldmapPathfinder.IsWalkableWorld(cand, mode))
                    return false;
                float d = (cand - playerPos).sqrMagnitude;
                if (d < bestWalkSq) { bestWalkSq = d; bestWalk = cand; }
                if (playerRegions.Count > 0 &&
                    playerRegions.Contains(
                        WorldmapPathfinder.GetRegionId(cand, mode)) &&
                    d < bestConnSq)
                {
                    bestConnSq = d;
                    bestConn = cand;
                }
                return false; // never stop early — we want the NEAREST
            });

            string playerSet = string.Join(",", playerRegions);
            if (bestConnSq < float.MaxValue)
            {
                DebugLogger.LogState(
                    $"NAV WM enter-trigger: connected ring point at " +
                    $"({bestConn.x:F1},{bestConn.z:F1}), " +
                    $"{Mathf.Sqrt(bestConnSq):F1}m from player " +
                    $"({mode}, player start regions [{playerSet}], " +
                    $"{rings.Count} triggers).");
                return bestConn;
            }

            if (bestWalkSq < float.MaxValue)
            {
                DebugLogger.LogState(
                    $"NAV WM enter-trigger: no candidate in player start " +
                    $"regions [{playerSet}] ({mode}); using nearest walkable " +
                    $"ring point at ({bestWalk.x:F1},{bestWalk.z:F1}), " +
                    $"{Mathf.Sqrt(bestWalkSq):F1}m from player " +
                    $"({rings.Count} triggers). If no route exists the " +
                    $"pathfinder will reject it honestly.");
                return bestWalk;
            }

            usedCenter = true;
            return locationPos;
        }

        /// <summary>
        /// A game-verified standing point for a fishing spot: where to stop
        /// and what water point to face so the game raises its fishing prompt.
        /// </summary>
        private struct FishingStand
        {
            /// <summary>Walkable shore cell to stand on.</summary>
            public Vector3 Stand;
            /// <summary>Water point to face on arrival.</summary>
            public Vector3 Face;
            /// <summary>Squared distance from the player when computed.</summary>
            public float DistSq;
        }

        /// <summary>
        /// Maximum verified stands the auto-walk will attempt routes to
        /// before announcing "no walkable route". Bounds worst-case planning
        /// time (a refused floor-tier route can cost several seconds).
        /// </summary>
        private const int MaxFishingStandAttempts = 3;

        /// <summary>
        /// Perimeter sampling step for a fishing spot's water box: fine
        /// enough for small ponds, capped for the huge coastal boxes (some
        /// are 1000m+ across).
        /// </summary>
        private static float WaterBoxEdgeStep(Bounds waterBox)
        {
            float perimeter = 2f * ((waterBox.max.x - waterBox.min.x)
                + (waterBox.max.z - waterBox.min.z));
            return Mathf.Clamp(perimeter / 64f, 2f, 12f);
        }

        /// <summary>
        /// Visits points along a water box's edge at <see cref="WaterBoxEdgeStep"/>
        /// spacing, passing each edge point and its outward normal.
        /// </summary>
        private static void ForEachWaterBoxEdgePoint(Bounds waterBox,
            Action<Vector3, Vector3> visit)
        {
            float minX = waterBox.min.x, maxX = waterBox.max.x;
            float minZ = waterBox.min.z, maxZ = waterBox.max.z;
            float waterY = waterBox.center.y;
            float step = WaterBoxEdgeStep(waterBox);

            for (float x = minX; x <= maxX; x += step)
            {
                visit(new Vector3(x, waterY, minZ), new Vector3(0, 0, -1));
                visit(new Vector3(x, waterY, maxZ), new Vector3(0, 0, 1));
            }
            for (float z = minZ; z <= maxZ; z += step)
            {
                visit(new Vector3(minX, waterY, z), new Vector3(-1, 0, 0));
                visit(new Vector3(maxX, waterY, z), new Vector3(1, 0, 0));
            }
        }

        /// <summary>
        /// Shore snaps that wander farther than this from their edge sample
        /// belong to another shore and are discarded — so the walkable-cell
        /// search is also capped here. The old uncapped (~50m) search froze
        /// the list build: ocean-facing samples on remote coastal boxes ran
        /// the full failed search per sample, only for the result to be
        /// rejected by this very distance check.
        /// </summary>
        private const float ShoreSnapMaxMeters = 6f;

        /// <summary>
        /// Visits each DISTINCT walkable shore cell around a water box: every
        /// edge point is nudged 1.5m outside the water, snapped to the
        /// nearest cell walkable for the travel mode, and passed to
        /// <paramref name="visit"/> once (snaps that wander more than
        /// <see cref="ShoreSnapMaxMeters"/> belong to another shore and are
        /// skipped). Shared by the walk-time stand search and the list
        /// build's same-side shore pick. Returns (edge samples, distinct
        /// walkable cells) for honest logging.
        /// </summary>
        private static (int sampled, int walkable) ForEachWaterBoxShoreCell(
            Bounds waterBox, WorldmapTravelMode mode, Action<Vector3> visit)
        {
            int sampled = 0, walkable = 0;
            float waterY = waterBox.center.y;
            var seenCells = new HashSet<(int, int)>();

            ForEachWaterBoxEdgePoint(waterBox, (e, outward) =>
            {
                sampled++;

                Vector3 s = e + outward * 1.5f;
                s.y = waterY;
                if (!WorldmapPathfinder.TryGetNearestWalkableWorld(
                        s, mode, out Vector3 cell, ShoreSnapMaxMeters))
                    return;
                float snapDx = cell.x - s.x, snapDz = cell.z - s.z;
                if (snapDx * snapDx + snapDz * snapDz >
                    ShoreSnapMaxMeters * ShoreSnapMaxMeters) return;

                var cellKey = (Mathf.RoundToInt(cell.x * 2f),
                               Mathf.RoundToInt(cell.z * 2f));
                if (!seenCells.Add(cellKey)) return;
                walkable++;

                visit(cell);
            });

            return (sampled, walkable);
        }

        /// <summary>
        /// Finds ALL standing points on a fishing spot's water box perimeter
        /// that the GAME confirms are fishable, on the player's side of the
        /// water, sorted nearest-first and thinned so retries approach from
        /// genuinely different directions. The list's coarse target (nearest
        /// walkable cell to the box center) can land on a cliff bank or the
        /// far shore where the prompt never fires; this samples the box edge,
        /// snaps each sample to a walkable cell, rejects cells in a different
        /// connected region than the player (opposite bank — proven by
        /// Fishing spot 3 picking the far side of a river), and asks the
        /// game's own water probe (IsWorldmapFishingPoint) whether a point in
        /// front of that cell — at the game's own worldmapFishingFrontDistance
        /// — is fishable water. Returning MULTIPLE candidates lets the caller
        /// fall back to the next stand when the route sweep refuses the
        /// nearest one (proven by Fishing spot 1: the nearest stand sat
        /// behind a rock while a reachable stand existed 40m further).
        /// Empty result = nothing verified; the caller keeps the coarse
        /// target, so this can never remove a walkable target.
        /// </summary>
        private List<FishingStand> ComputeWorldmapFishingStands(
            Bounds waterBox, Vector3 playerPos)
        {
            var candidates = new List<FishingStand>();

            var fm = FieldManager.Instance;
            if (fm == null) return candidates;
            var mode = WorldmapTravel.CurrentMode();

            // The game's own forward probe distance (how far ahead of the
            // player it looks for fishable water when deciding to prompt).
            float frontDist = 0f;
            try { frontDist = FieldManager.worldmapFishingFrontDistance; }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM fishing: worldmapFishingFrontDistance read " +
                    $"failed: {ex.Message}");
            }
            if (frontDist <= 0.01f) frontDist = 3f;

            float minX = waterBox.min.x, maxX = waterBox.max.x;
            float minZ = waterBox.min.z, maxZ = waterBox.max.z;
            float waterY = waterBox.center.y;

            // Player's connected regions, for rejecting far-bank stands.
            // Empty = unknown → no region filtering (fail open).
            var startRegions = new List<int>();
            WorldmapPathfinder.GetStartRegionIds(playerPos, mode, startRegions);

            int regionRejected = 0, verified = 0;

            var counts = ForEachWaterBoxShoreCell(waterBox, mode, stand =>
            {
                // Opposite-bank reject: a stand in a different connected
                // region than the player has no overland route by
                // definition. Region 0 = unknown → keep (fail open).
                if (startRegions.Count > 0)
                {
                    int standRegion = WorldmapPathfinder.GetRegionId(stand, mode);
                    if (standRegion != 0 && !startRegions.Contains(standRegion))
                    {
                        regionRejected++;
                        return;
                    }
                }

                // Water point in front of the stand (toward the box).
                float wx = Mathf.Clamp(stand.x, minX, maxX);
                float wz = Mathf.Clamp(stand.z, minZ, maxZ);
                Vector3 toWater = new Vector3(wx - stand.x, 0f, wz - stand.z);
                if (toWater.sqrMagnitude < 0.01f) return; // inside the box?
                Vector3 dir = toWater.normalized;

                // Game-truth probes at the game's own front distance, tried
                // at stand height and water height (the native check has a
                // height tolerance we don't want to re-implement).
                Vector3 probe = stand + dir * frontDist;
                bool fishable = false;
                try
                {
                    Vector3 pA = probe;
                    fishable = fm.IsWorldmapFishingPoint(pA, out _);
                    if (!fishable)
                    {
                        Vector3 pB = new Vector3(probe.x, waterY, probe.z);
                        fishable = fm.IsWorldmapFishingPoint(pB, out _);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"NAV WM fishing: probe failed: {ex.Message}");
                    return;
                }
                if (!fishable) return;

                verified++;
                candidates.Add(new FishingStand
                {
                    Stand = stand,
                    Face = new Vector3(wx, waterY, wz) + dir * frontDist,
                    DistSq = (stand - playerPos).sqrMagnitude,
                });
            });

            candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

            // Thin to stands at least an endpoint-exemption apart: a stand
            // inside that ring of a refused one shares its blocked approach
            // (the sweep exempts segments closer than this), so retrying it
            // would fail identically and waste a planning round.
            var spaced = new List<FishingStand>();
            float minSepSq = WmSweepEndpointExemptDist * WmSweepEndpointExemptDist;
            foreach (var c in candidates)
            {
                bool tooClose = spaced.Exists(k =>
                {
                    float dx = k.Stand.x - c.Stand.x;
                    float dz = k.Stand.z - c.Stand.z;
                    return dx * dx + dz * dz < minSepSq;
                });
                if (!tooClose) spaced.Add(c);
            }

            if (spaced.Count > 0)
            {
                var near = spaced[0];
                DebugLogger.LogState(
                    $"NAV WM fishing: {spaced.Count} verified stands " +
                    $"(from {verified} fishable of {counts.walkable} walkable " +
                    $"of {counts.sampled} edge samples, {regionRejected} on " +
                    $"the far bank, frontDist={frontDist:F1}). Nearest at " +
                    $"({near.Stand.x:F1},{near.Stand.y:F1},{near.Stand.z:F1}), " +
                    $"{Mathf.Sqrt(near.DistSq):F1}m from player.");
            }
            else
            {
                DebugLogger.LogState(
                    $"NAV WM fishing: NO game-verified stand on the " +
                    $"player's side ({counts.sampled} edge samples, " +
                    $"{counts.walkable} walkable, {regionRejected} far bank, " +
                    $"{verified} fishable, frontDist={frontDist:F1}) — using " +
                    $"the coarse shore point; prompt may need manual repositioning.");
            }
            return spaced;
        }

        /// <summary>
        /// Computes a safe exit point when the player is STARTING near a town:
        /// a point ~25m away in the direction AWAY from the nearest trigger
        /// (toward open terrain), so the A* leaves the town's wall ring cleanly
        /// before routing to the target. Checks both L22 obstacles and L23 CharaWalls.
        /// </summary>
        private Vector3 ComputeSafeExitPoint(Vector3 playerPos)
        {
            const float SafeDistance = 25f;
            int bothLayerMask = (1 << 22) | (1 << 23);

            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                    return playerPos;

                // Find the nearest ground-level trigger to the player.
                FieldMapjumpCollision nearestGround = null;
                float nearestGroundDist = float.MaxValue;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    var col = c.GetComponent<Collider>();
                    if (col == null || col.bounds.size.y > 20f) continue;
                    float dist = Vector3.Distance(
                        c.transform.position, playerPos);
                    if (dist < nearestGroundDist)
                    {
                        nearestGroundDist = dist;
                        nearestGround = c;
                    }
                }

                if (nearestGround == null || nearestGroundDist > 30f)
                    return playerPos; // Not near a town.

                Vector3 triggerCenter = nearestGround.transform.position;

                // Direction outward: from trigger center through player
                // position and beyond (away from the town).
                Vector3 outward = playerPos - triggerCenter;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f)
                    outward = Vector3.forward;
                outward.Normalize();

                // Try outward direction first, then 8 directions.
                Vector3[] directions = new Vector3[9];
                directions[0] = outward;
                for (int d = 0; d < 8; d++)
                {
                    float angle = d * 45f * Mathf.Deg2Rad;
                    directions[d + 1] = new Vector3(
                        Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                }

                // A candidate must be FLAT OPEN TERRAIN THE PLAYER CAN WALK
                // TO. The old checks (has ground + no walls within 2m) also
                // passed elevated rock plateaus: a top 6m above the player is
                // open, but the leg to it threads body-width gaps up the
                // rocks and physically wedges (D1 Salva failure, 2026-07-10).
                int playerRegion = WorldmapPathfinder.GetRegionId(playerPos);
                int rejHeight = 0, rejWalls = 0, rejGrid = 0, rejGround = 0;

                foreach (var dir in directions)
                {
                    Vector3 candidate = playerPos + dir * SafeDistance;

                    float h = GameUtility.CalcHeight(
                        candidate, out bool hasGround, 50f);
                    if (!hasGround) { rejGround++; continue; }
                    candidate.y = h;

                    // Same level as the player — an exit point is supposed to
                    // be the open field next to town, never a ledge above or
                    // a pit below.
                    if (Mathf.Abs(h - playerPos.y) > 2f) { rejHeight++; continue; }

                    // Check no L22 OR L23 obstacles — we want wide open terrain.
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            candidate, 2.0f, bothLayerMask);
                        bool blocked = false;
                        if (hits != null)
                        {
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    blocked = true;
                                    break;
                                }
                            }
                        }
                        if (blocked) { rejWalls++; continue; }
                    }
                    catch { rejWalls++; continue; }

                    // Grid sanity (fail open: unknown regions never reject):
                    // the cell must be walkable and in the player's region.
                    if (!WorldmapPathfinder.IsWalkableWorld(candidate))
                    { rejGrid++; continue; }
                    int candRegion = WorldmapPathfinder.GetRegionId(candidate);
                    if (playerRegion != 0 && candRegion != 0 &&
                        candRegion != playerRegion)
                    { rejGrid++; continue; }

                    DebugLogger.LogState(
                        $"NAV WM safe exit: found at ({candidate.x:F1}," +
                        $"{candidate.z:F1}) {SafeDistance:F0}m from player " +
                        $"(y={h:F1}, rejected before it: {rejGround} no-ground, " +
                        $"{rejHeight} height, {rejWalls} walls, {rejGrid} grid)");
                    return candidate;
                }

                DebugLogger.LogState(
                    "NAV WM safe exit: no valid exit point " +
                    $"({rejGround} no-ground, {rejHeight} height, " +
                    $"{rejWalls} walls, {rejGrid} grid) — going direct.");
                return playerPos;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM safe exit error: {ex.Message}");
                return playerPos;
            }
        }

        /// <summary>
        /// Calculates a world map path using the CalcHeight-based A* pathfinder.
        /// When starting near a town, routes to a safe exit point first.
        /// When targeting a location, routes to a safe approach point.
        /// Falls back to a single-waypoint straight line if pathfinding fails.
        /// </summary>
        /// <summary>The exact goal the current world-map path was computed
        /// for (the entrance RING POINT for locations, not the town-centre
        /// symbol). Stuck-recalcs and battle resumes MUST re-plan to this —
        /// re-planning to the centre sends every re-route INTO the town
        /// walls (proven by goal-cell logs, 2026-07-10).</summary>
        private Vector3 _wmPathGoal;

        /// <summary>
        /// True when the route ACCEPTED by the last successful
        /// <see cref="WorldmapCalculateAndStorePath"/> used the floor tier
        /// (0.50m clearance) — i.e. it threads body-width pinches. Read by the
        /// fishing stand selection to prefer stands with comfort-tier routes.
        /// (WorldmapPathfinder.LastPathUsedFloorTier reflects only the LAST
        /// FindPath call, which may be a dropped safe-exit leg or re-plan.)
        /// </summary>
        private bool _wmLastRouteFloorTier;

        private bool WorldmapCalculateAndStorePath(Vector3 playerPos, Vector3 targetPos,
            bool keepBlockedPositions = false, bool skipComfortTier = false)
        {
            _wmPathGoal = targetPos;
            _wmLastRouteFloorTier = false;
            _wmRecalcCount = 0;
            if (!keepBlockedPositions)
                _wmBlockedPositions.Clear();

            // The travel mode is queried at every path computation (walk
            // start, battle resume, mid-walk recalcs) so mounting or
            // dismounting between calls automatically re-plans on the right
            // per-mode grid lane.
            var mode = WorldmapTravel.CurrentMode();

            // For locations, targetPos is already the enter-trigger ring point
            // (resolved by ComputeEnterTriggerTarget when the walk started), so we
            // route straight to it — the model stays fully impassable in the grid.

            // If starting near a town, compute a safe exit point first.
            // Route: player → safe exit → target. This prevents the A* from
            // cutting through the town's obstacle ring or CharaWall boundary.
            Vector3 safeExit = ComputeSafeExitPoint(playerPos);
            bool usingSafeExit = Vector3.Distance(safeExit, playerPos) > 5f;

            // If using a safe exit, compute two-part path:
            // player → safe exit → target.
            Vector3 aStarStart = playerPos;
            Vector3[] exitPath = null;
            if (usingSafeExit)
            {
                DebugLogger.LogState(
                    $"NAV WM safe exit: routing via ({safeExit.x:F1}," +
                    $"{safeExit.z:F1}) before heading to target");
                exitPath = WorldmapPathfinder.FindPath(
                    playerPos, safeExit, mode, _wmBlockedPositions);
                if (exitPath != null && exitPath.Length > 0)
                {
                    // A floor-tier exit leg threads body-width gaps; sweep it
                    // and drop the safe exit rather than wedge on the way to
                    // it (the exit is an optimization, never required).
                    if (WorldmapPathfinder.LastPathUsedFloorTier &&
                        CountRouteWedges(exitPath, safeExit, 0f,
                            markBlocked: false) > 0)
                    {
                        DebugLogger.LogState(
                            "NAV WM safe exit: floor-tier exit leg is " +
                            "physically blocked (body sweep) — going direct.");
                        exitPath = null;
                        usingSafeExit = false;
                    }
                    else
                    {
                        aStarStart = safeExit;
                    }
                }
                else
                {
                    DebugLogger.LogState(
                        "NAV WM safe exit: no path to exit point, going direct.");
                    exitPath = null;
                    usingSafeExit = false;
                }
            }

            Vector3[] bestPath = null;
            bool bestPathFloorTier = false;
            int bestFirstBlockedIdx = -1;

            for (int round = 0; round < WmPreValidateMaxRounds; round++)
            {
                var path = WorldmapPathfinder.FindPath(aStarStart, targetPos,
                    mode,
                    _wmBlockedPositions.Count > 0 ? _wmBlockedPositions : null,
                    skipComfortTier: skipComfortTier);
                bool pathFloorTier = WorldmapPathfinder.LastPathUsedFloorTier;

                if (path == null || path.Length == 0)
                {
                    DebugLogger.LogState(
                        $"NAV WM pre-validate round {round}: no path found.");
                    break;
                }

                // Check the first N waypoints for L22 obstacles.
                int checkCount = Math.Min(WmPreValidateCount, path.Length);
                int firstBlockedIdx = -1;
                int totalBlocked = 0;

                for (int i = 0; i < checkCount; i++)
                {
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            path[i], WmPreValidateRadius, WmObstacleLayerMask);
                        if (hits != null && hits.Length > 0)
                        {
                            // Check if any are real non-trigger L22 obstacles.
                            bool hasRealObstacle = false;
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    hasRealObstacle = true;
                                    break;
                                }
                            }
                            if (hasRealObstacle)
                            {
                                totalBlocked++;
                                if (firstBlockedIdx < 0) firstBlockedIdx = i;
                            }
                        }
                    }
                    catch { }
                }

                // Track best path (one with obstacles furthest from start).
                if (firstBlockedIdx < 0)
                {
                    // Clean path — no L22 obstacles in first N waypoints.
                    bestPath = path;
                    bestPathFloorTier = pathFloorTier;
                    bestFirstBlockedIdx = -1;
                    DebugLogger.LogState(
                        $"NAV WM pre-validate round {round}: CLEAR. " +
                        $"{path.Length} waypoints, checked {checkCount}.");
                    break;
                }

                // This path has obstacles. Keep it if it's better than previous.
                if (bestPath == null || firstBlockedIdx > bestFirstBlockedIdx)
                {
                    bestPath = path;
                    bestPathFloorTier = pathFloorTier;
                    bestFirstBlockedIdx = firstBlockedIdx;
                }

                DebugLogger.LogState(
                    $"NAV WM pre-validate round {round}: {totalBlocked} blocked " +
                    $"waypoints in first {checkCount}. First blocked at wp[{firstBlockedIdx}] " +
                    $"({path[firstBlockedIdx].x:F1},{path[firstBlockedIdx].y:F1}," +
                    $"{path[firstBlockedIdx].z:F1}). Marking and re-pathfinding.");

                // Mark blocked waypoints so A* avoids them next round.
                for (int i = 0; i < checkCount; i++)
                {
                    try
                    {
                        var hits = UnityEngine.Physics.OverlapSphere(
                            path[i], WmPreValidateRadius, WmObstacleLayerMask);
                        if (hits != null)
                        {
                            foreach (var col in hits)
                            {
                                if (col != null && !col.isTrigger)
                                {
                                    _wmBlockedPositions.Add(path[i]);
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // PRE-WALK PHYSICS VALIDATION (foot only): sweep the body
            // capsule along the planned route BEFORE walking. Floor-tier
            // routes (0.50m clearance) thread body-width gaps and were the
            // proven wedge factories (Arlia route: 27 impassable segments),
            // but Mountain Palace showed comfort-tier routes wedge too: the
            // grid believes the Lasgus rock passages are wide enough while
            // the game's collision physics disagree. So EVERY foot route is
            // swept: impassable segments become blocked zones and the route
            // is re-planned around them; when no physically passable route
            // survives, refuse honestly instead of wedging through 5
            // stuck-recalcs. Segments near EITHER endpoint are exempt (see
            // WmSweepEndpointExemptDist: gate/canyon-mouth pinches sit there
            // and the walk survives them via slow-follow + prompt arrival —
            // marking a start-side gate pinch would seal the player in and
            // refuse routes they physically just walked, proven at Marze).
            if (bestPath != null && mode == WorldmapTravelMode.Foot)
            {
                for (int round = 0; round < 2 && bestPath != null; round++)
                {
                    int wedges = CountRouteWedges(
                        bestPath, targetPos, WmSweepEndpointExemptDist,
                        markBlocked: true,
                        startExemptDist: WmSweepEndpointExemptDist);
                    if (wedges == 0) break;

                    DebugLogger.LogState(
                        $"NAV WM route sweep round {round}: {wedges} " +
                        "physically impassable segments on " +
                        (bestPathFloorTier ? "floor" : "comfort") +
                        "-tier route — re-planning around them.");
                    // If this walk's plan already fell back to the floor tier,
                    // skip the comfort pass on the re-plan: wedge stamps only
                    // REMOVE passable cells, so the comfort tier that just
                    // failed cannot succeed now — repeating it burned ~1.2s
                    // per round (2026-08-29 log: 7s refusal, half of it spent
                    // re-failing the comfort tier).
                    bestPath = WorldmapPathfinder.FindPath(aStarStart,
                        targetPos, mode,
                        _wmBlockedPositions.Count > 0 ? _wmBlockedPositions : null,
                        skipComfortTier: bestPathFloorTier);
                    bestPathFloorTier = WorldmapPathfinder.LastPathUsedFloorTier;
                }

                if (bestPath != null &&
                    CountRouteWedges(bestPath, targetPos,
                        WmSweepEndpointExemptDist, markBlocked: false,
                        startExemptDist: WmSweepEndpointExemptDist) > 0)
                {
                    DebugLogger.LogState(
                        "NAV WM route sweep: no physically passable " +
                        (bestPathFloorTier ? "floor" : "comfort") +
                        "-tier route after re-planning — refusing honestly.");
                    WorldmapPathfinder.LastNoPathWasDisconnected = true;
                    bestPath = null;
                }
            }

            if (bestPath != null && bestPath.Length > 0)
            {
                _wmLastRouteFloorTier = bestPathFloorTier;

                // Concatenate exit path + main path if using safe exit.
                if (usingSafeExit && exitPath != null && exitPath.Length > 0)
                {
                    var combined = new Vector3[exitPath.Length + bestPath.Length];
                    exitPath.CopyTo(combined, 0);
                    bestPath.CopyTo(combined, exitPath.Length);
                    _wmPathWaypoints = combined;
                    DebugLogger.LogState(
                        $"NAV WM: combined path: {exitPath.Length} exit + " +
                        $"{bestPath.Length} main = {combined.Length} total waypoints");
                }
                else
                {
                    _wmPathWaypoints = bestPath;
                }
                _wmPathIndex = 0;

                // Diagnostic: log first 10 waypoints to verify path direction.
                int logCount = Math.Min(bestPath.Length, 10);
                DebugLogger.LogState(
                    $"NAV WM PATH: {bestPath.Length} waypoints. " +
                    $"Start=({playerPos.x:F1},{playerPos.z:F1}) " +
                    $"Target=({targetPos.x:F1},{targetPos.z:F1})");
                for (int i = 0; i < logCount; i++)
                {
                    DebugLogger.LogState(
                        $"NAV WM PATH wp[{i}]=({bestPath[i].x:F1},{bestPath[i].y:F1},{bestPath[i].z:F1})");
                }
                if (bestPath.Length > 10)
                {
                    DebugLogger.LogState(
                        $"NAV WM PATH wp[last]=({bestPath[bestPath.Length - 1].x:F1}," +
                        $"{bestPath[bestPath.Length - 1].y:F1},{bestPath[bestPath.Length - 1].z:F1})");
                }
            }
            else
            {
                // No grid path. For VERY CLOSE targets this can be a grid-snap
                // artifact (start/end in the same spot, or a cell-alignment
                // quirk), so a short straight-line hop is safe. For anything
                // farther, walking a blind player straight at an unknown
                // obstacle course was the old "grind into a wall for 10+
                // seconds, then give up" behavior — fail fast and honestly
                // instead. The caller announces unreachable.
                float fbDx = targetPos.x - playerPos.x;
                float fbDz = targetPos.z - playerPos.z;
                float fbDist = Mathf.Sqrt(fbDx * fbDx + fbDz * fbDz);
                if (fbDist > WmStraightLineFallbackMaxDist)
                {
                    DebugLogger.LogState(
                        $"NAV worldmap: no grid path and target is " +
                        $"{fbDist:F0}m away — refusing straight-line " +
                        $"fallback, reporting unreachable.");
                    return false;
                }

                DebugLogger.LogState(
                    "NAV worldmap: no grid path but target is close " +
                    $"({fbDist:F1}m). Using straight-line fallback.");
                _wmPathWaypoints = new Vector3[] { targetPos };
                _wmPathIndex = 0;
            }

            // Still need these for shared code compatibility.
            _pathCorners = new Vector3[] { targetPos };
            _pathCornerIndex = 0;
            _pathRecalcTimer = 0f;

            return true;
        }

        /// <summary>
        /// Body-capsule sweep over a planned route (see
        /// <see cref="SweepSegmentBlocked"/>). Returns the number of
        /// physically impassable segments, skipping those within
        /// <paramref name="goalExemptDist"/> of the goal and within
        /// <paramref name="startExemptDist"/> of the route start. When
        /// <paramref name="markBlocked"/> is set, each impassable segment's
        /// start is added to the walk's blocked zones so a re-plan avoids
        /// it. First few wedges are logged with their blocker.
        /// </summary>
        private int CountRouteWedges(Vector3[] path, Vector3 goal,
            float goalExemptDist, bool markBlocked,
            float startExemptDist = 0f)
        {
            var fm = FieldManager.Instance;
            var player = fm != null ? fm.GetControlPlayer() : null;
            if (player == null) return 0; // fail open — never block on a missing player
            int mask = ResolveBodySweepMask(player, out _);

            int wedges = 0;
            float exemptSq = goalExemptDist * goalExemptDist;
            float startExemptSq = startExemptDist * startExemptDist;
            for (int i = 0; i < path.Length - 1; i++)
            {
                float gdx = path[i].x - goal.x;
                float gdz = path[i].z - goal.z;
                if (gdx * gdx + gdz * gdz <= exemptSq) continue;
                float sdx = path[i].x - path[0].x;
                float sdz = path[i].z - path[0].z;
                if (sdx * sdx + sdz * sdz <= startExemptSq) continue;

                try
                {
                    if (!SweepSegmentBlocked(path[i], path[i + 1], mask,
                            out Collider blocker, out _, out _))
                        continue;

                    wedges++;
                    if (markBlocked) _wmBlockedPositions.Add(path[i]);
                    if (wedges <= 4)
                    {
                        DebugLogger.LogState(
                            $"NAV WM route sweep: impassable at wp[{i}] " +
                            $"({path[i].x:F1},{path[i].z:F1}) — " +
                            $"'{blocker.name}' L{blocker.gameObject.layer}");
                    }
                }
                catch { /* segment sweep error — treat as passable (fail open) */ }
            }
            return wedges;
        }

        #endregion
    }
}
