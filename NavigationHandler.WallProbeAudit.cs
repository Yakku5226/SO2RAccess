using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Wall-probe audit (F11, debug mode, field maps)

        /// <summary>A probe verdict this close to the walked edge's far end is not a false wall (m).</summary>
        private const float WallAuditSlack = 0.3f;
        /// <summary>Edges shorter than this cannot be judged (probe step is 0.75 m).</summary>
        private const float WallAuditMinEdge = 0.5f;

        /// <summary>
        /// Links no longer than this (m) are reported separately: breadcrumbs merge
        /// within 1.6 m, so a long straight link between merged nodes can clip a
        /// corner the player actually walked round, while a short one cannot by much.
        /// </summary>
        private const float WallAuditShortLink = 2f;
        /// <summary>Individual false walls printed (the counts are always complete).</summary>
        private const int WallAuditMaxListed = 30;
        /// <summary>At most this many breadcrumbs sampled for the wall-density figure.</summary>
        private const int WallAuditDensityNodes = 300;
        /// <summary>Range of the density probes (m).</summary>
        private const float WallAuditDensityRange = 3f;

        /// <summary>
        /// The gate for the manual-navigation wall sounds: replays every recorded
        /// breadcrumb edge (ground truth — the player walked it) through
        /// <see cref="WallProbe"/> and counts FALSE WALLS: an obstacle reported
        /// nearer than the edge's far end. Ledge edges (one-way drops, or steeper
        /// than the breadcrumb graph's own ledge ratio) are skipped: the player got
        /// down them by jumping, and a wall sound there is correct.
        ///
        /// Every false wall is logged with position, test, collider and layer, so
        /// any exclusion is made from evidence. Also logs an information-only wall
        /// density (share of breadcrumbs with an obstacle within 3 m in any of eight
        /// directions) to show the probe is not simply blind. PASS = zero false walls.
        /// </summary>
        public void RunWallProbeAudit(Vector3 playerPos)
        {
            if (!_traversal.HasData)
            {
                MelonLogger.Msg("[SO2RAccess] [WALLAUDIT] no breadcrumbs on this map — nothing to audit.");
                return;
            }

            var nodes = _traversal.Nodes;
            var drops = new HashSet<(int, int)>();
            foreach (var (hi, lo) in _traversal.OneWayDrops) drops.Add((hi, lo));

            int judged = 0, skippedShort = 0, skippedLedge = 0, falseWalls = 0, listed = 0;
            int judgedShort = 0, falseShort = 0;
            var byTest = new Dictionary<WallProbe.Test, int>();
            var byCollider = new Dictionary<string, int>();
            float startMs = Time.realtimeSinceStartup * 1000f;

            foreach (var (a, b) in _traversal.Edges)
            {
                Vector3 p = nodes[a], q = nodes[b];
                float dx = q.x - p.x, dz = q.z - p.z;
                float dxz = Mathf.Sqrt(dx * dx + dz * dz);
                float len = Vector3.Distance(p, q);
                if (len < WallAuditMinEdge || dxz < 0.2f) { skippedShort++; continue; }
                if (drops.Contains((a, b)) || Mathf.Abs(q.y - p.y) / dxz >= 1.2f) { skippedLedge++; continue; }

                judged++;
                bool shortLink = dxz <= WallAuditShortLink;
                if (shortLink) judgedShort++;
                var r = WallProbe.ProbeDirection(p, new Vector3(dx, 0f, dz), dxz, describe: true);
                if (!r.HasObstacle || r.Distance >= dxz - WallAuditSlack) continue;

                falseWalls++;
                if (shortLink) falseShort++;
                byTest[r.Test] = byTest.TryGetValue(r.Test, out int c) ? c + 1 : 1;
                string key = $"{r.Collider ?? "?"}/L{r.Layer}";
                byCollider[key] = byCollider.TryGetValue(key, out int k) ? k + 1 : 1;

                if (listed++ < WallAuditMaxListed)
                    MelonLogger.Msg(
                        $"[SO2RAccess] [WALLAUDIT] FALSE WALL {a}->{b} ({p.x:F1},{p.y:F1},{p.z:F1})->({q.x:F1},{q.y:F1},{q.z:F1}) " +
                        $"walked {dxz:F1} m, probe says {r}");
            }

            foreach (var kv in byTest)
                MelonLogger.Msg($"[SO2RAccess] [WALLAUDIT] false walls by test: {kv.Key} = {kv.Value}");
            var colliders = new List<KeyValuePair<string, int>>(byCollider);
            colliders.Sort((x, y) => y.Value.CompareTo(x.Value));
            for (int i = 0; i < colliders.Count && i < 15; i++)
                MelonLogger.Msg($"[SO2RAccess] [WALLAUDIT] false walls by collider: {colliders[i].Key} = {colliders[i].Value}");

            int density = AuditWallDensity(nodes);
            float ms = Time.realtimeSinceStartup * 1000f - startMs;

            bool pass = falseWalls == 0 && judged > 0;
            MelonLogger.Msg(
                $"[SO2RAccess] [WALLAUDIT] short links (<= {WallAuditShortLink:F0} m): {falseShort} false walls on {judgedShort} judged; " +
                $"long links: {falseWalls - falseShort} on {judged - judgedShort}");
            MelonLogger.Msg(
                $"[SO2RAccess] [WALLAUDIT] RESULT {(pass ? "PASS" : "FAIL")}: {falseWalls} false walls on {judged} walked edges " +
                $"(skipped {skippedShort} short, {skippedLedge} ledge), wall density {density}% of sampled breadcrumbs, {ms:F0} ms");
            ScreenReader.Say(Loc.Get("debug_wallaudit_result", falseWalls, judged, density), false);
        }

        /// <summary>
        /// Share (per cent) of sampled breadcrumbs that have an obstacle within
        /// <see cref="WallAuditDensityRange"/> in at least one of eight world
        /// directions. Information only — a probe that never sees a wall would
        /// pass the false-wall check trivially, and this exposes that.
        /// </summary>
        private static int AuditWallDensity(IReadOnlyList<Vector3> nodes)
        {
            if (nodes.Count == 0) return 0;
            int stride = Mathf.Max(1, nodes.Count / WallAuditDensityNodes);
            int sampled = 0, withWall = 0;
            for (int i = 0; i < nodes.Count; i += stride)
            {
                sampled++;
                for (int d = 0; d < 8; d++)
                {
                    float ang = d * Mathf.PI / 4f;
                    var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    if (WallProbe.ProbeDirection(nodes[i], dir, WallAuditDensityRange).HasObstacle)
                    {
                        withWall++;
                        break;
                    }
                }
            }
            return sampled == 0 ? 0 : Mathf.RoundToInt(100f * withWall / sampled);
        }

        #endregion
    }
}
