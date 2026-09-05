using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Floor-grid audit (F11, debug mode, field maps)

        /// <summary>Breadcrumb ↔ grid-node match tolerance, horizontal (m). Half a cell diagonal.</summary>
        private const float AuditMatchXz = 1.1f;
        /// <summary>Breadcrumb ↔ grid-node match tolerance, vertical (m).</summary>
        private const float AuditMatchY = 0.8f;
        /// <summary>A breadcrumb edge counts as connected when the grid route is at most this many times longer.</summary>
        private const float AuditEdgeRouteFactor = 3f;
        /// <summary>...and never shorter than this cap (m), so 1 m edges get a fair search.</summary>
        private const float AuditEdgeRouteMin = 6f;
        /// <summary>A one-way drop is violated when the grid climbs it nearly directly (route ≤ this × straight).</summary>
        private const float AuditDropRouteFactor = 1.5f;
        private const float AuditDropRouteMin = 3f;
        /// <summary>Max individual misses printed per category (the counts are always complete).</summary>
        private const int AuditMaxListedMisses = 30;
        /// <summary>How many of the largest / most-walked runs to print.</summary>
        private const int AuditRunsListed = 6;

        /// <summary>Per-breadcrumb audit facts, filled in by the checks in order.</summary>
        private sealed class AuditState
        {
            public FloorProbeGrid Grid;
            public int[] Match;            // breadcrumb -> grid node, -1 = none
            public bool[] Airborne;        // breadcrumb recorded mid-jump at a ledge
            public int NodesMatched, NodesAirborne, NodesMissed;
            public int EdgesOk, EdgesAirborne, EdgesMissed, EdgeTotal;
            public int Drops, DropViolations;
            public int[] WalkedPerRun;     // breadcrumbs lying inside each ramp run
        }

        /// <summary>
        /// The gate for any geometry-derived ramp finder: builds a fresh
        /// <see cref="FloorProbeGrid"/> for the current map and checks it against
        /// the recorded breadcrumbs, which are ground truth (a real player walked
        /// them). Logs <c>[FLOORGRID]</c> build stats and <c>[GRIDAUDIT]</c> results:
        ///  1. node coverage — every breadcrumb must have a grid node; breadcrumbs
        ///     recorded in mid-air at a jump-down ledge are reported separately;
        ///  2. edge connectivity — every walked edge must be a short grid route;
        ///  3. drop respect — no one-way ledge may be climbable in the grid;
        ///  4. walked slopes — which ramp runs the player's own climbs fall in;
        ///  5. targets — save points / chests: grid component and route vs traversal;
        ///  6. ramp candidates, flagged WALKED (a breadcrumb lies on the run) or not.
        /// Speaks a one-line summary. Changes nothing in live navigation.
        /// </summary>
        public void RunFloorGridAudit(Vector3 playerPos)
        {
            try
            {
                var grid = new FloorProbeGrid();
                string mapId = FieldManager.Instance?.currentFieldmapID.ToString() ?? "?";
                if (!grid.Build(mapId, playerPos, out string why))
                {
                    MelonLogger.Msg($"[SO2RAccess] [FLOORGRID] build failed on {mapId}: {why}");
                    ScreenReader.Say(Loc.Get("debug_gridaudit_nogrid"));
                    return;
                }

                var b = grid.ProbedBounds;
                MelonLogger.Msg(
                    $"[SO2RAccess] [FLOORGRID] built map={mapId} nodes={grid.NodeCount} " +
                    $"components={grid.ComponentCount} rampRuns={grid.RampRunCount} " +
                    $"ms={grid.BuildMs:F0} bounds=({b.min.x:F0},{b.min.y:F0},{b.min.z:F0})..({b.max.x:F0},{b.max.y:F0},{b.max.z:F0})");

                int playerNode = grid.FindNode(playerPos, AuditMatchXz * 2f, AuditMatchY * 2f);
                MelonLogger.Msg(
                    $"[SO2RAccess] [FLOORGRID] player node={playerNode} " +
                    (playerNode >= 0 ? $"component={grid.ComponentOf(playerNode)}" : "(no node under player!)"));

                var st = new AuditState { Grid = grid };
                if (_traversal.HasData)
                {
                    AuditNodeCoverage(st);
                    AuditEdges(st);
                    AuditDrops(st);
                    AuditWalkedSlopes(st);
                }
                else
                {
                    MelonLogger.Msg("[SO2RAccess] [GRIDAUDIT] no breadcrumbs on this map — coverage/edge/drop/slope checks skipped.");
                }

                AuditTargets(st, playerPos, playerNode);
                AuditCandidates(st, playerPos, playerNode);

                bool pass = _traversal.HasData && st.NodesMissed == 0
                            && st.EdgesMissed == 0 && st.DropViolations == 0;
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] RESULT {(pass ? "PASS" : "FAIL")}: " +
                    $"floor nodes {st.NodesMatched}/{st.NodesMatched + st.NodesMissed} (+{st.NodesAirborne} airborne at ledges), " +
                    $"floor edges {st.EdgesOk}/{st.EdgesOk + st.EdgesMissed} (+{st.EdgesAirborne} airborne), " +
                    $"dropViolations={st.DropViolations}/{st.Drops}, candidates={grid.Ramps.Count} (of {grid.RampRunCount} runs)");
                ScreenReader.Say(Loc.Get("debug_gridaudit_result",
                    st.NodesMatched, st.NodesMatched + st.NodesMissed,
                    st.EdgesOk, st.EdgesOk + st.EdgesMissed,
                    st.DropViolations, grid.Ramps.Count));
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[SO2RAccess] [GRIDAUDIT] error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Check 1: each breadcrumb → nearest grid node within tolerance. A miss
        /// whose nearest floor is BELOW it and that touches a recorded one-way drop
        /// is a breadcrumb laid down in mid-air during the jump — not a floor the
        /// grid failed to see. Those are counted as "airborne", not as misses.
        /// </summary>
        private void AuditNodeCoverage(AuditState st)
        {
            var grid = st.Grid;
            var nodes = _traversal.Nodes;
            int total = nodes.Count;
            st.Match = new int[total];
            st.Airborne = new bool[total];

            var dropEnds = new HashSet<int>();
            foreach (var (hi, lo) in _traversal.OneWayDrops) { dropEnds.Add(hi); dropEnds.Add(lo); }

            int listed = 0;
            for (int i = 0; i < total; i++)
            {
                st.Match[i] = grid.FindNode(nodes[i], AuditMatchXz, AuditMatchY);
                if (st.Match[i] >= 0) { st.NodesMatched++; continue; }

                int loose = grid.FindNode(nodes[i], AuditMatchXz, 50f);
                float dy = loose >= 0 ? grid.NodePosition(loose).y - nodes[i].y : float.NaN;
                bool touchesDrop = dropEnds.Contains(i);
                if (!touchesDrop)
                    foreach (int nb in _traversal.OutNeighbours(i))
                        if (dropEnds.Contains(nb)) { touchesDrop = true; break; }
                bool airborne = touchesDrop && loose >= 0 && dy < -AuditMatchY;

                if (airborne) { st.Airborne[i] = true; st.NodesAirborne++; }
                else st.NodesMissed++;

                if (listed++ < AuditMaxListedMisses)
                {
                    Vector3 p = nodes[i];
                    string near = loose >= 0
                        ? $"nearest grid floor dy={dy:+0.00;-0.00}"
                        : "no grid floor in this cell at all";
                    MelonLogger.Msg(
                        $"[SO2RAccess] [GRIDAUDIT] {(airborne ? "AIRBORNE" : "NODE MISS")} breadcrumb {i} " +
                        $"({p.x:F1},{p.y:F1},{p.z:F1}) — {near}{(airborne ? " (jump arc at a ledge)" : "")}");
                }
            }
            MelonLogger.Msg(
                $"[SO2RAccess] [GRIDAUDIT] node coverage {st.NodesMatched}/{total}: " +
                $"{st.NodesMissed} floor misses, {st.NodesAirborne} airborne");
        }

        /// <summary>Check 2: each walked edge must be a short walkable route in the grid.</summary>
        private void AuditEdges(AuditState st)
        {
            var grid = st.Grid;
            var nodes = _traversal.Nodes;
            int listed = 0;
            foreach (var (a, b) in _traversal.Edges)
            {
                st.EdgeTotal++;
                if (st.Airborne[a] || st.Airborne[b]) { st.EdgesAirborne++; continue; }

                float len = Vector3.Distance(nodes[a], nodes[b]);
                string fail = null;
                if (st.Match[a] < 0 || st.Match[b] < 0)
                {
                    fail = "endpoint has no grid node";
                }
                else
                {
                    float cap = Mathf.Max(AuditEdgeRouteFactor * len, AuditEdgeRouteMin);
                    if (grid.TryLocalRoute(st.Match[a], st.Match[b], cap, out _)) { st.EdgesOk++; continue; }
                    fail = grid.ComponentOf(st.Match[a]) != grid.ComponentOf(st.Match[b])
                        ? "different grid components"
                        : $"no grid route within {cap:F1} m";
                }

                st.EdgesMissed++;
                if (listed++ < AuditMaxListedMisses)
                {
                    Vector3 p = nodes[a], q = nodes[b];
                    MelonLogger.Msg(
                        $"[SO2RAccess] [GRIDAUDIT] EDGE MISS {a}->{b} ({p.x:F1},{p.y:F1},{p.z:F1})->({q.x:F1},{q.y:F1},{q.z:F1}) " +
                        $"len={len:F1} dy={q.y - p.y:+0.00;-0.00} — {fail}");
                }
            }
            MelonLogger.Msg(
                $"[SO2RAccess] [GRIDAUDIT] edge connectivity {st.EdgesOk}/{st.EdgeTotal}: " +
                $"{st.EdgesMissed} misses, {st.EdgesAirborne} airborne");
        }

        /// <summary>Check 3: a recorded one-way ledge must not be climbable in the grid.</summary>
        private void AuditDrops(AuditState st)
        {
            var grid = st.Grid;
            var nodes = _traversal.Nodes;
            foreach (var (hi, lo) in _traversal.OneWayDrops)
            {
                st.Drops++;
                if (st.Match[hi] < 0 || st.Match[lo] < 0) continue;
                float straight = Vector3.Distance(nodes[hi], nodes[lo]);
                float cap = Mathf.Max(AuditDropRouteFactor * straight, AuditDropRouteMin);
                if (!grid.TryLocalRoute(st.Match[lo], st.Match[hi], cap, out float len)) continue;

                st.DropViolations++;
                Vector3 h = nodes[hi], l = nodes[lo];
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] DROP VIOLATION ledge ({h.x:F1},{h.y:F1},{h.z:F1})->({l.x:F1},{l.y:F1},{l.z:F1}) " +
                    $"grid climbs it in {len:F1} m (straight {straight:F1} m)");
            }
            MelonLogger.Msg($"[SO2RAccess] [GRIDAUDIT] one-way drops {st.Drops}, violations {st.DropViolations}");
        }

        /// <summary>
        /// Check 4: where the player actually climbed (walked edges at ramp grade),
        /// does the grid see slope, and which runs hold those climbs? Explains a
        /// candidate list that misses a slope the player walked: the slope may be
        /// part of a run whose foot is far away, or its nodes may read as flat.
        /// </summary>
        private void AuditWalkedSlopes(AuditState st)
        {
            var grid = st.Grid;
            var nodes = _traversal.Nodes;
            st.WalkedPerRun = new int[grid.AllRuns.Count];

            // Breadcrumbs inside each run (any breadcrumb, flat or not).
            for (int i = 0; i < st.Match.Length; i++)
            {
                if (st.Match[i] < 0) continue;
                int run = grid.RunOf(st.Match[i]);
                if (run >= 0) st.WalkedPerRun[run]++;
            }

            int slopeEdges = 0, bothSloped = 0, inRun = 0, listedFlat = 0;
            var edgesPerRun = new Dictionary<int, int>();
            foreach (var (a, b) in _traversal.Edges)
            {
                if (st.Match[a] < 0 || st.Match[b] < 0) continue;
                Vector3 p = nodes[a], q = nodes[b];
                float dx = q.x - p.x, dz = q.z - p.z;
                float dxz = Mathf.Sqrt(dx * dx + dz * dz);
                if (dxz < 0.3f) continue;
                float ratio = Mathf.Abs(q.y - p.y) / dxz;
                if (ratio < FloorProbeGrid.RampMinRatio || ratio >= 1.2f) continue; // walked at ramp grade
                slopeEdges++;

                float ra = grid.SteepestStepRatio(st.Match[a]);
                float rb = grid.SteepestStepRatio(st.Match[b]);
                bool sloped = ra >= FloorProbeGrid.RampMinRatio && rb >= FloorProbeGrid.RampMinRatio;
                if (sloped) bothSloped++;
                else if (listedFlat++ < 10)
                    MelonLogger.Msg(
                        $"[SO2RAccess] [GRIDAUDIT] WALKED SLOPE reads FLAT in grid: ({p.x:F1},{p.y:F1},{p.z:F1})->({q.x:F1},{q.y:F1},{q.z:F1}) " +
                        $"walked ratio={ratio:F2} grid steepest a={ra:F2} b={rb:F2}");

                int run = grid.RunOf(st.Match[a]);
                if (run < 0) run = grid.RunOf(st.Match[b]);
                if (run < 0) continue;
                inRun++;
                edgesPerRun[run] = edgesPerRun.TryGetValue(run, out int c) ? c + 1 : 1;
            }
            MelonLogger.Msg(
                $"[SO2RAccess] [GRIDAUDIT] walked slope edges {slopeEdges}: grid sees slope at both ends {bothSloped}, inside a ramp run {inRun}");

            var runs = new List<int>(edgesPerRun.Keys);
            runs.Sort((x, y) => edgesPerRun[y].CompareTo(edgesPerRun[x]));
            for (int i = 0; i < runs.Count && i < AuditRunsListed; i++)
            {
                var r = grid.AllRuns[runs[i]];
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] WALKED RUN {r.RunId}: {edgesPerRun[r.RunId]} walked slope edges, " +
                    $"{st.WalkedPerRun[r.RunId]} breadcrumbs, nodes={r.NodeCount} rise={r.Rise:F1} " +
                    $"foot=({r.Foot.x:F1},{r.Foot.y:F1},{r.Foot.z:F1}) top=({r.Top.x:F1},{r.Top.y:F1},{r.Top.z:F1}) component={r.Component}");
            }

            // The biggest runs on the map, for scale.
            var byNodes = new List<FloorProbeGrid.RampCandidate>(grid.AllRuns);
            byNodes.Sort((x, y) => y.NodeCount.CompareTo(x.NodeCount));
            for (int i = 0; i < byNodes.Count && i < AuditRunsListed; i++)
            {
                var r = byNodes[i];
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] LARGEST RUN {r.RunId}: nodes={r.NodeCount} rise={r.Rise:F1} " +
                    $"breadcrumbs={st.WalkedPerRun[r.RunId]} foot=({r.Foot.x:F1},{r.Foot.y:F1},{r.Foot.z:F1}) top=({r.Top.x:F1},{r.Top.y:F1},{r.Top.z:F1})");
            }
        }

        /// <summary>
        /// Check 5: for every save point and treasure chest, does the grid put it in
        /// the player's component, how long is the grid route, and does the
        /// breadcrumb graph agree? Direct comparison with TRAVERSAL DIAG.
        /// </summary>
        private void AuditTargets(AuditState st, Vector3 playerPos, int playerNode)
        {
            var grid = st.Grid;
            var fm = FieldManager.Instance;
            var targets = new List<(string label, Vector3 pos)>();
            var saves = fm?.FieldSavePointList;
            if (saves != null)
                for (int i = 0; i < saves.Count; i++)
                    if (saves[i] != null) targets.Add(($"save {i}", saves[i].transform.position));
            var chests = UnityEngine.Object.FindObjectsOfType<FieldTreasureBox>();
            if (chests != null)
            {
                int n = 0;
                foreach (var c in chests)
                {
                    if (c == null) continue;
                    targets.Add((c.IsAcquired ? $"opened chest {n}" : $"UNOPENED chest {n}", c.transform.position));
                    n++;
                }
            }

            foreach (var (label, pos) in targets)
            {
                int node = grid.FindNode(pos, AuditMatchXz * 2f, 3f);
                bool trav = _traversal.HasData && _traversal.IsReachable(playerPos, pos);
                string gridSide;
                if (node < 0) gridSide = "no grid floor under target";
                else if (playerNode < 0) gridSide = "no grid floor under player";
                else if (grid.ComponentOf(node) != grid.ComponentOf(playerNode)) gridSide = "different component";
                else gridSide = grid.TryLocalRoute(playerNode, node, float.MaxValue, out float len)
                    ? $"same component, route {len:F0} m"
                    : "same component but no route (unexpected)";
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] TARGET {label} ({pos.x:F1},{pos.y:F1},{pos.z:F1}) grid: {gridSide}; traversal={trav}");
            }
        }

        /// <summary>Check 6: list ramp candidates, flagged WALKED when a breadcrumb lies on the run.</summary>
        private void AuditCandidates(AuditState st, Vector3 playerPos, int playerNode)
        {
            var grid = st.Grid;
            int playerComp = playerNode >= 0 ? grid.ComponentOf(playerNode) : -1;
            for (int i = 0; i < grid.Ramps.Count; i++)
            {
                var c = grid.Ramps[i];
                int walkedCrumbs = st.WalkedPerRun != null ? st.WalkedPerRun[c.RunId] : 0;
                float dist = Vector3.Distance(playerPos, c.Foot);
                string dir = GetCompassDirection(playerPos, c.Foot);
                MelonLogger.Msg(
                    $"[SO2RAccess] [GRIDAUDIT] RAMP {i} {(walkedCrumbs > 0 ? $"WALKED ({walkedCrumbs} breadcrumbs)" : "unverified")} run={c.RunId} " +
                    $"foot=({c.Foot.x:F1},{c.Foot.y:F1},{c.Foot.z:F1}) top=({c.Top.x:F1},{c.Top.y:F1},{c.Top.z:F1}) " +
                    $"rise={c.Rise:F1} nodes={c.NodeCount} component={c.Component}{(c.Component == playerComp ? " (player's)" : "")} " +
                    $"footDist={dist:F0}m {dir} on='{c.FootCollider}'");
            }
        }

        #endregion
    }
}
