# NavigationHandler.cs (2051 lines)

Field navigation system — Phase 2: audio navigation list + auto-walk.
Keyboard: NumPad 5 toggle list, 8/2 up/down, 4/6 switch category, 1 auto-walk.
Gamepad: Hold L1 opens list, D-pad navigates, LStick up walks, release L1 closes.
Auto-walk injects synthetic stick input via GetLeftStick() postfix; game pipeline handles physics.
This is a `partial class` — see also NavigationHandler.Patches.cs, .Worldmap.cs, .Build.cs, .AutoWalk.cs.

namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, UnityEngine, UnityEngine.AI

## partial class NavigationHandler (line 43)
Field navigation system — audio nav list + auto-walk. See file-level comment.

fields/properties (declaration order):

### Constants (lines 47–311)
- CAT_NPC : int (line 47)
- CAT_CHEST : int (line 48)
- CAT_EXIT : int (line 49)
- CAT_MARKER : int (line 50)
- CAT_EVENT : int (line 51)
- CAT_SAVE : int (line 52)
- CAT_ENEMY : int (line 53)
- CAT_STAIRS : int (line 54)
- CAT_DOOR : int (line 55)
- CAT_WARP : int (line 56)
- CAT_INTERACTABLE : int (line 57)
- CAT_LOCATION : int (line 58)
- CAT_COUNT : int (line 59)  — total category count = 12
- _categoryNames : string[] (line 61)
- _mapNameOverrides : Dictionary<string, string> (line 69)  — manual overrides (EXPEL, NEDE) checked before game data
- _mapNameCache : Dictionary<string, string> (line 80)  — session cache of resolved map names
- AutoWalkArrivalRadius : float (line 87)  — 1.8 units, NPC conversation range
- InteractableArrivalRadius : float (line 93)  — 1.3 units, tighter for chests/save points
- ArrivalVerticalTolerance : float (line 100)  — 2.0 units max Y gap for same-floor arrival
- AutoWalkTurnSpeed : float (line 103)  — 720 deg/s
- NavMeshSampleRadius : float (line 106)  — 5.0 units max snap distance
- PathRecalcInterval : float (line 109)  — 1.5s between NPC path recalcs
- WaypointArrivalThreshold : float (line 113)  — 0.8 units
- PathRecalcDistanceThreshold : float (line 120)  — 3.0 units NPC drift before recalc
- ArrivalRecentWindow : float (line 126)  — 0.5s window to detect recent speech
- FieldStuckCheckInterval : float (line 129)  — 2.0s between stuck checks
- FieldStuckMinMove : float (line 135)  — 0.5 units minimum progress per interval
- ObstacleAvoidanceTimeout : float (line 141)  — 8.0s max time on detour
- MaxAvoidanceAttempts : int (line 147)  — 3 attempts before giving up
- DetourArrivalRadius : float (line 152)  — 1.5 units
- CameraFollowDeadZone : float (line 158)  — 0.08 cross-product dead zone
- CameraFollowScale : float (line 164)  — 1.5 camera rotation scale
- FloorChangeThreshold : float (line 170)  — 2.0 units Y change = floor transition
- FloorChangeCooldown : float (line 176)  — 1.5s min between floor announcements
- ConfirmedCrossingTimeout : float (line 289)  — 5.0s max confirmed crossing time
- SpeculativeCrossingTimeout : float (line 291)  — 10.0s max speculative crossing time
- ExitZoneAvoidRadius : float (line 303)  — 3.5m exit-zone avoidance distance
- ExitZoneAvoidWeight : float (line 305)  — 1.5 steer-away strength
- ExitZoneWaypointExclusion : float (line 310)  — 4.0m: don't avoid exits near waypoints
- MapExitBarrierMargin : float (line 323)  — 0.5m added to exit collider bounds

### State Fields (lines 182–370)
- _categories : List<NavItem>[] (line 182)
- _isOpen : bool (line 183)
- _currentCategoryIndex : int (line 184)
- _currentItemIndex : int (line 185)
- _isAutoWalking : bool (line 187)
- _autoWalkSpeed : float (line 188)  — world map movement speed via GetMoveSpeed(true)
- _autoWalkTarget : Vector3 (line 189)
- _autoWalkLabel : string (line 190)
- LastAutoWalkTarget : Vector3? (line 193)  — static, public, for diagnostics
- LastAutoWalkLabel : string (line 196)  — static, public, for diagnostics
- _autoWalkTransform : Transform (line 202)  — live transform for moving NPC tracking
- _autoWalkArrived : bool (line 207)  — true once proximity-lock announced
- _autoWalkIsCounter : bool (line 212)  — arrival at last waypoint, not NPC proximity
- _autoWalkEventRef : FieldEventCollision (line 219)
- _autoWalkTriggerBounds : Bounds? (line 225)
- _autoWalkFacePosition : Vector3? (line 230)  — optional face-toward position on arrival (fishing)
- _autoWalkDifferentFloor : bool (line 236)
- _autoWalkCategoryIndex : int (line 242)
- _navPath : NavMeshPath (line 245)  — reusable, allocated once
- _pathCorners : Vector3[] (line 248)
- _pathCornerIndex : int (line 251)
- _pathRecalcTimer : float (line 254)
- _fieldStuckTimer : float (line 257)
- _fieldLastStuckCheckPos : Vector3 (line 260)
- _fieldStuckRecalcAttempted : bool (line 263)
- _isAvoidingObstacle : bool (line 266)
- _avoidanceDetourTarget : Vector3 (line 269)
- _avoidanceStartTime : float (line 272)
- _avoidanceAttempt : int (line 275)
- _routeSegments : List<RouteSegment> (line 279)  — cross-island route segments; null for same-island
- _routeSegmentIndex : int (line 281)
- _routeIsSpeculative : bool (line 283)
- _isCrossingPhase : bool (line 285)
- _crossingTimer : float (line 287)
- _routeFinalTarget : Vector3 (line 293)
- _routeFinalLabel : string (line 295)
- _crossingExitZones : List<Bounds> (line 302)  — cached exit trigger bounds to steer around
- _autoWalkAllowExit : bool (line 316)
- _lastPathBlockedByExit : bool (line 321)
- _gamepadNavActive : static bool (line 329)  — static, read by Harmony prefixes for input suppression
- _lastFieldmapID : FieldmapID (line 332)
- _fieldmapInitialized : bool (line 333)
- _lastPlayerY : float (line 336)
- _floorChangeCooldownTimer : float (line 337)
- _islandNav : IslandNavigator (line 340)
- _traversal : TraversalGraph (line 344)
- _traversalSaveTimer : float (line 346)
- _lastPlayerIsland : int (line 354)
- _lastIslandCrossPos : Vector3 (line 355)
- _islandPollTimer : float (line 356)
- _islandScanPendingMapId : string (line 358)
- _islandScanDelay : float (line 360)

### Properties (lines 363–370)
- IsListOpen : bool (line 363)
- IsAutoWalking : bool (line 366)
- IslandNav : IslandNavigator (line 369)

methods (declaration order):

- NavigationHandler() (line 375)
  - note: constructor allocates _categories array (CAT_COUNT lists) and _navPath

- void ApplyPatches(HarmonyLib.Harmony) (line 396)
  - note: patches GetLeftStick (postfix), GetFieldCameraRightStick (postfix), GetPlayerControlStick (postfix), GameInputManager.IsDown (prefix), IsRepeat (prefix), GetDPad (prefix), UIFieldFishingResultPresenter.Set (postfix); each in its own try-catch

- void ToggleNavList() (line 523)
  - note: cancels auto-walk if active, closes if open, calls ScanAndOpenList() if field is free

- void GamepadOpenNav() (line 548)
  - note: L1 press handler; sets _gamepadNavActive=true after opening

- void GamepadCloseNav() (line 583)
  - note: L1 release; closes silently (no announcement), clears _gamepadNavActive

- void NavDown() (line 597)
- void NavUp() (line 606)
- void NavCategoryNext() (line 617)
- void NavCategoryPrev() (line 629)

- void Update() (line 651)
  - note: per-frame; handles world-map resume, field-map resume, auto-walk cancellation on field-free loss, world-map vs field-map auto-walk dispatch, cross-island segment transitions, arrival detection, obstacle avoidance, stuck detection, NPC path recalc, waypoint following, input injection via _staticAutoWalkStickDir

- void LateUpdate() (line 1268)  — currently empty; kept as hook point

- bool IsAtRealTarget(Vector3, out float, out float) (line 1281)
  - note: single authority for "player is AT the target"; measures XZ+Y to the REAL final target (not a multi-segment waypoint); out horizDist, out vertGap

- void LogNpcArrivalDiagnostics(FieldPlayer, Vector3, float) (line 1304)
  - note: debug-only; dumps nearby FieldObject contactDistance, NPC conversationDistance, party positions, colliders within 3m of target

- void CloseList() (line 1415)  — private
- void AnnounceCurrentItem() (line 1423)  — private
- void AnnounceCategory() (line 1435)  — private
- void ScanAndOpenList() (line 1457)  — private; shared by keyboard and gamepad open paths
- bool IsFieldFree() (line 1550)  — private; delegates to FieldState.IsFieldFree()
- int FirstNonEmptyCategoryFrom(int) (line 1552)  — private; wrapping search forward
- int LastNonEmptyCategoryBefore(int) (line 1562)  — private; wrapping search backward
- static int DistanceUnits(float) (line 1572)  — private; rounds float distance to int

- void CheckDeferredIslandScan() (line 1583)
  - note: waits 1.5s after scene load for NavMesh to be ready, then calls _islandNav.LoadOrScan

- void SaveTraversal() (line 1607)  — public; flushes breadcrumbs to disk

- void LogTraversalDiagnostic(Vector3) (line 1618)
  - note: F11 debug; logs breadcrumb count, drop summary, save point reachability, chest reachability (NavMesh vs traversal)

- void CheckTraversalRecording() (line 1677)  — private; records breadcrumb every frame when field is free; autosaves every 10s

- void CheckIslandCrossing() (line 1712)
  - note: polls player island membership at 4 Hz; records bridge when island changes; called from Update but retired in favour of traversals

- void CheckFieldmapChange() (line 1763)
  - note: detects fieldmap ID change; announces new map name; defers island scan; calls _traversal.StartMap for field maps

- void CheckFloorChange() (line 1845)
  - note: monitors player Y each frame; announces "Went upstairs/downstairs" on >= FloorChangeThreshold change; uses FloorChangeCooldown to debounce

- static string ResolveMapName(FieldmapID) (line 1905)
  - note: priority: _mapNameOverrides → _mapNameCache → ConstFieldParameter+TextManager → last underscore suffix fallback; results cached in _mapNameCache

- static bool IsInteractableNpcType(NpcType) (line 1990)  — CHECK, BED → true
- static bool IsFunctionalNpcType(NpcType) (line 2000)  — INN, SHOP_*, GUILD, FISH_COLLECTOR, FACILITY → true
- static string GetNpcCategory(NpcType) (line 2015)  — maps NpcType to display string
- static List<ConstNpcParameter> TryGetNpcParams(FieldmapID) (line 2033)  — wraps ParameterManager.GetNpcParameter in try-catch
