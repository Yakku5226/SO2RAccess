using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region NavMesh audit (F11, debug mode, field maps)

        /// <summary>How far (m) a breadcrumb may sit from the NavMesh and still count as on it.</summary>
        private const float NavMeshAuditSnapRadius = 0.6f;
        /// <summary>Height difference (m) above which a NavMesh sample is another floor, not this one.</summary>
        private const float NavMeshAuditMaxDy = 0.6f;
        /// <summary>Most misses and cuts printed one per line.</summary>
        private const int NavMeshAuditMaxListed = 30;

        /// <summary>
        /// Checks the map's NavMesh against the recorded breadcrumbs: every
        /// breadcrumb should lie on the NavMesh (a MISS is a walked place it does
        /// not cover) and every walked link should be clear on it (a CUT is a
        /// link the NavMesh thinks is blocked). Each cut is also judged by the
        /// wall probe, so "cut, no wall" reads as a NavMesh that lies about the
        /// map and "cut, wall" as a breadcrumb link through a wall. Auto-walk
        /// trusts a complete NavMesh path first, so a lying NavMesh sends the
        /// player where it is wrong: the Krosse event copy (2026-09-23) linked
        /// the low road to the inn only through a closed door while the ramp the
        /// player really walks was cut. Log-only; nothing walks on it.
        /// </summary>
        public void RunNavMeshAudit(Vector3 playerPos)
        {
            const string tag = "[SO2RAccess] [NAVMESHAUDIT]";
            if (_isWorldmap || _traversal == null || !_traversal.HasData)
            {
                MelonLogger.Msg($"{tag} no breadcrumbs on this map — nothing to compare.");
                return;
            }

            float startMs = Time.realtimeSinceStartup * 1000f;
            var nodes = _traversal.Nodes;
            var onMesh = new Vector3?[nodes.Count];
            int misses = 0, listed = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (NavMesh.SamplePosition(nodes[i], out NavMeshHit hit, NavMeshAuditSnapRadius, NavMesh.AllAreas)
                    && Mathf.Abs(hit.position.y - nodes[i].y) <= NavMeshAuditMaxDy)
                {
                    onMesh[i] = hit.position;
                    continue;
                }
                misses++;
                if (listed++ < NavMeshAuditMaxListed)
                    MelonLogger.Msg($"{tag} MISS breadcrumb {i} ({nodes[i].x:F1},{nodes[i].y:F1},{nodes[i].z:F1}) is not on the NavMesh.");
            }

            var drops = new HashSet<(int, int)>();
            foreach (var (hi, lo) in _traversal.OneWayDrops) drops.Add((hi, lo));
            var seen = new HashSet<(int, int)>();
            int judged = 0, cuts = 0, unjudged = 0, skippedDrops = 0;
            listed = 0;
            foreach (var (a, b) in _traversal.Edges)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!seen.Add(key)) continue;
                if (drops.Contains((a, b)) || drops.Contains((b, a))) { skippedDrops++; continue; }
                if (onMesh[a] == null || onMesh[b] == null) { unjudged++; continue; }

                judged++;
                if (!NavMesh.Raycast(onMesh[a].Value, onMesh[b].Value, out NavMeshHit cut, NavMesh.AllAreas))
                    continue;

                cuts++;
                if (listed++ >= NavMeshAuditMaxListed) continue;
                Vector3 p = nodes[a], q = nodes[b];
                Vector3 dir = new Vector3(q.x - p.x, 0f, q.z - p.z);
                var wall = WallProbe.ProbeDirection(p, dir, dir.magnitude, describe: true);
                string verdict = wall.HasObstacle && wall.Distance < dir.magnitude - WallAuditSlack
                    ? "wall probe agrees (link through a wall?)"
                    : "no wall on the walked link (NavMesh lies here)";
                MelonLogger.Msg(
                    $"{tag} CUT {a}->{b} ({p.x:F1},{p.y:F1},{p.z:F1})->({q.x:F1},{q.y:F1},{q.z:F1}) " +
                    $"blocked at ({cut.position.x:F1},{cut.position.y:F1},{cut.position.z:F1}); {verdict}: {wall}");
            }

            float ms = Time.realtimeSinceStartup * 1000f - startMs;
            bool pass = misses == 0 && cuts == 0 && judged > 0;
            MelonLogger.Msg(
                $"{tag} RESULT {(pass ? "PASS" : judged == 0 ? "INCONCLUSIVE" : "FAIL")}: " +
                $"{misses} of {nodes.Count} breadcrumbs off the NavMesh; {cuts} of {judged} walked links cut " +
                $"(skipped {skippedDrops} jump-downs, {unjudged} with an end off the mesh), {ms:F0} ms");
            ScreenReader.Say(Loc.Get("debug_navmeshaudit_result", misses, nodes.Count, cuts, judged), false);
        }

        #endregion
    }
}
