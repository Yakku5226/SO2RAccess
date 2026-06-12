using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Records where a player actually WALKS on a field map and builds a graph
    /// of those traversals ("breadcrumbs"). Because every node and edge comes
    /// from real physical movement, the resulting routes are 100% walkable — no
    /// raycast guessing, no wall/ramp ambiguity.
    ///
    /// Workflow: a (sighted) player walks the dungeon once; breadcrumbs are saved
    /// to disk per map; afterwards auto-walk routes over them reliably. This is
    /// the "learn by observing" approach — the only walkability signal in this
    /// game that is fully reliable, since the player moves by physics the static
    /// NavMesh/raycasts cannot model.
    /// </summary>
    public class TraversalGraph
    {
        // ── Tunables ─────────────────────────────────────────────────────────
        /// <summary>Minimum spacing between breadcrumbs (m).</summary>
        private const float MinSpacing = 1.0f;
        /// <summary>A new breadcrumb links to existing ones within this radius (m).</summary>
        private const float MergeRadius = 1.6f;
        /// <summary>...as long as their height differs by less than this (m).</summary>
        private const float MergeMaxDy = 1.2f;
        /// <summary>Max distance for a trail edge — larger jumps are teleports/cutscenes.</summary>
        private const float TrailMaxStep = 3.0f;
        /// <summary>Max distance to snap a query point (player/chest) to a breadcrumb (m).</summary>
        private const float SnapRadius = 6.0f;
        /// <summary>Y weight when snapping (prefer the correct floor).</summary>
        private const float SnapYWeight = 2.0f;
        /// <summary>Spatial-hash cell size (m).</summary>
        private const float HashCell = 2.0f;

        private static readonly string Dir =
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess", "traversals");

        // ── Graph state ──────────────────────────────────────────────────────
        private readonly List<Vector3> _nodes = new List<Vector3>();
        private readonly List<List<int>> _adj = new List<List<int>>();
        private readonly Dictionary<(int, int), List<int>> _hash =
            new Dictionary<(int, int), List<int>>();
        private int[] _comp;
        private bool _compDirty = true;
        private int _lastNode = -1;
        private string _mapId;
        private bool _dirty;

        public bool HasData => _nodes.Count > 0;
        public int NodeCount => _nodes.Count;

        // ── Map lifecycle ────────────────────────────────────────────────────

        /// <summary>Switch to a map: save the previous one, load this one, reset the trail.</summary>
        public void StartMap(string mapId)
        {
            if (_mapId == mapId) { _lastNode = -1; return; }
            Save();          // flush the previous map
            Clear();
            _mapId = mapId;
            Load(mapId);
            _lastNode = -1;
            MelonLoader.MelonLogger.Msg(
                $"[SO2RAccess] TRAVERSAL: map {mapId} — {_nodes.Count} breadcrumbs loaded.");
        }

        private void Clear()
        {
            _nodes.Clear(); _adj.Clear(); _hash.Clear();
            _comp = null; _compDirty = true; _lastNode = -1; _dirty = false;
        }

        // ── Recording ────────────────────────────────────────────────────────

        /// <summary>
        /// Records the player's current position as a breadcrumb. Cheap to call
        /// every frame: only adds a node when the player has moved past
        /// <see cref="MinSpacing"/> from the nearest existing breadcrumb.
        /// </summary>
        public void RecordPosition(Vector3 pos)
        {
            int near = FindNearest(pos, MinSpacing, MinSpacing); // tight: "still here?"
            int current;
            if (near >= 0)
            {
                current = near;
            }
            else
            {
                current = AddNode(pos);
                // Link to nearby existing breadcrumbs (merge overlapping passes).
                foreach (int n in NodesWithin(pos, MergeRadius))
                {
                    if (n == current) continue;
                    if (Mathf.Abs(_nodes[n].y - pos.y) > MergeMaxDy) continue;
                    AddEdge(current, n);
                }
                _compDirty = true; _dirty = true;
            }

            if (_lastNode >= 0 && _lastNode != current)
            {
                float d = Vector3.Distance(_nodes[_lastNode], _nodes[current]);
                if (d <= TrailMaxStep) // ignore teleports / scene jumps
                {
                    AddEdge(_lastNode, current);
                    _compDirty = true; _dirty = true;
                }
            }
            _lastNode = current;
        }

        /// <summary>Call when control is lost (cutscene, battle, menu) so the trail doesn't jump.</summary>
        public void BreakTrail() => _lastNode = -1;

        private int AddNode(Vector3 pos)
        {
            int idx = _nodes.Count;
            _nodes.Add(pos);
            _adj.Add(new List<int>(4));
            var key = HashKey(pos);
            if (!_hash.TryGetValue(key, out var list)) { list = new List<int>(4); _hash[key] = list; }
            list.Add(idx);
            return idx;
        }

        private void AddEdge(int a, int b)
        {
            if (a == b) return;
            if (!_adj[a].Contains(b)) _adj[a].Add(b);
            if (!_adj[b].Contains(a)) _adj[b].Add(a);
        }

        // ── Queries ──────────────────────────────────────────────────────────

        /// <summary>True if both points snap to breadcrumbs in the same walked component.</summary>
        public bool IsReachable(Vector3 a, Vector3 b)
        {
            int na = SnapToNode(a), nb = SnapToNode(b);
            if (na < 0 || nb < 0) return false;
            EnsureComponents();
            return _comp[na] == _comp[nb];
        }

        /// <summary>
        /// A* over the breadcrumb graph from <paramref name="from"/> to
        /// <paramref name="to"/>. Output waypoints are real walked positions, so
        /// the route is walkable. Returns false if not connected.
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, out Vector3[] corners)
        {
            corners = null;
            int start = SnapToNode(from), goal = SnapToNode(to);
            if (start < 0 || goal < 0) return false;
            EnsureComponents();
            if (_comp[start] != _comp[goal]) return false;

            int n = _nodes.Count;
            var g = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; came[i] = -1; }

            var heap = new MinHeap(256);
            g[start] = 0f;
            heap.Push(start, Vector3.Distance(_nodes[start], _nodes[goal]));
            bool found = false;
            while (heap.Count > 0)
            {
                int cur = heap.Pop();
                if (cur == goal) { found = true; break; }
                if (closed[cur]) continue;
                closed[cur] = true;
                foreach (int nb in _adj[cur])
                {
                    if (closed[nb]) continue;
                    float t = g[cur] + Vector3.Distance(_nodes[cur], _nodes[nb]);
                    if (t < g[nb])
                    {
                        g[nb] = t; came[nb] = cur;
                        heap.Push(nb, t + Vector3.Distance(_nodes[nb], _nodes[goal]));
                    }
                }
            }
            if (!found) return false;

            var pts = new List<Vector3>(64);
            int node = goal;
            while (node != -1) { pts.Add(_nodes[node]); node = came[node]; }
            pts.Reverse();
            pts.Add(to); // exact target as the final corner
            corners = pts.ToArray();
            return true;
        }

        /// <summary>Nearest breadcrumb to a point (XZ distance + weighted Y), within SnapRadius.</summary>
        public int SnapToNode(Vector3 pos)
        {
            int best = -1; float bestScore = SnapRadius * SnapRadius * 4f;
            foreach (int i in NodesWithin(pos, SnapRadius))
            {
                Vector3 p = _nodes[i];
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float score = Mathf.Sqrt(dx * dx + dz * dz) + SnapYWeight * Mathf.Abs(p.y - pos.y);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private void EnsureComponents()
        {
            if (!_compDirty && _comp != null && _comp.Length == _nodes.Count) return;
            _comp = new int[_nodes.Count];
            for (int i = 0; i < _comp.Length; i++) _comp[i] = -1;
            int c = 0;
            var q = new Queue<int>();
            for (int s = 0; s < _nodes.Count; s++)
            {
                if (_comp[s] != -1) continue;
                int id = c++;
                _comp[s] = id; q.Enqueue(s);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    foreach (int nb in _adj[cur])
                        if (_comp[nb] == -1) { _comp[nb] = id; q.Enqueue(nb); }
                }
            }
            _compDirty = false;
        }

        // ── Spatial hash helpers ─────────────────────────────────────────────

        private (int, int) HashKey(Vector3 p) =>
            (Mathf.FloorToInt(p.x / HashCell), Mathf.FloorToInt(p.z / HashCell));

        /// <summary>Nearest node within radius (XZ), or -1. yLimit caps the Y difference.</summary>
        private int FindNearest(Vector3 pos, float radius, float yLimit)
        {
            int best = -1; float bestD = radius;
            foreach (int i in NodesWithin(pos, radius))
            {
                Vector3 p = _nodes[i];
                if (Mathf.Abs(p.y - pos.y) > yLimit) continue;
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Enumerates node indices in hash cells overlapping the radius.</summary>
        private IEnumerable<int> NodesWithin(Vector3 pos, float radius)
        {
            int r = Mathf.CeilToInt(radius / HashCell);
            int cx = Mathf.FloorToInt(pos.x / HashCell), cz = Mathf.FloorToInt(pos.z / HashCell);
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                if (_hash.TryGetValue((cx + dx, cz + dz), out var list))
                    foreach (int i in list) yield return i;
        }

        // ── Persistence ──────────────────────────────────────────────────────

        public void Save()
        {
            if (!_dirty || _mapId == null || _nodes.Count == 0) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var data = new TraversalData
                {
                    MapId = _mapId,
                    Nodes = new List<float[]>(_nodes.Count),
                    Edges = new List<int[]>()
                };
                foreach (var p in _nodes) data.Nodes.Add(new[] { p.x, p.y, p.z });
                for (int a = 0; a < _adj.Count; a++)
                    foreach (int b in _adj[a])
                        if (a < b) data.Edges.Add(new[] { a, b });

                File.WriteAllText(Path.Combine(Dir, _mapId + ".json"),
                    JsonSerializer.Serialize(data));
                _dirty = false;
                MelonLoader.MelonLogger.Msg(
                    $"[SO2RAccess] TRAVERSAL: saved {_nodes.Count} breadcrumbs, " +
                    $"{data.Edges.Count} edges for {_mapId}.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] TRAVERSAL save error: {ex.Message}");
            }
        }

        private void Load(string mapId)
        {
            try
            {
                // The user's own recording (also where new recording is saved)
                // takes priority; otherwise fall back to the map data bundled
                // into the mod DLL at release.
                string path = Path.Combine(Dir, mapId + ".json");
                string json = File.Exists(path)
                    ? File.ReadAllText(path)
                    : ReadEmbedded(mapId);
                if (string.IsNullOrEmpty(json)) return;

                var data = JsonSerializer.Deserialize<TraversalData>(json);
                if (data?.Nodes == null) return;
                foreach (var p in data.Nodes) AddNode(new Vector3(p[0], p[1], p[2]));
                if (data.Edges != null)
                    foreach (var e in data.Edges) AddEdge(e[0], e[1]);
                _compDirty = true;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] TRAVERSAL load error: {ex.Message}");
            }
        }

        /// <summary>Reads pre-recorded map data embedded in the mod DLL, or null.</summary>
        private static string ReadEmbedded(string mapId)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string suffix = "traversals." + mapId + ".json";
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = asm.GetManifestResourceStream(name);
                    if (s == null) return null;
                    using var r = new StreamReader(s);
                    return r.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Msg($"[SO2RAccess] TRAVERSAL embedded read error: {ex.Message}");
            }
            return null;
        }

        private sealed class TraversalData
        {
            public string MapId { get; set; }
            public List<float[]> Nodes { get; set; }
            public List<int[]> Edges { get; set; }
        }

        // ── Minimal binary min-heap ──────────────────────────────────────────
        private sealed class MinHeap
        {
            private int[] _items; private float[] _prio; private int _count;
            public int Count => _count;
            public MinHeap(int cap) { _items = new int[cap]; _prio = new float[cap]; }
            public void Push(int item, float prio)
            {
                if (_count == _items.Length)
                { Array.Resize(ref _items, _count * 2); Array.Resize(ref _prio, _count * 2); }
                int i = _count++; _items[i] = item; _prio[i] = prio;
                while (i > 0) { int p = (i - 1) / 2; if (_prio[p] <= _prio[i]) break; Swap(i, p); i = p; }
            }
            public int Pop()
            {
                int top = _items[0]; _count--;
                _items[0] = _items[_count]; _prio[0] = _prio[_count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, s = i;
                    if (l < _count && _prio[l] < _prio[s]) s = l;
                    if (r < _count && _prio[r] < _prio[s]) s = r;
                    if (s == i) break; Swap(i, s); i = s;
                }
                return top;
            }
            private void Swap(int a, int b)
            {
                (int ti, float tp) = (_items[a], _prio[a]);
                _items[a] = _items[b]; _prio[a] = _prio[b]; _items[b] = ti; _prio[b] = tp;
            }
        }
    }
}
