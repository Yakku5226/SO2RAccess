# TraversalGraph.cs (493 lines)

// "Learn by observing" approach: records where the player actually walks and builds
// a directed graph of those breadcrumbs. Every node and edge comes from real physical
// movement → routes are 100% walkable. Static NavMesh/raycasts unreliable in this game.
// Sighted player walks the dungeon once; data saved per-map JSON; auto-walk routes over it.
namespace: SO2RAccess (line 3)
usings (non-System / notable only): Il2CppGame, UnityEngine

## class TraversalGraph (line 23)
Records player movement as breadcrumbs, builds a directed walkability graph per field map, persists to JSON, and provides A* pathfinding and reachability queries over it.

fields/properties (declaration order):
- MinSpacing : private const float = 1.0f (line 26)  — minimum spacing between breadcrumbs (m)
- MergeRadius : private const float = 1.6f (line 28)  — radius within which a new node links to existing nodes
- MergeMaxDy : private const float = 1.2f (line 30)  — max Y difference for merge links
- TrailMaxStep : private const float = 3.0f (line 32)  — max trail-edge length; larger distances treated as teleports/cutscenes
- SnapRadius : private const float = 6.0f (line 34)  — max distance to snap a query point to a breadcrumb
- SnapYWeight : private const float = 2.0f (line 36)  — Y weight in snap scoring (prefer correct floor)
- HashCell : private const float = 2.0f (line 38)  — spatial-hash cell size (m)
- DropMinDy : private const float = 1.0f (line 41)  — min vertical fall for a one-way drop edge
- DropMinRatio : private const float = 1.2f (line 44)  — min vertical/horizontal ratio for a drop (real ramps stay below; jump-down ledges ~4-6)
- Dir : private static readonly string (line 46)  — path to UserData/SO2RAccess/traversals/
- _nodes : readonly List<Vector3> (line 50)
- _adj : readonly List<List<int>> (line 53)  — directed adjacency lists; steep "drop" edges appear only in the downhill node's list unless observed climbed
- _hash : readonly Dictionary<(int,int), List<int>> (line 54)  — spatial hash for O(1) neighbourhood queries
- _climbEdges : readonly HashSet<(int,int)> (line 57)  — steep edges the player was observed to climb upward (normalized low,high pairs); preserves uphill direction on reload
- _lastNode : int (line 60)
- _mapId : string (line 61)
- _dirty : bool (line 62)
- HasData : bool (line 64)  — true when _nodes.Count > 0
- NodeCount : int (line 65)

methods (declaration order):
- void StartMap(string mapId) (line 70)
  - note: If mapId matches current, only resets _lastNode. Otherwise: Save() the previous map, Clear(), set _mapId, Load(mapId), reset _lastNode.
- void Clear() (line 83)
  - note: Clears all graph state and resets _lastNode and _dirty.
- void RecordPosition(Vector3 pos) (line 95)
  - note: Called each frame during movement. Cheap: only adds a node when player has moved past MinSpacing from nearest breadcrumb. Links new node to nearby nodes (MergeRadius/MergeMaxDy). Links sequential trail with Connect(_lastNode, current, observedFrom: _lastNode) — observedFrom enables climb detection.
- void BreakTrail() (line 134)
  - note: Resets _lastNode to -1; call when control is lost (cutscene, battle, menu) to prevent trail from jumping across gaps.
- int AddNode(Vector3 pos) (line 136)
  - note: Appends to _nodes and _adj, registers in _hash, returns new index.
- void Connect(int a, int b, int observedFrom) (line 155)
  - note: Core edge logic. Gentle edges (not steep): bidirectional. Steep drop: always adds downhill (hi→lo); adds uphill (lo→hi) only when observedFrom==lo (player was seen climbing it) or _climbEdges already records it as a climb point. Pass observedFrom=-1 for directionless links (proximity merges, loaded edges).
- void AddDirected(int from, int to) (line 179)
  - note: Adds to if not already present in _adj[from].
- bool IsSteepDrop(int a, int b) (line 186)
  - note: Returns true when vertical fall >= DropMinDy AND dy/dxz >= DropMinRatio (or perfectly vertical).
- static (int,int) NormalizePair(int a, int b) (line 196)
  - note: Returns (min,max) so the pair is canonical for use as a HashSet key.
- bool IsReachable(Vector3 a, Vector3 b) (line 207)
  - note: Snaps both points to graph nodes then runs DirectedReachable BFS. Returns false if either point has no nearby node.
- bool DirectedReachable(int start, int goal) (line 215)
  - note: BFS following directed out-edges (_adj). Returns true if goal is reachable from start.
- bool FindPath(Vector3 from, Vector3 to, out Vector3[] corners) (line 239)
  - note: A* over the breadcrumb graph using MinHeap. Output corners are real walked positions + exact target appended as final corner. Returns false if start/goal cannot be snapped or no path exists.
- int SnapToNode(Vector3 pos) (line 284)
  - note: Finds nearest node within SnapRadius using XZ distance + SnapYWeight * |dy| scoring via NodesWithin. Returns -1 if none found.
- string DropSummary(int sampleMax = 6) (line 304)
  - note: Diagnostic. Counts one-way drop edges (downhill exists, uphill absent, not a climb point). Returns count string with up to sampleMax coordinate examples. Used by F11 diagnostic.
- (int,int) HashKey(Vector3 p) (line 329)
  - note: Returns spatial-hash cell key for position p.
- int FindNearest(Vector3 pos, float radius, float yLimit) (line 333)
  - note: Nearest node within radius (XZ) with |dy| <= yLimit, or -1.
- IEnumerable<int> NodesWithin(Vector3 pos, float radius) (line 347)
  - note: Enumerates node indices from all hash cells overlapping the radius (ceil(radius/HashCell) cell ring).
- void Save() (line 360)
  - note: Skips if !_dirty or no data. Serializes nodes as float[3] arrays, edges as undirected (int[2]) pairs (direction re-derived on load), climbEdges as (lo,hi) pairs. Writes to Dir/<mapId>.json.
- void Load(string mapId) (line 397)
  - note: Tries disk file first (Dir/<mapId>.json), falls back to ReadEmbedded(). Loads climb edges before regular edges so Connect() can restore uphill directions for known climb points. Older files without ClimbEdges → all steep edges are downhill-only.
- static string ReadEmbedded(string mapId) (line 427)
  - note: Reads pre-recorded map JSON from mod DLL embedded resources matching suffix "traversals.<mapId>.json". Returns null if not found.

## sealed class TraversalData (line 450)
JSON serialization DTO for traversal persistence.

fields/properties (declaration order):
- MapId : string (line 452)
- Nodes : List<float[]> (line 453)
- Edges : List<int[]> (line 454)
- ClimbEdges : List<int[]> (line 456)  — steep edges proven climbable (low,high); optional for back-compat with older saves

## sealed class MinHeap (line 460)
Minimal binary min-heap used by A* in FindPath.

fields/properties (declaration order):
- _items : int[] (line 462)
- _prio : float[] (line 462)
- _count : int (line 462)
- Count : int (line 463)

methods (declaration order):
- MinHeap(int cap) (line 464)
- void Push(int item, float prio) (line 465)
  - note: Grows arrays by doubling if at capacity. Sifts up.
- int Pop() (line 471)
  - note: Removes and returns min-priority item. Replaces root with last element, sifts down.
- void Swap(int a, int b) (line 486)
