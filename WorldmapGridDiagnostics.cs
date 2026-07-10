using Il2CppGame;
using MelonLoader;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Debug-only diagnostics for world-map travel modes and grid data
    /// (Phase A of the per-mode reachability feature). Everything here is
    /// investigation logging — no gameplay behavior. Contains:
    /// - <see cref="LogTravelMasks"/>: dumps the game's per-mode wall layer
    ///   masks (foot/bunny/psynard) and the current ride state (F10).
    /// - <see cref="LogPlayerCollider"/>: player capsule + gap measurements
    ///   (F10; measures the bunny body when pressed while mounted).
    /// - <see cref="Tick"/>: per-frame (debug-gated) mode-transition logging
    ///   and a bunny "ride trace" that records when the mounted player
    ///   crosses cells the FOOT grid considers blocked or ocean — real data
    ///   on what the bunny can actually cross.
    /// </summary>
    public static class WorldmapGridDiagnostics
    {
        // --- Ride-trace state (Tick) ---------------------------------------
        private const float TraceInterval = 0.5f;
        private const float ClimbLogThreshold = 2.0f;

        private static WorldmapTravelMode _lastMode = WorldmapTravelMode.Foot;
        private static bool _modeKnown;
        private static float _nextTraceTime;
        private static bool _hasLastSample;
        private static Vector3 _lastSamplePos;
        private static int _lastCellClass = int.MinValue;

        /// <summary>
        /// Dumps the game's own per-mode wall masks and the current ride
        /// state to the log. Masks are static game data (valid on any map);
        /// the ride flags describe the current moment. Called from F10.
        /// </summary>
        public static void LogTravelMasks()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[WMInvest] === TRAVEL MODE / WALL MASK DUMP ===");

            int footMask = 0;
            bool footMaskOk = false;
            AppendMask(sb, "LayerMaskWall (foot)   ", () =>
            {
                footMask = GameRenderManager.LayerMaskWall;
                footMaskOk = true;
                return footMask;
            });
            AppendMask(sb, "LayerMaskBunnyWall     ",
                () => GameRenderManager.LayerMaskBunnyWall);
            AppendMask(sb, "LayerMaskPsynardWall   ",
                () => GameRenderManager.LayerMaskPsynardWall);
            AppendMask(sb, "LayerMaskWallHeight    ",
                () => GameRenderManager.LayerMaskWallHeight);

            // The grid generator currently bakes with hardcoded L22|L23 —
            // flag any difference from the game's real foot mask (a
            // difference is the leading suspect for the Lasgus/Mountain
            // Palace false-connection).
            if (footMaskOk)
            {
                int baked = (1 << 22) | (1 << 23);
                if (footMask == baked)
                {
                    sb.AppendLine(
                        "[WMInvest] footMask == baked (L22|L23): TRUE");
                }
                else
                {
                    sb.AppendLine(
                        "[WMInvest] footMask == baked (L22|L23): FALSE — " +
                        $"extra {DescribeMask(footMask & ~baked)} | " +
                        $"missing {DescribeMask(baked & ~footMask)}");
                }
            }

            // Ride state right now.
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null)
                {
                    sb.AppendLine("[WMInvest] FieldManager unavailable — no ride state.");
                }
                else
                {
                    bool flagBunny = fm.IsFieldFlag(FieldBitFlag.Bunny);
                    bool flagPsynard = fm.IsFieldFlag(FieldBitFlag.Psynard);
                    bool bunnyExists = fm.FieldBunny != null;
                    bool psynardExists = fm.FieldPsynard != null;
                    sb.AppendLine(
                        $"[WMInvest] flags: Bunny={flagBunny} Psynard={flagPsynard} | " +
                        $"objects: FieldBunny={bunnyExists} FieldPsynard={psynardExists} | " +
                        $"CurrentMode()={WorldmapTravel.CurrentMode()}");

                    var player = fm.GetControlPlayer();
                    if (player != null)
                    {
                        string typeName;
                        try { typeName = player.GetIl2CppType().Name; }
                        catch { typeName = "?"; }
                        bool isBunnyType = player.TryCast<FieldBunny>() != null;
                        int virtualMask = player.GetLayerMaskWall();
                        sb.AppendLine(
                            $"[WMInvest] control player: type={typeName} " +
                            $"isFieldBunnyType={isBunnyType} " +
                            $"GetLayerMaskWall()={FormatMask(virtualMask)} " +
                            $"→ {DescribeMask(virtualMask)}");
                    }
                    else
                    {
                        sb.AppendLine("[WMInvest] control player: NULL");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[WMInvest] ride state read error: {ex.Message}");
            }

            sb.Append("[WMInvest] === END MASK DUMP ===");
            MelonLogger.Msg(sb.ToString());
        }

        /// <summary>
        /// Per-frame diagnostics tick (call only while debug mode is on).
        /// Logs travel-mode transitions and, while mounted, traces the
        /// player's position against the loaded FOOT grid so the log shows
        /// exactly which foot-blocked/ocean cells the bunny crosses.
        /// </summary>
        public static void Tick()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ResetTrace();
                    return;
                }

                var mode = WorldmapTravel.CurrentMode();
                if (!_modeKnown || mode != _lastMode)
                {
                    if (_modeKnown)
                        LogModeTransition(fm, _lastMode, mode);
                    _lastMode = mode;
                    _modeKnown = true;
                    _hasLastSample = false;
                    _lastCellClass = int.MinValue;
                }

                if (mode == WorldmapTravelMode.Foot) return;
                if (Time.time < _nextTraceTime) return;
                _nextTraceTime = Time.time + TraceInterval;

                var player = fm.GetControlPlayer();
                if (player == null) return;
                Vector3 pos = player.transform.position;

                TraceCell(mode, pos);
                TraceClimb(mode, pos);

                _lastSamplePos = pos;
                _hasLastSample = true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"WMInvest Tick error: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs collision diagnostics from the current control player
        /// (capsule size, all colliders, gap measurements). While mounted
        /// this measures the BUNNY body — the control player is the
        /// FieldBunny instance.
        /// </summary>
        public static void LogPlayerCollider()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    ScreenReader.Say(
                        "Player collider info only available on world map.");
                    return;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say("No player found.");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== PLAYER COLLISION DIAGNOSTICS ===");
                sb.AppendLine($"Player: {player.name}");
                var pos = player.transform.position;
                sb.AppendLine($"Position: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})");

                // --- Game collision properties ---
                sb.AppendLine("\n--- Game Collision Properties ---");
                try
                {
                    float mcr = player.MoveCollisionRadius;
                    sb.AppendLine($"MoveCollisionRadius: {mcr:F4}m");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"MoveCollisionRadius: ERROR ({ex.Message})");
                }

                // --- CapsuleCollider from FieldObject ---
                sb.AppendLine("\n--- CapsuleCollider (FieldObject) ---");
                try
                {
                    var capsule = player.capsuleCollider;
                    if (capsule != null)
                    {
                        sb.AppendLine($"Radius: {capsule.radius:F4}m");
                        sb.AppendLine($"Height: {capsule.height:F4}m");
                        sb.AppendLine($"Center: ({capsule.center.x:F3}, {capsule.center.y:F3}, {capsule.center.z:F3})");
                        sb.AppendLine($"Direction: {capsule.direction} (0=X, 1=Y, 2=Z)");
                        sb.AppendLine($"IsTrigger: {capsule.isTrigger}");
                        sb.AppendLine($"Enabled: {capsule.enabled}");
                        sb.AppendLine($"ContactOffset: {capsule.contactOffset:F4}m");
                        // World-space effective radius (accounts for scale)
                        var scale = player.transform.lossyScale;
                        float effectiveRadius = capsule.radius *
                            Mathf.Max(scale.x, scale.z);
                        sb.AppendLine($"Transform scale: ({scale.x:F3}, {scale.y:F3}, {scale.z:F3})");
                        sb.AppendLine($"Effective world radius: {effectiveRadius:F4}m");
                    }
                    else
                    {
                        sb.AppendLine("capsuleCollider is NULL");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"CapsuleCollider: ERROR ({ex.Message})");
                }

                // --- All colliders on the player GameObject ---
                sb.AppendLine("\n--- All Colliders on Player ---");
                try
                {
                    var allColliders = player.GetComponentsInChildren<Collider>();
                    if (allColliders != null && allColliders.Length > 0)
                    {
                        for (int i = 0; i < allColliders.Length; i++)
                        {
                            var col = allColliders[i];
                            if (col == null) continue;
                            sb.AppendLine($"  [{i}] {col.GetType().Name} " +
                                $"name=\"{col.name}\" " +
                                $"trigger={col.isTrigger} " +
                                $"enabled={col.enabled} " +
                                $"layer={col.gameObject.layer}");

                            if (col is CapsuleCollider cc)
                            {
                                sb.AppendLine($"       radius={cc.radius:F4} " +
                                    $"height={cc.height:F4} " +
                                    $"center=({cc.center.x:F3},{cc.center.y:F3},{cc.center.z:F3}) " +
                                    $"dir={cc.direction}");
                            }
                            else if (col is SphereCollider sc)
                            {
                                sb.AppendLine($"       radius={sc.radius:F4} " +
                                    $"center=({sc.center.x:F3},{sc.center.y:F3},{sc.center.z:F3})");
                            }
                            else if (col is BoxCollider bc)
                            {
                                sb.AppendLine($"       size=({bc.size.x:F3},{bc.size.y:F3},{bc.size.z:F3}) " +
                                    $"center=({bc.center.x:F3},{bc.center.y:F3},{bc.center.z:F3})");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("  No colliders found.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Collider scan error: {ex.Message}");
                }

                // --- Measure actual gap widths at known corridors ---
                sb.AppendLine("\n--- Gap Width Measurements ---");
                sb.AppendLine("Casting rays from known corridor midpoints to find actual wall distances.");
                MeasureGapWidth(sb, "Salva-Arlia junction (narrow)",
                    new Vector3(-174.7f, 22.9f, -305.4f));
                MeasureGapWidth(sb, "Salva-Arlia junction (east corridor)",
                    new Vector3(-158.0f, 23.0f, -310.0f));
                MeasureGapWidth(sb, "Krosse-Salva corridor",
                    new Vector3(-140.0f, 29.0f, -175.0f));
                MeasureGapWidth(sb, "Player current position", pos);

                sb.AppendLine("\n=== END DIAGNOSTICS ===");

                MelonLogger.Msg(sb.ToString());
                ScreenReader.Say("Collision diagnostics logged. Check log.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[WMDiag] Diagnostics error: {ex}");
                ScreenReader.Say("Diagnostics failed. Check log.");
            }
        }

        /// <summary>
        /// Compares the baked grid against LIVE terrain around the player and
        /// names the streamed collision nearby (F10). Built for the D1 Salva
        /// wedge (2026-07-10): the grid claimed gentle ground where the game
        /// measured a 1.3–2.1m higher rock — this dump identifies which
        /// prefab owns such a rock (ancestor chain) and how far grid and
        /// live heights disagree, so the bake fix targets the right pool.
        /// </summary>
        public static void LogHeightTruthCensus()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap()) return;
                var player = fm.GetControlPlayer();
                if (player == null) return;
                Vector3 pos = player.transform.position;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[WMCensus] === GRID vs LIVE HEIGHT CENSUS ===");
                sb.AppendLine($"[WMCensus] player at ({pos.x:F1},{pos.y:F1},{pos.z:F1})");

                // --- Part 1: ring comparison, live CalcHeight vs grid ---
                int samples = 0, mismatches = 0, liveNoGround = 0, gridNoData = 0;
                float worstDiff = 0f;
                for (int dir = 0; dir < 16; dir++)
                {
                    float ang = dir * Mathf.PI * 2f / 16f;
                    var d = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    for (float dist = 2f; dist <= 6f; dist += 2f)
                    {
                        Vector3 p = pos + d * dist;
                        p.y = pos.y + 30f; // probe from above local ground
                        float liveY = GameUtility.CalcHeight(p, out bool ok, 80f);
                        bool haveGrid = WorldmapPathfinder.TryGetCellInfo(
                            p, out ushort raw, out byte flags, out _);
                        samples++;
                        if (!ok) { liveNoGround++; continue; }
                        if (!haveGrid || raw == 0) { gridNoData++; continue; }
                        float gridY = raw / 100f - 100f;
                        float diff = liveY - gridY;
                        if (Mathf.Abs(diff) > 0.5f)
                        {
                            mismatches++;
                            if (Mathf.Abs(diff) > Mathf.Abs(worstDiff))
                                worstDiff = diff;
                            sb.AppendLine(
                                $"[WMCensus] MISMATCH at ({p.x:F1},{p.z:F1}) " +
                                $"dir={dir * 22.5f:F0}° dist={dist:F0}m: " +
                                $"live={liveY:F2} grid={gridY:F2} " +
                                $"diff={diff:+0.00;-0.00} " +
                                $"(flags: foot{(((flags & 1) != 0) ? "Blocked" : "Open")})");
                        }
                    }
                }
                sb.AppendLine(
                    $"[WMCensus] ring summary: {samples} samples, " +
                    $"{mismatches} mismatches >0.5m (worst {worstDiff:+0.00;-0.00}), " +
                    $"{liveNoGround} live-no-ground, {gridNoData} grid-no-data");

                // --- Part 2: nearby streamed collision (L24) with owners ---
                var hits = Physics.OverlapSphere(pos, 20f, 1 << 24);
                int total = hits != null ? hits.Length : 0;
                sb.AppendLine($"[WMCensus] L24 colliders within 20m: {total}" +
                    (total > 25 ? " (logging nearest 25)" : ""));
                if (total > 0)
                {
                    // Sort by distance so the wedge-causing rock is on top.
                    var list = new System.Collections.Generic.List<(float Dist, Collider Col)>();
                    for (int i = 0; i < hits.Length; i++)
                    {
                        if (hits[i] == null) continue;
                        float dd = Vector3.Distance(pos, hits[i].ClosestPoint(pos));
                        list.Add((dd, hits[i]));
                    }
                    list.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                    int shown = Mathf.Min(25, list.Count);
                    for (int i = 0; i < shown; i++)
                    {
                        var col = list[i].Col;
                        sb.AppendLine(
                            $"[WMCensus]   d={list[i].Dist:F2}m '{col.name}' " +
                            $"tag={col.tag} topY={col.bounds.max.y:F2} " +
                            $"chain={AncestorChain(col.transform)}");
                    }
                }

                sb.Append("[WMCensus] === END CENSUS ===");
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[WMCensus] census error: {ex.Message}");
            }
        }

        /// <summary>Joins up to 6 ancestor names, nearest parent first.</summary>
        private static string AncestorChain(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            var cur = t.parent;
            for (int i = 0; i < 6 && cur != null; i++)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            return parts.Count == 0 ? "(root)" : string.Join(" / ", parts);
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        /// <summary>Logs a mode transition with both detection signals so the
        /// log shows whether the bit flags and the control-player type agree.</summary>
        private static void LogModeTransition(FieldManager fm,
            WorldmapTravelMode from, WorldmapTravelMode to)
        {
            bool flagBunny = false, flagPsynard = false, isBunnyType = false;
            try
            {
                flagBunny = fm.IsFieldFlag(FieldBitFlag.Bunny);
                flagPsynard = fm.IsFieldFlag(FieldBitFlag.Psynard);
                var player = fm.GetControlPlayer();
                isBunnyType = player != null &&
                    player.TryCast<FieldBunny>() != null;
            }
            catch { /* logged values stay false */ }

            MelonLogger.Msg(
                $"[WMInvest] mode {from}→{to} (flagBunny={flagBunny} " +
                $"flagPsynard={flagPsynard} controlIsFieldBunny={isBunnyType})");
        }

        /// <summary>Logs transitions between grid cell classes (walkable /
        /// foot-blocked / bunny-blocked / no-ground / off-grid) under the
        /// mount. Understands both the legacy height-lane obstacle encoding
        /// and the v2 flags lane.</summary>
        private static void TraceCell(WorldmapTravelMode mode, Vector3 pos)
        {
            int cellClass;
            string detail;
            if (WorldmapPathfinder.TryGetCellInfo(pos, out ushort raw,
                out byte cellFlags, out bool isV2))
            {
                bool footBlocked = raw == 1 || (cellFlags &
                    WorldmapGridFormat.CachedGrid.FlagFootBlocked) != 0;
                bool bunnyBlocked = (cellFlags &
                    WorldmapGridFormat.CachedGrid.FlagBunnyBlocked) != 0;
                if (raw == 0)
                {
                    cellClass = 0;
                    detail = "OCEAN (no ground in grid)";
                }
                else if (footBlocked || bunnyBlocked)
                {
                    cellClass = 1;
                    detail = $"BLOCKED (foot={footBlocked} " +
                        $"bunny={(isV2 ? bunnyBlocked.ToString() : "unknown")})";
                }
                else
                {
                    cellClass = 2;
                    float gridY = raw / 100f - 100f;
                    detail = $"walkable (gridY={gridY:F1})";
                }
            }
            else
            {
                cellClass = -1;
                detail = "off-grid / no grid loaded";
            }

            if (cellClass != _lastCellClass)
            {
                MelonLogger.Msg(
                    $"[WMInvest] {mode} entered {detail} cell at " +
                    $"({pos.x:F1},{pos.z:F1}) playerY={pos.y:F1}");
                _lastCellClass = cellClass;
            }
        }

        /// <summary>Logs steep height changes between consecutive samples —
        /// evidence of the mount climbing terrain the foot player cannot.</summary>
        private static void TraceClimb(WorldmapTravelMode mode, Vector3 pos)
        {
            if (!_hasLastSample) return;
            float dy = pos.y - _lastSamplePos.y;
            if (Mathf.Abs(dy) < ClimbLogThreshold) return;
            float dxz = Vector2.Distance(
                new Vector2(pos.x, pos.z),
                new Vector2(_lastSamplePos.x, _lastSamplePos.z));
            MelonLogger.Msg(
                $"[WMInvest] {mode} climb ΔY={dy:+0.0;-0.0}m over {dxz:F1}m " +
                $"at ({pos.x:F1},{pos.z:F1}) playerY={pos.y:F1}");
        }

        private static void ResetTrace()
        {
            _modeKnown = false;
            _hasLastSample = false;
            _lastCellClass = int.MinValue;
        }

        /// <summary>Reads a mask via <paramref name="getter"/> and appends its
        /// hex value + decoded layer names; appends the error on failure.</summary>
        private static void AppendMask(System.Text.StringBuilder sb,
            string label, Func<int> getter)
        {
            try
            {
                int mask = getter();
                sb.AppendLine(
                    $"[WMInvest] {label}= {FormatMask(mask)} → {DescribeMask(mask)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[WMInvest] {label}= ERROR ({ex.Message})");
            }
        }

        private static string FormatMask(int mask) => $"0x{mask:X8}";

        /// <summary>Lists each set bit of a layer mask as "L{n}={name}".
        /// Public: the grid generator logs its live-read bake masks with it.</summary>
        public static string DescribeMask(int mask)
        {
            if (mask == 0) return "(empty)";
            var parts = new System.Collections.Generic.List<string>();
            for (int layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) == 0) continue;
                string name;
                try { name = LayerMask.LayerToName(layer); }
                catch { name = ""; }
                parts.Add(string.IsNullOrEmpty(name)
                    ? $"L{layer}=<unnamed>"
                    : $"L{layer}={name}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Measures the actual gap width at a position by casting rays and
        /// OverlapSphere in all directions to find the nearest solid walls.
        /// Logs the minimum clearance (distance to nearest wall) and the
        /// gap widths along each axis.
        /// </summary>
        private static void MeasureGapWidth(System.Text.StringBuilder sb,
            string label, Vector3 pos)
        {
            sb.AppendLine($"\n  [{label}] at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):");

            // Combined mask: L22 (terrain obstacles) + L23 (CharaWalls)
            int solidMask = (1 << 22) | (1 << 23);

            // Find all solid colliders within 10m
            var nearby = Physics.OverlapSphere(pos, 10f, solidMask);
            if (nearby == null || nearby.Length == 0)
            {
                sb.AppendLine("    No solid obstacles within 10m — wide open.");
                return;
            }

            // Find closest solid (non-trigger) collider and measure
            // distances in cardinal directions
            float minDist = float.MaxValue;
            string closestName = "";
            int solidCount = 0;

            // Track nearest wall in each direction
            float nearestNorth = float.MaxValue; // +Z
            float nearestSouth = float.MaxValue; // -Z
            float nearestEast = float.MaxValue;  // +X
            float nearestWest = float.MaxValue;  // -X
            string nearestNorthName = "", nearestSouthName = "";
            string nearestEastName = "", nearestWestName = "";

            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null || nearby[i].isTrigger) continue;
                solidCount++;

                var closest = nearby[i].ClosestPoint(pos);
                float dist = Vector3.Distance(pos, closest);
                string cName = $"{nearby[i].name} L{nearby[i].gameObject.layer}";

                if (dist < minDist)
                {
                    minDist = dist;
                    closestName = cName;
                }

                sb.AppendLine($"    d={dist:F3}m \"{cName}\" " +
                    $"closest=({closest.x:F2},{closest.y:F2},{closest.z:F2})" +
                    (nearby[i].transform.parent != null
                        ? $" parent=\"{nearby[i].transform.parent.name}\""
                        : ""));

                // Determine direction from pos to closest point
                float dx = closest.x - pos.x;
                float dz = closest.z - pos.z;

                // Track nearest in each cardinal direction
                if (dz > 0.01f && dist < nearestNorth)
                {
                    nearestNorth = dist;
                    nearestNorthName = cName;
                }
                if (dz < -0.01f && dist < nearestSouth)
                {
                    nearestSouth = dist;
                    nearestSouthName = cName;
                }
                if (dx > 0.01f && dist < nearestEast)
                {
                    nearestEast = dist;
                    nearestEastName = cName;
                }
                if (dx < -0.01f && dist < nearestWest)
                {
                    nearestWest = dist;
                    nearestWestName = cName;
                }
            }

            sb.AppendLine($"    SUMMARY: {solidCount} solid obstacles within 10m");
            sb.AppendLine($"    Nearest wall: {minDist:F3}m ({closestName})");
            if (nearestEast < float.MaxValue && nearestWest < float.MaxValue)
                sb.AppendLine($"    E-W gap: {nearestEast:F3}m + {nearestWest:F3}m = {nearestEast + nearestWest:F3}m total");
            if (nearestNorth < float.MaxValue && nearestSouth < float.MaxValue)
                sb.AppendLine($"    N-S gap: {nearestNorth:F3}m + {nearestSouth:F3}m = {nearestNorth + nearestSouth:F3}m total");
        }
    }
}
