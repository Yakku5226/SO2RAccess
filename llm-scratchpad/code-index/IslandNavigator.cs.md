# IslandNavigator.cs (745 lines)

NavMesh island graph system: data structures for islands/bridges/gaps, disk persistence, and BFS-based route planning between NavMesh islands. Part of the obsolete island-navigation approach (superseded by TraversalGraph).
namespace: SO2RAccess (line 8)
usings (non-System / notable only): UnityEngine, System.Text.Json, System.Text.Json.Serialization

## class IslandData (line 13)
One NavMesh island on a map.

fields/properties (declaration order):
- Id : int (line 15)
- CenterX : float (line 16)
- CenterY : float (line 17)
- CenterZ : float (line 18)
- MinX : float (line 19)
- MaxX : float (line 20)
- MinY : float (line 21)
- MaxY : float (line 22)
- MinZ : float (line 23)
- MaxZ : float (line 24)
- SampleCount : int (line 25)

## class BridgeData (line 29)
A confirmed walkable connection between two islands.

fields/properties (declaration order):
- IslandA : int (line 31)
- IslandB : int (line 32)
- CrossPointAX : float (line 33)
- CrossPointAY : float (line 34)
- CrossPointAZ : float (line 35)
- CrossPointBX : float (line 36)
- CrossPointBY : float (line 37)
- CrossPointBZ : float (line 38)
- Bidirectional : bool (line 39)

## class GapData (line 43)
A potential (unconfirmed) connection between two islands.

fields/properties (declaration order):
- IslandA : int (line 45)
- IslandB : int (line 46)
- PointAX : float (line 47)
- PointAY : float (line 48)
- PointAZ : float (line 49)
- PointBX : float (line 50)
- PointBY : float (line 51)
- PointBZ : float (line 52)
- Distance : float (line 53)
- YDelta : float (line 54)
- Blocked : bool (line 55)

## class MapIslandGraph (line 59)
Per-map container saved to disk as JSON.

fields/properties (declaration order):
- MapId : string (line 61)
- Version : int (line 62)
- ScanTimestamp : long (line 63)
- Islands : List<IslandData> (line 64)
- Bridges : List<BridgeData> (line 65)
- Gaps : List<GapData> (line 66)

## struct RouteSegment (line 72)
One segment in a multi-island route.

fields/properties (declaration order):
- FromIsland : int (line 75)
- ToIsland : int (line 79)
- WalkTarget : Vector3 (line 82)  — point to walk toward on the current island (bridge/gap edge)
- ArrivalPoint : Vector3 (line 85)  — point on the destination island after crossing
- IsConfirmed : bool (line 88)  — true if this crossing uses a confirmed bridge

## class RouteResult (line 92)
Result of route planning.

fields/properties (declaration order):
- Segments : List<RouteSegment> (line 95)
- HasSpeculativeSegments : bool (line 98)
- SpeculativeCount : int (line 101)

## class IslandNavigator (line 109)
Manages the island graph for the current map: loading, saving, bridge recording, and route planning via BFS.

fields/properties (declaration order):
- IslandsDir : static readonly string (line 112)  — path: UserData/SO2RAccess/islands
- JsonOpts : static readonly JsonSerializerOptions (line 116)
- _graph : MapIslandGraph (line 119)
- Graph : MapIslandGraph (line 122)  — the loaded/scanned island graph, or null
- HasGraph : bool (line 125)  — true if a graph is loaded for the current map

methods (declaration order):
- void LoadOrScan(string mapId) (line 134)
  - note: tries disk cache first; always re-scans via IslandScanner.Scan for fresh data; merges saved bridges from cache into fresh scan; then saves and logs. Also calls LogFieldStairsDiagnostic.
- void SaveToDisk() (line 171)
- MapIslandGraph LoadFromDisk(string path) (line 190)  [private static]
- string GetFilePath(string mapId) (line 206)  [private static]
- void RecordBridge(int fromIsland, int toIsland, Vector3 crossPointA, Vector3 crossPointB) (line 218)
  - note: deduplicates by normalized pair (smaller ID = IslandA); updates Bidirectional flag if reverse direction observed; saves to disk immediately.
- void MarkGapBlocked(int islandA, int islandB) (line 284)
  - note: sets Blocked=true on the matching gap entry and saves immediately.
- int GetIsland(Vector3 pos) (line 312)
  - note: delegates to IslandScanner.FindIsland; returns -1 if no island found.
- RouteResult PlanRoute(int fromIsland, int toIsland, HashSet<int> avoidTransit = null) (line 329)
  - note: two-pass BFS — first avoids transit islands with map exits; then retries allowing them. Tier 1: bridges only; Tier 2: bridges + non-blocked gaps. Returns null if unreachable.
- List<(int island, bool confirmed)> BfsIslandPath(int from, int to, bool useBridges, bool useGaps, HashSet<int> avoidTransit = null) (line 371)  [private]
  - note: builds adjacency list from graph, runs BFS, reconstructs path as (islandId, edgeIsConfirmed) list. Returns null if target unreachable.
- RouteResult BuildRoute(List<(int island, bool confirmed)> path, int fromIsland) (line 460)  [private]
  - note: converts BFS path to RouteSegment list by resolving bridge/gap world positions for walk targets and arrival points.
- (int, int) NormalizePair(int idA, int idB) (line 519)  [private static]
- bool HasBridge(int idA, int idB) (line 524)  [private]
- BridgeData FindBridge(int islandFrom, int islandTo) (line 534)  [private]
- void GetBridgePoints(BridgeData bridge, int currentIsland, out Vector3 walkTarget, out Vector3 arrivalPoint) (line 548)  [private]
  - note: orients WalkTarget to be on currentIsland's side of the bridge.
- void GetGapPoints(int currentIsland, int targetIsland, out Vector3 walkTarget, out Vector3 arrivalPoint) (line 571)  [private]
- void MergeBridges(MapIslandGraph target, MapIslandGraph cached) (line 607)  [private static]
  - note: copies bridges from cached whose island IDs still exist in fresh scan; skips duplicates; also merges Blocked state for gaps.
- void LogFieldStairsDiagnostic() (line 664)  [private]
  - note: logs all FieldStairs objects on current map and their island assignments. Cross-references with island summary. Diagnostic only.
