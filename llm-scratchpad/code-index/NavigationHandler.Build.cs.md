# NavigationHandler.Build.cs (1493 lines)

NOTE: This is a `partial class NavigationHandler` fragment. Contains the NavItem data model and all Build* methods that populate navigation categories.

namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, UnityEngine, UnityEngine.AI

## struct NavItem (line 15)
Private data model for a single navigable target.

fields/properties (declaration order):
- Label : string (line 17)
- Distance : float (line 18)
- Position : Vector3 (line 19)
- LiveTransform : Transform (line 25)  — live transform for moving targets (NPCs, chests, markers); null for exits
- IsCounterNpc : bool (line 31)  — skips NavMesh reachability filter; NPC is behind a counter
- EventRef : FieldEventCollision (line 39)  — used to call StartEvent() directly when NavMesh path ends short of trigger zone
- TriggerBounds : Bounds? (line 45)  — collider bounds of event trigger zone for proximity verification
- FacePosition : Vector3? (line 51)  — optional facing target on arrival (e.g. water center for fishing spots)

## partial class NavigationHandler (line 11)

methods (declaration order):
- void BuildNpcs(Vector3 playerPos, FieldmapID mapID) (line 65)
  - note: Scans FieldNpcCharacter objects; filters enemies, player, party, INVALID types; separates PA NPCs (code name "pa_" prefix) into CAT_EVENT, functional NPCs into CAT_INTERACTABLE, plain NPCs into CAT_NPC; numbers generic "NPC" labels by distance order.
- static string ResolveNpcName(FieldNpcCharacter npc, Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams, out string resolvedCodeName) (line 200)
  - note: 6-step resolution: dialogue instance map → position-matched ConstNpcParameter → persistent dialogue name → code name parse → NPC type category → "NPC" fallback.
- static string QualifyNpcName(string displayName, string category) (line 277)
  - note: Returns "[category] ([displayName])" for functional NPCs; plain displayName when category is "NPC".
- static string ParseNpcCodeName(string codeName) (line 295)
  - note: Extracts last underscore segment, splits trailing digits, title-cases the text portion. e.g. "NPC_0003_01a_18_GIRL1" → "Girl 1". Returns null if unparseable.
- void BuildChests(Vector3 playerPos) (line 325)
  - note: Scans FieldTreasureBox; uses IsAcquired (PascalCase property, not backing field); skips world map chests beyond WorldmapChestMaxDistance; numbers opened and unopened chests separately.
- void BuildExits(Vector3 playerPos) (line 383)
  - note: Scans FieldMapjumpCollision; resolves destination name via ResolveMapName(); labels GATE vs DOOR icon type.
- void BuildMarkers(Il2CppSystem.Collections.Generic.List<FieldLocationPoint> list, Vector3 playerPos) (line 425)
  - note: Reads from FieldManager.FieldLocationPointList; skips discovered markers (effectComponent == null); numbers if more than one present.
- void BuildEvents(Vector3 playerPos) (line 481)
  - note: Scans FieldEventCollision; skips inactive triggers, hidden PA/sub events (isDisableIcon); labels story/PA/sub-event with reward/battle hints; does NOT clear CAT_EVENT (BuildNpcs may have added PA NPCs already); numbers duplicates within each label type.
- void BuildSavePoints(Il2CppSystem.Collections.Generic.List<FieldSavePoint> list, Vector3 playerPos) (line 597)
  - note: Uses FieldManager.FieldSavePointList; labels "Save point" vs "Recovery save point" via IsRecovery; numbers each type separately if multiples exist.
- void BuildFishingSpots(Vector3 playerPos) (line 671)
  - note: Scans FieldFishingWaterPlace; walk target is nearest NavMesh point to collider center; FacePosition set to water center; LiveTransform left null to avoid off-NavMesh arrival distance issues.
- void BuildEnemies(Vector3 playerPos) (line 738)
  - note: Scans FieldEnemy; resolves name via EncountID → encounter params → partyID → enemy params → charaNameID → TextManager (multiple MessageTypes) → TextUtil.ParseCharaNameID fallback; labels include difficulty type from EnemySymbolType; numbers duplicate labels.
- static string GetEnemyTypeName(FieldEnemySymbolType type) (line 884)
  - note: Maps Weak/SubspecificWeak → "weak", Medium → "medium", Strong → "strong", Raid → "raid"; returns "" for unknown.
- void BuildStairs(Il2CppSystem.Collections.Generic.List<FieldStairs> list, Vector3 playerPos) (line 909)
  - note: Uses FieldManager.FieldStairsList; labels "Stairs up" / "Stairs down" via isUpperStage; numbers each direction separately if multiples.
- void BuildDoors(Il2CppSystem.Collections.Generic.List<FieldDoor> list, Vector3 playerPos) (line 983)
  - note: Filters to StoneDoor seType only; labels open/closed via doorState; numbers each state separately if multiples.
- void BuildWarpPoints(FieldManager fm, Vector3 playerPos) (line 1066)
  - note: Iterates FieldGimmickManager.FieldGimmickList; TryCast identifies Gimmick09 (warp panel), Gimmick17 (magic circle, skipped if disabled/isDisableWarp), Gimmick03 (moving platform); numbers each type separately.
- void BuildWorldmapLocations(Vector3 playerPos, WorldmapID wmID) (line 1208)
  - note: Uses ConstWorldmapSymbolParameter filtered by CITY/DUNGEON icon type and MainScenarioProgress range; resolves name via localityID → GetLocalityParameter → localityNameID → TextManager; matches runtime WorldmapSymbol for LiveTransform; calls LogWorldmapMapjumpColliders for diagnostics.
- void LogWorldmapMapjumpColliders(Vector3 playerPos) (line 1343)
  - note: Debug-only diagnostic; logs all FieldMapjumpCollision objects with collider type, bounds, and distance. Writes to MelonLogger.Msg directly.
- void SortAndFilterUnreachable(List<NavItem> items, Vector3 playerPos) (line 1404)
  - note: Sorts by distance, then removes items failing IsReachable(); counter NPCs skip filter; if ALL non-counter items would be removed (disconnected NavMesh), keeps everything; appends floor labels via LabelFloorDifferences. Island graph code is present but disabled (hasIslandGraph=false).
- void LabelFloorDifferences(List<NavItem> items, Vector3 playerPos) (line 1475)
  - note: Appends "(above)" or "(below)" loc string to item labels when Y difference >= FloorChangeThreshold.
