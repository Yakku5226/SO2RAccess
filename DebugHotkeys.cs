using Il2CppGame;
using MelonLoader;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Debug-only investigation hotkeys, separated from the production input
    /// dispatch in <see cref="Main.ProcessHotkeys"/>. All keys here are gated on
    /// <see cref="Main.DebugMode"/> by the caller and do nothing in normal play.
    ///
    /// F5  — scan L22/L23 obstacle collider parents within 50m (world map).
    /// F8  — CharaWall boundary scan (world map).
    /// F9  — generate + cache the world-map walkability grid.
    /// F10 — log player collider details.
    /// F11 — diagnostics: world map pathfinding (RunAll) or, on field maps,
    ///       recorded-traversal reachability report.
    /// </summary>
    internal sealed class DebugHotkeys
    {
        private readonly NavigationHandler _navigationHandler;

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
            // F5 — scan L22/L23 obstacle parents near player (debug only, world map)
            if (kb[Key.F5].wasPressedThisFrame)
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
            if (kb[Key.F8].wasPressedThisFrame)
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
            if (kb[Key.F9].wasPressedThisFrame)
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
            // F10 — player collider diagnostics (debug only)
            if (kb[Key.F10].wasPressedThisFrame)
            {
                try
                {
                    WorldmapGridGenerator.LogPlayerCollider();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F10 collider diagnostics error: {ex.Message}");
                }
                return true;
            }
            // F11 — diagnostics (world map or field map NavMesh islands)
            if (kb[Key.F11].wasPressedThisFrame)
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
                                // Recorded-traversal reachability report.
                                _navigationHandler.LogTraversalDiagnostic(
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
            return false;
        }
    }
}
