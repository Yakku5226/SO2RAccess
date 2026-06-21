using System;
using System.Collections.Generic;
using Il2CppGame;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Lightweight spatial-awareness sensor for the player on field/town maps.
    ///
    /// Provides "soft steering": given a desired travel direction (e.g. toward an
    /// auto-walk waypoint), it nudges that direction away from nearby obstacles —
    /// primarily NPCs and enemy symbols, which are NOT baked into the NavMesh and
    /// so are the usual cause of the player getting stuck while auto-walking.
    ///
    /// Steering is normally gentle (capped at <see cref="BaseCap"/>) so it can't
    /// veer the player off-route in open areas. But a stationary blocker less than a
    /// metre ahead needs a sharper sidestep than a gentle cap allows, so the cap is
    /// ADAPTIVE: when the player is provably wedged (an obstacle is ahead and no
    /// real progress has been made for a moment), the cap opens up toward
    /// <see cref="MaxWedgeCap"/> and the speed drops, letting the sensor thread past
    /// the blocker smoothly. If even the widened sidestep fails for
    /// <see cref="HandoffDelay"/>, <see cref="IsHardWedged"/> goes true so the caller
    /// can escalate to its physical detour — the sensor never gets the player
    /// permanently stuck on its own.
    ///
    /// Detection is HYBRID:
    ///   - Dynamic bodies (NPCs / enemy symbols) are sensed by POSITION from a
    ///     cached transform list. They may not sit on a raycastable physics layer,
    ///     so reading transforms we already enumerate elsewhere is reliable.
    ///   - Static clutter is sensed with a short forward fan of RAYCASTS against the
    ///     game's obstacle / CharaWall layers.
    ///
    /// The component is self-contained and side-effect-free (it only reads the scene
    /// and returns a direction + a speed scale), so it can later back an exploration
    /// mode and manual-walk assist for blind players.
    /// </summary>
    public class SpatialSensor
    {
        #region Tuning

        /// <summary>Radius (m) within which dynamic obstacles influence steering.</summary>
        private const float SenseRadius = 2.5f;

        /// <summary>
        /// An obstacle only pushes the player if it lies within this cone (as a
        /// cosine) of the desired heading — i.e. roughly ahead. ~0.30 ≈ ±72°.
        /// </summary>
        private const float AheadCosThreshold = 0.30f;

        /// <summary>
        /// Gentle steering cap (degrees) used in open areas — the "soft" default.
        /// The player is nudged, never redirected.
        /// </summary>
        private const float BaseCap = 35f;

        /// <summary>
        /// Widened cap (degrees) the sensor escalates to when wedged, so it can
        /// actually sidestep a close blocker. Kept below 90° so there is always a
        /// forward component toward the waypoint.
        /// </summary>
        private const float MaxWedgeCap = 70f;

        /// <summary>
        /// Grace period (s) of being wedged before the cap starts widening. Brief
        /// brush-bys against a passing NPC stay gentle and never escalate.
        /// </summary>
        private const float WedgeGrace = 0.5f;

        /// <summary>Time (s) over which the cap ramps from <see cref="BaseCap"/> to
        /// <see cref="MaxWedgeCap"/> once the grace period passes.</summary>
        private const float WedgeRamp = 0.7f;

        /// <summary>
        /// Total wedge time (s) with no real progress after which the widened
        /// sidestep is declared a failure and <see cref="IsHardWedged"/> is raised
        /// so the caller escalates to a physical detour.
        /// </summary>
        private const float HandoffDelay = 1.6f;

        /// <summary>Movement (m) since the last reference point that counts as real
        /// progress and resets the wedge timer (back to gentle steering).</summary>
        private const float WedgeProgressDist = 0.30f;

        /// <summary>Stick magnitude scale applied while threading a wedge (slows the
        /// player for finer control near the blocker). 1.0 = full run.</summary>
        private const float WedgeSpeedScale = 0.6f;

        /// <summary>Scales how strongly accumulated repulsion bends the heading
        /// before the cap clamp.</summary>
        private const float SteerStrength = 1.5f;

        /// <summary>Frames between full obstacle rescans (FindObjectsOfType is costly).</summary>
        private const int RescanIntervalFrames = 30;

        /// <summary>Whether short forward wall raycasts contribute to steering.</summary>
        private const bool UseWallRays = true;

        /// <summary>Forward wall-ray sensing range (m).</summary>
        private const float WallRayRange = 1.3f;

        /// <summary>Offset angles (deg) of the forward wall-ray fan, relative to heading.</summary>
        private static readonly float[] WallRayAngles = { -25f, 0f, 25f };

        /// <summary>Obstacle (L22) + CharaWall (L23) physics layers.</summary>
        private static readonly int WallLayerMask = (1 << 22) | (1 << 23);

        /// <summary>Seconds between detailed diagnostic log lines while near an obstacle.</summary>
        private const float DiagInterval = 0.33f;

        #endregion

        #region Public State

        /// <summary>
        /// Stick-magnitude scale the caller should apply this frame (1.0 = full run,
        /// lower while threading a wedge). Set by <see cref="Steer"/>.
        /// </summary>
        public float LastSpeedScale { get; private set; } = 1f;

        /// <summary>
        /// True when an obstacle is ahead and even the widened sidestep has made no
        /// progress for <see cref="HandoffDelay"/> — the caller should escalate to a
        /// physical detour. Reset by <see cref="Reset"/> or when progress resumes.
        /// </summary>
        public bool IsHardWedged { get; private set; }

        #endregion

        #region Private State

        private int _rescanTimer;
        private readonly List<Transform> _obstacles = new List<Transform>();

        // Wedge tracking: the last position/time where the player clearly progressed.
        private Vector3 _wedgeRefPos;
        private float _wedgeRefTime;

        // Diagnostics: nearest in-cone obstacle from the last accumulation pass.
        private Transform _nearestObstacle;
        private float _nearestDist;
        private Vector3 _nearestPos;
        private float _lastDiagTime;

        #endregion

        #region Public API

        /// <summary>
        /// Returns a steered travel direction: <paramref name="desiredDir"/> nudged
        /// away from nearby obstacles and clamped to an adaptive cap (gentle by
        /// default, widening when wedged). Returns <paramref name="desiredDir"/>
        /// unchanged when the way is clear. Also updates <see cref="LastSpeedScale"/>
        /// and <see cref="IsHardWedged"/>. All vectors are world-space, XZ plane.
        /// </summary>
        /// <param name="playerPos">Current player world position.</param>
        /// <param name="desiredDir">Normalized desired heading (toward the waypoint).</param>
        /// <param name="exclude">A transform to ignore — e.g. the NPC being walked
        /// to — so the sensor never steers the player away from their destination.</param>
        /// <param name="deviated">Set true when the result differs from desiredDir.</param>
        public Vector3 Steer(Vector3 playerPos, Vector3 desiredDir,
            Transform exclude, out bool deviated)
        {
            deviated = false;
            LastSpeedScale = 1f;
            IsHardWedged = false;

            desiredDir.y = 0f;
            if (desiredDir.sqrMagnitude < 1e-4f) return desiredDir;
            desiredDir.Normalize();

            // Periodic rescan of dynamic bodies.
            _rescanTimer--;
            if (_rescanTimer <= 0)
            {
                _rescanTimer = RescanIntervalFrames;
                Rescan();
            }

            float now = Time.time;

            _nearestObstacle = null;
            _nearestDist = float.MaxValue;
            Vector3 steer = AccumulateObstacleRepulsion(playerPos, desiredDir, exclude);
            if (UseWallRays)
                steer += AccumulateWallRepulsion(playerPos, desiredDir);

            if (steer.sqrMagnitude < 1e-4f)
            {
                // Clear ahead — reset wedge tracking and steer nothing.
                _wedgeRefPos = playerPos;
                _wedgeRefTime = now;
                LogDiagnostic(playerPos, desiredDir, 0f, BaseCap, 0f, false);
                return desiredDir;
            }

            // --- Progress / wedge tracking ---
            if ((playerPos - _wedgeRefPos).sqrMagnitude >
                WedgeProgressDist * WedgeProgressDist)
            {
                _wedgeRefPos = playerPos;
                _wedgeRefTime = now;
            }
            float stuckDuration = now - _wedgeRefTime;

            // --- Adaptive cap + speed based on how long we've been wedged ---
            float effectiveCap = BaseCap;
            if (stuckDuration > WedgeGrace)
            {
                float t = Mathf.Clamp01((stuckDuration - WedgeGrace) / WedgeRamp);
                effectiveCap = Mathf.Lerp(BaseCap, MaxWedgeCap, t);
                LastSpeedScale = WedgeSpeedScale; // slow down to thread the gap
            }

            Vector3 blended = desiredDir + steer * SteerStrength;
            blended.y = 0f;
            if (blended.sqrMagnitude < 1e-4f) return desiredDir;
            blended.Normalize();

            float rawAngle = Vector3.SignedAngle(desiredDir, blended, Vector3.up);
            float angle = Mathf.Clamp(rawAngle, -effectiveCap, effectiveCap);

            // Hard wedge: a body is ahead and even the widened sidestep hasn't freed
            // the player — signal the caller to escalate to a physical detour.
            if (_nearestObstacle != null && stuckDuration > HandoffDelay)
                IsHardWedged = true;

            if (Mathf.Abs(angle) < 0.5f)
            {
                LogDiagnostic(playerPos, desiredDir, 0f, effectiveCap, stuckDuration, false);
                return desiredDir;
            }

            Vector3 result =
                (Quaternion.AngleAxis(angle, Vector3.up) * desiredDir).normalized;
            deviated = true;
            LogDiagnostic(playerPos, desiredDir, angle, effectiveCap, stuckDuration, true);
            return result;
        }

        /// <summary>
        /// Clears cached obstacle data and wedge state. Call when starting/ending an
        /// auto-walk or leaving a map so stale data is not reused.
        /// </summary>
        public void Reset()
        {
            _obstacles.Clear();
            _rescanTimer = 0;
            _wedgeRefPos = Vector3.zero;
            _wedgeRefTime = 0f;
            LastSpeedScale = 1f;
            IsHardWedged = false;
        }

        #endregion

        #region Private

        /// <summary>
        /// Refreshes the cached list of dynamic obstacles (NPCs and enemy symbols,
        /// which subclass <see cref="FieldNpcCharacter"/>), excluding the player and
        /// trailing party followers — those move WITH the player.
        /// </summary>
        private void Rescan()
        {
            _obstacles.Clear();
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (found == null) return;
                foreach (var npc in found)
                {
                    if (npc == null) continue;
                    if (npc.TryCast<FieldPlayer>() != null) continue;
                    if (npc.TryCast<FieldFollowCharacter>() != null) continue;
                    try { _obstacles.Add(npc.transform); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SpatialSensor.Rescan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sums lateral push-away contributions from cached dynamic obstacles that
        /// lie ahead of the player within <see cref="SenseRadius"/>, and records the
        /// nearest such body for diagnostics/wedge tracking.
        /// </summary>
        private Vector3 AccumulateObstacleRepulsion(Vector3 playerPos,
            Vector3 desiredDir, Transform exclude)
        {
            Vector3 right = new Vector3(desiredDir.z, 0f, -desiredDir.x);
            Vector3 steer = Vector3.zero;

            for (int i = _obstacles.Count - 1; i >= 0; i--)
            {
                Transform t = _obstacles[i];
                Vector3 pos;
                try
                {
                    if (t == null) { _obstacles.RemoveAt(i); continue; }
                    if (exclude != null && t == exclude) continue;
                    pos = t.position;
                }
                catch { _obstacles.RemoveAt(i); continue; }

                Vector3 to = pos - playerPos;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.01f || dist > SenseRadius) continue;

                Vector3 toDir = to / dist;
                float ahead = Vector3.Dot(desiredDir, toDir);
                if (ahead < AheadCosThreshold) continue; // not in front

                // Track the nearest in-cone body.
                if (dist < _nearestDist)
                {
                    _nearestDist = dist;
                    _nearestObstacle = t;
                    _nearestPos = pos;
                }

                float prox = 1f - (dist / SenseRadius); // 0..1, closer = larger
                float weight = prox * ahead;

                float side = Vector3.Dot(toDir, right); // >0: obstacle on the right
                Vector3 push = (side >= 0f ? -right : right);
                steer += push * weight;
            }
            return steer;
        }

        /// <summary>
        /// Casts a short forward fan of rays against the obstacle/wall layers and
        /// adds lateral push away from any near hit. Conservative (short range,
        /// modest weight); symmetric corridor hits cancel out.
        /// </summary>
        private Vector3 AccumulateWallRepulsion(Vector3 playerPos, Vector3 desiredDir)
        {
            Vector3 origin = playerPos + Vector3.up * 0.5f;
            Vector3 right = new Vector3(desiredDir.z, 0f, -desiredDir.x);
            Vector3 steer = Vector3.zero;

            for (int i = 0; i < WallRayAngles.Length; i++)
            {
                Vector3 rayDir =
                    Quaternion.AngleAxis(WallRayAngles[i], Vector3.up) * desiredDir;
                try
                {
                    if (UnityEngine.Physics.Raycast(origin, rayDir, out RaycastHit hit,
                        WallRayRange, WallLayerMask))
                    {
                        float prox = 1f - (hit.distance / WallRayRange); // 0..1
                        float sideSign;
                        if (WallRayAngles[i] > 0.1f) sideSign = -1f;
                        else if (WallRayAngles[i] < -0.1f) sideSign = 1f;
                        else sideSign = Vector3.Dot(hit.normal, right) >= 0f ? 1f : -1f;

                        steer += right * sideSign * (prox * 0.5f);
                    }
                }
                catch { }
            }
            return steer;
        }

        /// <summary>
        /// Logs the nearest in-cone obstacle and the steering decision, throttled to
        /// <see cref="DiagInterval"/> (debug mode). Reading the obstacle's world
        /// position across lines reveals whether the blocker MOVES; a steady small
        /// <c>d=</c> means the player is pressed against it; <c>CAPPED</c> means the
        /// current cap is the limiter; <c>wedge=</c> is the no-progress duration.
        /// </summary>
        private void LogDiagnostic(Vector3 playerPos, Vector3 desiredDir,
            float nudgeDeg, float cap, float stuckDur, bool deviated)
        {
            if (_nearestObstacle == null) return; // nothing ahead — silent

            float now = Time.time;
            if (now - _lastDiagTime < DiagInterval) return;
            _lastDiagTime = now;

            float desiredDeg = Mathf.Atan2(desiredDir.x, desiredDir.z) * Mathf.Rad2Deg;
            if (desiredDeg < 0f) desiredDeg += 360f;
            bool capped = Mathf.Abs(Mathf.Abs(nudgeDeg) - cap) < 0.5f;

            string name;
            try { name = _nearestObstacle.name; }
            catch { name = "?"; }

            DebugLogger.LogState(
                $"WALK-ASSIST: near='{name}' d={_nearestDist:F2}m " +
                $"npc=({_nearestPos.x:F1},{_nearestPos.z:F1}) " +
                $"player=({playerPos.x:F1},{playerPos.z:F1}) " +
                $"desired={desiredDeg:F0}° nudge={nudgeDeg:F0}° cap={cap:F0}° " +
                $"wedge={stuckDur:F1}s" +
                (capped ? " CAPPED" : "") +
                (IsHardWedged ? " HARD-WEDGE" : "") +
                (deviated ? "" : " (no-net-steer)"));
        }

        #endregion
    }
}
