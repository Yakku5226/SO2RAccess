using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Debug-only investigation hotkeys, separated from the production input
    /// dispatch in <see cref="Main.ProcessHotkeys"/>. All keys here are gated on
    /// <see cref="Main.DebugMode"/> by the caller and do nothing in normal play.
    ///
    /// F5  — scan L22/L23 obstacle collider parents within 50m (world map).
    /// F6  — toggle the collision STREAMING trace (world map): logs colliders
    ///       appearing/vanishing/toggling as the player moves, to find the
    ///       game's collision streaming controller (see
    ///       <see cref="WorldmapStreamingDiagnostics"/>).
    /// F7  — world map: ROUTE AUDITOR (plan + capsule-sweep every nav-list
    ///       route, log every physically impassable segment); field map:
    ///       NavMesh carving proof-of-concept.
    /// F8  — CharaWall boundary scan (world map).
    /// F9  — generate + cache the world-map walkability grid.
    /// F10 — log player collider details + travel-mode wall masks (press
    ///       once on foot and once mounted to capture both bodies/masks).
    /// F11 — diagnostics: world map pathfinding (RunAll) or, on field maps,
    ///       recorded-traversal reachability report.
    /// </summary>
    internal sealed class DebugHotkeys
    {
        private readonly NavigationHandler _navigationHandler;

        // --- F7 NavMesh-carving proof-of-concept state ---
        private NavMeshCarverPool _pocPool;
        private readonly NavMeshPath _pocPath = new NavMeshPath();
        private bool _pocActive;
        private float _pocFireTime;
        private Vector3 _pocPlayerSample;
        private Vector3 _pocTargetSample;
        private int _pocUncarvedCount;
        private NavMeshPathStatus _pocUncarvedStatus;
        private int _pocCarvedNpcs;

        public DebugHotkeys(NavigationHandler navigationHandler)
        {
            _navigationHandler = navigationHandler;
        }

        /// <summary>
        /// Handles the debug hotkeys for this frame. Returns true if a key was
        /// consumed. Caller gates this on DebugMode.
        /// </summary>
        public bool Process(Keyboard kb)
        {
            // Advance the F7 carving proof-of-concept (deferred path comparison).
            TickPoc();

            // Phase-A travel-mode investigation: mode-transition logging +
            // bunny ride trace (world map only; cheap, debug-gated by caller).
            WorldmapGridDiagnostics.Tick();

            // Collision streaming trace (only sweeps while F6-armed).
            WorldmapStreamingDiagnostics.Tick();

            // F6 — collision streaming trace toggle (debug only, world map)
            if (kb[ModKeys.Get(ModAction.DebugCollisionTrace)].wasPressedThisFrame)
            {
                try
                {
                    WorldmapStreamingDiagnostics.Toggle();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F6 streaming trace error: {ex.Message}");
                }
                return true;
            }

            // F7 — world map: route auditor (plan + physics-validate every
            // nav-list route without walking). Field map: NavMesh carving
            // proof-of-concept (unchanged).
            if (kb[ModKeys.Get(ModAction.DebugRouteAuditor)].wasPressedThisFrame)
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null && fm.IsWorldmap())
                        _navigationHandler.RunWorldmapRouteAudit();
                    else
                        StartCarvePoc();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F7 route audit error: {ex.Message}");
                }
                return true;
            }
            // F5 — scan L22/L23 obstacle parents near player (debug only, world map)
            if (kb[ModKeys.Get(ModAction.DebugObstacleScan)].wasPressedThisFrame)
            {
                try
                {
                    var player = Il2CppGame.FieldManager.Instance?.GetControlPlayer();
                    if (player != null)
                    {
                        Vector3 pos = player.transform.position;
                        MelonLogger.Msg($"[F5] Scanning L22+L23 colliders within 50m of ({pos.x:F1},{pos.y:F1},{pos.z:F1})...");

                        int layerMask = (1 << 22) | (1 << 23);
                        var cols = UnityEngine.Physics.OverlapSphere(pos, 50f, layerMask);
                        if (cols == null || cols.Length == 0)
                        {
                            MelonLogger.Msg("[F5] No L22/L23 colliders found within 50m.");
                        }
                        else
                        {
                            // Group by parent name
                            var groups = new System.Collections.Generic.Dictionary<string,
                                (int count, float minX, float maxX, float minZ, float maxZ, int layer)>();

                            foreach (var col in cols)
                            {
                                if (col == null || col.isTrigger) continue;
                                string parentName = "?";
                                var parentT = col.transform.parent;
                                if (parentT != null) parentName = parentT.gameObject.name;
                                int layer = col.gameObject.layer;
                                string key = $"{parentName}(L{layer})";

                                var b = col.bounds;
                                if (groups.ContainsKey(key))
                                {
                                    var g = groups[key];
                                    g.count++;
                                    if (b.min.x < g.minX) g.minX = b.min.x;
                                    if (b.max.x > g.maxX) g.maxX = b.max.x;
                                    if (b.min.z < g.minZ) g.minZ = b.min.z;
                                    if (b.max.z > g.maxZ) g.maxZ = b.max.z;
                                    groups[key] = g;
                                }
                                else
                                {
                                    groups[key] = (1, b.min.x, b.max.x, b.min.z, b.max.z, layer);
                                }
                            }

                            MelonLogger.Msg($"[F5] Found {cols.Length} colliders in {groups.Count} groups:");
                            foreach (var kv in groups)
                            {
                                var g = kv.Value;
                                float sizeX = g.maxX - g.minX;
                                float sizeZ = g.maxZ - g.minZ;
                                MelonLogger.Msg(
                                    $"[F5]   {kv.Key}: {g.count} colliders, " +
                                    $"X=[{g.minX:F1},{g.maxX:F1}] Z=[{g.minZ:F1},{g.maxZ:F1}] " +
                                    $"size={sizeX:F1}x{sizeZ:F1}m");
                            }
                        }

                        ScreenReader.Say("Obstacle scan complete. Check log.");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Msg($"[F5] Error: {ex.Message}");
                }
                return true;
            }
            // F8 — CharaWall boundary scan (debug only, world map)
            if (kb[ModKeys.Get(ModAction.DebugCharaWallScan)].wasPressedThisFrame)
            {
                try
                {
                    if (FieldManager.Instance != null &&
                        FieldManager.Instance.IsWorldmap())
                    {
                        var player = FieldManager.Instance.GetControlPlayer();
                        if (player != null)
                        {
                            WorldmapDiagnostics.ScanCharaWalls(
                                player.transform.position);
                        }
                    }
                    else
                    {
                        ScreenReader.Say("Wall scan only available on world map.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F8 wall scan error: {ex.Message}");
                }
                return true;
            }
            // F9 — generate world map grid (debug only)
            if (kb[ModKeys.Get(ModAction.DebugGridBake)].wasPressedThisFrame)
            {
                try
                {
                    WorldmapGridGenerator.GenerateAndSave();
                    WorldmapPathfinder.ClearCache();
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Msg($"F9 grid generation error: {ex.Message}");
                }
                return true;
            }
            // F10 — travel-mode masks + player collider diagnostics (debug only)
            if (kb[ModKeys.Get(ModAction.DebugTravelMask)].wasPressedThisFrame)
            {
                try
                {
                    WorldmapGridDiagnostics.LogTravelMasks();
                    WorldmapGridDiagnostics.LogPlayerCollider();
                    WorldmapGridDiagnostics.LogHeightTruthCensus();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F10 collider diagnostics error: {ex.Message}");
                }
                return true;
            }
            // F11 — diagnostics (world map or field map NavMesh islands)
            if (kb[ModKeys.Get(ModAction.DebugPathDiagnostics)].wasPressedThisFrame)
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null)
                    {
                        var player = fm.GetControlPlayer();
                        if (player != null)
                        {
                            if (fm.IsWorldmap())
                            {
                                WorldmapDiagnostics.RunAll(
                                    player.transform.position);
                            }
                            else
                            {
                                // Recorded-traversal reachability report, then the
                                // floor-grid audit against those breadcrumbs.
                                _navigationHandler.LogTraversalDiagnostic(
                                    player.transform.position);
                                _navigationHandler.RunFloorGridAudit(
                                    player.transform.position);
                                // Wall-probe gate for the manual-nav wall sounds.
                                _navigationHandler.RunWallProbeAudit(
                                    player.transform.position);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F11 diagnostics error: {ex.Message}");
                }
                return true;
            }

            // Semicolon — dump every visible on-screen text with its object path.
            // Works anywhere, so a cutscene whose text the caption hooks never see
            // can still be located: press it while the text is on screen.
            if (kb[ModKeys.Get(ModAction.DebugTextDump)].wasPressedThisFrame)
            {
                try
                {
                    SubtitleHandler.DumpVisibleText();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"Text dump error: {ex.Message}");
                }
                return true;
            }
            return false;
        }

        #region F7 — NavMesh carving proof-of-concept

        /// <summary>
        /// Phase A test: computes a NavMesh path to a far NPC on the UN-carved mesh,
        /// then parks carver markers on the nearest NPCs and (after a short settle, in
        /// <see cref="TickPoc"/>) recomputes the path on the CARVED mesh and logs the
        /// difference. Answers whether runtime carving (a) works in this IL2CPP interop
        /// and (b) actually reroutes the path around standing NPCs without sealing the
        /// crowd. Field maps only.
        /// </summary>
        private void StartCarvePoc()
        {
            try
            {
                var fm = FieldManager.Instance;
                var player = fm?.GetControlPlayer();
                if (fm == null || player == null || fm.IsWorldmap())
                {
                    ScreenReader.Say("Carve test only available on a field map.");
                    return;
                }
                if (_pocActive)
                {
                    MelonLogger.Msg("[F7] Carve test already running.");
                    return;
                }

                Vector3 playerPos = player.transform.position;

                // Gather candidate NPC bodies (exclude player, followers, enemies).
                var npcs = GatherNpcBodies(playerPos);
                if (npcs.Count < 2)
                {
                    ScreenReader.Say("Not enough NPCs nearby for the carve test.");
                    MelonLogger.Msg($"[F7] Only {npcs.Count} NPC bodies found.");
                    return;
                }

                // Target = farthest NPC within 60m (forces a long crossing); fallback farthest.
                npcs.Sort((a, b) => a.dist.CompareTo(b.dist));
                Vector3 targetPos = npcs[npcs.Count - 1].pos;
                for (int i = npcs.Count - 1; i >= 0; i--)
                {
                    if (npcs[i].dist <= 60f) { targetPos = npcs[i].pos; break; }
                }

                if (!SampleNav(playerPos, out _pocPlayerSample) ||
                    !SampleNav(targetPos, out _pocTargetSample))
                {
                    ScreenReader.Say("Could not sample NavMesh for the carve test.");
                    MelonLogger.Msg("[F7] NavMesh sample failed for player or target.");
                    return;
                }

                // 1) UN-CARVED baseline path.
                NavMesh.CalculatePath(_pocPlayerSample, _pocTargetSample, NavMesh.AllAreas, _pocPath);
                _pocUncarvedStatus = _pocPath.status;
                _pocUncarvedCount = _pocPath.corners != null ? _pocPath.corners.Length : 0;
                MelonLogger.Msg($"[F7] UN-CARVED path to ({_pocTargetSample.x:F1},{_pocTargetSample.z:F1}): "
                    + $"status={_pocUncarvedStatus} corners={_pocUncarvedCount} "
                    + $"len={PathLength(_pocPath):F1}m");

                // 2) Park carvers on the nearest NPCs (excluding the target), carve now.
                var carvePositions = new List<Vector3>();
                foreach (var n in npcs)
                {
                    if ((n.pos - _pocTargetSample).sqrMagnitude < 1.0f) continue; // skip target
                    carvePositions.Add(n.pos);
                    if (carvePositions.Count >= 10) break;
                }

                if (_pocPool == null) _pocPool = new NavMeshCarverPool(12) { CarveOnlyStationary = false };
                _pocPool.Suppress(false);
                _pocPool.SetPositions(carvePositions);
                _pocCarvedNpcs = carvePositions.Count;

                _pocFireTime = Time.time + 0.4f; // let carving rebuild the local mesh
                _pocActive = true;

                MelonLogger.Msg($"[F7] Carving {_pocCarvedNpcs} NPCs (radius 0.35m); "
                    + "recomputing carved path in 0.4s...");
                ScreenReader.Say("Carve test started. Check log.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[F7] StartCarvePoc error: {ex.Message}");
                _pocActive = false;
                _pocPool?.Destroy();
            }
        }

        /// <summary>Deferred half of the F7 test: recomputes the carved path and reports.</summary>
        private void TickPoc()
        {
            if (!_pocActive || Time.time < _pocFireTime) return;
            _pocActive = false;

            try
            {
                NavMesh.CalculatePath(_pocPlayerSample, _pocTargetSample, NavMesh.AllAreas, _pocPath);
                NavMeshPathStatus carvedStatus = _pocPath.status;
                int carvedCount = _pocPath.corners != null ? _pocPath.corners.Length : 0;
                float carvedLen = PathLength(_pocPath);

                bool changed = carvedCount != _pocUncarvedCount || carvedStatus != _pocUncarvedStatus;
                MelonLogger.Msg($"[F7] CARVED path ({_pocCarvedNpcs} NPCs carved): "
                    + $"status={carvedStatus} corners={carvedCount} len={carvedLen:F1}m "
                    + $"| baseline status={_pocUncarvedStatus} corners={_pocUncarvedCount} "
                    + $"| changed={changed}");

                if (carvedStatus != NavMeshPathStatus.PathComplete
                    && _pocUncarvedStatus == NavMeshPathStatus.PathComplete)
                {
                    MelonLogger.Msg("[F7] WARNING: carving turned a COMPLETE path into "
                        + $"{carvedStatus} — crowd may be getting sealed (try a smaller radius / fewer carvers).");
                }

                ScreenReader.Say(changed
                    ? "Carve test done. The path changed. Check log."
                    : "Carve test done. The path did not change. Check log.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[F7] TickPoc error: {ex.Message}");
            }
            finally
            {
                _pocPool?.Destroy(); // never leave carvers in the world
            }
        }

        /// <summary>Collects FieldNpcCharacter body positions, excluding player/followers/enemies.</summary>
        private static List<(Vector3 pos, float dist)> GatherNpcBodies(Vector3 playerPos)
        {
            var list = new List<(Vector3, float)>();
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (found == null) return list;
                foreach (var npc in found)
                {
                    if (npc == null) continue;
                    if (npc.TryCast<FieldPlayer>() != null) continue;
                    if (npc.TryCast<FieldFollowCharacter>() != null) continue;
                    if (npc.TryCast<FieldEnemy>() != null) continue;
                    Vector3 p = npc.transform.position;
                    list.Add((p, Vector3.Distance(playerPos, p)));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[F7] GatherNpcBodies error: {ex.Message}");
            }
            return list;
        }

        /// <summary>Snaps a world position onto the NavMesh (2 m search). False if off-mesh.</summary>
        private static bool SampleNav(Vector3 pos, out Vector3 result)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
            result = pos;
            return false;
        }

        /// <summary>Total XZ length of a NavMesh path's corner polyline.</summary>
        private static float PathLength(NavMeshPath path)
        {
            var c = path?.corners;
            if (c == null || c.Length < 2) return 0f;
            float len = 0f;
            for (int i = 1; i < c.Length; i++)
                len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        #endregion
    }
}
