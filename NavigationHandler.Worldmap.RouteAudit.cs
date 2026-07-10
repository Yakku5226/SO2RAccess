using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// World-map ROUTE AUDITOR (debug only, F7 on the world map). For every
    /// location in the nav list it replays the exact auto-walk planning
    /// pipeline (enter-trigger ring point → safe exit → both path legs) and
    /// then PHYSICS-VALIDATES the planned route without walking it: the
    /// player's body capsule is swept along every waypoint segment against
    /// the game's live collision. Every segment the body cannot pass is a
    /// spot where a real walk would wedge. Built 2026-07-10 after three
    /// D1 wedge repros — one keypress replaces a full in-game walk test per
    /// location and pinpoints every grid-vs-physics disagreement at once.
    /// </summary>
    public partial class NavigationHandler
    {
        /// <summary>Capsule radius used for the audit sweep. Slightly under
        /// the real 0.50m body so brushing a wall is not counted — only
        /// segments the body DEFINITELY cannot pass are reported.</summary>
        private const float AuditCapsuleRadius = 0.45f;
        /// <summary>Vertical lift of the swept capsule: steps/slopes lower
        /// than this are walkable and must not count as hits.</summary>
        private const float AuditStepAllowance = 0.45f;
        /// <summary>Player capsule height (measured: 1.70m).</summary>
        private const float AuditBodyHeight = 1.7f;
        /// <summary>Max wedge lines logged per route.</summary>
        private const int AuditMaxWedgeLogs = 8;

        /// <summary>
        /// Runs the full route audit from the player's current position.
        /// Synchronous — the game freezes while it runs (announced).
        /// </summary>
        internal void RunWorldmapRouteAudit()
        {
            var fm = FieldManager.Instance;
            if (fm == null || !fm.IsWorldmap())
            {
                ScreenReader.Say("Route audit only works on the world map.");
                return;
            }
            var player = fm.GetControlPlayer();
            if (player == null)
            {
                ScreenReader.Say("No player found.");
                return;
            }

            ScreenReader.Say(
                "Route audit started. The game freezes while it runs.");
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            Vector3 playerPos = player.transform.position;
            var mode = WorldmapTravel.CurrentMode();

            var sb = new StringBuilder();
            sb.AppendLine("[RouteAudit] ================= WORLD MAP ROUTE AUDIT =================");
            sb.AppendLine($"[RouteAudit] player=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1}) mode={mode}");

            int wallMask = AuditWallMask(player, sb);

            // Same list build the nav menu uses (logs its own [WMReach] lines).
            BuildWorldmapLocations(playerPos, fm.WorldmapID);
            var locations = _categories[CAT_LOCATION];
            sb.AppendLine($"[RouteAudit] {locations.Count} locations to audit.");
            MelonLogger.Msg(sb.ToString());
            sb.Clear();

            int clean = 0, wedgy = 0, noRoute = 0;
            var wedgyNames = new List<string>();

            for (int i = 0; i < locations.Count; i++)
            {
                var item = locations[i];
                try
                {
                    bool hasWedge = AuditOneLocation(
                        item.Label, item.Position, playerPos, mode,
                        wallMask, sb, out bool routed);
                    if (!routed) noRoute++;
                    else if (hasWedge) { wedgy++; wedgyNames.Add(item.Label); }
                    else clean++;
                }
                catch (Exception ex)
                {
                    sb.AppendLine(
                        $"[RouteAudit] {item.Label}: AUDIT ERROR {ex.Message}");
                }
                MelonLogger.Msg(sb.ToString());
                sb.Clear();
            }

            swTotal.Stop();
            string summary =
                $"[RouteAudit] ===== SUMMARY: {clean} clean, {wedgy} with wedge " +
                $"points, {noRoute} no-route, in {swTotal.ElapsedMilliseconds}ms =====";
            if (wedgyNames.Count > 0)
                summary += $" wedgy: {string.Join(", ", wedgyNames)}";
            MelonLogger.Msg(summary);
            ScreenReader.Say(
                $"Route audit complete. {clean} routes clean, {wedgy} with " +
                $"wedge points, {noRoute} without a route. Check log.");
        }

        /// <summary>Plans and physics-validates the route to one location.
        /// Returns true when the planned route has wedge points;
        /// <paramref name="routed"/> is false when no route exists.</summary>
        private bool AuditOneLocation(string label, Vector3 locationPos,
            Vector3 playerPos, WorldmapTravelMode mode, int wallMask,
            StringBuilder sb, out bool routed)
        {
            routed = false;

            // 1. Same target resolution as a real walk.
            Vector3 target = ComputeEnterTriggerTarget(locationPos, playerPos);

            // 2. Same safe-exit logic.
            Vector3 safeExit = ComputeSafeExitPoint(playerPos);
            bool usingSafeExit = Vector3.Distance(safeExit, playerPos) > 5f;

            // 3. Same two legs (fresh planning state: no blocked zones).
            Vector3[] exitLeg = null;
            string exitTier = "-";
            Vector3 mainStart = playerPos;
            if (usingSafeExit)
            {
                exitLeg = WorldmapPathfinder.FindPath(playerPos, safeExit, mode);
                if (exitLeg != null && exitLeg.Length > 0)
                {
                    exitTier = WorldmapPathfinder.LastPathUsedFloorTier
                        ? "FLOOR" : "comfort";
                    mainStart = safeExit;
                }
                else
                {
                    exitLeg = null; // real walk goes direct then, so do we
                }
            }

            var mainLeg = WorldmapPathfinder.FindPath(mainStart, target, mode);
            if (mainLeg == null || mainLeg.Length == 0)
            {
                sb.AppendLine(
                    $"[RouteAudit] {label}: NO ROUTE ({mode}) — " +
                    (WorldmapPathfinder.LastNoPathWasDisconnected
                        ? "proven disconnected (honest refusal)."
                        : "pathfinder returned nothing (transient?)."));
                return false;
            }
            routed = true;
            string mainTier = WorldmapPathfinder.LastPathUsedFloorTier
                ? "FLOOR" : "comfort";

            // 4. Physics sweep over both legs.
            int wedges = 0, heightMismatches = 0;
            float worstMismatch = 0f;
            if (exitLeg != null)
                SweepLeg(exitLeg, "exit", label, wallMask, sb,
                    ref wedges, ref heightMismatches, ref worstMismatch);
            SweepLeg(mainLeg, "main", label, wallMask, sb,
                ref wedges, ref heightMismatches, ref worstMismatch);

            int totalWps = (exitLeg?.Length ?? 0) + mainLeg.Length;
            sb.AppendLine(
                $"[RouteAudit] {label}: " +
                (wedges == 0 ? "WALKABLE" : $"{wedges} WEDGE SEGMENTS") +
                $" | legs: exit={exitTier}({exitLeg?.Length ?? 0}wp) " +
                $"main={mainTier}({mainLeg.Length}wp) total={totalWps}wp" +
                $" | grid-vs-live height mismatches>1m: {heightMismatches}" +
                (heightMismatches > 0 ? $" (worst {worstMismatch:F1}m)" : ""));
            return wedges > 0;
        }

        /// <summary>Sweeps the body capsule along every segment of one leg,
        /// logging each blocked segment (capped) and height mismatches.</summary>
        private void SweepLeg(Vector3[] leg, string legName, string label,
            int wallMask, StringBuilder sb, ref int wedges,
            ref int heightMismatches, ref float worstMismatch)
        {
            int unresolved = 0;
            for (int i = 0; i < leg.Length - 1; i++)
            {
                try
                {
                    if (!SweepSegmentBlocked(leg[i], leg[i + 1], wallMask,
                            out Collider blocker, out float liveY,
                            out bool hitUnresolved))
                    {
                        if (hitUnresolved) unresolved++;
                        float mism = Mathf.Abs(liveY - leg[i].y);
                        if (mism > 1f)
                        {
                            heightMismatches++;
                            if (mism > Mathf.Abs(worstMismatch))
                                worstMismatch = liveY - leg[i].y;
                        }
                        continue;
                    }

                    wedges++;
                    if (wedges <= AuditMaxWedgeLogs)
                    {
                        sb.AppendLine(
                            $"[RouteAudit] {label}: WEDGE {legName} wp[{i}] " +
                            $"({leg[i].x:F1},{leg[i].z:F1})→" +
                            $"({leg[i + 1].x:F1},{leg[i + 1].z:F1}) " +
                            $"hit '{blocker.name}' " +
                            $"L{blocker.gameObject.layer} tag={blocker.tag} " +
                            $"(liveY={liveY:F1} gridY={leg[i].y:F1})");
                    }
                    else if (wedges == AuditMaxWedgeLogs + 1)
                    {
                        sb.AppendLine(
                            $"[RouteAudit] {label}: further wedge segments " +
                            "suppressed (counted in summary).");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine(
                        $"[RouteAudit] {label}: sweep error at {legName} " +
                        $"wp[{i}]: {ex.Message} — segment skipped.");
                }
            }
            if (unresolved > 0)
                sb.AppendLine(
                    $"[RouteAudit] {label}: {unresolved} {legName} segments " +
                    "had unresolvable overlap hits (ignored).");
        }

        /// <summary>
        /// Physics-sweeps ONE route segment with the player's body capsule.
        /// Returns true when a real collider blocks it (reported in
        /// <paramref name="blocker"/>). Hits whose collider cannot be
        /// resolved (cast started overlapping geometry — probe artifact) are
        /// NOT counted as blocked; <paramref name="hitUnresolved"/> reports
        /// them. The capsule stands on live ground probed from just above
        /// the waypoint (short ray — a long ray from high up lands on
        /// OVERHANGS above passes and sweeps inside solid rock, which is
        /// what poisoned the 2026-07-10 audits 1 and 2).
        /// </summary>
        internal static bool SweepSegmentBlocked(Vector3 a, Vector3 b,
            int wallMask, out Collider blocker, out float liveY,
            out bool hitUnresolved)
        {
            blocker = null;
            hitUnresolved = false;

            var probe = new Vector3(a.x, a.y + 2f, a.z);
            liveY = GameUtility.CalcHeight(probe, out bool ok, 8f);
            if (!ok) liveY = a.y;

            Vector3 baseA = new Vector3(a.x, liveY, a.z);
            Vector3 dir = b - a;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.01f) return false;
            dir /= dist;

            Vector3 p1 = baseA + Vector3.up *
                (AuditStepAllowance + AuditCapsuleRadius);
            Vector3 p2 = baseA + Vector3.up *
                (AuditBodyHeight - AuditCapsuleRadius);

            if (!UnityEngine.Physics.CapsuleCast(p1, p2, AuditCapsuleRadius,
                    dir, out RaycastHit hit, dist, wallMask,
                    QueryTriggerInteraction.Ignore))
                return false;

            var col = hit.collider;
            if (col == null)
            {
                hitUnresolved = true;
                return false;
            }
            blocker = col;
            return true;
        }

        /// <summary>Resolves the collision mask the sweep uses: the game's
        /// LIVE per-mode wall mask plus layer 24 (streamed rock bodies —
        /// physically solid even though movement masks omit them). Falls
        /// back to L22|L23|L24 with a log line when the live mask is
        /// unreadable.</summary>
        private static int AuditWallMask(FieldPlayer player, StringBuilder sb)
        {
            int mask = ResolveBodySweepMask(player, out string note);
            sb.AppendLine($"[RouteAudit] {note}");
            return mask;
        }

        /// <summary>Sweep-mask resolution shared by the auditor and the
        /// pre-walk route validation in the planner.</summary>
        internal static int ResolveBodySweepMask(FieldPlayer player,
            out string note)
        {
            int mask;
            try
            {
                mask = player.GetLayerMaskWall();
                note = $"wall mask (live): 0x{mask:X8} + L24 rocks";
            }
            catch (Exception ex)
            {
                mask = (1 << 22) | (1 << 23);
                note = $"live wall mask unreadable ({ex.Message}) — " +
                    "using L22|L23 + L24.";
            }
            return mask | (1 << 24);
        }
    }
}
