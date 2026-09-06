using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Answers "how far can I walk in this direction before something stops me?"
    /// for the manual-navigation wall sounds, WITHOUT trusting the game's wall
    /// layer mask (documented to over-report: layers 15 and 22 are in it but do not
    /// block the player) and WITHOUT mistaking a walkable slope for a wall.
    ///
    /// Two independent tests per direction; the nearer verdict wins:
    ///
    ///  A. Floor walk — every <see cref="SampleStep"/> metres along the direction a
    ///     downward ray finds the floor. A rise or drop between neighbouring samples
    ///     bigger than <see cref="FloorProbeGrid.MaxStepRatio"/> × step (0.5 m) is an
    ///     obstacle (wall base, ledge, cliff); no floor at all is an edge. A ramp
    ///     rises gently sample to sample and passes. This is the exact step rule that
    ///     matched 100 % of the user's recorded breadcrumb walks, so anything already
    ///     walked passes it.
    ///  B. Face rays — horizontal rays at knee and waist height catch thin walls and
    ///     fences the floor walk steps over. Only hits whose surface is near-vertical
    ///     (|normal.y| below <see cref="FloorProbeGrid.MinFloorNormalY"/>) count; a
    ///     slope face has a floor-like normal and is deliberately ignored here, so
    ///     slopes are judged by test A alone.
    ///
    /// Triggers, the player's own layer and character bodies (capsule / sphere /
    /// CharacterController colliders) are ignored in both tests, the same filter the
    /// floor grid uses. Pure geometry: no game state, safe to call from an audit.
    ///
    /// TUNING ROUND 1 (2026-09-06, from the F11 audit evidence — Lasgus 2,749 false
    /// walls on 18,814 walked edges, Krosse 469 on 16,436):
    ///  - Face rays now count only layers in the game's own foot wall mask
    ///    (<see cref="FaceMask"/>). Every top offender was OUTSIDE it: L24 Mesh_Col /
    ///    Col_Height (floor meshes), L25 Blend / In_House / Camera_Blend_Box /
    ///    TresureArea (camera and area volumes), L27 FootstepCollision, L10 Global
    ///    Volume, L20 Crossing. The mask over-reports (some L22 boxes are walked
    ///    through), but it is still the superset of what blocks the player.
    ///  - The knee ray moved from 0.35 m to 0.6 m, above <see cref="MaxStepY"/>, so a
    ///    step the floor walk allows can no longer read as a face (the L22 / L15 false
    ///    walls in Krosse sat 0.5 to 0.7 m from walked breadcrumbs).
    ///  - Floor rays ignore the volume layers (<see cref="VolumeLayerMask"/>) so a
    ///    volume's top face is never taken for a floor (FloorStep false walls).
    /// Re-audit after round 1: Lasgus 2,749 → 331, Krosse 469 → 119.
    ///
    /// ROUND 2 (2026-09-06, user asked for one more attempt before dropping walls):
    ///  - The floor walk applied 0.67 × 0.75 m = 0.5 m per sample, stricter than the
    ///    breadcrumb-validated grid rule (0.67 × 1.5 m cells = 1.0 m). On Lasgus the
    ///    rough mountain meshes failed it on walked slopes (274 FloorStep false walls
    ///    on L24 floor meshes). It now applies the validated rule over a two-sample
    ///    window (<see cref="MaxWindowY"/>) with a 0.75 m per-sample cap.
    ///  - The remaining Face false walls sit on real wall layers (L22 / L15) along
    ///    2.8 to 4.1 m breadcrumb links. Breadcrumbs merge within 1.6 m
    ///    (TraversalGraph.MergeRadius), so a straight link between two merged nodes
    ///    can clip a corner the player walked round: an audit artefact rather than a
    ///    probe fault. The audit now reports short links (≤ 2 m) separately, where
    ///    that error is small.
    /// Range is capped at 8 m (<see cref="MaxRange"/>) on the user's request.
    /// </summary>
    public static class WallProbe
    {
        #region Tuning

        /// <summary>
        /// Default probe reach (m). The live wall tones pass the player's own
        /// <see cref="ModSettings.WallRangeMeters"/> instead; audits use this.
        /// </summary>
        public const float Range = 6f;

        /// <summary>Spacing of the floor-walk samples (m).</summary>
        public const float SampleStep = 0.75f;

        /// <summary>
        /// The step rule the breadcrumb audit validated is 0.67 × a 1.5 m grid cell =
        /// 1.0 m of rise per 1.5 m (<see cref="FloorProbeGrid.MaxStepRatio"/>). The
        /// floor walk samples every 0.75 m, so it applies that rule over a two-sample
        /// window: a rise over the last 1.5 m above <see cref="MaxWindowY"/> is an
        /// obstacle. A single sample may not rise more than <see cref="MaxStepY"/>
        /// either (a wall base rises a metre or more within one sample; a walked
        /// slope's roughness does not).
        /// </summary>
        public const float MaxWindowY = FloorProbeGrid.MaxStepRatio * 2f * SampleStep;
        public const float MaxStepY = MaxWindowY * 0.75f;

        /// <summary>Floor rays start this far above the previous floor sample (m).</summary>
        private const float FloorRayUp = 2f;

        /// <summary>...and reach this far down (m): 2 m above to 2 m below the previous floor.</summary>
        private const float FloorRayLength = 4f;

        /// <summary>Longest reach a caller may ask for (m); the menu's wall distance slider tops out here.</summary>
        public const float MaxRange = 8f;

        /// <summary>
        /// Heights of the two face rays above the player's feet (m). The knee ray sits
        /// above <see cref="MaxStepY"/> so a climbable step is never a face.
        /// </summary>
        private const float KneeHeight = 0.6f;
        private const float WaistHeight = 0.9f;

        /// <summary>Unity layer of the player capsule (excluded from every ray).</summary>
        private const int PlayerLayer = 6;

        /// <summary>
        /// Layers holding non-blocking volumes the audit caught posing as floors or
        /// faces: 10 post-processing "Global Volume", 25 camera / area boxes (Blend,
        /// In_House, Camera_Blend_Box, TresureArea), 27 FootstepCollision.
        /// </summary>
        private const int VolumeLayerMask = (1 << 10) | (1 << 25) | (1 << 27);

        /// <summary>Floor rays: everything solid except the player and the volume layers.</summary>
        private const int FloorMask = ~((1 << PlayerLayer) | VolumeLayerMask);

        /// <summary>
        /// Documented foot wall mask (game-api.md: L15 ObjectWall, L17 PsynardWall,
        /// L21 GimmickWall, L22 Wall, L23 CharacterWall, L26), used if the live
        /// value cannot be read.
        /// </summary>
        private const int FallbackWallMask = 0x04E28000;
        private static int _faceMask;

        /// <summary>
        /// Face rays: only the game's own foot wall layers. Read live once from
        /// <see cref="Il2CppGame.GameRenderManager.LayerMaskWall"/>; the player's layer
        /// is always excluded.
        /// </summary>
        private static int FaceMask
        {
            get
            {
                if (_faceMask == 0)
                {
                    int mask = FallbackWallMask;
                    try
                    {
                        int live = Il2CppGame.GameRenderManager.LayerMaskWall;
                        if (live != 0) mask = live;
                        else DebugLogger.LogState("WallProbe: LayerMaskWall read as 0 — using the documented mask.");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState($"WallProbe: LayerMaskWall unavailable ({ex.Message}) — using the documented mask.");
                    }
                    _faceMask = mask & ~(1 << PlayerLayer);
                    DebugLogger.LogState($"WallProbe: face mask 0x{_faceMask:X8}.");
                }
                return _faceMask;
            }
        }

        #endregion

        #region Types

        /// <summary>Which test produced a reading.</summary>
        public enum Test
        {
            /// <summary>Nothing within range.</summary>
            None,
            /// <summary>Floor walk: height step too big (wall base, ledge, cliff).</summary>
            FloorStep,
            /// <summary>Floor walk: no floor found (edge, void, or a wall taller than the ray).</summary>
            FloorGap,
            /// <summary>Face ray: near-vertical surface hit.</summary>
            Face,
            /// <summary>No floor under the start point (mid-air); no verdict.</summary>
            NoStart
        }

        /// <summary>The verdict for one direction.</summary>
        public struct Reading
        {
            /// <summary>Distance to the obstacle (m); <see cref="Range"/> when none.</summary>
            public float Distance;
            public Test Test;
            /// <summary>Collider name that decided it (only when asked to describe), else null.</summary>
            public string Collider;
            /// <summary>Unity layer of that collider, -1 when unknown.</summary>
            public int Layer;

            public bool HasObstacle => Test == Test.FloorStep || Test == Test.FloorGap || Test == Test.Face;

            public override string ToString() =>
                HasObstacle
                    ? $"{Distance:F1}m {Test}{(Collider != null ? $" '{Collider}'/L{Layer}" : "")}"
                    : Test == Test.NoStart ? "no floor" : "clear";
        }

        /// <summary>Index of each camera-relative direction in <see cref="ProbeAround"/>'s result.</summary>
        public const int Front = 0, Right = 1, Behind = 2, Left = 3;

        #endregion

        #region Public API

        /// <summary>
        /// Probes front, right, behind and left relative to a flattened camera
        /// forward. <paramref name="describe"/> adds collider names for logs.
        /// </summary>
        public static Reading[] ProbeAround(Vector3 feetPos, Vector3 cameraForwardFlat, float range = Range,
            bool describe = false)
        {
            Vector3 fwd = Flatten(cameraForwardFlat, Vector3.forward);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x); // 90° clockwise seen from above

            return new[]
            {
                ProbeDirection(feetPos, fwd, range, describe),
                ProbeDirection(feetPos, right, range, describe),
                ProbeDirection(feetPos, -fwd, range, describe),
                ProbeDirection(feetPos, -right, range, describe)
            };
        }

        /// <summary>
        /// Probes one horizontal direction from the player's feet position.
        /// </summary>
        /// <param name="feetPos">Player (or breadcrumb) position, at floor level.</param>
        /// <param name="direction">Horizontal direction; Y is ignored.</param>
        /// <param name="range">How far to look (m), at most <see cref="Range"/>.</param>
        /// <param name="describe">Fill in collider name and layer for logging.</param>
        public static Reading ProbeDirection(Vector3 feetPos, Vector3 direction, float range = Range,
            bool describe = false)
        {
            Vector3 dir = Flatten(direction, Vector3.zero);
            range = Mathf.Clamp(range, SampleStep, MaxRange);
            var result = new Reading { Distance = range, Test = Test.None, Layer = -1 };
            if (dir == Vector3.zero) return result;

            // Test A: floor walk.
            if (!TryFloorY(feetPos, feetPos.y, out float floorY, out _))
            {
                result.Test = Test.NoStart;
                return result;
            }

            float prevY = floorY, prev2Y = floorY;
            for (float s = SampleStep; s <= range + 0.001f; s += SampleStep)
            {
                Vector3 at = feetPos + dir * s;
                if (!TryFloorY(at, prevY, out float y, out Collider floorCol))
                {
                    result.Distance = s - SampleStep * 0.5f;
                    result.Test = Test.FloorGap;
                    break;
                }
                // Validated grid rule over a 1.5 m window, plus a per-sample cap.
                if (Mathf.Abs(y - prevY) > MaxStepY || Mathf.Abs(y - prev2Y) > MaxWindowY)
                {
                    result.Distance = s - SampleStep * 0.5f;
                    result.Test = Test.FloorStep;
                    if (describe) Describe(ref result, floorCol);
                    break;
                }
                prev2Y = prevY;
                prevY = y;
            }

            // Test B: face rays at knee and waist. Nearer verdict wins.
            float faceLimit = result.HasObstacle ? result.Distance : range;
            if (TryFace(feetPos + Vector3.up * KneeHeight, dir, faceLimit, out float dKnee, out Collider cKnee))
            {
                result.Distance = dKnee;
                result.Test = Test.Face;
                if (describe) Describe(ref result, cKnee);
                faceLimit = dKnee;
            }
            if (TryFace(feetPos + Vector3.up * WaistHeight, dir, faceLimit, out float dWaist, out Collider cWaist))
            {
                result.Distance = dWaist;
                result.Test = Test.Face;
                if (describe) Describe(ref result, cWaist);
            }

            return result;
        }

        /// <summary>Camera forward projected onto the ground plane; world forward when there is no camera.</summary>
        public static Vector3 CameraForwardFlat()
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.forward;
            return Flatten(cam.transform.forward, Vector3.forward);
        }

        #endregion

        #region Rays

        /// <summary>
        /// Floor height at a horizontal position, tracking the floor nearest to
        /// <paramref name="nearY"/> (so a bridge overhead or a pit below does not
        /// hijack the walk). False when no floor-like surface is within the ray.
        /// </summary>
        private static bool TryFloorY(Vector3 at, float nearY, out float floorY, out Collider collider)
        {
            floorY = 0f;
            collider = null;
            var origin = new Vector3(at.x, nearY + FloorRayUp, at.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, FloorRayLength, FloorMask,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            float best = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.normal.y < FloorProbeGrid.MinFloorNormalY) continue;
                var col = hit.collider;
                if (col == null || !FloorProbeGrid.IsSolidFloorCollider(col)) continue;

                float d = Mathf.Abs(hit.point.y - nearY);
                if (d < best)
                {
                    best = d;
                    floorY = hit.point.y;
                    collider = col;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Nearest near-vertical solid surface along a horizontal ray, within
        /// <paramref name="limit"/>. Floor-like and ceiling-like hits are skipped.
        /// </summary>
        private static bool TryFace(Vector3 origin, Vector3 dir, float limit, out float distance,
            out Collider collider)
        {
            distance = limit;
            collider = null;
            var hits = Physics.RaycastAll(origin, dir, limit, FaceMask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (Mathf.Abs(hit.normal.y) >= FloorProbeGrid.MinFloorNormalY) continue; // slope or ceiling
                var col = hit.collider;
                if (col == null || !FloorProbeGrid.IsSolidFloorCollider(col)) continue;
                if (hit.distance < distance)
                {
                    distance = hit.distance;
                    collider = col;
                    found = true;
                }
            }
            return found;
        }

        private static void Describe(ref Reading r, Collider col)
        {
            if (col == null) return;
            try
            {
                r.Collider = col.gameObject.name;
                r.Layer = col.gameObject.layer;
            }
            catch
            {
                r.Collider = "?";
            }
        }

        private static Vector3 Flatten(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? fallback : v.normalized;
        }

        #endregion
    }
}
