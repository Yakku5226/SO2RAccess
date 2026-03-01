# Code Index: NavigationHandler.cs

## Top-Level Comments (lines 11-41)

Class-level XML doc block describing the full navigation system:
- Keyboard controls: NumPad 5 open/close, NumPad 8/2 up/down, NumPad 4/6 category switch, NumPad 1 auto-walk
- Gamepad controls: Hold L1 open, D-pad Up/Down category, D-pad Left/Right items, LStick Up auto-walk, release L1 close
- Auto-walk behavior: closes list, announces "Walking to", moves player via direct transform, announces "Arrived"
- Item sorting: by distance (closest first) within each category
- NPC notes: party members filtered (<2 units), names parsed from ConstNpcParameter code names
- Chest notes: numbered by distance (Unopened chest 1, 2, ...)
- Exit notes: map names resolved from ConstFieldParameter via TextManager at runtime

---

## Struct: NavItem (line 46)
Private struct — one entry in a navigation category list.

### Fields
- `public string Label` (line 48)
- `public float Distance` (line 49)
- `public Vector3 Position` (line 50)
- `public Transform LiveTransform` (line 56) — live transform for moving objects (NPCs, chests, markers); null for exits
- `public bool IsCounterNpc` (line 62) — true for shop/inn/guild NPCs behind counters; skips NavMesh reachability filter

---

## Class: NavigationHandler (line 42)

### Constants

- `private const int CAT_NPC = 0` (line 65)
- `private const int CAT_CHEST = 1` (line 66)
- `private const int CAT_EXIT = 2` (line 67)
- `private const int CAT_MARKER = 3` (line 68)
- `private const int CAT_EVENT = 4` (line 69)
- `private const int CAT_SAVE = 5` (line 70)
- `private const int CAT_ENEMY = 6` (line 71)
- `private const int CAT_COUNT = 7` (line 72)
- `private const float AutoWalkArrivalRadius = 1.8f` (line 99) — horizontal distance in world units to consider target reached
- `private const float AutoWalkTurnSpeed = 720f` (line 102) — degrees per second rotation during auto-walk
- `private const float NavMeshSampleRadius = 5f` (line 105) — max distance to snap a position onto the NavMesh surface
- `private const float PathRecalcInterval = 1.5f` (line 108) — seconds between path recalculations when following moving NPCs
- `private const float WaypointArrivalThreshold = 0.3f` (line 111) — distance to advance to next path waypoint
- `private const float PathRecalcDistanceThreshold = 3f` (line 117) — how far an NPC must move before triggering a path recalc
- `private const float ArrivalRecentWindow = 0.5f` (line 1504) — seconds back to check for a recently spoken message on arrival

### Static Fields

- `private static readonly string[] _categoryNames` (line 74) — display names for the 7 categories
- `private static readonly Dictionary<string, string> MapNameOverrides` (line 81) — manual map name overrides checked before game data (EXPEL → "Overworld", NEDE → "Nede")
- `private static readonly Dictionary<string, string> _mapNameCache` (line 92) — session-scoped cache of resolved map names keyed by FieldmapID string
- `private static bool _staticIsApproaching` (line 165) — static mirror of _isAutoWalking for the Harmony prefix; true only during approach phase (not proximity-lock)
- `private static bool _gamepadNavActive` (line 171) — true while L1 gamepad nav overlay is active; read by Harmony prefixes to suppress game input

### Instance Fields

- `private readonly List<NavItem>[] _categories` (line 123) — array of 7 lists, one per category
- `private bool _isOpen` (line 124)
- `private int _currentCategoryIndex` (line 125)
- `private int _currentItemIndex` (line 126)
- `private bool _isAutoWalking` (line 128)
- `private float _autoWalkSpeed` (line 129) — queried from player at walk start via GetMoveSpeed(true)
- `private Vector3 _autoWalkTarget` (line 130)
- `private string _autoWalkLabel` (line 131)
- `private Transform _autoWalkTransform` (line 137) — live transform of current auto-walk target; null for exits
- `private bool _autoWalkArrived` (line 142) — true once player reached target and "Arrived" was announced; player stays in proximity-lock until NumPad 5 pressed
- `private bool _autoWalkIsCounter` (line 147) — true when walking to a counter NPC via partial path; arrival detected at last waypoint not NPC proximity
- `private NavMeshPath _navPath` (line 150) — reusable NavMeshPath object allocated once and reused for every path calculation
- `private Vector3[] _pathCorners` (line 153) — waypoint positions from the last NavMesh path calculation
- `private int _pathCornerIndex` (line 156) — index of the current waypoint being walked toward in _pathCorners
- `private float _pathRecalcTimer` (line 159) — timer for periodic path recalculation when following moving NPCs
- `private FieldmapID _lastFieldmapID` (line 174) — tracks current fieldmap to detect area changes
- `private bool _fieldmapInitialized` (line 175)

### Properties

- `public bool IsListOpen => _isOpen` (line 178)
- `public bool IsAutoWalking => _isAutoWalking` (line 181)

---

### Methods

#### Constructor

- `public NavigationHandler()` (line 187)
  Note: Allocates _categories array (7 lists) and the reusable _navPath object.

---

#### Region: Patch Application

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 207)
  Note: Applies four Harmony prefixes — PlayMoveAnimation (block animation resets during auto-walk), GameInputManager.IsDown, GameInputManager.IsRepeat, GameInputManager.GetDPad (all three suppress game input while gamepad nav is active). Each patch is wrapped in its own try-catch so a single failure does not block the others.

---

#### Region: Public Methods

- `public void ToggleNavList()` (line 284)
  Note: Keyboard entry point (NumPad 5). If auto-walking, cancels walk instead of toggling. If open, closes. If field is free, scans and opens.

- `public void GamepadOpenNav()` (line 309)
  Note: L1 pressed. Cancels auto-walk if active, checks field is free, scans and opens the list, then sets _gamepadNavActive=true to enable input suppression.

- `public void GamepadCloseNav()` (line 344)
  Note: L1 released. Sets _gamepadNavActive=false and silently closes the list (no "closed" announcement). Category and item indices are preserved so the user can quickly reopen.

- `public void NavDown()` (line 358)
  Note: Moves to the next item in the current category. Wraps around. Announces new item.

- `public void NavUp()` (line 368)
  Note: Moves to the previous item in the current category. Wraps around. Announces new item.

- `public void NavCategoryNext()` (line 378)
  Note: Advances to the next non-empty category and announces it. Does nothing if already on the only non-empty category.

- `public void NavCategoryPrev()` (line 389)
  Note: Moves to the previous non-empty category and announces it. Does nothing if already on the only non-empty category.

- `public void AutoWalkTo()` (line 405)
  Note: Called by NumPad 1 (keyboard) or L1+LStick Up (gamepad). Calculates a NavMesh path to the highlighted item before committing. Counter NPCs use allowPartial=true. On success: closes the list, sets walking state, queries run speed, plays Run animation, announces "Walking to [label]". On failure: announces an error and aborts.

- `public void CancelAutoWalk(bool announce = true)` (line 498)
  Note: Resets all auto-walk state fields. Optionally announces cancellation. Called by NumPad 5 during walking or automatically on scene change.

- `public void Update()` (line 522)
  Note: Per-frame update called from Main.OnUpdate(). Calls CheckFieldmapChange() every frame regardless of walk state. During auto-walk: updates live target position, checks arrival at final target, handles proximity-lock for moving NPCs, recalculates path for moving NPCs on a timer, advances along NavMesh waypoints, handles counter NPC arrival at last waypoint, moves and rotates the player transform directly.

- `public void LateUpdate()` (line 716)
  Note: Empty stub. Kept as a hook point. Animation is now managed via the PlayMoveAnimation Harmony prefix rather than per-frame LateUpdate overrides.

---

#### Region: Private — Build

- `private void BuildNpcs(Vector3 playerPos, FieldmapID mapID)` (line 729)
  Note: Scans FindObjectsOfType<FieldNpcCharacter>(), skips FieldEnemy casts and party members (<2 units). Resolves names via ResolveNpcName(). Filters unreachable NPCs via NavMesh (counter NPCs exempt). Numbers any remaining generic "NPC" labels by distance order.

- `private static string ResolveNpcName(FieldNpcCharacter npc, Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams)` (line 817)
  Note: Six-step name resolution: (1) check DialogueHandler.NpcDisplayNames by instance ID, (2) match initial position against ConstNpcParameter entries, (3) check DialogueHandler.PersistentNpcNames by code name, (4) parse code name with ParseNpcCodeName, (5) fall back to NPC type category label, (6) return "NPC" for generic fallback. Qualifies functional NPC names with their category (e.g. "Equipment shop (Hahn)").

- `private static string QualifyNpcName(string displayName, string category)` (line 887)
  Note: Returns "[category] ([displayName])" for functional NPCs (shop/inn/guild) and plain "[displayName]" for generic NPCs (category == "NPC").

- `private static string ParseNpcCodeName(string codeName)` (line 905)
  Note: Parses internal code names like "NPC_0003_01a_18_GIRL1" into "Girl 1". Takes the last underscore segment, splits trailing digits from the descriptor, and title-cases the result. Returns null if the code name cannot be parsed.

- `private void BuildChests(Vector3 playerPos)` (line 935)
  Note: Scans FindObjectsOfType<FieldTreasureBox>(). Labels as "Opened chest" or "Unopened chest" based on isAcquired. Filters unreachable via NavMesh. Numbers opened and unopened chests independently by distance.

- `private void BuildExits(Vector3 playerPos)` (line 995)
  Note: Scans FindObjectsOfType<FieldMapjumpCollision>(). Labels each exit by icon type (GATE vs DOOR) and resolves the destination name via ResolveMapName(). Filters unreachable via NavMesh. Each exit's LiveTransform is null (static position).

- `private void BuildMarkers(Il2CppSystem.Collections.Generic.List<FieldLocationPoint> list, Vector3 playerPos)` (line 1047)
  Note: Takes FieldManager.FieldLocationPointList directly (not a scene scan). Labels as "Marker" or "Marker N" if multiple. Filters unreachable via NavMesh.

- `private void BuildEvents(Vector3 playerPos)` (line 1102)
  Note: Scans FindObjectsOfType<FieldEventCollision>(), skips events where IsEventActivate() returns false. Labels as story/PA/side/generic event based on which event sub-type is active. Filters unreachable via NavMesh. Numbers each type independently.

- `private void BuildSavePoints(Il2CppSystem.Collections.Generic.List<FieldSavePoint> list, Vector3 playerPos)` (line 1183)
  Note: Takes FieldManager.FieldSavePointList directly. Labels as "Save point" or "Recovery save point" based on IsRecovery. Filters unreachable via NavMesh. Numbers each type independently only when there are multiples of that type.

- `private void BuildEnemies(Vector3 playerPos)` (line 1262)
  Note: Scans FindObjectsOfType<FieldEnemy>(). Resolves enemy name via the four-step encounter chain (encountID → GetFieldmapEncountParameter → enemyPartyID → GetEnemyParameterListByPartyID → charaNameID → TextManager, then ParseCharaNameID as fallback). Combines name and difficulty type into the label. Filters unreachable via NavMesh. Numbers duplicate labels.

- `private static string ParseCharaNameID(string key)` (line 1420)
  Note: Strips "CHARA_" or "MON_" prefix and title-cases the remainder. e.g. "CHARA_LIZARDAXE" → "Lizardaxe". Used as a fallback when TextManager cannot resolve the name key.

- `private static string GetEnemyTypeName(FieldEnemySymbolType type)` (line 1438)
  Note: Maps FieldEnemySymbolType enum values to localized strings ("weak", "medium", "strong", "raid"). Subspecific variants map to the same label as their base type.

---

#### Region: Private — Announce

- `private void CloseList()` (line 1462)
  Note: Clears all category lists, sets _isOpen=false, announces closure via ScreenReader.

- `private void AnnounceCurrentItem()` (line 1470)
  Note: Announces the currently highlighted item as "[label], [distance] units."

- `private void AnnounceCategory()` (line 1482)
  Note: Announces the current category name and its first item as "[category]. [label], [distance] units."

---

#### Region: Private — Helpers

- `private void AnnounceArrival(string arrivalText)` (line 1512)
  Note: If another message was spoken within the last 0.5 seconds (e.g. a tutorial popup), combines the arrival text with that message so the user hears both. Otherwise announces arrival text alone.

- `private void ScanAndOpenList()` (line 1531)
  Note: Shared by ToggleNavList() and GamepadOpenNav(). Gets player position and map ID from FieldManager, calls all seven Build* methods, sets _isOpen=true, selects the first non-empty category, and announces the first item.

- `private bool IsFieldFree()` (line 1603)
  Note: Returns true if FieldManager.Instance is not null, a control player exists, and CampMenuHandler.IsCampOpen is false. Used to gate nav activation.

- `private int FirstNonEmptyCategoryFrom(int startIndex)` (line 1622)
  Note: Searches forward (wrapping) from startIndex for the first non-empty category. Returns startIndex if all categories are empty.

- `private int LastNonEmptyCategoryBefore(int startIndex)` (line 1632)
  Note: Searches backward (wrapping) from startIndex for the last non-empty category. Returns startIndex if all categories are empty.

- `private static int DistanceUnits(float dist)` (line 1642)
  Note: Rounds float distance to int for announcement. Despite the name, it is simply Math.Round cast to int.

- `private bool IsReachable(Vector3 playerPos, Vector3 targetPos)` (line 1650)
  Note: Snaps both positions to the NavMesh surface within NavMeshSampleRadius and calculates a path. Returns true only if PathComplete. Returns true (permissive fallback) if NavMesh is unavailable near the player or if an exception occurs.

- `private bool CalculateAndStorePath(Vector3 playerPos, Vector3 targetPos, bool allowPartial = false)` (line 1694)
  Note: Calculates a NavMesh path and copies the corners into the managed _pathCorners array. Sets _pathCornerIndex to 1 (skipping the start position at index 0). When allowPartial=true, a PathPartial result is accepted (used for counter NPCs). Returns false if no usable path is found.

- `private void CheckFieldmapChange()` (line 1734)
  Note: Called every frame from Update(). Detects when currentFieldmapID changes. Skips the very first detection to avoid announcing on game load. Resets tracking state when FieldManager is null (not on a field). Announces the new map name via ScreenReader when a valid transition is detected.

- `private static string ResolveMapName(FieldmapID destId)` (line 1784)
  Note: Three-priority resolution: (1) manual MapNameOverrides dict, (2) _mapNameCache, (3) ParameterManager.GetFieldParameter → FieldmapNameID → TextManager.GetMessage(System). Falls back to the raw FieldmapNameID key if TextManager returns empty, then to the last underscore segment of the FieldmapID code. Caches all results in _mapNameCache.

- `private static bool PlayMoveAnimation_Prefix(FieldBillboardObject __instance, FieldAnimationKind animationKind)` (line 1865)
  Note: Harmony prefix on FieldBillboardObject.PlayMoveAnimation. During the approach phase (_staticIsApproaching=true), blocks any non-Run animation from being applied to the player character. This prevents the game's state machine from resetting the Run animation to Idle each frame. Returns false (skip original) to block; true to allow.

- `private static bool IsDown_Prefix(GameInputManager.InputAction inputAction, ref bool __result)` (line 1897)
  Note: Harmony prefix on GameInputManager.IsDown. While _gamepadNavActive=true, sets __result=false and returns false for D-pad directions (Up/Down/Left/Right), shortcut actions (ShortCutUp/Down/Left/Right), and FieldCameraLeft (L1 camera pan). Returns true (allow original) for all other actions.

- `private static bool IsRepeat_Prefix(GameInputManager.InputAction inputAction, ref bool __result)` (line 1927)
  Note: Harmony prefix on GameInputManager.IsRepeat. Mirrors IsDown_Prefix suppression so held D-pad inputs do not auto-repeat while gamepad nav is active.

- `private static bool GetDPad_Prefix(ref Vector2 __result)` (line 1954)
  Note: Harmony prefix on GameInputManager.GetDPad. Sets __result to Vector2.zero and returns false while _gamepadNavActive=true, so D-pad analog input does not move the player character.

- `private static bool IsFunctionalNpcType(NpcType type)` (line 1969)
  Note: Returns true for INN, SHOP_EQUIPMENT, SHOP_ITEM, SHOP_FOOD, GUILD, FISH_COLLECTOR, FACILITY. Used to decide whether to skip NavMesh reachability filtering (counter NPCs) and whether to use partial NavMesh paths during auto-walk.

- `private static string GetNpcCategory(NpcType type)` (line 1984)
  Note: Maps NpcType enum to a readable category label string (e.g. SHOP_EQUIPMENT → "Equipment shop", INN → "Innkeeper"). Returns "NPC" for all unrecognized types.

- `private static Il2CppSystem.Collections.Generic.List<ConstNpcParameter> TryGetNpcParams(FieldmapID mapID)` (line 2002)
  Note: Wraps ParameterManager.Instance.GetNpcParameter(mapID) in a try-catch. Returns null on failure rather than propagating the exception.
