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
        /// <summary>An edge is a one-way "drop" (jump-down ledge) when its vertical
        /// fall is at least this large (m)...</summary>
        private const float DropMinDy = 1.0f;
        /// <summary>...AND it is steeper than this (vertical fall / horizontal run).
        /// Real ramps/stairs stay far below; observed jump-down ledges are ~4-6.</summary>
        private const float DropMinRatio = 1.2f;

        private static readonly string Dir =
            Path.Combine(Directory.GetCurrentDirectory(), "UserData", "SO2RAccess", "traversals");

        // ── Graph state ──────────────────────────────────────────────────────
        private readonly List<Vector3> _nodes = new List<Vector3>();
        // Directed out-neighbours. A steep "drop" edge appears only in the high
        // node's list (downhill), never the low node's — you can fall down a ledge
        // but not climb it. Gentle edges appear in both directions.
        private readonly List<List<int>> _adj = new List<List<int>>();
        private readonly Dictionary<(int, int), List<int>> _hash =
            new Dictionary<(int, int), List<int>>();
        // Steep edges the player was OBSERVED to climb upward (ladders / climb
        // points). Stored normalised (low,high). These keep their uphill direction.
        private readonly HashSet<(int, int)> _climbEdges = new HashSet<(int, int)>();
        private int _lastNode = -1;
        private string _mapId;
        private bool _dirty;

        public bool HasData => _nodes.Count > 0;
        public int NodeCount => _nodes.Count;

        // ── Read-only views (audits / diagnostics) ───────────────────────────

        /// <summary>All breadcrumb positions, by node index.</summary>
        public IReadOnlyList<Vector3> Nodes => _nodes;

        /// <summary>Every directed edge (from, to) the graph will route along.</summary>
        public IEnumerable<(int from, int to)> Edges
        {
            get
            {
                for (int a = 0; a < _adj.Count; a++)
                    foreach (int b in _adj[a])
                        yield return (a, b);
            }
        }

        /// <summary>
        /// Every one-way drop (jump-down ledge) as (high, low): a steep edge that
        /// exists downhill but whose uphill direction was never observed.
        /// </summary>
        public IEnumerable<(int high, int low)> OneWayDrops
        {
            get
            {
                for (int hi = 0; hi < _adj.Count; hi++)
                    foreach (int lo in _adj[hi])
                    {
                        if (!IsSteepDrop(hi, lo)) continue;
                        if (_nodes[hi].y < _nodes[lo].y) continue;   // count each drop once (high side)
                        if (_adj[lo].Contains(hi)) continue;         // climb point, not one-way
                        yield return (hi, lo);
                    }
            }
        }

        /// <summary>Directed out-neighbours of a breadcrumb.</summary>
        public IReadOnlyList<int> OutNeighbours(int node) => _adj[node];

        /// <summary>True when a breadcrumb lies within the radius (XZ) and height limit.</summary>
        public bool HasNodeWithin(Vector3 pos, float radius, float yLimit) =>
            FindNearest(pos, radius, yLimit) >= 0;

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
            _nodes.Clear(); _adj.Clear(); _hash.Clear(); _climbEdges.Clear();
            _lastNode = -1; _dirty = false;
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
                // Proximity links have no direction of travel, so a steep one is
                // treated as downhill-only.
                foreach (int n in NodesWithin(pos, MergeRadius))
                {
                    if (n == current) continue;
                    if (Mathf.Abs(_nodes[n].y - pos.y) > MergeMaxDy) continue;
                    Connect(current, n, observedFrom: -1);
                }
                _dirty = true;
            }

            if (_lastNode >= 0 && _lastNode != current)
            {
                float d = Vector3.Distance(_nodes[_lastNode], _nodes[current]);
                if (d <= TrailMaxStep) // ignore teleports / scene jumps
                {
                    // The trail link carries a real direction of travel
                    // (_lastNode -> current), which lets a steep edge be recognised
                    // as a climb when the player actually moved uphill.
                    Connect(_lastNode, current, observedFrom: _lastNode);
                    _dirty = true;
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

        /// <summary>
        /// Links two breadcrumbs, honouring one-way drops. A gentle edge is
        /// bidirectional. A steep edge (jump-down ledge) is downhill-only by
        /// default; its uphill direction is added only when the player was
        /// OBSERVED to climb it — i.e. <paramref name="observedFrom"/> is the lower
        /// node — marking it a climb point (ladder). Pass observedFrom = -1 for
        /// links with no direction of travel (proximity merges, loaded edges).
        /// </summary>
        private void Connect(int a, int b, int observedFrom)
        {
            if (a == b) return;

            if (!IsSteepDrop(a, b))
            {
                AddDirected(a, b);
                AddDirected(b, a);
                return;
            }

            int hi = _nodes[a].y >= _nodes[b].y ? a : b;
            int lo = (hi == a) ? b : a;
            AddDirected(hi, lo);                 // you can always fall down

            // Uphill only if observed climbing, or already known to be a climb point.
            var pair = NormalizePair(lo, hi);
            if (observedFrom == lo || _climbEdges.Contains(pair))
            {
                AddDirected(lo, hi);
                if (_climbEdges.Add(pair)) _dirty = true;
            }
        }

        private void AddDirected(int from, int to)
        {
            if (!_adj[from].Contains(to)) _adj[from].Add(to);
        }

        /// <summary>True when the edge is a near-vertical drop the player can fall
        /// down but not walk up (large vertical fall over little horizontal run).</summary>
        private bool IsSteepDrop(int a, int b)
        {
            float dy = Mathf.Abs(_nodes[a].y - _nodes[b].y);
            if (dy < DropMinDy) return false;
            float dx = _nodes[a].x - _nodes[b].x, dz = _nodes[a].z - _nodes[b].z;
            float dxz = Mathf.Sqrt(dx * dx + dz * dz);
            if (dxz < 0.0001f) return true; // perfectly vertical
            return dy / dxz >= DropMinRatio;
        }

        private static (int, int) NormalizePair(int a, int b) =>
            a < b ? (a, b) : (b, a);

        // ── Queries ──────────────────────────────────────────────────────────

        /// <summary>
        /// True if <paramref name="b"/> can be reached from <paramref name="a"/> by
        /// following walked edges in their allowed direction. Directed, so a target
        /// reachable only by climbing a one-way drop returns false (honest), while a
        /// target below a drop stays reachable (you can descend).
        /// </summary>
        public bool IsReachable(Vector3 a, Vector3 b)
        {
            int na = SnapToNode(a), nb = SnapToNode(b);
            if (na < 0 || nb < 0) return false;
            return DirectedReachable(na, nb);
        }

        /// <summary>Directed BFS from <paramref name="start"/> following out-edges.</summary>
        private bool DirectedReachable(int start, int goal)
        {
            if (start == goal) return true;
            var seen = new bool[_nodes.Count];
            var q = new Queue<int>();
            seen[start] = true; q.Enqueue(start);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (int nb in _adj[cur])
                {
                    if (seen[nb]) continue;
                    if (nb == goal) return true;
                    seen[nb] = true; q.Enqueue(nb);
                }
            }
            return false;
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

        // ── Diagnostics ──────────────────────────────────────────────────────

        /// <summary>
        /// Summarises detected one-way drops (jump-down ledges): edges that exist
        /// downhill but whose uphill direction was removed. Used by the F11
        /// diagnostic to confirm ledges are caught at the expected locations.
        /// </summary>
        public string DropSummary(int sampleMax = 6)
        {
            int count = 0;
            var sample = new List<string>(sampleMax);
            foreach (var (hi, lo) in OneWayDrops)
            {
                count++;
                if (sample.Count < sampleMax)
                {
                    Vector3 h = _nodes[hi], l = _nodes[lo];
                    sample.Add($"({h.x:F1},{h.y:F1},{h.z:F1})->({l.x:F1},{l.y:F1},{l.z:F1})");
                }
            }
            return $"{count} one-way drops" +
                   (sample.Count > 0 ? "; e.g. " + string.Join(", ", sample) : "");
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
                    Edges = new List<int[]>(),
                    ClimbEdges = new List<int[]>(_climbEdges.Count)
                };
                foreach (var p in _nodes) data.Nodes.Add(new[] { p.x, p.y, p.z });
                // Store connectivity as undirected pairs (each unordered pair once);
                // direction is re-derived geometrically on load. ClimbEdges records
                // the steep edges proven climbable so their uphill survives reload.
                var seen = new HashSet<(int, int)>();
                for (int a = 0; a < _adj.Count; a++)
                    foreach (int b in _adj[a])
                        if (seen.Add(NormalizePair(a, b)))
                            data.Edges.Add(a < b ? new[] { a, b } : new[] { b, a });
                foreach (var (lo, hi) in _climbEdges) data.ClimbEdges.Add(new[] { lo, hi });

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
                // Known climb points first, so Connect restores their uphill edge.
                // Older files have no ClimbEdges → every steep edge is downhill-only.
                if (data.ClimbEdges != null)
                    foreach (var e in data.ClimbEdges)
                        _climbEdges.Add(NormalizePair(e[0], e[1]));
                if (data.Edges != null)
                    foreach (var e in data.Edges) Connect(e[0], e[1], observedFrom: -1);
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
            /// <summary>Steep edges proven climbable (low,high). Optional/back-compat.</summary>
            public List<int[]> ClimbEdges { get; set; }
        }

        // ── Minimal binary min-heap (shared with FloorProbeGrid) ─────────────
        internal sealed class MinHeap
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
