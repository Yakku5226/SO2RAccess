using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// A DISCOVERY-ONLY map of the floors on a field map, built from downward
    /// physics raycasts on a 1.5 m grid. Every place a ray finds walkable-looking
    /// ground (surface normal pointing mostly up) becomes a node; neighbouring
    /// nodes whose height step is gentle enough to walk are connected; connected
    /// nodes form components; runs of sloped nodes with enough total rise become
    /// ramp candidates.
    ///
    /// This is the same technique whose reachability verdicts passed the 2026-06
    /// Krosse Cave gate (31 612 nodes / 240 ms, every chest found), rebuilt with a
    /// tight step rule. It is NEVER used to move the player: raycasts cannot tell a
    /// wall top from a floor, so its answers are hints to be verified — first by
    /// the breadcrumb audit (<c>NavigationHandler.RunFloorGridAudit</c>), later by
    /// the player's own feet. Movement stays on breadcrumbs, NavMesh and the user.
    /// </summary>
    public sealed class FloorProbeGrid
    {
        // ── Tunables ─────────────────────────────────────────────────────────
        /// <summary>Grid spacing (m). 1.5 m is the size that passed the 2026-06 gate.</summary>
        public const float CellSize = 1.5f;
        /// <summary>
        /// Max walkable height step between neighbours as a ratio of horizontal
        /// distance (|dy| / dxz). 0.67 = 1.0 m per 1.5 m cell, well under the
        /// breadcrumb graph's 1.2 one-way-ledge rule and far under the old 5 m
        /// allowance that connected floors across walls.
        /// </summary>
        public const float MaxStepRatio = 0.67f;
        /// <summary>A ray hit counts as floor when its normal's Y is at least this.</summary>
        public const float MinFloorNormalY = 0.4f;
        /// <summary>Two hits in one cell closer than this in Y are the same floor level.</summary>
        public const float LevelDedupeY = 0.5f;
        /// <summary>A node is "sloped" when its steepest walkable step is at least this ratio.</summary>
        public const float RampMinRatio = 0.15f;
        /// <summary>A sloped run must climb at least this much (m) to be a ramp candidate.</summary>
        public const float RampMinRise = 2.0f;
        /// <summary>Hard cap on the probed area (m), centred on the player.</summary>
        public const float MaxExtent = 400f;
        /// <summary>Route endpoints snap to a node within this horizontal distance (m).</summary>
        public const float RouteSnapXz = 2.2f;
        /// <summary>...and this vertical distance for the player's end (m).</summary>
        public const float RouteSnapYFrom = 1.6f;
        /// <summary>...and this vertical distance for the target's end (chests sit on things).</summary>
        public const float RouteSnapYTarget = 3.0f;
        /// <summary>Route simplification looks at most this many nodes ahead for a straight cut.</summary>
        private const int SimplifyLookahead = 40;
        /// <summary>Spacing of the floor samples that validate a straight cut (m).</summary>
        private const float SimplifySampleStep = 0.75f;
        /// <summary>A straight cut is walkable when every sample finds floor within this of the line's height (m).</summary>
        private const float SimplifyLineY = 1.0f;
        /// <summary>Ramp candidates kept per build (nearest to the build centre).</summary>
        public const int MaxCandidates = 12;
        /// <summary>Unity layer of the player capsule (excluded from probing).</summary>
        private const int PlayerLayer = 6;

        /// <summary>One ramp candidate: a connected run of sloped floor nodes.</summary>
        public struct RampCandidate
        {
            /// <summary>Index into <see cref="AllRuns"/>.</summary>
            public int     RunId;
            public Vector3 Foot;
            public Vector3 Top;
            public float   Rise;
            public int     Component;
            public int     NodeCount;
            public string  FootCollider;
        }

        // ── Graph state ──────────────────────────────────────────────────────
        private readonly List<Vector3>   _pos  = new List<Vector3>();
        private readonly List<List<int>> _adj  = new List<List<int>>();
        private readonly List<int>       _comp = new List<int>();
        private readonly Dictionary<(int, int), List<int>> _cells =
            new Dictionary<(int, int), List<int>>();
        private float _minX, _minZ;

        public bool   IsReady        { get; private set; }
        public string MapId          { get; private set; }
        public int    NodeCount      => _pos.Count;
        public int    ComponentCount { get; private set; }
        public int    RampRunCount   { get; private set; }
        public float  BuildMs        { get; private set; }
        public Bounds ProbedBounds   { get; private set; }
        /// <summary>Nearest <see cref="MaxCandidates"/> ramp runs to the build centre.</summary>
        public IReadOnlyList<RampCandidate> Ramps => _ramps;
        private readonly List<RampCandidate> _ramps = new List<RampCandidate>();
        /// <summary>Every sloped run that rises at least <see cref="RampMinRise"/>, by run id.</summary>
        public IReadOnlyList<RampCandidate> AllRuns => _runs;
        private readonly List<RampCandidate> _runs = new List<RampCandidate>();
        /// <summary>Run id per node, -1 when the node is flat or in a run that rises too little.</summary>
        private int[] _runOf = Array.Empty<int>();

        // ── Build ────────────────────────────────────────────────────────────

        /// <summary>
        /// Probes the whole map around <paramref name="center"/> and builds the
        /// graph. Returns false with a reason when there is nothing to probe.
        /// Takes on the order of a few hundred milliseconds on a large dungeon.
        /// </summary>
        public bool Build(string mapId, Vector3 center, out string failReason)
        {
            failReason = null;
            Clear();
            MapId = mapId;
            var sw = Stopwatch.StartNew();

            if (!TryComputeBounds(center, out Bounds bounds))
            {
                failReason = "no solid colliders found in the scene";
                return false;
            }
            ProbedBounds = bounds;

            ProbeFloors(bounds);
            if (_pos.Count == 0)
            {
                failReason = "no floor hits in the probed area";
                return false;
            }

            ConnectNeighbours();
            ComponentCount = LabelComponents();
            ExtractRamps(center);

            sw.Stop();
            BuildMs = (float)sw.Elapsed.TotalMilliseconds;
            IsReady = true;
            return true;
        }

        private void Clear()
        {
            _pos.Clear(); _adj.Clear(); _comp.Clear(); _cells.Clear(); _ramps.Clear(); _runs.Clear();
            _runOf = Array.Empty<int>();
            IsReady = false; ComponentCount = 0; RampRunCount = 0; BuildMs = 0f;
        }

        /// <summary>
        /// Union of every solid (non-trigger, non-character) collider's bounds,
        /// clipped to <see cref="MaxExtent"/> around the centre.
        /// </summary>
        private static bool TryComputeBounds(Vector3 center, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var cols = UnityEngine.Object.FindObjectsOfType<Collider>();
            if (cols == null) return false;

            foreach (var col in cols)
            {
                if (col == null || !IsSolidFloorCollider(col)) continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            if (!any) return false;

            float half = MaxExtent * 0.5f;
            Vector3 min = bounds.min, max = bounds.max;
            min.x = Mathf.Max(min.x, center.x - half);
            min.z = Mathf.Max(min.z, center.z - half);
            max.x = Mathf.Min(max.x, center.x + half);
            max.z = Mathf.Min(max.z, center.z + half);
            bounds.SetMinMax(min, max);
            return true;
        }

        /// <summary>
        /// Solid level geometry only: no triggers (event boxes), no character
        /// bodies (capsules / character controllers — NPCs and enemies would
        /// otherwise leave floating "floors" on their heads), not the player.
        /// </summary>
        private static bool IsSolidFloorCollider(Collider col)
        {
            if (col.isTrigger) return false;
            if (col.gameObject.layer == PlayerLayer) return false;
            if (col.TryCast<CapsuleCollider>() != null) return false;
            if (col.TryCast<CharacterController>() != null) return false;
            if (col.TryCast<SphereCollider>() != null) return false;
            return true;
        }

        /// <summary>One downward ray per grid cell; every floor-like hit is a node.</summary>
        private void ProbeFloors(Bounds bounds)
        {
            _minX = bounds.min.x;
            _minZ = bounds.min.z;
            int w = Mathf.CeilToInt(bounds.size.x / CellSize) + 1;
            int h = Mathf.CeilToInt(bounds.size.z / CellSize) + 1;
            float topY   = bounds.max.y + 2f;
            float rayLen = (bounds.max.y - bounds.min.y) + 6f;
            int mask = ~(1 << PlayerLayer);

            var levels = new List<float>(4);
            for (int ix = 0; ix < w; ix++)
            for (int iz = 0; iz < h; iz++)
            {
                var origin = new Vector3(_minX + ix * CellSize, topY, _minZ + iz * CellSize);
                var hits = Physics.RaycastAll(origin, Vector3.down, rayLen, mask,
                    QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) continue;

                levels.Clear();
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (hit.normal.y < MinFloorNormalY) continue;
                    var col = hit.collider;
                    if (col == null || !IsSolidFloorCollider(col)) continue;
                    levels.Add(hit.point.y);
                }
                if (levels.Count == 0) continue;

                // Highest first; merge hits that are really the same surface.
                levels.Sort((a, b) => b.CompareTo(a));
                float last = float.PositiveInfinity;
                foreach (float y in levels)
                {
                    if (last - y < LevelDedupeY) continue;
                    AddNode(ix, iz, new Vector3(origin.x, y, origin.z));
                    last = y;
                }
            }
        }

        private int AddNode(int ix, int iz, Vector3 p)
        {
            int idx = _pos.Count;
            _pos.Add(p);
            _adj.Add(new List<int>(8));
            _comp.Add(-1);
            if (!_cells.TryGetValue((ix, iz), out var list))
            {
                list = new List<int>(2);
                _cells[(ix, iz)] = list;
            }
            list.Add(idx);
            return idx;
        }

        /// <summary>
        /// Links each node to nodes in its 8 neighbouring cells whose height step
        /// is walkable (<see cref="MaxStepRatio"/>). Links are symmetric.
        /// </summary>
        private void ConnectNeighbours()
        {
            // Half the neighbour set — each unordered pair is visited once.
            (int dx, int dz)[] half = { (1, 0), (0, 1), (1, 1), (1, -1) };
            foreach (var kv in _cells)
            {
                var (ix, iz) = kv.Key;
                foreach (var (dx, dz) in half)
                {
                    if (!_cells.TryGetValue((ix + dx, iz + dz), out var other)) continue;
                    float dxz = CellSize * ((dx != 0 && dz != 0) ? 1.41421f : 1f);
                    float maxDy = MaxStepRatio * dxz;
                    foreach (int a in kv.Value)
                    foreach (int b in other)
                    {
                        if (Mathf.Abs(_pos[a].y - _pos[b].y) > maxDy) continue;
                        _adj[a].Add(b);
                        _adj[b].Add(a);
                    }
                }
            }
        }

        /// <summary>Flood-fills connected components; returns how many.</summary>
        private int LabelComponents()
        {
            int next = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < _pos.Count; s++)
            {
                if (_comp[s] >= 0) continue;
                _comp[s] = next;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    foreach (int nb in _adj[cur])
                    {
                        if (_comp[nb] >= 0) continue;
                        _comp[nb] = next;
                        stack.Push(nb);
                    }
                }
                next++;
            }
            return next;
        }

        /// <summary>
        /// Groups sloped nodes into connected runs and keeps the runs that climb
        /// at least <see cref="RampMinRise"/>. Nearest <see cref="MaxCandidates"/>
        /// to the centre are kept; <see cref="RampRunCount"/> records the total.
        /// </summary>
        private void ExtractRamps(Vector3 center)
        {
            int n = _pos.Count;
            var sloped = new bool[n];
            for (int i = 0; i < n; i++)
                sloped[i] = SteepestStepRatio(i) >= RampMinRatio;

            var visited = new bool[n];
            var stack = new Stack<int>();
            var members = new List<int>(64);
            _runOf = new int[n];
            for (int i = 0; i < n; i++) _runOf[i] = -1;

            for (int s = 0; s < n; s++)
            {
                if (!sloped[s] || visited[s]) continue;
                visited[s] = true;
                stack.Push(s);
                members.Clear();
                int lo = s, hi = s;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    members.Add(cur);
                    if (_pos[cur].y < _pos[lo].y) lo = cur;
                    if (_pos[cur].y > _pos[hi].y) hi = cur;
                    foreach (int nb in _adj[cur])
                    {
                        if (!sloped[nb] || visited[nb]) continue;
                        visited[nb] = true;
                        stack.Push(nb);
                    }
                }
                float rise = _pos[hi].y - _pos[lo].y;
                if (rise < RampMinRise) continue;

                int runId = _runs.Count;
                foreach (int m in members) _runOf[m] = runId;
                _runs.Add(new RampCandidate
                {
                    RunId = runId, Foot = _pos[lo], Top = _pos[hi], Rise = rise,
                    Component = _comp[lo], NodeCount = members.Count,
                });
            }

            RampRunCount = _runs.Count;
            var nearest = new List<RampCandidate>(_runs);
            nearest.Sort((a, b) => Vector3.Distance(center, a.Foot)
                .CompareTo(Vector3.Distance(center, b.Foot)));
            for (int i = 0; i < nearest.Count && i < MaxCandidates; i++)
            {
                var c = nearest[i];
                c.FootCollider = ColliderNameUnder(c.Foot);
                _ramps.Add(c);
            }
        }

        /// <summary>Run id of a node (see <see cref="AllRuns"/>), or -1.</summary>
        public int RunOf(int node) => _runOf.Length > node ? _runOf[node] : -1;

        /// <summary>Steepest walkable step out of a node as |dy| / horizontal distance.</summary>
        public float SteepestStepRatio(int node)
        {
            float best = 0f;
            Vector3 p = _pos[node];
            foreach (int nb in _adj[node])
            {
                Vector3 q = _pos[nb];
                float dx = q.x - p.x, dz = q.z - p.z;
                float dxz = Mathf.Sqrt(dx * dx + dz * dz);
                if (dxz < 0.01f) continue;
                float r = Mathf.Abs(q.y - p.y) / dxz;
                if (r > best) best = r;
            }
            return best;
        }

        /// <summary>Name of the solid collider directly under a point (for the log).</summary>
        private static string ColliderNameUnder(Vector3 p)
        {
            try
            {
                var hits = Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 1.5f,
                    ~(1 << PlayerLayer), QueryTriggerInteraction.Ignore);
                if (hits == null) return "?";
                for (int i = 0; i < hits.Length; i++)
                {
                    var col = hits[i].collider;
                    if (col != null && IsSolidFloorCollider(col)) return col.name;
                }
            }
            catch (Exception ex)
            {
                return "error: " + ex.Message;
            }
            return "?";
        }

        // ── Queries ──────────────────────────────────────────────────────────

        /// <summary>Position of a node.</summary>
        public Vector3 NodePosition(int node) => _pos[node];

        /// <summary>Component id of a node.</summary>
        public int ComponentOf(int node) => _comp[node];

        /// <summary>
        /// Nearest node to <paramref name="p"/> within the given horizontal and
        /// vertical tolerances (searches the containing cell and its neighbours),
        /// or -1. Y is weighted double so the right floor wins over a nearer XZ.
        /// </summary>
        public int FindNode(Vector3 p, float xzTolerance, float yTolerance)
        {
            int cx = Mathf.RoundToInt((p.x - _minX) / CellSize);
            int cz = Mathf.RoundToInt((p.z - _minZ) / CellSize);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!_cells.TryGetValue((cx + dx, cz + dz), out var list)) continue;
                foreach (int i in list)
                {
                    Vector3 q = _pos[i];
                    float ddx = q.x - p.x, ddz = q.z - p.z;
                    float dxz = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                    float dy = Mathf.Abs(q.y - p.y);
                    if (dxz > xzTolerance || dy > yTolerance) continue;
                    float score = dxz + 2f * dy;
                    if (score < bestScore) { bestScore = score; best = i; }
                }
            }
            return best;
        }

        /// <summary>
        /// A* route between two world positions over the floor grid, simplified
        /// to straight stretches. <paramref name="corners"/>[0] is the node under
        /// the start (the player's own position, as NavMesh corners do), the last
        /// entry is the exact target. Returns false when either end has no floor
        /// node nearby or they lie in different components. DISCOVERY ONLY: the
        /// result is spoken or described, never used to drive the character.
        /// </summary>
        public bool TryRoute(Vector3 from, Vector3 to, out Vector3[] corners, out float length)
        {
            corners = null;
            length = 0f;
            int start = FindNode(from, RouteSnapXz, RouteSnapYFrom);
            int goal  = FindNode(to, RouteSnapXz, RouteSnapYTarget);
            if (start < 0 || goal < 0) return false;
            if (_comp[start] != _comp[goal]) return false;

            var path = AStar(start, goal, out length);
            if (path == null) return false;

            var simple = SimplifyRoute(path);
            corners = new Vector3[simple.Count + 1];
            for (int i = 0; i < simple.Count; i++) corners[i] = _pos[simple[i]];
            corners[simple.Count] = to;
            return true;
        }

        /// <summary>A* over grid nodes (binary heap, Euclidean heuristic). Null when unreachable.</summary>
        private List<int> AStar(int start, int goal, out float length)
        {
            length = 0f;
            int n = _pos.Count;
            var g = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; came[i] = -1; }

            var heap = new TraversalGraph.MinHeap(256);
            g[start] = 0f;
            heap.Push(start, Vector3.Distance(_pos[start], _pos[goal]));
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
                    float t = g[cur] + Vector3.Distance(_pos[cur], _pos[nb]);
                    if (t < g[nb])
                    {
                        g[nb] = t; came[nb] = cur;
                        heap.Push(nb, t + Vector3.Distance(_pos[nb], _pos[goal]));
                    }
                }
            }
            if (!found) return null;

            length = g[goal];
            var path = new List<int>(64);
            for (int node = goal; node != -1; node = came[node]) path.Add(node);
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Greedy string-pulling: from each kept node, jump to the farthest node
        /// (within the lookahead) that a straight line over real floor can reach.
        /// Removes the 45° zig-zag of an 8-direction grid path so spoken legs are
        /// long straight stretches instead of a new word every cell.
        /// </summary>
        private List<int> SimplifyRoute(List<int> path)
        {
            var result = new List<int> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int j = Mathf.Min(path.Count - 1, i + SimplifyLookahead);
                while (j > i + 1 && !LineWalkable(path[i], path[j])) j--;
                result.Add(path[j]);
                i = j;
            }
            return result;
        }

        /// <summary>
        /// True when every sample along the straight line between two nodes has
        /// grid floor of the same component within <see cref="SimplifyLineY"/> of
        /// the line's own height — no gap, no cliff, no wall top in between.
        /// </summary>
        private bool LineWalkable(int a, int b)
        {
            Vector3 p = _pos[a], q = _pos[b];
            float dist = Vector3.Distance(p, q);
            int steps = Mathf.CeilToInt(dist / SimplifySampleStep);
            for (int k = 1; k < steps; k++)
            {
                Vector3 s = Vector3.Lerp(p, q, (float)k / steps);
                int node = FindNode(s, CellSize * 0.75f, SimplifyLineY);
                if (node < 0 || _comp[node] != _comp[a]) return false;
            }
            return true;
        }

        /// <summary>
        /// Shortest walkable route between two nodes if it is no longer than
        /// <paramref name="maxLength"/> (Dijkstra, abandoned past the cap). Used
        /// by the audit to ask "does the grid agree this short hop is walkable?"
        /// </summary>
        public bool TryLocalRoute(int from, int to, float maxLength, out float length)
        {
            length = 0f;
            if (from == to) return true;
            if (_comp[from] != _comp[to]) return false;

            var dist = new Dictionary<int, float> { [from] = 0f };
            var heap = new TraversalGraph.MinHeap(64);
            heap.Push(from, 0f);
            while (heap.Count > 0)
            {
                int cur = heap.Pop();
                float d = dist[cur];
                if (d > maxLength) return false;
                if (cur == to) { length = d; return true; }
                foreach (int nb in _adj[cur])
                {
                    float nd = d + Vector3.Distance(_pos[cur], _pos[nb]);
                    if (nd > maxLength) continue;
                    if (dist.TryGetValue(nb, out float old) && old <= nd) continue;
                    dist[nb] = nd;
                    heap.Push(nb, nd);
                }
            }
            return false;
        }
    }
}
