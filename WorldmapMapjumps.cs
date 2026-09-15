using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The world map's location entrances as ONE shared scene scan: every
    /// <c>FieldMapjumpCollision</c> with its ground-level trigger colliders (the
    /// "Press X to enter" rings). Town-wide detection volumes (tall colliders)
    /// are never rings. Shared by the nav-list reachability cache, the
    /// safe-exit picker and the fishing stand bake so all three agree on what
    /// counts as an entrance.
    /// </summary>
    public static class WorldmapMapjumps
    {
        /// <summary>Trigger colliders taller than this are town-wide zones, not road entrances.</summary>
        public const float GroundLevelMaxHeight = 20f;

        /// <summary>
        /// A blocked sweep segment this close (m, flat) to an entrance ring is a
        /// GATE PINCH, not a wedge: the body sweep is conservative and the town's
        /// own collider hugs the road the player walks through. Such segments are
        /// neither counted nor stamped — in the bake proof, the live walk and the
        /// F7 auditor alike. Calibration 2026-09-09 (log 20:31–20:41): Krosse gate
        /// wedges 0.0–3.0 m from MF_0006_01A, Arlia gate wedges 4.2–4.4 m from
        /// MF_0003_01A, every rock-belt wedge 23 m or more from any ring. Bake
        /// refusals with a first wedge 7.7–12.4 m from a ring exist and stay
        /// refusals. User decision: 5 m.
        /// </summary>
        public const float RingWedgeMeters = 5f;

        /// <summary>
        /// Flat distance (m) from a point to the nearest entrance ring — 0 when
        /// the point lies inside one — with the ring's destination fieldmap in
        /// <paramref name="label"/>. Null when the list has no usable ring
        /// (nothing scanned, or every ClosestPoint call failed).
        /// </summary>
        public static float? NearestRingDistance(
            IReadOnlyList<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps,
            Vector3 pos, out string label)
        {
            label = null;
            float? best = null;
            if (mapjumps == null) return null;
            foreach (var (fieldmapID, _, rings) in mapjumps)
            {
                foreach (var ring in rings)
                {
                    if (ring == null) continue;
                    try
                    {
                        Vector3 cp = ring.ClosestPoint(pos);
                        float dx = cp.x - pos.x, dz = cp.z - pos.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (best == null || d < best.Value)
                        {
                            best = d;
                            label = fieldmapID.ToString();
                        }
                    }
                    catch { /* unsupported collider type — skip this ring */ }
                }
            }
            return best;
        }

        /// <summary>Log fragment for a wedge position: "ring MF_0003_01A 1.4 m" or "no ring data".</summary>
        public static string DescribeNearestRing(
            IReadOnlyList<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps,
            Vector3 pos)
        {
            float? d = NearestRingDistance(mapjumps, pos, out string label);
            return d.HasValue ? $"ring {label} {d.Value:F1} m" : "no ring data";
        }

        /// <summary>True when the point is within <see cref="RingWedgeMeters"/> of an entrance ring.</summary>
        public static bool IsAtRing(
            IReadOnlyList<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps,
            Vector3 pos)
        {
            float? d = NearestRingDistance(mapjumps, pos, out _);
            return d.HasValue && d.Value <= RingWedgeMeters;
        }

        /// <summary>
        /// True when a sweep blocker belongs to a location's own map-jump object
        /// (the gate arch / wall segment that carries the "Press X" trigger), as
        /// opposed to terrain rock beside a gate. Distance alone cannot tell them
        /// apart: on 2026-09-09 the 5 m rule forgave an Arlia riverbank
        /// 'Col_Obstacle' 4.6 m from the ring and the walk stuck on it exactly
        /// there. <paramref name="path"/> is the blocker's transform chain for the
        /// log. Errors count as "not a gate" (never forgive on a guess).
        /// </summary>
        public static bool IsGateCollider(Collider blocker, out string path)
        {
            path = "?";
            if (blocker == null) return false;
            try
            {
                var names = new List<string>();
                var t = blocker.transform;
                for (int depth = 0; t != null && depth < 6; depth++, t = t.parent)
                    names.Add(t.name);
                path = string.Join("/", names);
                return blocker.GetComponentInParent<FieldMapjumpCollision>() != null;
            }
            catch (Exception ex)
            {
                path = "error: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// One-line classification for a blocked segment near a ring: whether it
        /// is forgivable (at a ring AND the town's own collider) and why.
        /// </summary>
        public static bool IsGatePinch(
            IReadOnlyList<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps,
            Vector3 pos, Collider blocker, out string why)
        {
            float? d = NearestRingDistance(mapjumps, pos, out string label);
            if (!d.HasValue || d.Value > RingWedgeMeters)
            {
                why = d.HasValue ? $"ring {label} {d.Value:F1} m" : "no ring data";
                return false;
            }
            bool gate = IsGateCollider(blocker, out string path);
            why = $"ring {label} {d.Value:F1} m, blocker {(gate ? "IS" : "is NOT")} a map-jump collider ({path})";
            return gate;
        }

        /// <summary>
        /// False when the list cannot answer ring questions: empty, no ring on any
        /// entry, or a ring collider destroyed by a scene change (a battle reloads
        /// the world map objects — 2026-09-09: every sweep after a battle read "no
        /// ring data" from a full cache of dead colliders).
        /// </summary>
        public static bool IsUsable(
            IReadOnlyList<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> mapjumps)
        {
            if (mapjumps == null || mapjumps.Count == 0) return false;
            bool anyRing = false;
            foreach (var (_, _, rings) in mapjumps)
            {
                foreach (var ring in rings)
                {
                    if (ring == null) return false;
                    try { _ = ring.bounds; }
                    catch { return false; }
                    anyRing = true;
                }
            }
            return anyRing;
        }

        /// <summary>
        /// Scans the scene once. Every map jump is returned, with a possibly
        /// empty ring list; callers that need entrances filter on
        /// <c>rings.Count &gt; 0</c>. Returns an empty list on failure (logged)
        /// so verdicts degrade to "unknown".
        /// </summary>
        public static List<(FieldmapID fieldmapID, Vector3 position, List<Collider> rings)> CollectAll()
        {
            var result = new List<(FieldmapID, Vector3, List<Collider>)>();
            try
            {
                var collisions = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null) return result;

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
                            if (col.bounds.size.y > GroundLevelMaxHeight) continue;
                            rings.Add(col);
                        }
                    }
                    result.Add((c.fieldmapID, c.transform.position, rings));
                }
            }
            catch (Exception ex)
            {
                result.Clear();
                DebugLogger.LogState($"NAV WM mapjump scan error: {ex.Message}");
            }
            return result;
        }
    }
}
