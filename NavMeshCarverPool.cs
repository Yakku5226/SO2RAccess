using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    /// <summary>
    /// A pool of mod-owned, invisible <see cref="NavMeshObstacle"/> "carver" markers.
    /// Parking a carver on a standing NPC's position cuts a small hole in the baked
    /// NavMesh there, so the game's own <c>NavMesh.CalculatePath</c> routes around it —
    /// making the field pathfinder NPC-aware without any custom steering. The mod never
    /// touches game objects: it spawns its own GameObjects and removes them on cleanup.
    ///
    /// The carve radius is deliberately small (~0.35 m, body-sized) so single NPCs are
    /// avoided but a crowd is not sealed into an impassable wall. See
    /// <see cref="NavMeshCarverPool"/> usage in NavigationHandler auto-walk.
    ///
    /// Validated against this game's NavMesh: the game itself carves at runtime
    /// (FieldGimmick16 uses a NavMeshObstacle for destructibles).
    /// </summary>
    public sealed class NavMeshCarverPool
    {
        /// <summary>Capsule radius of each carver (m). Body-sized so gaps stay walkable.</summary>
        public float CarveRadius { get; set; } = 0.35f;

        /// <summary>Capsule height of each carver (m).</summary>
        public float CarveHeight { get; set; } = 2.0f;

        /// <summary>
        /// When true, a carver only cuts the mesh once it has been stationary
        /// (cheap for standing spectators). When false it carves immediately every
        /// frame (used by the proof-of-concept so the with/without comparison has no
        /// settle delay). Set before the first <see cref="SetPositions"/> call.
        /// </summary>
        public bool CarveOnlyStationary { get; set; } = true;

        private readonly int _capacity;
        private readonly List<GameObject> _markers = new List<GameObject>();
        private bool _suppressed;

        /// <summary>Number of carvers currently active (carving).</summary>
        public int ActiveCount { get; private set; }

        /// <summary>True while all carving is temporarily suppressed (see <see cref="Suppress"/>).</summary>
        public bool Suppressed => _suppressed;

        /// <param name="capacity">Maximum number of NPCs carved at once.</param>
        public NavMeshCarverPool(int capacity = 12)
        {
            _capacity = Math.Max(1, capacity);
        }

        /// <summary>
        /// Parks carvers on the given world positions (capped to the pool capacity),
        /// activating exactly that many and deactivating the rest. No-op while
        /// suppressed. Lazily creates the marker GameObjects on first use.
        /// </summary>
        public void SetPositions(IReadOnlyList<Vector3> positions)
        {
            if (_suppressed) return;

            try
            {
                EnsureCreated();

                int n = positions == null ? 0 : Math.Min(positions.Count, _capacity);
                for (int i = 0; i < _markers.Count; i++)
                {
                    var go = _markers[i];
                    if (go == null) continue;

                    if (i < n)
                    {
                        go.transform.position = positions[i];
                        if (!go.activeSelf) go.SetActive(true);
                    }
                    else if (go.activeSelf)
                    {
                        go.SetActive(false);
                    }
                }
                ActiveCount = n;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NavMeshCarverPool.SetPositions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Temporarily disables (true) or restores (false) all carving — used by the
        /// disconnection fallback so a path can be recomputed on the un-carved mesh.
        /// While suppressed, <see cref="SetPositions"/> is a no-op.
        /// </summary>
        public void Suppress(bool on)
        {
            if (_suppressed == on) return;
            _suppressed = on;

            if (on)
            {
                DeactivateAll();
                ActiveCount = 0;
            }
        }

        /// <summary>Deactivates every carver without destroying the pool.</summary>
        public void DeactivateAll()
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                var go = _markers[i];
                if (go != null && go.activeSelf) go.SetActive(false);
            }
            ActiveCount = 0;
        }

        /// <summary>Destroys all marker GameObjects. Call on scene change / shutdown.</summary>
        public void Destroy()
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                var go = _markers[i];
                if (go != null)
                {
                    try { UnityEngine.Object.Destroy(go); }
                    catch (Exception ex) { DebugLogger.LogState($"NavMeshCarverPool.Destroy: {ex.Message}"); }
                }
            }
            _markers.Clear();
            ActiveCount = 0;
            _suppressed = false;
        }

        /// <summary>Lazily creates the pool's marker GameObjects (inactive) on first use.</summary>
        private void EnsureCreated()
        {
            if (_markers.Count >= _capacity) return;

            while (_markers.Count < _capacity)
            {
                GameObject go = null;
                try
                {
                    go = new GameObject("SO2RAccess_Carver");
                    go.SetActive(false);
                    // Persist across map loads so the pool stays valid for the whole
                    // session; carving is stopped via DeactivateAll on each map change.
                    UnityEngine.Object.DontDestroyOnLoad(go);

                    // Generic AddComponent<T> over an Il2Cpp engine type; fall back to
                    // the reflection form if the generic resolution ever fails.
                    NavMeshObstacle obs;
                    try { obs = go.AddComponent<NavMeshObstacle>(); }
                    catch
                    {
                        obs = go.AddComponent(Il2CppType.Of<NavMeshObstacle>())
                                .TryCast<NavMeshObstacle>();
                    }

                    if (obs == null)
                    {
                        DebugLogger.LogState("NavMeshCarverPool: AddComponent<NavMeshObstacle> returned null.");
                        UnityEngine.Object.Destroy(go);
                        return;
                    }

                    obs.shape = NavMeshObstacleShape.Capsule;
                    obs.center = Vector3.zero;
                    obs.radius = CarveRadius;
                    obs.height = CarveHeight;
                    obs.carveOnlyStationary = CarveOnlyStationary;
                    obs.carving = true;

                    _markers.Add(go);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NavMeshCarverPool.EnsureCreated error: {ex.Message}");
                    if (go != null) { try { UnityEngine.Object.Destroy(go); } catch { } }
                    return; // stop trying this frame
                }
            }

            MelonLogger.Msg($"NavMeshCarverPool: created {_markers.Count} carver markers "
                + $"(radius={CarveRadius:F2}m, carveOnlyStationary={CarveOnlyStationary}).");
        }
    }
}
