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
        /// <summary>Fishing stands audited per water place (designated + first alternate).</summary>
        private const int AuditMaxStandsPerPlace = 2;

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
            EnsureWmMapjumpCache("route audit");
            var locations = _categories[CAT_LOCATION];
            sb.AppendLine($"[RouteAudit] {locations.Count} locations to audit; " +
                $"{_wmMapjumpCache.Count} map jumps for the gate pinch rule.");
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

            // Fishing stands from the baked file: the designated stand and the
            // first alternate of every place (capped — each audit is an A* plus
            // a sweep). Tells whether a refused fishing walk fails on the start
            // side (wedges near the player) or at the stand.
            int fClean = 0, fWedgy = 0, fNoRoute = 0;
            var standsFile = WorldmapFishingStands.Load(fm.WorldmapID);
            if (standsFile != null)
            {
                sb.AppendLine(
                    $"[RouteAudit] fishing stands: {standsFile.Places.Count} places, " +
                    (standsFile.ProofsBaked ? $"proofs baked from {standsFile.ProofAnchors} anchors" : "no route proofs") + ".");
                foreach (var place in standsFile.Places)
                {
                    int count = Math.Min(place.Stands.Count, AuditMaxStandsPerPlace);
                    for (int k = 0; k < count; k++)
                    {
                        var stand = place.Stands[k];
                        string label = $"Fishing place {place.WaterPlaceId} stand {k}" +
                            (stand.Proven
                                ? $" (proven from {stand.ProvenFrom}, {stand.ProofTier})"
                                : standsFile.ProofsBaked && place.ProofAttempted ? " (UNPROVEN)" : "") +
                            (stand.WallClearance >= 0f ? $" wallClear={stand.WallClearance:F1} m" : "") +
                            (stand.RingDistance >= 0f ? $" ring={stand.RingDistance:F1} m" : "");
                        try
                        {
                            bool hasWedge = AuditOneTarget(label, stand.Position, playerPos,
                                mode, wallMask, sb, out bool routed);
                            if (!routed) fNoRoute++;
                            else if (hasWedge) fWedgy++;
                            else fClean++;
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"[RouteAudit] {label}: AUDIT ERROR {ex.Message}");
                        }
                        MelonLogger.Msg(sb.ToString());
                        sb.Clear();
                    }
                }
            }

            swTotal.Stop();
            string summary =
                $"[RouteAudit] ===== SUMMARY: {clean} clean, {wedgy} with wedge " +
                $"points, {noRoute} no-route; fishing stands {fClean} clean, {fWedgy} wedgy, " +
                $"{fNoRoute} no-route; in {swTotal.ElapsedMilliseconds}ms =====";
            if (wedgyNames.Count > 0)
                summary += $" wedgy: {string.Join(", ", wedgyNames)}";
            MelonLogger.Msg(summary);
            ScreenReader.Say(
                $"Route audit complete. {clean} routes clean, {wedgy} with " +
                $"wedge points, {noRoute} without a route. Fishing stands: {fClean} clean, " +
                $"{fWedgy} with wedge points, {fNoRoute} without a route. Check log.");
        }

        /// <summary>Plans and physics-validates the route to one location
        /// (target = its enter-trigger ring point, as in a real walk).</summary>
        private bool AuditOneLocation(string label, Vector3 locationPos,
            Vector3 playerPos, WorldmapTravelMode mode, int wallMask,
            StringBuilder sb, out bool routed)
        {
            // 1. Same target resolution as a real walk.
            Vector3 target = ComputeEnterTriggerTarget(locationPos, playerPos);
            return AuditOneTarget(label, target, playerPos, mode, wallMask, sb, out routed);
        }

        /// <summary>Plans and physics-validates the route to one exact target
        /// (a ring point or a fishing stand). Returns true when the planned
        /// route has wedge points; <paramref name="routed"/> is false when no
        /// route exists.</summary>
        private bool AuditOneTarget(string label, Vector3 target,
            Vector3 playerPos, WorldmapTravelMode mode, int wallMask,
            StringBuilder sb, out bool routed)
        {
            routed = false;

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

            // Same goal set as a real walk (the whole entrance ring for a
            // location, the exact point for a fishing stand).
            var mainLeg = WorldmapPathfinder.FindPath(mainStart,
                WmRouteGoals(target), mode);
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
            int wedges = 0, heightMismatches = 0, forgiven = 0;
            float worstMismatch = 0f;
            Vector3? firstWedge = null;
            if (exitLeg != null)
                SweepLeg(exitLeg, "exit", label, wallMask, sb,
                    ref wedges, ref heightMismatches, ref worstMismatch, ref firstWedge, ref forgiven);
            SweepLeg(mainLeg, "main", label, wallMask, sb,
                ref wedges, ref heightMismatches, ref worstMismatch, ref firstWedge, ref forgiven);

            int totalWps = (exitLeg?.Length ?? 0) + mainLeg.Length;
            string firstWedgeNote = "";
            if (firstWedge.HasValue)
            {
                Vector3 w = firstWedge.Value;
                firstWedgeNote = $" | first wedge {FlatDistance(w, playerPos):F0}m from player, " +
                    $"{FlatDistance(w, target):F0}m from target";
            }
            sb.AppendLine(
                $"[RouteAudit] {label}: " +
                (wedges == 0 ? "WALKABLE" : $"{wedges} WEDGE SEGMENTS") +
                (forgiven > 0 ? $" ({forgiven} gate pinch segments forgiven, within " +
                    $"{WorldmapMapjumps.RingWedgeMeters:F0}m of an entrance ring)" : "") +
                $" | legs: exit={exitTier}({exitLeg?.Length ?? 0}wp) " +
                $"main={mainTier}({mainLeg.Length}wp) total={totalWps}wp" +
                $" | grid-vs-live height mismatches>1m: {heightMismatches}" +
                (heightMismatches > 0 ? $" (worst {worstMismatch:F1}m)" : "") +
                firstWedgeNote);
            return wedges > 0;
        }

        /// <summary>Sweeps the body capsule along every segment of one leg,
        /// logging each blocked segment (capped) and height mismatches.
        /// <paramref name="firstWedge"/> receives the first blocked segment's
        /// start when it is still unset.</summary>
        private void SweepLeg(Vector3[] leg, string legName, string label,
            int wallMask, StringBuilder sb, ref int wedges,
            ref int heightMismatches, ref float worstMismatch,
            ref Vector3? firstWedge, ref int forgiven)
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

                    if (WorldmapMapjumps.IsGatePinch(_wmMapjumpCache, leg[i], blocker, out string why))
                    {
                        // Same rule as the walk: a gate pinch is not a wedge.
                        forgiven++;
                        if (forgiven <= 2)
                            sb.AppendLine(
                                $"[RouteAudit] {label}: GATE PINCH {legName} wp[{i}] " +
                                $"({leg[i].x:F1},{leg[i].z:F1}) hit '{blocker.name}' " +
                                $"L{blocker.gameObject.layer}, {why} — forgiven.");
                        continue;
                    }
                    wedges++;
                    firstWedge ??= leg[i];
                    if (wedges <= AuditMaxWedgeLogs)
                    {
                        sb.AppendLine(
                            $"[RouteAudit] {label}: WEDGE {legName} wp[{i}] " +
                            $"({leg[i].x:F1},{leg[i].z:F1})→" +
                            $"({leg[i + 1].x:F1},{leg[i + 1].z:F1}) " +
                            $"hit '{blocker.name}' " +
                            $"L{blocker.gameObject.layer} tag={blocker.tag} " +
                            $"(liveY={liveY:F1} gridY={leg[i].y:F1}) {why}");
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

        /// <summary>
        /// Horizontal distance (m) the player's body capsule can move from
        /// <paramref name="pos"/> before a wall-mask collider stops it, over
        /// the 8 compass directions, capped at <see cref="BodyClearanceCap"/>.
        /// Returns 0 with <paramref name="blocker"/> set when the capsule
        /// already overlaps a collider standing still — nobody can stand
        /// there whatever route reaches it (2026-09-13: the Arlia stand 5 m
        /// south of the town gate sat 0.2 m inside the town's own wall boxes;
        /// the game's bubble ray from that cell saw water, the player never
        /// could reach the cell). Same capsule, ground probe and mask as
        /// <see cref="SweepSegmentBlocked"/>, so the answer matches what the
        /// walk experiences.
        /// </summary>
        internal static float BodyWallClearance(Vector3 pos, int wallMask, out Collider blocker)
        {
            blocker = null;
            var probe = new Vector3(pos.x, pos.y + 2f, pos.z);
            float liveY = GameUtility.CalcHeight(probe, out bool ok, 8f);
            if (!ok) liveY = pos.y;
            Vector3 p1 = new Vector3(pos.x, liveY + AuditStepAllowance + StandBodyRadius, pos.z);
            Vector3 p2 = new Vector3(pos.x, liveY + AuditBodyHeight - StandBodyRadius, pos.z);

            var overlaps = UnityEngine.Physics.OverlapCapsule(p1, p2, StandBodyRadius,
                wallMask, QueryTriggerInteraction.Ignore);
            if (overlaps != null)
            {
                for (int i = 0; i < overlaps.Length; i++)
                {
                    if (overlaps[i] == null) continue;
                    blocker = overlaps[i];
                    return 0f;
                }
            }

            float nearest = BodyClearanceCap;
            for (int i = 0; i < BodyClearanceDirections.Length; i++)
            {
                if (UnityEngine.Physics.CapsuleCast(p1, p2, StandBodyRadius,
                        BodyClearanceDirections[i], out RaycastHit hit, BodyClearanceCap,
                        wallMask, QueryTriggerInteraction.Ignore)
                    && hit.collider != null && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    blocker = hit.collider;
                }
            }
            return nearest;
        }

        /// <summary>Cap (m) of <see cref="BodyWallClearance"/>: "at least this far" is all a stand needs to know.</summary>
        internal const float BodyClearanceCap = 2f;

        /// <summary>
        /// Body radius for the standing-still fit test: the game's own character
        /// capsule (bounds 1.0 m wide), not the route sweep's slightly slimmer
        /// 0.45 m. A stand where the game's body does not fit cannot be stood on;
        /// the Arlia stand at (−44.5,−414) read 0.03 m with the slim capsule and
        /// stuck the 2026-09-09 walk. The route sweep keeps its own radius.
        /// </summary>
        internal const float StandBodyRadius = 0.5f;

        /// <summary>The 8 compass directions the clearance casts along.</summary>
        private static readonly Vector3[] BodyClearanceDirections =
        {
            new Vector3(0, 0, 1), new Vector3(0.7071f, 0, 0.7071f), new Vector3(1, 0, 0),
            new Vector3(0.7071f, 0, -0.7071f), new Vector3(0, 0, -1), new Vector3(-0.7071f, 0, -0.7071f),
            new Vector3(-1, 0, 0), new Vector3(-0.7071f, 0, 0.7071f),
        };

        /// <summary>Joins up to 6 ancestor names of a collider, nearest parent first (log evidence).</summary>
        internal static string ColliderChain(Collider col)
        {
            if (col == null) return "(none)";
            var parts = new System.Collections.Generic.List<string> { col.name };
            var cur = col.transform.parent;
            for (int i = 0; i < 6 && cur != null; i++)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", parts);
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
