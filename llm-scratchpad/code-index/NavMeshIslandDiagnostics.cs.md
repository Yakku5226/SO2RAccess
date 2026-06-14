# NavMeshIslandDiagnostics.cs (518 lines)

Diagnostic tool that scans the NavMesh on field maps to identify disconnected
islands, bounds, Y-ranges, and gaps between them. Triggered by F11 in debug
mode on non-world-map fields. Results logged and saved to a file.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): Il2CppGame, UnityEngine, UnityEngine.AI

## static class NavMeshIslandDiagnostics (line 17)

fields/properties (declaration order):
- CellSize : float = 2.0f (line 20)  — const; grid cell spacing for NavMesh sampling (meters)
- SampleRadius : float = 1.5f (line 23)  — const; radius for NavMesh.SamplePosition per grid cell
- SearchExtent : float = 300f (line 29)  — const; bounds expansion beyond player position
- VerticalLayerThreshold : float = 2.0f (line 37)  — const; Y-difference to consider two samples on separate vertical layers
- MaxConnectedYDelta : float = 1.5f (line 44)  — const; maximum Y-difference between adjacent samples to treat as connected (same island)
- OutputDir : string (line 47)  — static readonly; UserData/SO2RAccess under current directory

methods (declaration order):

- void Run(Vector3 playerPos, FieldmapID mapId) (line 108)
  - note: public entry point. Executes all 9 steps: (1) discover bounds, (2) sample NavMesh at multi-Y probes per cell (catches stacked floors), (3) build spatial index, (4) BFS flood-fill to assign island IDs (4-directional adjacency + MaxConnectedYDelta Y gate), (5) build IslandInfo objects, (6) log island details, (7) find inter-island gaps, (8) log FieldMapjumpCollision triggers, (9) save results to island_diag_{mapId}.txt and announce summary via ScreenReader.

- List\<IslandGap\> FindIslandGaps(List\<NavSample\> allSamples, int[] islandIds, List\<IslandInfo\> islands) (line 401)
  - note: static; filters to islands with 3+ samples; uses bounding-box pre-check (>30m skip); brute-force closest-pair within 30m; sorts by distance.

- float BoundsDistance(IslandInfo a, IslandInfo b) (line 469)
  - note: static; axis-aligned bounding box distance (XZ only); returns 0 if boxes overlap.

- void LogMapjumpTriggers(StringBuilder sb) (line 479)
  - note: static; FindObjectsOfType\<FieldMapjumpCollision\>() and logs dest fieldmapID + position for each trigger.

## private struct NavSample (line 53)

Represents a single NavMesh sample point on the grid.

fields/properties (declaration order):
- GridX : int (line 55)
- GridZ : int (line 56)
- Position : Vector3 (line 57)  — actual NavMesh position (snapped)
- IslandId : int (line 58)  — -1 = unassigned

## private class IslandInfo (line 64)

Holds summary data for one NavMesh island.

fields/properties (declaration order):
- Id : int (line 65)
- Samples : List\<NavSample\> (line 66)
- MinX : float (line 67)
- MaxX : float (line 67)
- MinY : float (line 68)
- MaxY : float (line 68)
- MinZ : float (line 69)
- MaxZ : float (line 69)
- Center : Vector3 (line 83)  — property; average of min/max per axis
- YRange : float (line 88)  — property; MaxY - MinY

methods (declaration order):
- void AddSample(NavSample s) (line 72)

## private struct IslandGap (line 94)

Represents a gap between two islands — the closest pair of edge samples between them.

fields/properties (declaration order):
- IslandA : int (line 96)
- IslandB : int (line 97)
- PointA : Vector3 (line 98)
- PointB : Vector3 (line 99)
- Distance : float (line 100)
- YDelta : float (line 101)
