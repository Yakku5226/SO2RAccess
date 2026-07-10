using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: world-map REACHABILITY
    // for the current travel mode (foot / bunny / psynard). Provides the
    // honest "unreachable on foot / by bunny" verdicts for the nav list and
    // the region-based chest/enemy check. HARD RULES (project memory):
    // a reachable target must NEVER be reported unreachable — every failure
    // or unknown path here degrades to "reachable"; and every verdict is
    // logged with its reason ([WMReach]).
    public partial class NavigationHandler
    {
        #region World Map Reachability

        /// <summary>Reachability verdict for a world-map location in the
        /// player's CURRENT travel mode.</summary>
        private enum WmReachability
        {
            /// <summary>An entrance is walkable and connected to the player.</summary>
            Reachable,

            /// <summary>Proven disconnected: the player's region is known,
            /// at least one entrance candidate had a known region, and none
            /// matched. The only verdict that may annotate a list item.</summary>
            Unreachable,

            /// <summary>Anything less than proof — treated as reachable.</summary>
            Unknown,
        }

        /// <summary>
        /// Cached world-map entrance triggers: one entry per
        /// FieldMapjumpCollision, with its ground-level (small, Y extent
        /// ≤ 20m) trigger colliders. Refreshed ONCE per nav-list build
        /// (<see cref="RefreshWmMapjumpCache"/>) so the per-location
        /// reachability checks never rescan the scene. Town-wide detection
        /// volumes are excluded, as in the walk-target picker.
        /// </summary>
        private readonly List<(FieldmapID fieldmapID, Vector3 position,
            List<Collider> rings)> _wmMapjumpCache
            = new List<(FieldmapID, Vector3, List<Collider>)>();

        /// <summary>Rebuilds <see cref="_wmMapjumpCache"/> with a single
        /// scene scan. Failures leave the cache empty (verdicts degrade to
        /// Unknown → reachable).</summary>
        private void RefreshWmMapjumpCache()
        {
            _wmMapjumpCache.Clear();
            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null) return;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;

                    var rings = new List<Collider>();
                    var cols = c.GetComponents<Collider>();
                    if (cols != null)
                    {
                        for (int k = 0; k < cols.Length; k++)
                        {
                            var col = cols[k];
                            if (col == null || !col.isTrigger) continue;
                            if (col.bounds.size.y > 20f) continue;
                            rings.Add(col);
                        }
                    }
                    _wmMapjumpCache.Add(
                        (c.fieldmapID, c.transform.position, rings));
                }
            }
            catch (Exception ex)
            {
                _wmMapjumpCache.Clear();
                DebugLogger.LogState(
                    $"NAV WM mapjump cache error: {ex.Message}");
            }
        }

        /// <summary>
        /// Enumerates entrance-ring candidate points for a set of trigger
        /// colliders: each trigger's center, its closest point to the
        /// player, and its perimeter projected from 16 directions (covers
        /// every approach side). SHARED by the nav-list reachability check
        /// and the auto-walk target picker (PickReachableRingPoint) so the
        /// two can never disagree about which points count as an entrance.
        /// <paramref name="consider"/> returns true to STOP the enumeration
        /// early (e.g. once a location is proven reachable) — the perimeter
        /// samples cost a physics ClosestPoint call each, so stopping
        /// matters when this runs for every location on a list open.
        /// </summary>
        private static void ForEachRingCandidate(List<Collider> rings,
            Vector3 playerPos, Func<Vector3, bool> consider)
        {
            foreach (var ring in rings)
            {
                if (ring == null) continue;
                Bounds b = ring.bounds;

                // Trigger center — the middle of the entrance road, and of
                // the passable cells the grid generator punched for it.
                if (consider(new Vector3(b.center.x, b.center.y, b.center.z)))
                    return;

                // Plain closest point to the player (skip the degenerate
                // "player already inside" case).
                try
                {
                    Vector3 cp = ring.ClosestPoint(playerPos);
                    if ((cp - playerPos).sqrMagnitude > 0.0001f &&
                        consider(cp))
                        return;
                }
                catch { /* unsupported collider type — sampling below */ }

                // Perimeter samples from all around this trigger.
                float reach = b.extents.x + b.extents.z + 4f;
                const int Steps = 16;
                for (int i = 0; i < Steps; i++)
                {
                    float ang = (i / (float)Steps) * Mathf.PI * 2f;
                    Vector3 refPt = new Vector3(
                        b.center.x + Mathf.Cos(ang) * reach,
                        b.center.y,
                        b.center.z + Mathf.Sin(ang) * reach);
                    try
                    {
                        if (consider(ring.ClosestPoint(refPt))) return;
                    }
                    catch { /* skip this sample */ }
                }
            }
        }

        /// <summary>
        /// Maximum distance (m) between a location and its nearest mapjump
        /// for that mapjump's rings to count as the location's entrances.
        /// A location with NO trigger of its own would otherwise borrow the
        /// nearest OTHER town's rings and could be falsely annotated
        /// unreachable — beyond this cap the verdict degrades to Unknown.
        /// </summary>
        private const float WmReachMaxTriggerDistance = 100f;

        /// <summary>
        /// All cached ground-level entrance rings belonging to the same
        /// destination fieldmap as the mapjump nearest to
        /// <paramref name="locationPos"/> (boundary towns have several
        /// entrances). Empty list when nothing is cached or the nearest
        /// mapjump is implausibly far (see
        /// <see cref="WmReachMaxTriggerDistance"/>).
        /// </summary>
        private List<Collider> GetCachedRingsForLocation(Vector3 locationPos)
        {
            var rings = new List<Collider>();
            int nearestIdx = -1;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < _wmMapjumpCache.Count; i++)
            {
                float d = Vector3.Distance(
                    _wmMapjumpCache[i].position, locationPos);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearestIdx = i;
                }
            }
            if (nearestIdx < 0) return rings;
            if (nearestDist > WmReachMaxTriggerDistance)
            {
                DebugLogger.LogState(
                    $"NAV WM reach: nearest mapjump is {nearestDist:F0}m " +
                    $"from location ({locationPos.x:F1},{locationPos.z:F1}) " +
                    "— not its entrance, verdict stays Unknown.");
                return rings;
            }

            var fieldmapID = _wmMapjumpCache[nearestIdx].fieldmapID;
            for (int i = 0; i < _wmMapjumpCache.Count; i++)
            {
                if (_wmMapjumpCache[i].fieldmapID.Equals(fieldmapID))
                    rings.AddRange(_wmMapjumpCache[i].rings);
            }
            return rings;
        }

        /// <summary>
        /// Decides whether a world-map location is reachable in the given
        /// travel mode, by testing its entrance-ring candidates against the
        /// mode's connected-region map. The player side is the START REGION
        /// SET (WorldmapPathfinder.GetStartRegionIds) — every region the
        /// pathfinder's start-clearing disc can bridge onto — NOT just the
        /// player's exact cell, so the verdict can never disagree with what
        /// FindPath would actually do. Verdicts:
        /// - Reachable: any candidate is walkable AND in a start region.
        /// - Unreachable: ONLY with full proof — start regions known, at
        ///   least one walkable candidate with a known region existed, and
        ///   none matched.
        /// - Unknown (treated as reachable): every other case — no grid or
        ///   mode support, start regions unknown, no triggers, no walkable
        ///   candidate, or candidate regions unknown.
        /// <paramref name="reason"/> always explains the verdict for the
        /// [WMReach] log.
        /// </summary>
        private WmReachability ResolveLocationReachability(
            Vector3 locationPos, Vector3 playerPos, List<int> playerRegions,
            WorldmapTravelMode mode, out string reason)
        {
            try
            {
                if (mode == WorldmapTravelMode.Psynard)
                {
                    reason = "psynard flies — everything reachable";
                    return WmReachability.Reachable;
                }
                if (!WorldmapPathfinder.GridSupportsMode(mode))
                {
                    reason = $"grid has no {mode} data — fail open";
                    return WmReachability.Unknown;
                }
                if (playerRegions == null || playerRegions.Count == 0)
                {
                    reason = "player start regions unknown — fail open";
                    return WmReachability.Unknown;
                }

                var rings = GetCachedRingsForLocation(locationPos);
                if (rings.Count == 0)
                {
                    reason = "no ground-level entrance triggers cached — fail open";
                    return WmReachability.Unknown;
                }

                int walkable = 0;
                int knownRegion = 0;
                int bestOtherRegion = 0;
                bool connected = false;

                ForEachRingCandidate(rings, playerPos, cand =>
                {
                    if (!WorldmapPathfinder.IsWalkableWorld(cand, mode))
                        return false;
                    walkable++;
                    int r = WorldmapPathfinder.GetRegionId(cand, mode);
                    if (r == 0) return false;
                    knownRegion++;
                    if (playerRegions.Contains(r))
                    {
                        connected = true;
                        return true; // proven reachable — stop sampling
                    }
                    bestOtherRegion = r;
                    return false;
                });

                string playerSet = string.Join(",", playerRegions);
                if (connected)
                {
                    reason = $"entrance in player start regions " +
                        $"[{playerSet}] ({rings.Count} triggers)";
                    return WmReachability.Reachable;
                }
                if (knownRegion > 0)
                {
                    reason = $"{walkable} walkable candidates, " +
                        $"{knownRegion} with known region (e.g. " +
                        $"{bestOtherRegion}), player start regions " +
                        $"[{playerSet}], none connected";
                    return WmReachability.Unreachable;
                }
                reason = walkable > 0
                    ? $"{walkable} walkable candidates but all regions " +
                      "unknown — fail open"
                    : "no walkable entrance candidate — fail open";
                return WmReachability.Unknown;
            }
            catch (Exception ex)
            {
                reason = $"error ({ex.Message}) — fail open";
                return WmReachability.Unknown;
            }
        }

        /// <summary>Scratch list for start-region queries (avoids a per-item
        /// allocation in the chest/enemy filter loop).</summary>
        private readonly List<int> _wmStartRegionScratch = new List<int>();

        /// <summary>
        /// Region-based reachability for world-map chests/enemies in the
        /// player's CURRENT travel mode. Replaces the old 10-sample
        /// CalcHeight ocean line-check, which false-hid reachable targets
        /// whenever the straight line crossed a lake finger the real route
        /// walks around. The player side is the START REGION SET (same disc
        /// bridging as FindPath — see GetStartRegionIds), so this can never
        /// call a target unreachable that FindPath would reach. Unknown on
        /// either side keeps the target (fail open); psynard reaches
        /// everything.
        /// </summary>
        private bool WorldmapModeRegionReachable(Vector3 playerPos,
            Vector3 targetPos)
        {
            try
            {
                var mode = WorldmapTravel.CurrentMode();
                if (mode == WorldmapTravelMode.Psynard) return true;

                int tr = WorldmapPathfinder.GetRegionId(targetPos, mode);
                if (tr == 0) return true; // unknown — keep

                WorldmapPathfinder.GetStartRegionIds(
                    playerPos, mode, _wmStartRegionScratch);
                if (_wmStartRegionScratch.Count == 0) return true; // unknown
                if (_wmStartRegionScratch.Contains(tr)) return true;

                DebugLogger.LogState(
                    $"[WMReach] target ({targetPos.x:F1},{targetPos.z:F1}) " +
                    $"UNREACHABLE ({mode}): player start regions " +
                    $"[{string.Join(",", _wmStartRegionScratch)}] vs target " +
                    $"region {tr}.");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM region reachable error: {ex.Message}");
                return true; // fail open
            }
        }

        #endregion
    }
}
