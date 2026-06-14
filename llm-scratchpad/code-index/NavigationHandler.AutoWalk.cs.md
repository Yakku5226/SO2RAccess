# NavigationHandler.AutoWalk.cs (1434 lines)

Partial class fragment — auto-walk execution, battle-resume, multi-segment island routing,
NavMesh path utilities, obstacle avoidance, and camera follow helpers.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, UnityEngine, UnityEngine.AI

## partial class NavigationHandler (line 11)

### Fields/properties — Field Battle-Resume State (declaration order):
- _fieldResumePending : bool (line 22)  — true when a field auto-walk was interrupted and may resume
- _fieldResumeBattleSeen : bool (line 25)  — true once a battle was detected during the pending window
- _fieldFreeFailCount : int (line 33)  — consecutive frames IsFieldFree returned false; tolerates brief flicker
- _fieldResumeFreeTimer : float (line 35)  — seconds field has been continuously free while pending
- _fieldResumeTarget : Vector3 (line 38)
- _fieldResumeLabel : string (line 39)
- _fieldResumeCategoryIndex : int (line 40)
- _fieldResumeTransform : Transform (line 41)
- _fieldResumeFacePosition : Vector3? (line 42)
- _fieldResumeIsCounter : bool (line 43)
- _fieldResumeEventRef : FieldEventCollision (line 44)
- _fieldResumeTriggerBounds : Bounds? (line 45)
- _fieldResumeMapId : FieldmapID (line 46)
- FieldResumeDiscardDelay : const float (line 55)  — grace period (0.6s) before discarding a pending non-battle resume

### Static field:
- _completePathProbes : static readonly Vector3[] (line 1123)  — 13 XZ probe offsets used to overcome NavMesh fragmentation

### Methods (declaration order):

- void AutoWalkTo() (line 65)
  - note: Public entry point (NumPad 5). Calculates NavMesh path via CalculateAndStorePath, sets all auto-walk state fields, closes nav list, queries run speed. Aborts with ScreenReader message if path fails or player unavailable.

- void CancelAutoWalk() (line 203)
  - note: Clears all auto-walk, obstacle-avoidance, crossing, and route state. No announcement.

- static bool IsBattleActive() (line 243)
  - note: Checks BattleManager.Instance.battlePlayerList.Count > 0. Used by UpdateFieldResume.

- void SaveFieldResume() (line 264)
  - note: Snapshots all live auto-walk fields into _fieldResume* before CancelAutoWalk clears them. Must be called BEFORE CancelAutoWalk.

- void ClearFieldResume() (line 284)

- void UpdateFieldResume() (line 300)
  - note: Per-frame handler called from Update() while resume is pending and auto-walk is not running. Classifies interruption as battle (resumes) or non-battle (discards after FieldResumeDiscardDelay). Also discards if map changed.

- void ResumeFieldAutoWalk() (line 355)
  - note: Re-routes from current player position to saved resume target; restores all auto-walk state; announces "resuming". Calls ClearFieldResume at end.

- void StartMultiSegmentWalk(NavItem item, Vector3 playerPos, RouteResult route) (line 432)
  - note: Sets up _routeSegments, caches crossing exit zones, calculates first segment path, closes nav list. Announces island-route or speculative-explore message.

- bool PathCrossesMapExit(Vector3[] corners) (line 532)
  - note: Samples densely along NavMesh path corners and tests against every FieldMapjumpCollision collider bounds expanded by MapExitBarrierMargin. Hard barrier to prevent routing the player out of the area.

- HashSet<int> GetExitIslandSet() (line 586)
  - note: Returns set of island IDs containing a FieldMapjumpCollision. Used by route planning to avoid transit through exit islands.

- void CacheCrossingExitZones(Vector3 finalTarget) (line 619)
  - note: Populates _crossingExitZones with bounds of FieldMapjumpCollision colliders, excluding those near route waypoints or final target.

- bool IsNearRouteWaypoint(Vector3 pos, Vector3 finalTarget) (line 663)
  - note: XZ-only distance check against finalTarget and all _routeSegments WalkTarget/ArrivalPoint within ExitZoneWaypointExclusion radius.

- static float FlatSqrDistance(Vector3 a, Vector3 b) (line 680)  — squared XZ distance, ignores Y

- Vector3 AvoidExitZones(Vector3 playerPos, Vector3 desiredDir) (line 694)
  - note: Returns adjusted walk direction that arcs around cached _crossingExitZones using tangential deflection + quadratic push-away at close range.

- bool CheckSegmentTransition(Vector3 playerPos) (line 745)
  - note: Called from Update() during multi-segment route. Handles crossing-phase (stick input toward ArrivalPoint, timeout, island-arrival detection) and path-exhausted transition to crossing phase. Returns true if frame was handled.

- void StartNextSegment(Vector3 playerPos) (line 855)
  - note: Advances to next route segment, calculates NavMesh path; falls through to crossing phase immediately if no path found.

- void StartFinalSegment(Vector3 playerPos) (line 899)
  - note: Called after all crossings complete. Clears route state, switches to normal single-path walk to _routeFinalTarget. Cancels if final path not found.

- void AnnounceArrival(string arrivalText) (line 945)
  - note: Combines arrival message with any ScreenReader message from the last ArrivalRecentWindow seconds to avoid the user missing a tutorial popup.

- static bool IsExitCategory(int categoryIndex) (line 964)
  - note: Returns true for CAT_EXIT, CAT_STAIRS, CAT_DOOR, CAT_WARP, CAT_LOCATION.

- static Vector2 WorldDirToCameraStick(Vector3 worldDir) (line 976)
  - note: Projects world-space direction onto camera forward/right XZ axes; returns normalized Vector2 for left-stick injection.

- void StopAutoWalk() (line 1008)  — clears all input-injection state at arrival; lighter than CancelAutoWalk (no route/segment fields)

- static string GetCompassDirection(Vector3 playerPos, Vector3 targetPos, bool worldRelative = false) (line 1030)
  - note: Camera-relative by default (North = camera forward); pass worldRelative=true for world map (North = Z+). Returns 8-direction string.

- bool SampleNavMeshFloorAware(Vector3 pos, out NavMeshHit hit) (line 1089)
  - note: Tries tight radius (1.0) first to stay on correct floor, then full NavMeshSampleRadius. Logs when sampled Y differs significantly from requested Y.

- bool TryFindCompletePath(Vector3 playerPos, Vector3 targetPos, NavMeshPath result) (line 1143)
  - note: Iterates _completePathProbes offsets around playerPos, samples NavMesh at each, calls CalculatePath, returns true on first PathComplete result. Beats NavMesh fragmentation.

- static Vector3[] CopyCorners(NavMeshPath path) (line 1175)  — copies IL2CPP NavMeshPath.corners into managed array

- bool HasCompleteNavMeshPath(Vector3 playerPos, Vector3 targetPos) (line 1190)
  - note: Thin wrapper around TryFindCompletePath; logs result. Used by IsReachable and CalculateAndStorePath.

- bool IsReachable(Vector3 playerPos, Vector3 targetPos) (line 1209)
  - note: World map delegates to WorldmapIsReachableViaCalcHeight; field tries complete NavMesh path then traversal graph.

- bool CalculateAndStorePath(Vector3 playerPos, Vector3 targetPos, bool allowPartial = false) (line 1232)
  - note: Three-tier path: (1) complete NavMesh (TryFindCompletePath), (2) recorded traversal (_traversal.FindPath), (3) partial NavMesh (only if allowPartial). Applies PathCrossesMapExit hard barrier for NavMesh paths. Populates _pathCorners/_pathCornerIndex.

- static void LogPath(Vector3[] corners) (line 1290)  — debug: logs waypoint count and coordinates

- bool TryStartObstacleAvoidance(Vector3 playerPos) (line 1305)
  - note: Increments _avoidanceAttempt, tries 5 candidate directions x 3 distances (3/5/8m) perpendicular/diagonal to heading; picks first candidate that has a NavMesh path to target. Sets _isAvoidingObstacle and _avoidanceDetourTarget.

- static void UpdateCameraFollow(Vector3 worldMoveDir) (line 1407)
  - note: Sets _staticCameraStickX using cross product of camera-forward and movement direction. Dead zone CameraFollowDeadZone, scale -CameraFollowScale. Static so Harmony postfix can read it.
