using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// F10 grid-truth probe (debug only, world map): for every grid cell near
    /// the player, compares the BAKED cell with a LIVE replay of the bake's own
    /// probe (<see cref="WorldmapGridProbe.ProbeObstacles"/>, same masks, same
    /// ground query) and with the game-body capsule
    /// (<see cref="NavigationHandler.BodyWallClearance"/>, at the cell's stored
    /// clearance offset — the point the walk actually aims at). Built
    /// 2026-09-13 for the Arlia gate, where the grid has the town wall as open
    /// ground. Always logs the cells around the nearest fishing stand ("FOCUS"),
    /// then the disagreeing cells nearest the player per class, each with every
    /// collider physically within the probe radius on ANY layer ("seen=") so a
    /// wall the probe ignores is visible next to the walls it counts.
    /// </summary>
    public static partial class WorldmapGridDiagnostics
    {
        /// <summary>Half-size (m) of the square of cells around the player that is compared.</summary>
        private const float TruthRadiusMeters = 8f;

        /// <summary>Per-class caps on logged cells (nearest to the player first).</summary>
        private const int TruthAbsentLines = 15, TruthGeometryLines = 10, TruthHeightLines = 10, TruthReverseLines = 3;

        /// <summary>Grid vs live ground difference (m) that counts as a height disagreement.</summary>
        private const float TruthHeightTolerance = 0.5f;

        /// <summary>A fishing stand this close to the player becomes the FOCUS centre.</summary>
        private const float TruthFocusStandMeters = 15f;

        /// <summary>One compared cell.</summary>
        private sealed class TruthCell
        {
            public int Ax, Az;
            public Vector3 World;
            public float DistToPlayer;
            public string Verdict;
            public bool Open, Sealed;
            public float Clearance, GridY, LiveY, Body;
            public bool Offset;
            public WorldmapGridProbe.ProbeResult Replay;
            public Collider BodyBlocker;
        }

        /// <summary>Runs the comparison and logs it under the [WMTruth] tag; speaks the mismatch count.</summary>
        public static void LogGridTruthProbe()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap()) return;
                var player = fm.GetControlPlayer();
                if (player == null) return;
                var grid = WorldmapPathfinder.GetCachedGrid(fm.WorldmapID);
                if (grid == null)
                {
                    MelonLogger.Msg("[WMTruth] no cached grid — nothing to compare.");
                    return;
                }

                int footMask = GameRenderManager.LayerMaskWall;
                int bunnyMask = GameRenderManager.LayerMaskBunnyWall;
                int footSolidMask = footMask & ~(1 << WorldmapGridProbe.CharaWallLayer);
                int charaWallMask = footMask & (1 << WorldmapGridProbe.CharaWallLayer);
                int bodyMask = NavigationHandler.ResolveBodySweepMask(player, out string bodyNote);

                Vector3 pos = player.transform.position;
                var sb = new StringBuilder();
                sb.AppendLine("[WMTruth] === GRID TRUTH PROBE ===");
                sb.AppendLine($"[WMTruth] player=({pos.x:F1},{pos.y:F1},{pos.z:F1}) radius={TruthRadiusMeters:F0} m | " +
                    $"masks: live foot=0x{footMask:X8} grid foot=0x{grid.FootMask:X8} bunny=0x{bunnyMask:X8} | body {bodyNote}" +
                    (footMask != grid.FootMask ? " | FOOT MASK DIFFERS FROM THE BAKE" : ""));

                grid.WorldToGrid(pos.x, pos.z, out int cx, out int cz);
                int r = (int)(TruthRadiusMeters / grid.CellSize);
                var all = new List<TruthCell>();
                int gridOpen = 0, absent = 0, geometry = 0, height = 0, reverse = 0, sealedCount = 0, noGround = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                for (int ax = cx - r; ax <= cx + r; ax++)
                {
                    for (int az = cz - r; az <= cz + r; az++)
                    {
                        if (ax < 0 || ax >= grid.GridW || az < 0 || az >= grid.GridH) continue;
                        var c = new TruthCell { Ax = ax, Az = az, World = grid.GridToWorld(ax, az) };
                        c.DistToPlayer = Vector2.Distance(new Vector2(c.World.x, c.World.z), new Vector2(pos.x, pos.z));
                        byte flags = grid.Flags[(long)ax * grid.GridH + az];
                        c.Sealed = (flags & (WorldmapGridFormat.CachedGrid.FlagSealedInterior
                            | WorldmapGridFormat.CachedGrid.FlagBunnySealed)) != 0;
                        c.Open = grid.IsPassable(ax, az, WorldmapGridFormat.CachedGrid.FlagFootBlocked);
                        if (c.Sealed) sealedCount++;
                        if (c.Open) gridOpen++;
                        c.Clearance = grid.GetClearance(ax, az);
                        c.GridY = grid.GetHeightM(ax, az);

                        c.LiveY = WorldmapGridProbe.BakeGroundHeight(c.World.x, c.World.z, out bool ok);
                        if (!ok) { noGround++; c.LiveY = float.NaN; all.Add(c); continue; }
                        c.Replay = WorldmapGridProbe.ProbeObstacles(c.World.x, c.World.z, c.LiveY,
                            footSolidMask, charaWallMask, bunnyMask);
                        // Body at the point the walk aims at: the stored clearance offset.
                        Vector3 aim = grid.GridToWorldWithClearance(ax, az);
                        c.Offset = aim.x != c.World.x || aim.z != c.World.z;
                        c.Body = NavigationHandler.BodyWallClearance(new Vector3(aim.x, c.LiveY, aim.z), bodyMask, out c.BodyBlocker);
                        bool bodyFits = c.Body > 0f;

                        if (c.Open && c.Replay.FootBlocked) { absent++; c.Verdict = "ABSENT-AT-BAKE"; }
                        else if (c.Open && !bodyFits) { geometry++; c.Verdict = "GEOMETRY"; }
                        else if (c.Open && Mathf.Abs(c.GridY - c.LiveY) > TruthHeightTolerance) { height++; c.Verdict = "HEIGHT"; }
                        else if (!c.Open && !c.Sealed && !c.Replay.FootBlocked && bodyFits) { reverse++; c.Verdict = "REVERSE"; }
                        all.Add(c);
                    }
                }

                // FOCUS: the nearest fishing stand's cell and its 8 neighbours, whatever their verdict.
                Vector3? focus = NearestStand(fm.WorldmapID, pos);
                if (focus.HasValue)
                {
                    grid.WorldToGrid(focus.Value.x, focus.Value.z, out int fx, out int fz);
                    sb.AppendLine($"[WMTruth] FOCUS = nearest fishing stand ({focus.Value.x:F1},{focus.Value.z:F1}) cell ({fx},{fz}) and its neighbours:");
                    foreach (var c in all)
                        if (Math.Abs(c.Ax - fx) <= 1 && Math.Abs(c.Az - fz) <= 1)
                            sb.AppendLine(Describe(c, "FOCUS " + (c.Verdict ?? "agree")));
                }
                else sb.AppendLine("[WMTruth] FOCUS: no fishing stand within " + TruthFocusStandMeters + " m.");

                LogClass(sb, all, "ABSENT-AT-BAKE", TruthAbsentLines);
                LogClass(sb, all, "GEOMETRY", TruthGeometryLines);
                LogClass(sb, all, "HEIGHT", TruthHeightLines);
                LogClass(sb, all, "REVERSE", TruthReverseLines);

                int mismatches = absent + geometry + height;
                sb.AppendLine(
                    $"[WMTruth] summary: {all.Count} cells, {gridOpen} grid-open, {absent} ABSENT-AT-BAKE, {geometry} GEOMETRY, " +
                    $"{height} HEIGHT, {reverse} reverse (grid blocked, live open), {sealedCount} sealed, {noGround} live-no-ground; " +
                    $"{sw.ElapsedMilliseconds} ms");
                sb.AppendLine("[WMTruth] VERDICT: " + TruthVerdict(absent, geometry, height));
                MelonLogger.Msg(sb.ToString());
                ScreenReader.Say($"Grid truth probe: {mismatches} mismatching cells of {all.Count}. Check log.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[WMTruth] error: {ex.Message}");
            }
        }

        /// <summary>Logs the cells of one verdict class, nearest to the player first, up to the cap.</summary>
        private static void LogClass(StringBuilder sb, List<TruthCell> all, string verdict, int cap)
        {
            var cells = all.FindAll(c => c.Verdict == verdict);
            if (cells.Count == 0) return;
            cells.Sort((a, b) => a.DistToPlayer.CompareTo(b.DistToPlayer));
            sb.AppendLine($"[WMTruth] {verdict}: {cells.Count} cells, nearest {Math.Min(cap, cells.Count)} listed:");
            for (int i = 0; i < cells.Count && i < cap; i++) sb.AppendLine(Describe(cells[i], verdict));
        }

        /// <summary>One cell line: grid view, replay view, body view, evidence collider, everything within reach.</summary>
        private static string Describe(TruthCell c, string label)
        {
            if (float.IsNaN(c.LiveY))
                return $"[WMTruth] cell ({c.Ax},{c.Az}) w=({c.World.x:F1},{c.World.z:F1}) {label} " +
                       $"grid={(c.Open ? "open" : c.Sealed ? "SEALED" : "blocked")} | live: NO GROUND";
            var rp = c.Replay;
            Collider evidence = rp.FootBlocked ? rp.NearestCollider : c.BodyBlocker ?? rp.NearestCollider;
            return $"[WMTruth] cell ({c.Ax},{c.Az}) w=({c.World.x:F1},{c.World.z:F1}) {c.DistToPlayer:F1}m {label} " +
                $"grid={(c.Open ? "open" : c.Sealed ? "SEALED" : "blocked")} " +
                $"clr={(c.Clearance == float.MaxValue ? "none" : c.Clearance.ToString("F2"))} gridY={c.GridY:F2} | " +
                $"replay={(rp.FootBlocked ? "BLOCKED" : "open")} " +
                $"foot={(rp.NearestFootSolidDist == float.MaxValue ? "none" : rp.NearestFootSolidDist.ToString("F2") + "m")} " +
                $"chara={(rp.HasCharaWall ? rp.CharaWallBestClearance.ToString("F2") + "m" : "none")} " +
                $"bunny={(rp.BunnyBlocked ? "BLOCKED" : "open")} liveY={c.LiveY:F2} | " +
                $"body{(c.Offset ? "@offset" : "")}={(c.Body > 0f ? c.Body.ToString("F2") + "m" : "OVERLAP")} | " +
                $"nearest: {WorldmapGridProbe.DescribeCollider(evidence)} | seen: {SeenWithinReach(c)}";
        }

        /// <summary>
        /// Every collider within the bake's search radius of the probe point on
        /// ANY layer, triggers included (marked T) — what physics has there,
        /// regardless of what the probe's masks admit.
        /// </summary>
        private static string SeenWithinReach(TruthCell c)
        {
            try
            {
                var p = new Vector3(c.World.x, c.LiveY + WorldmapGridProbe.ProbeHeightAboveGround, c.World.z);
                var hits = Physics.OverlapSphere(p, WorldmapGridProbe.ObstacleSearchRadius, ~0, QueryTriggerInteraction.Collide);
                if (hits == null || hits.Length == 0) return "nothing within 1 m";
                var parts = new List<string>();
                for (int i = 0; i < hits.Length && parts.Count < 6; i++)
                {
                    var h = hits[i];
                    if (h == null) continue;
                    int layer = h.gameObject.layer;
                    if (layer == 6) continue; // party bodies
                    string dist;
                    try { dist = Vector3.Distance(p, h.ClosestPoint(p)).ToString("F2"); }
                    catch { dist = "?"; }
                    parts.Add($"{h.name}/L{layer}/{dist}m{(h.isTrigger ? "T" : "")}");
                }
                return parts.Count == 0 ? "nothing solid within 1 m" : string.Join(", ", parts) + (hits.Length > 6 ? $" (+{hits.Length - 6})" : "");
            }
            catch (Exception ex)
            {
                return "seen failed: " + ex.Message;
            }
        }

        /// <summary>The baked fishing stand nearest the player within <see cref="TruthFocusStandMeters"/>, or null.</summary>
        private static Vector3? NearestStand(WorldmapID wmID, Vector3 pos)
        {
            var file = WorldmapFishingStands.Load(wmID);
            if (file?.Places == null) return null;
            Vector3? best = null;
            float bestSq = TruthFocusStandMeters * TruthFocusStandMeters;
            foreach (var place in file.Places)
                foreach (var s in place.Stands)
                {
                    float dx = s.X - pos.x, dz = s.Z - pos.z;
                    float dSq = dx * dx + dz * dz;
                    if (dSq < bestSq) { bestSq = dSq; best = s.Position; }
                }
            return best;
        }

        /// <summary>One sentence naming the dominant disagreement class, or "mixed" with the counts.</summary>
        private static string TruthVerdict(int absent, int geometry, int height)
        {
            if (absent == 0 && geometry == 0 && height == 0)
                return "no mismatch within the probed square — the grid agrees with the live world here.";
            int max = Math.Max(absent, Math.Max(geometry, height));
            int classes = (absent > 0 ? 1 : 0) + (geometry > 0 ? 1 : 0) + (height > 0 ? 1 : 0);
            if (classes > 1 && max < 3 * (absent + geometry + height - max))
                return $"mixed — absent-at-bake {absent}, geometry {geometry}, height {height}; no single cause dominates.";
            if (max == absent)
                return "bake-time absence — the bake probe run today blocks cells the grid leaves open with identical " +
                       "geometry, so these walls were not in the physics world when F9 ran.";
            if (max == geometry)
                return "probe geometry — the sphere probe agrees with the grid; only the game-body capsule hits the wall " +
                       "(the bake rule must use the body capsule).";
            return "height — the grid's ground differs from live CalcHeight by more than 0.5 m; the bake probed at the wrong height.";
        }
    }
}
