# WorldmapGridGenerator.cs (1103 lines)

Generates and saves a terrain height + obstacle grid for a world map at 0.5m resolution.
Uses CalcHeight for terrain detection and OverlapSphere for solid obstacle detection.
Col_Obstacle objects with isTrigger=true are passthrough; solid colliders (isTrigger=false)
block the player. Rock faces detected via slope checking. Cells near CharaWalls store
sub-cell position with maximum clearance. File format WMGH (v10 = entrance trigger passability).
namespace: SO2RAccess (line 7)
usings (non-System / notable only): Il2CppGame, UnityEngine

## static class WorldmapGridGenerator (line 21)
Generates and saves terrain height + obstacle grids for world map navigation.

fields/properties (declaration order):
- Magic : string (line 24)  — file format identifier "WMGH"
- CellSize : float (line 27)  — 0.5f; grid cell spacing in world units (meters)
- RaycastStartY : float (line 33)  — 150f; CalcHeight raycast origin Y above all terrain
- RaycastMaxDist : float (line 36)  — 300f; max downward distance for CalcHeight raycast
- TerrainObstacleMask : int (line 42)  — 1 << 22 (layer 22 = Wall); checked at 0.50m radius
- CharaWallMask : int (line 50)  — 1 << 23 (layer 23 = CharacterWall); checked at 0.25m radius to preserve road gaps
- MinPassableClearance : float (line 59)  — 0.50f; minimum clearance for a cell to be passable (player capsule radius)
- SubCellSteps : int (line 69)  — 2; -2..+2 = 5 points per axis for sub-cell CharaWall gap detection
- ObstacleSearchRadius : float (line 75)  — 1.0f; OverlapSphere radius for finding solid obstacles

methods (declaration order):
- void GenerateAndSave() (line 81)
  - note: Entry point for grid generation (call with F9 in debug mode on world map). Runs CalcHeight + OverlapSphere per cell, flood-fills to seal town interiors, clears entrance triggers, saves binary file. Announces progress via ScreenReader.
- void SaveGrid(string, float, float, int, int, ushort[,], Dictionary<long,(float,float)>, Dictionary<long,float>) (line 577)
  - note: Writes binary grid file: 4-byte magic "WMGH", header floats/ints, row-major ushort height data, sparse clearance offset table, sparse clearance value table.
- byte EncodeClearanceOffset(float) (line 631)
  - note: Maps -0.25..+0.25m to 0..250 (byte). Precision ~0.002m per step.
- float DecodeClearanceOffset(byte) (line 639)
- CachedGrid LoadGrid(WorldmapID) (line 648)
  - note: Loads .grid file; supports legacy WMGD format (no offsets), WMGE/WMGF (clearance offsets), WMGG (clearance values). Returns null if file missing or magic mismatch.
- void LogPlayerCollider() (line 758)
  - note: Debug diagnostic: logs MoveCollisionRadius, CapsuleCollider properties, all child colliders, and gap width measurements at known corridors. Announces to ScreenReader.
- void MeasureGapWidth(StringBuilder, string, Vector3) (line 902)
  - note: Private. Casts OverlapSphere (L22|L23 mask, 10m radius), finds nearest solid collider per cardinal direction, logs distances and E-W/N-S gap totals.

## class CachedGrid (line 989)  [nested inside WorldmapGridGenerator]
Holds a loaded cached height grid with its metadata and provides coordinate conversion helpers.

fields/properties (declaration order):
- Height : ushort[,] (line 995)  — per cell: 0=ocean, 1=solid obstacle, 2+=(realHeight+100)*100
- WorldMinX : float (line 997)
- WorldMinZ : float (line 998)
- CellSize : float (line 999)
- GridW : int (line 1000)
- GridH : int (line 1001)
- ClearanceOffsets : Dictionary<long,(float,float)> (line 1008)  — sparse; key=ax*GridH+az; value=(offsetX,offsetZ) in meters from cell center; null for legacy format
- ClearanceValues : Dictionary<long,float> (line 1015)  — sparse; actual clearance distance in meters; null for older format

methods (declaration order):
- void WorldToGrid(float, float, out int, out int) (line 1018)
- Vector3 GridToWorld(int, int) (line 1026)
- Vector3 GridToWorldWithClearance(int, int) (line 1039)
  - note: Like GridToWorld but applies ClearanceOffsets if available, returning sub-cell position with maximum wall clearance.
- bool HasClearanceOffset(int, int) (line 1060)
- float GetClearance(int, int) (line 1072)
  - note: Returns float.MaxValue for cells with no clearance data (wide open).
- bool IsWalkable(int, int) (line 1083)
  - note: Returns true only if cell is in bounds and Height >= 2 (not ocean, not obstacle).
- float GetHeightM(int, int) (line 1093)
  - note: Returns real height in meters: (stored/100.0)-100.0. Returns float.MinValue for ocean or obstacle cells.
