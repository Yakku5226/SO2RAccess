using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Bake-time loader for the world map's STREAMED collision chunks.
    /// Background (proven 2026-07-07): the game's CullingManager keeps rock
    /// ground-detail collision (layer 24 Mesh_Col/Col_Height) instantiated
    /// only near the camera (Near tier = 100m), so a bake can only trust
    /// what happens to be loaded. This class reads the manager's own
    /// CullingData — the authoritative list of every streamed unit's prefab,
    /// position, rotation, scale and bounds for the whole map — indexes the
    /// units into bake tiles, and instantiates each tile's chunks on demand
    /// so the generator probes every cell with its REAL local geometry.
    ///
    /// Instantiation is side-effect-free by construction: clones are created
    /// under an INACTIVE staging root (Unity defers Awake), all MonoBehaviour
    /// scripts (CullingSettings, DistanceLOD, ...) are destroyed while still
    /// asleep, and only the bare geometry (transforms, meshes, colliders) is
    /// then activated for physics. The game's culler never sees our clones.
    /// </summary>
    public sealed class WorldmapChunkLoader : IDisposable
    {
        /// <summary>Extra metres around a unit's bounds when assigning it to
        /// tiles — covers the generator's obstacle probe radius so a cell at
        /// a tile edge still sees a chunk that barely overlaps.</summary>
        private const float BoundsMargin = 2.0f;

        private readonly List<CullingData.CullingUnit>[] _tileUnits;
        private readonly int _tilesX;
        private readonly int _tilesZ;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly float _tileSize;

        private readonly List<GameObject> _live = new List<GameObject>();
        /// <summary>Per-prefab cache (key = layoutItem instance id): which
        /// prefab to actually instantiate for baking — the layoutItem itself,
        /// the pool prefab (when the layoutItem is visuals-only but the pool
        /// carries the collision — the D1 Salva missing-rock bug, 2026-07-10),
        /// or null when neither has colliders (skip).</summary>
        private readonly Dictionary<int, Transform> _prefabBakeSource =
            new Dictionary<int, Transform>();
        /// <summary>Pool prefabs by name — the culler's own instancing source.
        /// Used as collision fallback when a unit's layoutItem has no
        /// colliders. Null when poolInfoList was unreadable (fallback off,
        /// logged).</summary>
        private Dictionary<string, Transform> _poolByName;
        /// <summary>Pools already reported for inactive-collider activation
        /// (one log line per pool, not per clone).</summary>
        private readonly HashSet<string> _activationLoggedPools =
            new HashSet<string>();

        private GameObject _stagingRoot; // inactive — suppresses Awake
        private GameObject _liveRoot;    // active — physics sees children

        /// <summary>Units indexed into at least one tile.</summary>
        public int UnitsIndexed { get; private set; }
        /// <summary>Total chunk instantiations (border units count once per
        /// tile they touch).</summary>
        public int InstantiationsTotal { get; private set; }
        /// <summary>Instantiations skipped because the prefab has no colliders.</summary>
        public int SkippedNoColliders { get; private set; }
        /// <summary>Clones where inactive collider objects had to be activated
        /// (LOD children that ship disabled in the prefab).</summary>
        public int ActivationFixes { get; private set; }

        private WorldmapChunkLoader(int tilesX, int tilesZ, float minX,
            float minZ, float tileSize)
        {
            _tilesX = tilesX;
            _tilesZ = tilesZ;
            _minX = minX;
            _minZ = minZ;
            _tileSize = tileSize;
            _tileUnits = new List<CullingData.CullingUnit>[tilesX * tilesZ];
        }

        /// <summary>
        /// Reads the live CullingManager database and builds the tile index.
        /// Tile counts come from the caller so loader and bake loop share
        /// EXACTLY the same tiling. Returns null with a reason when the data
        /// is unavailable — the caller must ABORT the bake then (a bake
        /// without the streamed chunks is exactly the far-field fiction this
        /// class exists to fix).
        /// </summary>
        public static WorldmapChunkLoader TryCreate(float worldMinX,
            float worldMinZ, int tilesX, int tilesZ,
            float tileSize, out string failReason)
        {
            failReason = null;
            CullingManager mgr;
            try
            {
                mgr = CullingManager.Instance;
            }
            catch (Exception ex)
            {
                failReason = $"CullingManager.Instance threw: {ex.Message}";
                return null;
            }
            if (mgr == null)
            {
                failReason = "CullingManager.Instance is null";
                return null;
            }

            CullingData data;
            try
            {
                data = mgr.cullingData;
            }
            catch (Exception ex)
            {
                failReason = $"cullingData read threw: {ex.Message}";
                return null;
            }
            if (data == null)
            {
                failReason = "CullingManager.cullingData is null";
                return null;
            }

            var units = data.unitList;
            int unitCount = units != null ? units.Count : 0;
            if (unitCount == 0)
            {
                failReason = "cullingData.unitList is empty";
                return null;
            }

            var loader = new WorldmapChunkLoader(tilesX, tilesZ,
                worldMinX, worldMinZ, tileSize);

            int outOfBounds = 0;
            for (int i = 0; i < unitCount; i++)
            {
                var u = units[i];
                if (u == null || u.layoutItem == null) continue;
                var b = u.unitBounds;
                float uMinX = b.min.x - BoundsMargin;
                float uMaxX = b.max.x + BoundsMargin;
                float uMinZ = b.min.z - BoundsMargin;
                float uMaxZ = b.max.z + BoundsMargin;

                int tx0 = Math.Max(0, (int)((uMinX - worldMinX) / tileSize));
                int tx1 = Math.Min(tilesX - 1, (int)((uMaxX - worldMinX) / tileSize));
                int tz0 = Math.Max(0, (int)((uMinZ - worldMinZ) / tileSize));
                int tz1 = Math.Min(tilesZ - 1, (int)((uMaxZ - worldMinZ) / tileSize));
                if (tx1 < 0 || tz1 < 0 || tx0 >= tilesX || tz0 >= tilesZ ||
                    tx1 < tx0 || tz1 < tz0)
                {
                    outOfBounds++;
                    continue;
                }

                for (int tx = tx0; tx <= tx1; tx++)
                {
                    for (int tz = tz0; tz <= tz1; tz++)
                    {
                        int idx = tx * tilesZ + tz;
                        if (loader._tileUnits[idx] == null)
                            loader._tileUnits[idx] =
                                new List<CullingData.CullingUnit>();
                        loader._tileUnits[idx].Add(u);
                    }
                }
                loader.UnitsIndexed++;
            }

            // Index the pool prefabs by name — the culler's real instancing
            // source. A unit's layoutItem can be visuals-only while the pool
            // prefab carries the collision; without this fallback such units
            // bake as missing rocks (proven: D1 Salva wedge, 2026-07-10).
            int poolCount = 0;
            try
            {
                var pools = data.poolInfoList;
                if (pools != null)
                {
                    loader._poolByName = new Dictionary<string, Transform>();
                    for (int i = 0; i < pools.Count; i++)
                    {
                        var p = pools[i];
                        if (p == null || p.poolObject == null) continue;
                        loader._poolByName[p.poolObject.name] = p.poolObject;
                    }
                    poolCount = loader._poolByName.Count;
                }
            }
            catch (Exception ex)
            {
                loader._poolByName = null;
                MelonLogger.Msg(
                    "[GridGen] poolInfoList unreadable — collider fallback " +
                    $"to pool prefabs DISABLED: {ex.Message}");
            }

            loader._stagingRoot = new GameObject("SO2R_BakeChunkStaging");
            loader._stagingRoot.SetActive(false);
            loader._liveRoot = new GameObject("SO2R_BakeChunksLive");

            MelonLogger.Msg(
                $"[GridGen] Chunk loader ready: {loader.UnitsIndexed}/{unitCount} " +
                $"culling units indexed into {tilesX}x{tilesZ} tiles of " +
                $"{tileSize:F0}m (outOfBakeBounds={outOfBounds}, " +
                $"poolPrefabs={poolCount}).");
            return loader;
        }

        /// <summary>Number of units assigned to a tile (0 = nothing to load).</summary>
        public int UnitCount(int tx, int tz)
        {
            var list = _tileUnits[tx * _tilesZ + tz];
            return list != null ? list.Count : 0;
        }

        /// <summary>
        /// Instantiates all chunks for one tile and syncs physics so raycasts
        /// see them immediately. Returns the number of live clones. Always
        /// pair with <see cref="UnloadTile"/> (the generator wraps the whole
        /// bake in try/finally via <see cref="Dispose"/>).
        /// </summary>
        public int LoadTile(int tx, int tz)
        {
            var list = _tileUnits[tx * _tilesZ + tz];
            if (list == null || list.Count == 0) return 0;

            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                var prefab = u.layoutItem;
                if (prefab == null) continue;

                // Resolve the bake source once per prefab: layoutItem when it
                // has colliders, else the pool prefab (the culler's own
                // instancing source), else skip as genuinely visual-only.
                int prefabId = prefab.GetInstanceID();
                if (!_prefabBakeSource.TryGetValue(prefabId, out Transform source))
                {
                    source = ResolveBakeSource(prefab);
                    _prefabBakeSource[prefabId] = source;
                }
                if (source == null) { SkippedNoColliders++; continue; }

                var clone = InstantiateBareGeometry(u, source);
                if (clone != null) _live.Add(clone);
            }

            if (_live.Count > 0) Physics.SyncTransforms();
            return _live.Count;
        }

        /// <summary>Destroys all live chunk clones immediately (the bake is
        /// synchronous — deferred Destroy would never run mid-bake).</summary>
        public void UnloadTile()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i] != null)
                    UnityEngine.Object.DestroyImmediate(_live[i]);
            }
            _live.Clear();
        }

        /// <summary>Removes any leftover clones and the helper roots.</summary>
        public void Dispose()
        {
            UnloadTile();
            if (_stagingRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_stagingRoot);
                _stagingRoot = null;
            }
            if (_liveRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_liveRoot);
                _liveRoot = null;
            }
        }

        /// <summary>
        /// Picks which prefab to instantiate for a unit: the layoutItem when
        /// it carries colliders; otherwise the same-named pool prefab when
        /// THAT carries colliders (the game's culler instantiates pool
        /// prefabs, so this is what physically stands in the world); null
        /// when both are collider-free. Logged once per prefab.
        /// </summary>
        private Transform ResolveBakeSource(Transform layoutItem)
        {
            var cols = layoutItem.GetComponentsInChildren<Collider>(true);
            if (cols != null && cols.Length > 0) return layoutItem;

            if (_poolByName != null &&
                _poolByName.TryGetValue(layoutItem.name, out Transform poolObj) &&
                poolObj != null)
            {
                var poolCols = poolObj.GetComponentsInChildren<Collider>(true);
                if (poolCols != null && poolCols.Length > 0)
                {
                    MelonLogger.Msg(
                        $"[GridGen] chunk pool '{layoutItem.name}': layoutItem " +
                        $"is visual-only but the POOL prefab has " +
                        $"{poolCols.Length} colliders — baking with the pool " +
                        "prefab (was missing from earlier bakes).");
                    return poolObj;
                }
            }

            MelonLogger.Msg(
                $"[GridGen] chunk pool '{layoutItem.name}' has no colliders " +
                "on layoutItem or pool prefab — skipping its units (visual-only).");
            return null;
        }

        /// <summary>
        /// Clones a unit's prefab as pure geometry at its recorded transform:
        /// created under the inactive staging root (Awake deferred), game
        /// scripts destroyed while asleep, then moved to the live root and
        /// fully activated so every collider (including disabled LOD
        /// children) participates in physics.
        /// </summary>
        private GameObject InstantiateBareGeometry(
            CullingData.CullingUnit u, Transform prefab)
        {
            try
            {
                Transform inst = UnityEngine.Object.Instantiate(
                    prefab, u.position, u.rotation, _stagingRoot.transform);
                var go = inst.gameObject;
                inst.localScale = u.scale;
                go.layer = u.layer; // mimic the culler; children keep prefab layers

                // Strip game scripts BEFORE activation — they never wake up.
                var scripts = inst.GetComponentsInChildren<MonoBehaviour>(true);
                if (scripts != null)
                {
                    for (int i = 0; i < scripts.Length; i++)
                    {
                        if (scripts[i] != null)
                            UnityEngine.Object.DestroyImmediate(scripts[i]);
                    }
                }

                // Activate the full hierarchy: prefabs ship with some LOD
                // children disabled; their colliders are real geometry too.
                bool activatedAny = ActivateAll(inst);
                if (activatedAny)
                {
                    ActivationFixes++;
                    if (_activationLoggedPools.Add(prefab.name))
                    {
                        MelonLogger.Msg(
                            $"[GridGen] chunk pool '{prefab.name}': activated " +
                            "disabled child objects for baking (once per pool).");
                    }
                }

                inst.SetParent(_liveRoot.transform, true);
                go.SetActive(true);
                InstantiationsTotal++;
                return go;
            }
            catch (Exception ex)
            {
                MelonLogger.Msg(
                    $"[GridGen] chunk instantiate FAILED for pool " +
                    $"'{prefab.name}' at ({u.position.x:F0},{u.position.z:F0}): " +
                    $"{ex.Message}");
                return null;
            }
        }

        /// <summary>Sets every GameObject in the hierarchy active; returns
        /// true if any child was inactive.</summary>
        private static bool ActivateAll(Transform root)
        {
            bool any = false;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                var go = t.gameObject;
                if (!go.activeSelf)
                {
                    go.SetActive(true);
                    any = true;
                }
                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }
            return any;
        }
    }
}
