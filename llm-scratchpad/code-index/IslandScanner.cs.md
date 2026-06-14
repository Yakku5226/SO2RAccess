# IslandScanner.cs (638 lines)

Scans the NavMesh on a field map to identify disconnected islands and gaps between them. Returns a MapIslandGraph for use by IslandNavigator. Uses a fixed grid origin at (0,0) for deterministic island IDs. Pipeline: grid sample → BFS flood-fill → stable ID sort → gap finding → ground raycast verification → bridge promotion.
namespace: SO2RAccess (line 7)
usings (non-System / notable only): UnityEngine, UnityEngine.AI

## static class IslandScanner (line 13)

fields/properties (declaration order):
- CellSize : float = 2.0f (line 16)  [grid cell spacing in meters]
- SampleRadius : float = 1.5f (line 19)  [NavMesh.SamplePosition radius per cell]
- SearchExtent : float = 350f (line 22)  [half-width/height of search grid in meters]
- VerticalLayerThreshold : float = 2.0f (line 27)  [min Y diff to treat same-cell samples as separate floors]
- MaxConnectedYDelta : float = 1.5f (line 32)  [max Y diff between adjacent BFS samples to stay same island]
- MinSignificantSamples : int = 3 (line 35)  [minimum sample count for island to be included in gap search]
- MaxGapDistance : float = 15f (line 38)  [max 3D distance for a gap candidate]
- MaxGapYDelta : float = 12f (line 41)  [max Y delta for a gap candidate]
- ProbeYs : float[] = { 50f, 30f, 15f, 5f, 0f, -5f, -15f } (line 44)  [Y heights probed per grid cell to detect stacked floors]
- Dx : int[] = { -1, 1, 0, 0 } (line 49)  [BFS 4-directional neighbor X offsets]
- Dz : int[] = { 0, 0, -1, 1 } (line 50)  [BFS 4-directional neighbor Z offsets]
- RayHeightOffset : float = 5f (line 460)  [raycast start height above expected Y during gap verification]
- RayMaxDist : float = 30f (line 463)  [max downward raycast distance]
- GapVerifySamples : int = 5 (line 466)  [number of lerp sample points along a gap line]
- MaxGroundStepY : float = 5f (line 474)  [max Y jump between adjacent ground samples before rejecting as cliff]

methods (declaration order):
- MapIslandGraph Scan(string mapId) (line 69)  [public static]
  - note: full pipeline — grid sample, BFS flood-fill, stable ID sort (by MinY/MinX/MinZ rounded to 0.1), gap finding, ground raycast verification, bridge promotion. Unverified gaps become BridgeData with Bidirectional=true; failed verification is discarded. Returns null if no NavMesh found.
- int FindIsland(Vector3 pos, List<IslandData> islands) (line 211)  [public static]
  - note: snaps pos to NavMesh, then tries NavMesh.CalculatePath to each island center (PathComplete = same island); falls back to bounding-box proximity with CellSize*3 margin. Returns -1 if no island found.
- void SampleCell(float worldX, float worldZ, int gx, int gz, List<Sample> samples, Dictionary<(int,int),List<int>> cellToIndices) (line 285)  [private static]
  - note: probes each Y in ProbeYs, deduplicates by VerticalLayerThreshold, appends non-duplicate hits to samples and cellToIndices
- int[] FloodFill(List<Sample> samples, Dictionary<(int,int),List<int>> cellToIndices) (line 328)  [private static]
  - note: BFS 4-directional; connects neighbors only when Y delta <= MaxConnectedYDelta; returns island ID array parallel to samples
- List<GapData> FindGaps(List<Sample> samples, int[] islandIds, List<IslandData> islands) (line 380)  [private static]
  - note: O(n^2) over significant island pairs; bounding-box pre-check, then closest-sample-pair brute force; filters by MaxGapDistance and MaxGapYDelta; result sorted by distance ascending
- List<GapData> VerifyGapsWithGround(List<GapData> candidates) (line 482)  [private static]
  - note: for each gap lerps GapVerifySamples+1 points; raycasts downward from expectedY+RayHeightOffset using Physics.RaycastAll; picks hit closest to expectedY (not highest) to avoid upper-floor false positives; rejects if no upward-facing non-trigger hit, or if adjacent ground Ys jump > MaxGroundStepY (cliff detection)
- float BoundsDistance(IslandData a, IslandData b) (line 606)  [private static]
  - note: axis-aligned bounding box distance (returns 0 if overlapping)
- float Round1(float v) (line 613)  [private static — rounds to 1 decimal place for deterministic sort]

## struct Sample (line 54)  [private, internal to IslandScanner]
Internal sample record used during scanning.

fields/properties (declaration order):
- Gx : int (line 56)
- Gz : int (line 56)
- Position : Vector3 (line 57)

## class IslandBuildData (line 616)  [private nested class]
Temporary accumulator for building island bounding box and sample count during Scan.

fields/properties (declaration order):
- OriginalId : int (line 618)
- Count : int (line 619)
- MinX : float (line 620)
- MaxX : float (line 620)
- MinY : float (line 621)
- MaxY : float (line 621)
- MinZ : float (line 622)
- MaxZ : float (line 622)

methods (declaration order):
- IslandBuildData(int id) (line 624)  [constructor]
- void Add(Vector3 pos) (line 626)  [expands bounding box and increments Count]
