# NavigationHandler.Worldmap.cs (1288 lines)

Partial class fragment of NavigationHandler — world map auto-walk logic, pathfinding, stuck recovery, and location entry.
namespace: SO2RAccess (line 6)
usings: Il2CppGame, UnityEngine

## partial class NavigationHandler (line 8)

fields/properties (declaration order):
- WorldmapStuckCheckInterval : float (line 13)  [— const; seconds between stuck checks]
- WorldmapStuckMinMove : float (line 21)  [— const; minimum distance moved per interval to not be considered stuck]
- WorldmapCalcHeightSamples : int (line 27)  [— const; number of CalcHeight ocean-barrier samples]
- WorldmapLocationArrivalRadius : float (line 35)  [— const; arrival radius for location targets (10m, covers obstacle ring)]
- WorldmapChestMaxDistance : float (line 38)  [— const; max distance to show chests on world map]
- WorldmapEnemyMaxDistance : float (line 41)  [— const; max distance to show enemies on world map]
- _isWorldmap : bool (line 52)  [— true when current field is a world map; persists during auto-walk]
- _wmDirectMoveActive : bool (line 59)  [— static; true when using OnMove() instead of stick injection]
- _wmPathFinder : AIPathFinder<FieldCharacter> (line 65)  [— cached AI pathfinder for ocean reachability checks]
- _wmStuckTimer : float (line 68)
- _wmLastStuckCheckPos : Vector3 (line 71)
- _wmDiagTimer : float (line 74)  [— diagnostic logging timer; logs once per second]
- _wmPathWaypoints : Vector3[] (line 77)  [— waypoints from CalcHeight-based A* pathfinder]
- _wmPathIndex : int (line 80)  [— current index into _wmPathWaypoints]
- _wmRecalcCount : int (line 83)  [— number of path recalculations attempted for current auto-walk]
- _wmBlockedPositions : List<Vector3> (line 90)  [— positions where player got stuck; passed to pathfinder on recalc to avoid]
- WmMaxRecalcAttempts : int (line 93)  [— const; max recalc attempts before giving up]
- _wmOriginalTarget : Vector3 (line 99)  [— original auto-walk target before safe-approach substitution]
- _wmResumeActive : bool (line 104)  [— true when auto-walk was interrupted by battle and should resume]
- _wmResumeTarget : Vector3 (line 107)
- _wmResumeLabel : string (line 110)
- _wmResumeCategoryIndex : int (line 113)
- _wmResumeTransform : Transform (line 116)
- WmWaypointArrivalThreshold : float (line 121)  [— const; normal waypoint arrival distance]
- WmGapWaypointArrivalThreshold : float (line 128)  [— const; tighter threshold for narrow gap areas]
- WmGapDetectionDistance : float (line 134)  [— const; if next waypoint is closer than this, use tight threshold]
- WmSkipAheadLookahead : int (line 141)  [— const; how many waypoints ahead to check for skip-ahead recovery]
- WmSkipAheadMaxDist : float (line 146)  [— const; max distance to a future waypoint for skip-ahead to trigger]
- _wmFieldFreeFailCount : int (line 152)  [— consecutive frames IsFieldFree returned false; tolerates brief transitions]
- LidarRayCount : int (line 457)  [— const; number of LIDAR rays cast around player]
- LidarRange : float (line 460)  [— const; max LIDAR sensing range in meters]
- LidarActivationRange : float (line 466)  [— const; distance threshold to activate LIDAR]
- LidarWaypointBias : float (line 473)  [— const; 0=pure gap direction, 1=pure waypoint direction]
- WmLidarLayerMask : int (line 477)  [— static readonly; layers 22 (obstacles) + 23 (CharaWalls)]
- LidarCommitTime : float (line 484)  [— const; seconds to commit to a LIDAR direction before re-evaluating]
- _lidarCommittedDir : Vector3 (line 487)
- _lidarSmoothedDir : Vector3 (line 490)  [— smoothed LIDAR direction via exponential moving average]
- WmPreValidateCount : int (line 737)  [— const; waypoints to pre-validate against L22 before walking starts]
- WmPreValidateMaxRounds : int (line 743)  [— const; max pre-validation rounds before accepting best path]
- WmPreValidateRadius : float (line 750)  [— const; OverlapSphere radius for waypoint pre-validation (0.55m)]
- WmObstacleLayerMask : int (line 755)  [— static readonly; layer 22 (Col_Obstacle) only]

methods (declaration order):
- void UpdateWorldmapAutoWalk(FieldPlayer player, Vector3 playerPos) (line 165)
  - note: Per-frame auto-walk update; follows CalcHeight A* waypoints via stick injection; handles arrival by category (CAT_LOCATION vs others), stuck detection with skip-ahead and recalculation, and falls back to straight-line if no waypoints.
- void ApplyWorldmapMovement(FieldPlayer player, Vector3 moveDir, Vector3 playerPos) (line 498)
  - note: Sets _staticAutoWalkStickDir via WorldDirToCameraStick. LIDAR logic disabled; this is the active path.
- void ApplyWorldmapMovement_Lidar(FieldPlayer player, Vector3 moveDir, Vector3 playerPos) (line 506)
  - note: Disabled LIDAR variant preserved for potential future use. Uses OverlapSphere + raycast cone-constrained gap-finding to steer around L22/L23 obstacles.
- AIPathFinder<FieldCharacter> GetWorldmapPathFinder() (line 670)
  - note: Walks player → FieldAIController → aiParameter → aiPathFinder; caches result.
- bool WorldmapIsReachableViaCalcHeight(Vector3 playerPos, Vector3 targetPos) (line 702)
  - note: Samples CalcHeight at N evenly spaced points along line; any success=false means ocean barrier. Returns true on exception (fallback).
- Vector3 ComputeSafeApproachPoint(Vector3 targetPos) (line 763)
  - note: Finds nearest FieldMapjumpCollision, then picks a point 20m outward with ground and no L22 obstacles. Falls back through 8 compass directions, then trigger position.
- Vector3 ComputeSafeExitPoint(Vector3 playerPos) (line 896)
  - note: Finds nearest ground-level FieldMapjumpCollision within 30m, then picks a point 25m outward (away from town) with no L22 or L23 obstacles.
- bool WorldmapCalculateAndStorePath(Vector3 playerPos, Vector3 targetPos, bool keepBlockedPositions = false) (line 1004)
  - note: Computes safe-approach/exit points, runs multi-round pre-validation loop (up to WmPreValidateMaxRounds) to mark blocked waypoints, concatenates exit+main path, stores in _wmPathWaypoints. Falls back to single-waypoint straight line.
- void ClearWorldmapCache() (line 1212)
- bool TryEnterWorldmapLocation() (line 1229)
  - note: Finds nearest FieldMapjumpCollision to _wmOriginalTarget and calls ChangeFieldmap() on it. Retained as fallback; normal flow enters locations via stick injection into trigger colliders.
