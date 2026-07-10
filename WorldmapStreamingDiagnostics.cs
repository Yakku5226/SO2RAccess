using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Debug-only (F6) investigation of world-map collision STREAMING.
    /// Background: the B7 audit (2026-07-06) proved the game loads detail
    /// ground collision (rock Mesh_Col/Col_Height, layer 24) only within
    /// ~100-150m of the player, so a single-spot F9 bake is fiction far away.
    /// This tracker answers HOW the streaming works so the bake can force
    /// everything loaded (or, failing that, bake progressively):
    /// - F6 press 1: baseline census of ALL colliders on the wall/height
    ///   layers (including inactive ones), grouped by parent GameObject,
    ///   plus component dumps of the biggest groups' ancestor chains.
    /// - While tracking: periodic re-sweeps diff the scene against the last
    ///   sweep and log every collider that APPEARS (streamed in), VANISHES
    ///   (destroyed/unloaded) or flips active/enabled — with hierarchy,
    ///   scene name and player distance. First appearance of a new parent
    ///   also dumps its ancestor components (the streaming controller is
    ///   expected to show up there, or as an additive scene name).
    /// - F6 press 2: stop + summary (event counts, streaming radius
    ///   estimate, destroyed-vs-deactivated verdict).
    /// </summary>
    public static class WorldmapStreamingDiagnostics
    {
        /// <summary>Seconds between full collider sweeps while tracking.
        /// A sweep walks every collider in the scene — debug-only cost.</summary>
        private const float SweepInterval = 2.5f;

        /// <summary>Cap on per-sweep event lines so one big chunk swap
        /// cannot flood the log; the overflow count is always logged.</summary>
        private const int MaxEventLinesPerSweep = 40;

        /// <summary>Cap on ancestor-component dumps (one per parent name).</summary>
        private const int MaxRootDumps = 30;

        /// <summary>Consecutive sweep errors before the trace aborts itself.</summary>
        private const int MaxConsecutiveErrors = 5;

        /// <summary>Fallback watch mask if the game's live masks are
        /// unreadable: all known wall layers + L24 height detail.</summary>
        private static readonly int FallbackWatchMask =
            (1 << 15) | (1 << 17) | (1 << 21) | (1 << 22) |
            (1 << 23) | (1 << 24) | (1 << 26);

        /// <summary>Everything remembered about a watched collider between
        /// sweeps. Strings are captured up front because a streamed-OUT
        /// collider is already destroyed when we notice it is gone.</summary>
        private struct ColliderRecord
        {
            public string Name;
            public string Parent;
            public string Scene;
            public byte Layer;
            public bool ActiveInHierarchy;
            public bool Enabled;
            public Vector3 Center;
        }

        private static bool _tracking;
        private static int _watchMask;
        private static float _nextSweepTime;
        private static int _sweepCount;
        private static int _consecutiveErrors;
        private static int _lastCensusCount = -1;
        private static Dictionary<int, ColliderRecord> _known;
        private static readonly HashSet<string> _dumpedParents = new HashSet<string>();

        // Event statistics for the stop summary.
        private static int _inEvents, _outEvents, _activateEvents, _deactivateEvents;
        private static float _minInDist = float.MaxValue, _maxInDist;
        private static float _minOutDist = float.MaxValue, _maxOutDist;
        private static readonly HashSet<string> _streamedParents = new HashSet<string>();
        private static readonly HashSet<string> _streamedScenes = new HashSet<string>();

        /// <summary>Whether the F6 trace is currently running (exposed so a
        /// future force-load bake step can refuse to run mid-trace).</summary>
        public static bool IsTracking => _tracking;

        /// <summary>F6 entry point: starts the trace on first press, stops
        /// and summarizes on the second. World map only.</summary>
        public static void Toggle()
        {
            if (_tracking)
            {
                StopTracking("F6 pressed");
                return;
            }

            var fm = FieldManager.Instance;
            if (fm == null || !fm.IsWorldmap())
            {
                ScreenReader.Say("Streaming trace only available on the world map.");
                return;
            }
            var player = fm.GetControlPlayer();
            if (player == null)
            {
                ScreenReader.Say("Streaming trace needs a player. Not started.");
                return;
            }

            StartTracking(player.transform.position);
        }

        /// <summary>Per-frame tick (call only in debug mode). Runs the
        /// periodic sweep while tracking; stops itself off the world map.</summary>
        public static void Tick()
        {
            if (!_tracking) return;

            var fm = FieldManager.Instance;
            if (fm == null || !fm.IsWorldmap())
            {
                StopTracking("left the world map");
                return;
            }
            if (Time.time < _nextSweepTime) return;
            _nextSweepTime = Time.time + SweepInterval;

            var player = fm.GetControlPlayer();
            if (player == null) return;

            try
            {
                Sweep(player.transform.position, logEvents: true);
                _consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                MelonLogger.Msg(
                    $"[WMStream] sweep error ({_consecutiveErrors}/{MaxConsecutiveErrors}): {ex.Message}");
                if (_consecutiveErrors >= MaxConsecutiveErrors)
                    StopTracking("too many sweep errors");
            }
        }

        // --------------------------------------------------------------------
        // Start / stop
        // --------------------------------------------------------------------

        private static void StartTracking(Vector3 playerPos)
        {
            _watchMask = ReadWatchMask();
            _known = new Dictionary<int, ColliderRecord>(16384);
            _dumpedParents.Clear();
            _streamedParents.Clear();
            _streamedScenes.Clear();
            _sweepCount = 0;
            _consecutiveErrors = 0;
            _lastCensusCount = -1;
            _inEvents = _outEvents = _activateEvents = _deactivateEvents = 0;
            _minInDist = float.MaxValue; _maxInDist = 0f;
            _minOutDist = float.MaxValue; _maxOutDist = 0f;

            MelonLogger.Msg(
                $"[WMStream] === TRACE START === player=({playerPos.x:F1},{playerPos.z:F1}) " +
                $"watchMask=0x{_watchMask:X8} → {WorldmapGridDiagnostics.DescribeMask(_watchMask)}");

            try
            {
                Sweep(playerPos, logEvents: false); // baseline, no diff yet
                LogBaselineGroups(playerPos);
                DumpCullingData();
                _tracking = true;
                _nextSweepTime = Time.time + SweepInterval;
                ScreenReader.Say(
                    "Streaming trace started. Ride around, then press F6 again to stop.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[WMStream] baseline sweep FAILED: {ex}");
                ScreenReader.Say("Streaming trace failed to start. Check log.");
                _known = null;
            }
        }

        private static void StopTracking(string reason)
        {
            _tracking = false;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[WMStream] === TRACE STOP ({reason}) === sweeps={_sweepCount}");
            sb.AppendLine(
                $"[WMStream] events: IN={_inEvents} OUT={_outEvents} " +
                $"activated={_activateEvents} deactivated={_deactivateEvents}");
            if (_inEvents > 0)
                sb.AppendLine(
                    $"[WMStream] stream-IN player distance: {_minInDist:F0}m – {_maxInDist:F0}m " +
                    "(≈ the radius at which collision appears)");
            if (_outEvents > 0)
                sb.AppendLine(
                    $"[WMStream] stream-OUT player distance: {_minOutDist:F0}m – {_maxOutDist:F0}m");
            if (_streamedParents.Count > 0)
                sb.AppendLine(
                    $"[WMStream] parents involved in streaming ({_streamedParents.Count}): " +
                    string.Join(", ", _streamedParents));
            if (_streamedScenes.Count > 0)
                sb.AppendLine(
                    $"[WMStream] scenes involved in streaming: " +
                    string.Join(", ", _streamedScenes));

            // Mechanism verdict — this decides the force-load strategy.
            if (_inEvents == 0 && _outEvents == 0 &&
                _activateEvents == 0 && _deactivateEvents == 0)
            {
                sb.AppendLine(
                    "[WMStream] VERDICT: no streaming observed — either the player did not " +
                    "move far enough (ride 200m+), or collision is all resident and the " +
                    "far-field bake error has another cause.");
            }
            else if (_outEvents > 0 || _inEvents > 0)
            {
                sb.AppendLine(
                    "[WMStream] VERDICT: colliders are CREATED/DESTROYED at runtime " +
                    "(not just toggled) — streaming is object (or additive-scene) load/unload. " +
                    "Force-load must drive the loader itself; enabling objects won't be enough.");
            }
            else
            {
                sb.AppendLine(
                    "[WMStream] VERDICT: colliders persist but flip active/enabled — " +
                    "force-load can likely just activate everything during the bake.");
            }
            MelonLogger.Msg(sb.ToString());

            _known = null; // free ~16k+ records
            ScreenReader.Say("Streaming trace stopped. Summary is in the log.");
        }

        // --------------------------------------------------------------------
        // Sweeping
        // --------------------------------------------------------------------

        /// <summary>Reads the game's live wall/height masks and unions them
        /// into the watch mask; falls back to the known layer set (logged)
        /// so the trace still runs if a mask read fails.</summary>
        private static int ReadWatchMask()
        {
            try
            {
                int mask = GameRenderManager.LayerMaskWall |
                           GameRenderManager.LayerMaskBunnyWall |
                           GameRenderManager.LayerMaskPsynardWall |
                           GameRenderManager.LayerMaskWallHeight;
                if (mask != 0) return mask;
                MelonLogger.Msg(
                    "[WMStream] live masks read as 0 — using fallback watch mask.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg(
                    $"[WMStream] live mask read failed ({ex.Message}) — using fallback watch mask.");
            }
            return FallbackWatchMask;
        }

        /// <summary>One full pass over every collider in the scene
        /// (inactive included). Diffs against the previous pass and logs
        /// IN/OUT/ACTIVATED/DEACTIVATED events when <paramref name="logEvents"/>.</summary>
        private static void Sweep(Vector3 playerPos, bool logEvents)
        {
            _sweepCount++;
            var current = new Dictionary<int, ColliderRecord>(
                _known != null ? _known.Count + 256 : 16384);
            var perLayer = new int[32];
            var perLayerActive = new int[32];
            int eventLines = 0;
            int suppressed = 0;

            var found = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            if (found == null)
            {
                MelonLogger.Msg("[WMStream] FindObjectsOfType returned null — sweep skipped.");
                return;
            }

            for (int i = 0; i < found.Length; i++)
            {
                var col = found[i];
                if (col == null) continue;
                var go = col.gameObject;
                if (go == null) continue;
                int layer = go.layer;
                if ((_watchMask & (1 << layer)) == 0) continue;

                perLayer[layer]++;
                bool active = go.activeInHierarchy;
                if (active) perLayerActive[layer]++;

                var rec = new ColliderRecord
                {
                    Name = go.name,
                    Parent = go.transform.parent != null
                        ? go.transform.parent.name : "<scene root>",
                    Scene = go.scene.name,
                    Layer = (byte)layer,
                    ActiveInHierarchy = active,
                    Enabled = col.enabled,
                    Center = col.bounds.center,
                };
                int id = col.GetInstanceID();
                current[id] = rec;

                if (!logEvents || _known == null) continue;

                if (!_known.TryGetValue(id, out var old))
                {
                    _inEvents++;
                    float d = DistXZ(playerPos, rec.Center);
                    if (d < _minInDist) _minInDist = d;
                    if (d > _maxInDist) _maxInDist = d;
                    _streamedParents.Add(rec.Parent);
                    _streamedScenes.Add(rec.Scene);
                    LogEvent(ref eventLines, ref suppressed,
                        $"[WMStream] IN    L{layer} '{rec.Name}' parent='{rec.Parent}' " +
                        $"scene='{rec.Scene}' at ({rec.Center.x:F0},{rec.Center.z:F0}) " +
                        $"dist={d:F0}m active={rec.ActiveInHierarchy} enabled={rec.Enabled}");
                    DumpAncestors(col);
                }
                else if (old.ActiveInHierarchy != rec.ActiveInHierarchy ||
                         old.Enabled != rec.Enabled)
                {
                    bool nowOn = rec.ActiveInHierarchy && rec.Enabled;
                    if (nowOn) _activateEvents++; else _deactivateEvents++;
                    float d = DistXZ(playerPos, rec.Center);
                    _streamedParents.Add(rec.Parent);
                    LogEvent(ref eventLines, ref suppressed,
                        $"[WMStream] {(nowOn ? "ACT  " : "DEACT")} L{layer} '{rec.Name}' " +
                        $"parent='{rec.Parent}' at ({rec.Center.x:F0},{rec.Center.z:F0}) " +
                        $"dist={d:F0}m active {old.ActiveInHierarchy}→{rec.ActiveInHierarchy} " +
                        $"enabled {old.Enabled}→{rec.Enabled}");
                    DumpAncestors(col);
                }
            }

            if (logEvents && _known != null)
            {
                foreach (var kv in _known)
                {
                    if (current.ContainsKey(kv.Key)) continue;
                    var old = kv.Value;
                    _outEvents++;
                    float d = DistXZ(playerPos, old.Center);
                    if (d < _minOutDist) _minOutDist = d;
                    if (d > _maxOutDist) _maxOutDist = d;
                    _streamedParents.Add(old.Parent);
                    _streamedScenes.Add(old.Scene);
                    LogEvent(ref eventLines, ref suppressed,
                        $"[WMStream] OUT   L{old.Layer} '{old.Name}' parent='{old.Parent}' " +
                        $"scene='{old.Scene}' was at ({old.Center.x:F0},{old.Center.z:F0}) " +
                        $"dist={d:F0}m (collider destroyed or scene unloaded)");
                }
            }

            if (suppressed > 0)
                MelonLogger.Msg($"[WMStream] ... +{suppressed} more events this sweep (capped).");

            // Census only when the total changes — one line per real shift.
            if (current.Count != _lastCensusCount)
            {
                _lastCensusCount = current.Count;
                var parts = new List<string>();
                for (int l = 0; l < 32; l++)
                {
                    if (perLayer[l] == 0) continue;
                    parts.Add($"L{l}={perLayer[l]}({perLayerActive[l]} active)");
                }
                MelonLogger.Msg(
                    $"[WMStream] census #{_sweepCount}: {current.Count} watched colliders — " +
                    $"{string.Join(", ", parts)} — player=({playerPos.x:F0},{playerPos.z:F0})");
            }

            _known = current;
        }

        private static void LogEvent(ref int lines, ref int suppressed, string msg)
        {
            if (lines >= MaxEventLinesPerSweep) { suppressed++; return; }
            lines++;
            MelonLogger.Msg(msg);
        }

        private static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // --------------------------------------------------------------------
        // CullingManager / CullingData dump
        // --------------------------------------------------------------------

        /// <summary>
        /// Dumps the game's culling database (the streaming controller found
        /// by the 2026-07-07 trace: CullingManager instantiates pooled prefab
        /// chunks per frustum+distance). The unit list is the authoritative
        /// full-map layout of every streamed object — pool prefab names,
        /// per-layer unit counts, XZ coverage and the live culling distances
        /// are exactly what the force-load bake design needs.
        /// </summary>
        private static void DumpCullingData()
        {
            try
            {
                var mgr = CullingManager.Instance;
                if (mgr == null)
                {
                    MelonLogger.Msg("[WMStream] CullingManager.Instance is NULL — no dump.");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[WMStream] === CULLING DATA DUMP ===");
                try
                {
                    sb.AppendLine(
                        $"[WMStream] manager: isLanding={mgr.isLanding} " +
                        $"maxShowPerFrame={mgr.GetMaxShowCountPerFrame()} " +
                        $"farDistThresholdSqr={mgr.GetFarDistanceThresholdSqr():F0}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[WMStream] manager state read error: {ex.Message}");
                }

                // Live culling distances per type.
                foreach (CullingDistanceType t in new[] {
                    CullingDistanceType.Near, CullingDistanceType.Middle,
                    CullingDistanceType.Far, CullingDistanceType.Farthest })
                {
                    try
                    {
                        sb.AppendLine(
                            $"[WMStream] cullingDistance[{t}] = " +
                            $"{CullingManager.GetCullingDistance(t):F1}m");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"[WMStream] cullingDistance[{t}] read error: {ex.Message}");
                    }
                }

                var data = mgr.cullingData;
                if (data == null)
                {
                    sb.AppendLine("[WMStream] cullingData is NULL.");
                    MelonLogger.Msg(sb.ToString());
                    return;
                }

                // Pools: prefab name, distance tier, pool size.
                var pools = data.poolInfoList;
                int poolCount = pools != null ? pools.Count : 0;
                sb.AppendLine($"[WMStream] poolInfoList: {poolCount} pools");
                for (int i = 0; i < poolCount; i++)
                {
                    var p = pools[i];
                    if (p == null) continue;
                    string poolName;
                    try
                    {
                        poolName = p.poolObject != null ? p.poolObject.name : "<null prefab>";
                    }
                    catch { poolName = "<unreadable>"; }
                    sb.AppendLine(
                        $"[WMStream]   pool[{i}] '{poolName}' " +
                        $"tier={p.cullingDistanceType} poolSize={p.count}");
                }

                // Units: totals, per-layer counts, XZ coverage, samples.
                var units = data.unitList;
                int unitCount = units != null ? units.Count : 0;
                sb.AppendLine($"[WMStream] unitList: {unitCount} units");
                if (unitCount > 0)
                {
                    var perLayer = new int[32];
                    int nullLayout = 0;
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minZ = float.MaxValue, maxZ = float.MinValue;
                    for (int i = 0; i < unitCount; i++)
                    {
                        var u = units[i];
                        if (u == null) continue;
                        int layer = u.layer;
                        if (layer >= 0 && layer < 32) perLayer[layer]++;
                        if (u.layoutItem == null) nullLayout++;
                        var pos = u.position;
                        if (pos.x < minX) minX = pos.x;
                        if (pos.x > maxX) maxX = pos.x;
                        if (pos.z < minZ) minZ = pos.z;
                        if (pos.z > maxZ) maxZ = pos.z;
                    }
                    var parts = new List<string>();
                    for (int l = 0; l < 32; l++)
                        if (perLayer[l] > 0) parts.Add($"L{l}={perLayer[l]}");
                    sb.AppendLine($"[WMStream]   per-layer: {string.Join(", ", parts)}");
                    sb.AppendLine(
                        $"[WMStream]   XZ coverage: X=[{minX:F0},{maxX:F0}] " +
                        $"Z=[{minZ:F0},{maxZ:F0}], layoutItem null for {nullLayout} units");

                    int samples = Math.Min(5, unitCount);
                    for (int i = 0; i < samples; i++)
                    {
                        var u = units[i];
                        if (u == null) continue;
                        string layoutName;
                        try
                        {
                            layoutName = u.layoutItem != null ? u.layoutItem.name : "<null>";
                        }
                        catch { layoutName = "<unreadable>"; }
                        var b = u.unitBounds;
                        sb.AppendLine(
                            $"[WMStream]   unit[{i}] layout='{layoutName}' L{u.layer} " +
                            $"pos=({u.position.x:F0},{u.position.y:F0},{u.position.z:F0}) " +
                            $"bounds c=({b.center.x:F0},{b.center.y:F0},{b.center.z:F0}) " +
                            $"ext=({b.extents.x:F0},{b.extents.y:F0},{b.extents.z:F0})");
                    }
                }
                sb.Append("[WMStream] === END CULLING DATA DUMP ===");
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[WMStream] culling data dump FAILED: {ex}");
            }
        }

        // --------------------------------------------------------------------
        // Hierarchy / component dumps
        // --------------------------------------------------------------------

        /// <summary>Logs the baseline census grouped by parent GameObject
        /// (top groups by collider count, with XZ extents) and dumps the
        /// ancestor components of the biggest groups — the first place to
        /// look for the streaming controller.</summary>
        private static void LogBaselineGroups(Vector3 playerPos)
        {
            if (_known == null) return;

            var groups = new Dictionary<string, (int count, Collider sample,
                float minX, float maxX, float minZ, float maxZ)>();

            // Need live collider references for the dumps — cheap second
            // pass over the same frame's objects.
            var found = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            if (found == null) return;
            for (int i = 0; i < found.Length; i++)
            {
                var col = found[i];
                if (col == null) continue;
                var go = col.gameObject;
                if (go == null) continue;
                if ((_watchMask & (1 << go.layer)) == 0) continue;

                string parent = go.transform.parent != null
                    ? go.transform.parent.name : "<scene root>";
                string key = $"{parent} (L{go.layer}, scene {go.scene.name})";
                var b = col.bounds;
                if (groups.TryGetValue(key, out var g))
                {
                    g.count++;
                    if (b.min.x < g.minX) g.minX = b.min.x;
                    if (b.max.x > g.maxX) g.maxX = b.max.x;
                    if (b.min.z < g.minZ) g.minZ = b.min.z;
                    if (b.max.z > g.maxZ) g.maxZ = b.max.z;
                    groups[key] = g;
                }
                else
                {
                    groups[key] = (1, col, b.min.x, b.max.x, b.min.z, b.max.z);
                }
            }

            var ordered = new List<KeyValuePair<string,
                (int count, Collider sample, float minX, float maxX, float minZ, float maxZ)>>(groups);
            ordered.Sort((a, b) => b.Value.count.CompareTo(a.Value.count));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(
                $"[WMStream] baseline: {_known.Count} watched colliders in {groups.Count} " +
                $"parent groups near player ({playerPos.x:F0},{playerPos.z:F0}). Top groups:");
            int shown = 0;
            foreach (var kv in ordered)
            {
                if (shown++ >= 40) { sb.AppendLine($"[WMStream]   ... +{groups.Count - 40} more groups"); break; }
                var g = kv.Value;
                sb.AppendLine(
                    $"[WMStream]   {kv.Key}: {g.count} colliders, " +
                    $"X=[{g.minX:F0},{g.maxX:F0}] Z=[{g.minZ:F0},{g.maxZ:F0}]");
            }
            MelonLogger.Msg(sb.ToString());

            // Ancestor component dumps for the biggest groups.
            int dumps = 0;
            foreach (var kv in ordered)
            {
                if (dumps++ >= 8) break;
                DumpAncestors(kv.Value.sample);
            }
        }

        /// <summary>Logs the component types on every ancestor of a
        /// collider, once per parent name (the streaming controller should
        /// appear as a non-standard component on some ancestor).</summary>
        private static void DumpAncestors(Collider col)
        {
            try
            {
                var go = col.gameObject;
                if (go == null) return;
                string parentName = go.transform.parent != null
                    ? go.transform.parent.name : "<scene root>";
                if (_dumpedParents.Count >= MaxRootDumps) return;
                if (!_dumpedParents.Add(parentName)) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(
                    $"[WMStream] ancestor chain of '{go.name}' (parent group '{parentName}', " +
                    $"scene '{go.scene.name}'):");
                var t = go.transform;
                int depth = 0;
                while (t != null && depth < 10)
                {
                    var comps = t.gameObject.GetComponents<Component>();
                    var names = new List<string>();
                    if (comps != null)
                    {
                        for (int i = 0; i < comps.Length && names.Count < 15; i++)
                        {
                            if (comps[i] == null) continue;
                            string n;
                            try { n = comps[i].GetIl2CppType().Name; }
                            catch { n = "?"; }
                            names.Add(n);
                        }
                    }
                    sb.AppendLine(
                        $"[WMStream]   {new string(' ', depth)}'{t.gameObject.name}' " +
                        $"active={t.gameObject.activeSelf} → [{string.Join(", ", names)}]");
                    t = t.parent;
                    depth++;
                }
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[WMStream] ancestor dump failed: {ex.Message}");
            }
        }
    }
}
