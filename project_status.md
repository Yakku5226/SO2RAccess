# Project Status: SO2RAccess

## Project Info

- **Game:** Star Ocean: The Second Story R
- **Engine:** Unity (IL2CPP)
- **Architecture:** 64-bit
- **Mod Loader:** MelonLoader v0.7.2-ci.2398
- **Runtime:** net6
- **Unity Version:** 2021.3.22f1
- **Developer:** SquareEnix
- **Game directory:** E:\Program Files\Steam\steamapps\common\STAR OCEAN THE SECOND STORY R
- **User experience level:** Little/None
- **User game familiarity:** Somewhat
- **Languages:** English only

## Setup Progress

- [x] Experience level determined
- [x] Game name and path confirmed
- [x] Game familiarity assessed
- [x] Game directory auto-check completed
- [x] Mod loader selected and installed (MelonLoader)
- [x] Tolk DLLs in place (Tolk.dll + nvdaControllerClient64.dll)
- [x] .NET SDK available (8.0.418)
- [x] Decompiler tool ready (ilspycmd 9.1.0.7988)
- [x] Game launched once with MelonLoader (log + IL2CPP stubs generated)
- [x] Game code decompiled to `decompiled/Assembly-CSharp/`
- [ ] Tutorial texts extracted (if applicable)
- [x] Multilingual support decided (English only)
- [x] Project directory set up (SO2RAccess.csproj, Main.cs, ScreenReader.cs, DebugLogger.cs, Loc.cs)
- [ ] CLAUDE.md updated with project-specific values
- [x] First build successful (SO2RAccess.dll copied to Mods folder)
- [x] "Mod loaded" announcement working in game

## Current Phase

**Phase:** Phase 3 — Feature Implementation
**Currently working on:** Dungeon navigation via OBSERVED TRAVERSALS (2026-06-12) — WORKING
**Last completed:** Traversal recording + routing + release bundling (2026-06-12)

### Dungeon Navigation — OBSERVED TRAVERSALS (2026-06-12) — WORKING

After exhausting every static approach (NavMesh = 12% coverage + fragmented;
CalcHeight FAILs in dungeons; OffMeshLink stripped from IL2CPP build; collision
raycasts can't tell walls from ramps), the reliable solution is to record where a
player ACTUALLY walks and route over that. See the detailed "PIVOT TO
OBSERVED-TRAVERSAL NAVIGATION" entry lower in this file.

- **TraversalGraph.cs:** breadcrumb every ~1m of real movement, A* routing, per-map
  JSON persistence. User tested: walked the whole of Krosse Cave; 1035+ breadcrumbs,
  94% one connected component, multi-floor. Load/save/connectivity all verified.
- **Release bundling (DONE):** recorded JSONs live in project `traversals/`, embedded
  into the DLL via `<EmbeddedResource Include="traversals\*.json" />`. TraversalGraph.Load
  uses UserData first, else the embedded copy. Verified 4 maps embedded as
  `SO2RAccess.traversals.MF_000X_01A.json`. To refresh: copy
  UserData\SO2RAccess\traversals\*.json into project traversals\ and rebuild.
  (Tiny near-empty maps MF_0005/0007 can be pruned from project traversals\ before release.)
- **Cleanup done:** deleted dead experimental files (DungeonNavGraph, DungeonGraphDiagnostics,
  OffMeshLinkSpike); island/bridge runtime calls removed from Update.
- **TODO next session:** (1) jump-prompt audio cue — ledge "press X to jump" shows via
  UIFieldIconSelector.ShowFieldIcon (UIDefine.FieldIconType); hook it. (2) Fully delete the
  island/multi-segment system (IslandScanner/IslandNavigator/NavMeshIslandDiagnostics +
  ~45 refs in NavigationHandler.AutoWalk.cs: StartMultiSegmentWalk, CheckSegmentTransition,
  GetExitIslandSet, exit-avoidance, _routeSegments). (3) Blind-exploration mode for unmapped areas.

### Field Shortcut & Specialty Sub-menus — DONE (2026-03-26)

- Train switch selector: reads party member names + ON/OFF state + "Turn all on/off" items
- Scout action menu: reads "Look for enemies" / "Avoid enemies" / "Do nothing" options
- Pickpocket field menu: reads item names + success rate percentages
- Fix: creation hook with empty dataList no longer blocks fallback polling
- Fix: _icActiveSkillCategory gates Train/Scout polls to prevent stale-state cross-interference

### Cross-Island Navigation System — IN PROGRESS (2026-06-12)

Core architecture built. Ground verification approach identified but has bugs to fix.

**What's built and working:**
- IslandScanner.cs: NavMesh scanning, BFS island grouping, gap detection
- IslandNavigator.cs: Data structures, JSON persistence, BFS route planning
- Multi-segment auto-walk: RouteSegment execution, crossing phase, segment transitions
- NavigationHandler: Deferred island scan (1.5s delay), bridge recording polling at 4Hz
- Island-aware filtering in Build.cs, 10 Loc keys, F11 field map diagnostics
- Ground verification via Physics.RaycastAll + surface normal check

**Bug fixes applied (2026-06-12) — PENDING IN-GAME TEST:**
1. [FIXED] Raycast hits wrong floor: VerifyGapsWithGround now raycasts from
   `expectedY + offset` where expectedY = Lerp(ptA.y, ptB.y, t), and picks the
   ground hit CLOSEST to expectedY (not the highest). Upper-floor overhangs are
   no longer selected over the ramp. (IslandScanner.cs)
2. [FIXED] Bridge deduplication: MergeBridges now seeds a HashSet of the fresh
   scan's island pairs (unordered, via NormalizePair) and skips any cached bridge
   whose pair already exists. Bridges no longer grow on scene reload. (IslandNavigator.cs)
3. [FIXED] MaxGroundStepY raised 3f -> 5f for gradual ramps. (IslandScanner.cs)
4. [FIXED — REVISED] Player warps to world map mid-walk. LOG ANALYSIS (11:34:39,
   route to "Opened chest 3", 3 segments): warp happened during the NORMAL
   segment-0 walk, NOT the crossing phase. Segment-0 path ran from (-8,46) to a
   bridge edge at (0,0.1,7.6), passing the EXPEL town gate at ~(0,~12) on the way.
   Two-part fix:
   (a) ROOT CAUSE — route planning now avoids passing THROUGH islands that contain
       a map exit. GetExitIslandSet() maps each FieldMapjumpCollision to its island;
       PlanRoute/BfsIslandPath skip those as transit (still allowed as final dest).
       Falls back to allowing them only if no safe route exists. (IslandNavigator.cs)
   (b) SAFETY NET — exit-zone steering (AvoidExitZones) now applied to ALL auto-walk
       movement during a cross-island route (normal segment walk + obstacle detour),
       not just the crossing phase. Steering is now TANGENTIAL (arcs around the gate)
       instead of pure repulsion, so a head-on approach slips past instead of stalling.
       (NavigationHandler.cs/.AutoWalk.cs)
   Cache cleared for single-target walks so normal walks are unaffected.
5. [WON'T FIX — limitation] FieldStairs empty on Krosse Cave — ramps are plain
   geometry, not game objects. Ground verification (fix #1) is the universal bridge
   source, so FieldStairs is not needed. Diagnostic logging retained for reference.

**ROOT CAUSE FOUND (2026-06-12, log 11:43) — island routing was firing when it
shouldn't.** Krosse Cave scans into 16 flood-fill islands; bridges connect only
{0,1,4,6,7,11,13}. Player spawns on island 1 (entrance sliver); its ONLY bridge is
1<->0 whose crossing point (0,7.6) is on the far side of the EXPEL gate. So every
route off island 1 walks south through the gate. Chest 1 is on island 3, which has
NO bridge -> false "unreachable" even though the chest was physically opened.
KEY INSIGHT: flood-fill island IDs are FINER than real NavMesh connectivity. A
single walkable floor gets split into many islands, so "different island" does NOT
mean "needs multi-segment routing."
FIX: AutoWalkTo now calls HasCompleteNavMeshPath(player, target) BEFORE island
routing. If NavMesh returns a COMPLETE path, it does a normal single-path walk and
skips island routing entirely (logs "NavMesh path is COMPLETE"). Island routing now
only fires for genuine NavMesh disconnection (partial/invalid path — separate floors
via ramps). This fixes both symptoms: chest 1 no longer false-unreachable, and far
chests route directly (deeper into the cave) instead of south through the entrance.
(NavigationHandler.AutoWalk.cs)
NOTE: if a chest is genuinely on a partial-path floor AND its island has no bridge,
it can still be unreachable — that's a real bridge-gap to address separately, but the
common case (complete NavMesh path) is now handled. Watch the F12 log for "COMPLETE".

**FALSE-ARRIVAL FIX (2026-06-12, log 11:56) — the actual user-facing bug.**
Log proved every false "Arrived" came from arrival being announced against the
WRONG position: e.g. "Arrived at Unopened chest 2" with ARRIVAL DIAG distXZ=1.20
to target (17.88,120.64) — a multi-segment bridge waypoint — while the real chest
was 81m away. Also "Arrived near ... target is above/below you" fired with
playerY==targetY (identical) from StartMultiSegmentWalk hardcoding
_autoWalkDifferentFloor=true.
FIX — single arrival authority: new IsAtRealTarget(playerPos) checks the REAL
final target (_routeFinalTarget for multi-segment, else _autoWalkTarget) in full
3D — within arrival radius horizontally AND |vertGap| <= 2.0m. All three
announcement sites now gated through it:
  1. Proximity arrival: only fires when _routeSegments==null AND on the target's
     level (never on a bridge waypoint, never from another floor).
  2. Path-exhausted: announces real arrival only if IsAtRealTarget; otherwise an
     honest "Could not reach {0}. Stopped/above/below ..." message.
  3. Different-floor partial handler: same — never says "Arrived", says
     "Could not reach ... above/below" instead.
  4. Multi-segment segment-path exhaustion now hands off to CheckSegmentTransition
     (crossing) instead of leaking into the normal arrival handlers.
New Loc keys: nav_autowalk_cannot_reach[_above|_below]. Result: the mod can no
longer claim arrival unless the player is genuinely at the real chest.
(NavigationHandler.cs, Loc.cs)

**WALK-OUT ROOT CAUSE + HARD EXIT BARRIER (2026-06-12, log 12:29).**
Mechanism: player's NavMesh island is huge (cave deep z=86 down to entrance z=7.6).
For an un-navigated chest (no complete path), island routing picks a bridge whose
crossing point is (0,0.1,7.6) — PAST the exit gate (~z=10.5). Segment-0's NavMesh
path therefore runs from deep cave straight down through the gate → Overworld.
Exit-zone steering can't help (the bridge destination is on the far side of the gate);
"avoid exit island 1" can't help (the bridge belongs to island 0, gate sits between 0/1).
Only inferred-bridge routes do this; complete-path direct walks go deep correctly.
FIX (hard barrier): PathCrossesMapExit(corners) samples every NavMesh path densely and
tests against all FieldMapjumpCollision bounds (+0.5m). CalculateAndStorePath now rejects
any path that crosses a map exit UNLESS the target itself is an exit (_autoWalkAllowExit,
set from IsExitCategory). Rejected routes announce nav_autowalk_route_exits ("Cannot reach
{0} without leaving the area"). Centralized in CalculateAndStorePath so it covers single
+ all multi-segment paths. (NavigationHandler.cs/.AutoWalk.cs, Loc.cs)
CONSEQUENCE: chests whose only inferred route is through the gate now say "cannot reach
without leaving the area" instead of walking the player out. Honest, but does NOT yet make
them reachable — that needs reliable OBSERVED-crossing bridges (next step, see
feedback-navigation-reliability memory + confirmed/unconfirmed design).

**NAVMESH FRAGMENTATION / SNAP-TO-SLIVER FIX (2026-06-12, log 13:05).**
Smoking gun: standing at (0.5,1.1,18.5) the mod found a 59-waypoint COMPLETE path that
runs THROUGH the player's later z=40 spot and goes deep to (108.6,8.3,82.2) — so the main
dungeon IS one connected, auto-walkable NavMesh. Yet standing at (-2,1.5,40) the same deep
chests returned "no complete path" → island routing → refused at the gate (every unopened
chest, save, event). Root cause: the dungeon NavMesh is fragmented into slivers, and the
player's exact footing sometimes snaps onto a tiny disconnected sliver ~1-2m off the main
floor; from there CalculatePath can't see the connection that obviously exists.
FIX: TryFindCompletePath probes ~13 nearby points (same floor, ≤3m) and accepts the first
snap that yields a COMPLETE path. HasCompleteNavMeshPath and CalculateAndStorePath both use
it — preferring a complete path, falling back to partial only when allowed. Logs "complete
path found via probe offset ..." when a sliver snap was rescued. Still 100% reliable (requires
a genuine complete path); turns many false "no route" cases into real deep walks that never
go through the gate. (NavigationHandler.AutoWalk.cs)
EXPECTATION: most chests on the connected main mesh should now route directly and correctly.
Truly disconnected chests (separate NavMesh joined only by un-baked ramps) will still need the
observed-bridge approach; those remain "cannot reach without leaving the area".

**NEW DIRECTION — CUSTOM COLLISION WALKABILITY MAP (2026-06-12, plan approved).**
Root cause accepted: the baked NavMesh is fragmented and unfixable at runtime
(NavMesh.AddLink/bake are IL2CPP stubs). The player actually moves by collision
physics (Il2CppCommon.Physics2), so the TRUE walkable space is the collision
geometry (ramps the NavMesh omits). Plan: build our OWN walkability graph from
downward collision raycasts → flood-fill components → reachability + A* paths that
follow real ramps and never route through the exit gate. Replaces island/bridge
system for field maps; world map unchanged. Full plan:
C:\Users\Jaco\.claude\plans\woolly-frolicking-cray.md (Phases 0-5).
Two design pillars (from our own past notes): do NOT use CalcHeight on dungeon
floors (use raw Physics.RaycastAll + normal.y>=0.4); do NOT trust wall masks
(connect cells by FLOOR CONTINUITY / step-delta, not wall-presence).

**PHASE 0 (validation diagnostic) — BUILT, AWAITING IN-GAME TEST.**
New file DungeonGraphDiagnostics.cs. Wired to F11 (debug mode) on field maps,
alongside the existing island diagnostic. Builds the collision walkability graph
in-memory (1.5m cells, RaycastAll-down, 8-neighbour step-delta connect, flood-fill),
marks map-exit nodes, then logs for EVERY treasure chest: reachable (same component
as player) / component / snapY / hops / viaExit. Also logs RaycastAll-vs-CalcHeight
primitive test and an OverlapSphere wall-mask probe. Changes NOTHING in live nav.
GO/NO-GO GATE: do the previously-"unreachable" UNOPENED chests log reachable=true
with viaExit=false? If yes → build Phases 1-5. If no → they're truly disconnected.
HOW TO TEST: enter Krosse Cave, ensure debug on (F12), press F11, send the log
lines tagged [DUNGEONGRAPH].

**PHASE 0 RESULT — PASSED DECISIVELY (2026-06-12, log 16:02).** Krosse Cave: graph
built 31612 nodes / 184 components / 240ms. ALL 14 chests (incl. all 9 unopened ones
the NavMesh called unreachable) reported reachable=True, comp=1 (= player), viaExit=False,
snapY matching targetY within 0.1m. CalcHeight=FAIL everywhere (confirms raw-RaycastAll
choice). Approach proven.

**PHASES 1+2+4 — BUILT & INTEGRATED, AWAITING IN-GAME TEST.**
- DungeonNavGraph.cs (NEW): collision walkability graph — Build (coarse-AABB then 1.5m
  RaycastAll fine grid, 8-neighbour step-delta connect, flood-fill components, exit-node
  flags), SnapToNode (Y-weighted), IsReachable (same component), FindPath (A* with binary
  min-heap, excludes exit nodes unless target is an exit) → Vector3[] corners.
- Integration (NavigationHandler.cs/.AutoWalk.cs/.Build.cs): _dungeonGraph field; deferred
  Build in CheckDeferredIslandScan (on map load); UseDungeonGraph() predicate
  (!worldmap && graph ready). When active it supersedes island routing: IsReachable (nav
  list filter + pre-walk), AutoWalkTo (island block guarded off), CalculateAndStorePath
  (new DungeonCalculateAndStorePath via graph A*). NavMesh/island kept as fallback only.
- Old island/bridge/multi-segment code still PRESENT but bypassed — retire in Phase 5
  after this is verified. Disk caching (Phase 3) deferred — 240ms build is acceptable.
HOW TO TEST: restart game, enter Krosse Cave, wait ~2s for "DUNGEONGRAPH: built" in log,
open nav list, pick a previously-unreachable UNOPENED chest, auto-walk. Expect it to
follow real ramps to the chest and announce real arrival (no walk-out, no false arrival).
Watch for the player clipping/sticking on walls (the one known risk of continuity-only
connection — would need a wall-refinement pass).

**PIVOT TO OBSERVED-TRAVERSAL NAVIGATION (2026-06-12) — collision graph retired.**
Why: exhaustive iteration proved NO static walkability source is reliable in dungeons —
NavMesh covers only 12% of walkable floor AND is fragmented; CalcHeight FAILs in dungeons;
OffMeshLink is stripped from the IL2CPP build (can't extend NavMesh); collision raycasts
can't distinguish walls from ramps/wall-tops (every heuristic rammed walls or over-segmented).
The ONLY 100%-reliable walkability signal is the player's ACTUAL movement (physics).
NEW SYSTEM (TraversalGraph.cs): records a breadcrumb every ~1m as the player walks a field
map (manual or auto), links consecutive + nearby breadcrumbs into a graph, persists per map
to UserData/SO2RAccess/traversals/{mapId}.json. Auto-walk/reachability route over breadcrumbs
(A*) — guaranteed walkable because a real player walked them. No raycasts, no wall guessing.
INTEGRATION: NavigationHandler records in Update (CheckTraversalRecording, gated on IsFieldFree;
BreakTrail on cutscene/menu; autosave every 10s + on map change via StartMap). IsReachable =
complete NavMesh path (towns) OR traversal-connected (dungeons). CalculateAndStorePath = complete
NavMesh path, else traversal A* path, else partial (counters). Island/multi-segment + collision
DungeonNavGraph fully bypassed (files kept, unused — delete later). F11 = LogTraversalDiagnostic
(breadcrumb count + per-chest navMeshComplete/traversal reachability).
TEST PLAN (user): sighted player walks the whole dungeon (records breadcrumbs, autosaves),
then load save + auto-walk over the recorded routes. Caveat: only reaches where the sighted
player actually walked; player must start near a breadcrumb (<6m).
NEXT (future): true blind-exploration mode for discovering unmapped areas.

**Build:** Succeeds (0 warnings, 0 errors), deployed to Mods folder.

**Key findings (documented for next session):**
- Krosse Cave (MF_0008_01A): 16 islands, 9 significant, single scene, no internal triggers
- CalcHeight does NOT work on dungeon floors (world-map-specific)
- GameRenderManager.LayerMaskHeight too restrictive — misses ramp geometry
- Physics.RaycastAll with normal.y>0.4 works but needs floor-aware origin height
- NavMesh not ready on scene load — 1.5s deferred scan required
- FindIsland via CalculatePath to island centers works (bounding box was unreliable)

**Files:** IslandScanner.cs, IslandNavigator.cs, NavMeshIslandDiagnostics.cs (new);
NavigationHandler.cs/.AutoWalk.cs/.Build.cs, Loc.cs, Main.cs (modified)

### TODO: Quick Heal Menu (D-pad Right)
- UIFieldQuickRecoverySelector — field overlay, same pattern as pickpocket
- Has recoveryDataList, listItemDataList, currentChoice, playerIDList
- Needs new handler with FindObjectOfType polling + data count gate (like pickpocket)
- No existing mod code handles it at all

### World Map Cached Grid System — WORKING (resolved 2026-06-12)

Grid format WMGH. Salva↔Krosse routing issue resolved by user during break.
Full investigation record in docs/worldmap-pathfinding.md and memory file worldmap-navigation.md.

### Fishing Accessibility (2026-03-18) — WORKING

- **What works (tested 2026-03-18):**
  - Fishing spots appear in Interactables nav category via `FindObjectsOfType<FieldFishingWaterPlace>()`
  - Auto-walk navigates player to the water's edge and arrives close enough to interact
  - Player faces the water on arrival via FacePosition (collider center, separate from walk target)
  - Catch result announcements: Harmony postfix on `UIFieldFishingResultPresenter.Set()` (CallerCount 1)
    announces "Caught: [fish name], [size], [new record/max size/new]." — deduped (game calls Set ~19x per catch)
  - "Fish got away" already caught by existing dialogue system
  - Game's built-in audio/vibration cues are sufficient for the minigame itself (no custom cues needed)
  - User completed Fishing Mission 1 successfully
- **Previous bugs fixed (2026-03-18):**
  - **Arrival too far:** LiveTransform tracked collider center (in water, off NavMesh), making arrival
    distance ~2m instead of using the NavMesh walk target. Fix: FacePosition field on NavItem stores
    water center for facing only; LiveTransform left null so arrival uses static Position.
  - **Catch result spam:** Set() hook fired ~19 times per catch. Fix: dedup guard (same text + 2s window).
- **Files:**
  - `NavigationHandler.Build.cs` — `BuildFishingSpots()` method
  - `NavigationHandler.cs` — `BuildFishingSpots()` call in scan, fishing result hook in ApplyPatches
  - `NavigationHandler.Patches.cs` — `FishingResultSet_Postfix()` for catch announcements
  - `Loc.cs` — 6 keys: nav_fishing, nav_fishing_n, fish_caught, fish_new_record, fish_new, fish_max_size

### Item Creation Sub-screen (2026-03-19) — CONFIRMED WORKING (2026-03-20)

- **What works (confirmed 2026-03-20):**
  - Skill selection: skill name, description, level, tab switching — all working
  - Action list: category name, creation hook, character tab — working
  - "????" item names: fixed, now says "Unknown" (SanitizeItemName helper)
  - Create mode: after selecting a material (e.g. Silver), announces "Create [count].
    Success rate: [X] percent." Count changes announced as user adjusts with D-pad.
    Detection via `actionPresenter.currentCreateCount` (-1 = inactive, >0 = Create visible).
  - Result screen: fully working — item name, success/failure, position
  - Stale suppression: all IC sub-screens (skill, action, result) properly seed
    LastIndex and tab values on camp open. Scrolling past IC in root menu is silent.
  - **Field shortcut IC (D-pad Down on field):** fully working (2026-03-19).
    Game reuses `UICampWindow` with `OpenCampState=SelectSpecialSkill`.
    Detected via `UICampWindow.OpenCampState` property in Open postfix.
    `_isFieldShortcutIC` flag + `IsICActive()` helper unlocks all 4 IC gates
    (polling + 3 hooks). Announces "IC Specialty." on open. Flag cleared on
    window close, skill selector hidden, or root menu activation.
  - **Result announcement fix (2026-03-19):** single-item results at index 0
    were not announced due to stale seed. Fixed by resetting result index on
    create mode exit with 1.5s delay to sync with result animation.
- **What's NOT yet accessible (future work):**
  - **Material selection screen** (`UICampSpecialSkillAddMaterialSelector`):
    ALL sub-selectors have stale `activeInHierarchy=true`. The `Set` hook (CallerCount 1)
    does NOT fire (native-only call). The `currentState` field stays at `Normal` (never
    transitions). This screen likely only appears for Compounding/Customization at higher
    skill levels. Hook + polling code is dormant, ready when encountered.
- **Files:**
  - `CampMenuHandler.ItemCreation.cs` — all IC logic (skill, action, create mode, result, field shortcut flag)
  - `CampMenuHandler.cs` — selector caching in Open postfix, shortcut detection, 3 Harmony patches, Update call
  - `Loc.cs` — 18 localization keys (ic_screen, ic_shortcut_screen, ic_tab_*, ic_skill_*, ic_action_*, ic_result_*, ic_unknown_item)

### Skill Development Screen Fix (2026-03-19) — CONFIRMED WORKING (2026-03-20)

- **Bug:** Specialty skills (Scouting, Familiar, Art, etc.) showed wrong SP cost and max level.
  - SP cost frozen at initial value (e.g. always "1" for Scouting, even when actual cost was 20+)
  - Max level flag stale — some specialties showed no cost (implying max) when still levelable
  - Knowledge skills (Determination, Biology, etc.) were correct — game refreshes their data
- **Root cause:** `itemDataList` on `UICampSkillSelector` is stale for specialties after leveling.
  The game updates the visual display but doesn't refresh the data objects for specialties.
  `consumeSP` and `isLevelMax` stayed frozen at list-build-time values.
- **Fix** (in `CampMenuHandler.Formation.cs`, `SkillInfoPresenter_Set_Postfix`):
  - Specialties: calls `UICommon.CalcNeedSpecialSkillForLevelUp(charaParam, specialSkillID)` fresh
    each time the hook fires. Sums `consumeSP` from returned list for total cost. Empty list = max.
  - Knowledge skills: still uses `itemData.consumeSP` (reliable), but verifies `isLevelMax` against
    `ConstSkillParameter.levelupSp.Count` instead of trusting the stale flag.
  - Gets current character via `_skillSelector` → `UICharacterTabListSelectorBase.currentPlayerID`
    → `ParameterManager.Instance.UserParameter.GetCharacterParameter()`
- **Files changed:** `CampMenuHandler.Formation.cs` (postfix logic), `CampMenuHandler.cs` (5 new RuntimeHelpers)
- **Build:** Succeeds, deployed to Mods folder
- **Tested (2026-03-20):** All confirmed working — SP costs accurate, reads correctly.

### Camp Quest & Mission Lists (2026-03-16) — COMPLETE

- **Quests** (camp → Quests and Missions → Quests):
  - Polls UIQuestSelector (UIListSelectorBase) for cursor + data
  - Announces: quest name, status (Available/In progress/Ready to report/Completed), position
  - New quests marked with "New"
  - Confirm press reads full description (title + description + rewards)
  - Hook: GameUIManager.OpenQuestWindow captures UIQuestWindow reference
- **Missions** (camp → Quests and Missions → Missions):
  - Polls UIMissionListSelector (UIListSelectorBase) for cursor + data
  - Announces: mission name, status (Complete/Incomplete/In progress/etc.), position
  - Category changes (Beginner/Expert/Specialist/Legend) announced on switch
  - Hook: GameUIManager.OpenMissionWindow captures UIMissionWindow (camp-only)
- **Guild handler fix:** Skips detection when camp is open (IsCampOpen guard)
  to prevent false "Guild." announcements on camp quest/mission screens
- **Files:** CampMenuHandler.Quest.cs, CampMenuHandler.Mission.cs, GuildHandler.cs, Loc.cs

### Guild Mission Menu (2026-03-15 → 2026-03-16) — NATIVE CODE WALL

- **What works:**
  - Window open/close detection via gameObject.activeInHierarchy
  - "Guild." announced on open
  - Dialog system catches "Mission accepted.", provisions, "There are no more missions"
- **What's blocked — EXHAUSTIVELY TESTED (2026-03-16):**
  The entire guild UI operates in native C++ that is invisible to managed code.
  Every approach below was tested with diagnostic dumps across full guild sessions:
  - currentDataList: always empty (0 items)
  - currentIndex: stuck at 0, never changes when user navigates
  - windowState: stuck at None, never transitions to List
  - FindObjectsOfTypeAll<UIMissionListItemPresenter>: found 14, ALL with empty text/state
  - All 59 TMPro components: only template/placeholder text, never updated
  - GetParsedText() internal buffer: same as .text (no hidden data)
  - textInfo.characterCount: 0 on all components
  - informationSelector.missionName: Japanese placeholder "ミッション名" only
  - ParameterManager bypass: shows 93 missions when guild shows 4 (wrong filtering)
  - Mission name text keys (MISSION_023 etc.): don't resolve via TextManager
  The game renders mission text through a native pipeline that bypasses Unity's
  managed TextMeshPro entirely — text is drawn on screen but never written to
  any managed field.
- **Current state:** GuildHandler detects window open/close, announces "Guild.",
  and relies on dialogue system for accept/provisions. Individual mission names
  and cursor tracking are not possible from managed code.
- **Files:** GuildHandler.cs, Main.cs, Loc.cs (guild_screen only)

### NavMesh Partial Path Progress Fix (2026-03-15) — TESTED OK (2026-03-16)

- **Problem:** The Krosse Guild entrance was filtered as unreachable from the upper level.
  NavMesh surfaces are disconnected (PathPartial) but Y difference is only ~1.0m — below
  the 2.0m FloorChangeThreshold, so the "different floor" exception didn't apply.
- **Fix:** IsReachable now accepts PathPartial when the partial path endpoint gets at least
  30% closer to the target than the player's start position. This catches disconnected
  NavMesh surfaces regardless of Y difference, without showing truly unreachable targets.
- **Also:** AutoWalkTo now always allows partial paths (allowPartial: true) since
  IsReachable already filtered out truly unreachable targets.
- **Test:** Confirmed working — Krosse Guild appears from upper town, auto-walk reaches it.

### Dialogue Choice Menu Fixes (2026-03-15) — Tested and confirmed

1. **Stale index on open:** 1-frame defer lets game reset selectChoiceIndex before announcing.
2. **Opening heading:** Menu now announces "Choice, N items." followed by the initial item
   in a single combined string (no double-read from screen reader interruption).
3. **Correct item count:** Uses `choiceMessageIDList.Count` (actual active choices) instead of
   `MaxChoiceIndex` (pre-allocated presenter slots). Was showing "X of 9" for 2-item menus.

### Auto-Walk Bug Fixes (2026-03-15) — Tested and confirmed

1. **Obstacle avoidance NavMesh sampling fix:** `TryStartObstacleAvoidance` now samples
   `_autoWalkTarget` onto NavMesh before checking detour paths. Previously, different-floor
   targets caused `PathInvalid` for all detour candidates (raw Y=8.8 wasn't on NavMesh surface).
2. **IsReachable accepts partial paths for different floors:** Targets on different floors
   (connected by stairs) get `PathPartial` — now accepted instead of being filtered from nav list.
3. **Chest IsAcquired fix:** Switched from `chest.isAcquired` (backing field, stale at distance)
   to `chest.IsAcquired` (property, calls native getter). Also fixed numbering loop using
   `StartsWith` instead of exact `==` so floor-suffixed labels ("Opened chest (above)") are
   correctly recognized as opened.
4. **Interactable arrival radius:** Added `InteractableArrivalRadius = 1.3f` for chests, save
   points, and interactables. Previously used 1.8f (NPC radius) — too far for chest interaction.
5. **Stuck loop prevention:** Max 3 obstacle avoidance attempts before cancelling with
   "Path blocked" message. Avoidance counter no longer resets on "progress" (detour movement
   counted as progress, causing infinite loops when path was truly blocked by guards).
6. **Quest marker filtering:** Discovered location points filtered by `effectComponent == null`
   (sparkle removed after discovery). `IsEnd` and `isEnd` properties don't work for this.
7. **Diagnostic cleanup:** Removed verbose NAV DIAG per-frame logging and marker diagnostic fields.

### Dialogue Choice Menu Stale Index Fix (2026-03-15) — Pending test

`selectChoiceIndex` returned a stale value from the previous menu on the activation frame,
causing the wrong item to be announced on open. Fix: defer `ActivateChoiceMenu` by one frame
after the presenter becomes visible, letting the game reset the index to 0 first.
Same one-frame deferral pattern used in dialogue voice detection.

### Auto-Walk Overhaul — Summary of Changes (2026-03-10)

**Core change:** Replaced `transform.position` direct movement with `GetLeftStick()` postfix
input injection. The game's own movement pipeline now handles physics, colliders, animations,
triggers, party AI, and terrain — all naturally.

**What was done:**
1. **GetLeftStick postfix** (NavigationHandler.Patches.cs): Harmony postfix on
   `GameInputManager.GetLeftStick()` overrides stick input with synthetic direction
   toward current waypoint. `WorldDirToCameraStick()` converts world-space direction
   to camera-relative stick coordinates.
2. **Removed old workarounds:** PlayMoveAnimation prefix, CacheEventTriggers/CheckEventTriggers,
   TryEnterFieldExit, InteractDist snapping, manual transform.rotation, Y interpolation,
   _staticIsApproaching field, CachedEventTrigger struct, DirectWalkMaxDistance constant.
3. **Counter NPC detection fix** (NavigationHandler.Build.cs): NPCs with contactDistance >= 1.0
   are now flagged as counter NPCs (skip reachability filter, use partial path). This fixes
   the castle receptionist (WARRIOR1b, contactDistance=1.50, type=NORMAL) disappearing
   from the nav list.
4. **Pre-walk path validation** (NavigationHandler.AutoWalk.cs): Before walking, SphereCast
   validates every segment of the NavMesh path against actual physics colliders. If a segment
   is blocked, a temporary NavMeshObstacle is placed at the midpoint and the path is
   recalculated (up to 4 attempts). All obstacles stay during retries so each recalculation
   routes around ALL found barriers. Obstacles are destroyed after path is accepted.
5. **WaypointArrivalThreshold** increased from 0.3 to 0.8 for physics-based movement.
6. **World map unchanged** — still uses transform.position (different physics model).

**Files modified:**
- `NavigationHandler.Patches.cs` — GetLeftStick postfix, removed PlayMoveAnimation prefix
- `NavigationHandler.AutoWalk.cs` — WorldDirToCameraStick(), path validation with SphereCast,
  StopAutoWalk() helper, removed CacheEventTriggers/CheckEventTriggers/TryEnterFieldExit
- `NavigationHandler.cs` — Update() uses stick injection, simplified arrival, new ApplyPatches
- `NavigationHandler.Build.cs` — contactDistance-based counter NPC detection

**Pending tests (user will test 2026-03-11):**
- [ ] Basic NPC auto-walk (run toward NPC, proper animation/footsteps)
- [ ] Wall collision (player stops at walls, doesn't clip through)
- [ ] Door interaction (stops at closed doors like Krosse Castle guard gate)
- [ ] Event triggers fire naturally (story triggers, PA triggers)
- [ ] Map exits trigger naturally (building entrances, town gates)
- [ ] Counter NPCs (receptionist appears in list, walks to counter edge)
- [ ] Path validation rerouting (Krosse town → castle should find clear path)
- [ ] Moving NPCs (path recalculation, arrival)
- [ ] Stuck detection still works
- [ ] Party members follow naturally
- [ ] World map auto-walk still works
- [ ] Cancel auto-walk (NumPad 5 / L1)
- [ ] Gamepad auto-walk (L1 + LStick)

**Known issue from first test:**
- King/Soldier in Krosse Castle unreachable — this is correct behavior (guard blocks
  corridor until receptionist grants audience). Not a bug.
- Krosse town → castle path initially went through a dead-end area where NavMesh and
  game colliders disagreed. Path validation (SphereCast + NavMeshObstacle rerouting)
  was added to fix this. Needs re-testing.

### SphereCast Removal (2026-03-15)

**Problem:** LayerMaskWall fix (2026-03-13) still caused widespread false "Cannot reach" errors.
Testing showed two types of colliders blocking valid paths:
- Layer 15 (`collider`) — invisible collision volumes throughout scenes, player walks through fine
- Layer 22 (`Col_Obstacle_Col*`) — named "obstacle" but not actually impassable

Both layers are included in GameRenderManager.LayerMaskWall but do not block player movement.
NavMesh paths are inherently walkable — SphereCast validation was redundant and harmful.

**Fix:** Removed SphereCast path validation entirely. CalculateAndStorePath now trusts the
NavMesh path directly. Stuck detection (2-second timer, recalculates from current position)
remains as the safety net for genuine obstacles encountered at runtime.

**Removed code:**
- `FindBlockedSegment()`, `GetSegmentMidpoint()`, `CreateTempNavMeshObstacle()` methods
- `MaxPathValidationAttempts`, `PathValidationRadius` constants
- `_wallLayerMask`, `_wallLayerMaskResolved`, `GetWallLayerMask()` from NavigationHandler.cs
- Wall mask cache reset in `CheckFieldmapChange()`

**Files modified:**
- `NavigationHandler.AutoWalk.cs` — simplified CalculateAndStorePath, removed validation methods
- `NavigationHandler.cs` — removed wall mask fields/method/reset

**Pending tests (user will test):**
- [ ] Auto-walk to nearby NPC — should no longer say "Cannot reach"
- [ ] Auto-walk to building entrances (Inn, Church, etc.) — should work
- [ ] Auto-walk to distant exits (Krosse Castle gate) — should work
- [ ] Stuck detection still triggers if player gets physically blocked
- [ ] Indoor areas still navigable
- [ ] Previous test checklist items from 2026-03-10 also still apply
- [ ] **NEW: Obstacle avoidance** — if auto-walk gets blocked (e.g. enemy in path), it should try walking around instead of giving up. Walk toward an enemy-blocked path to test.
- [ ] **NEW: Camera follow** — camera should gently rotate to face walking direction during auto-walk. If camera rotates the WRONG way (away from path), report it — sign flip needed.
- [ ] Camera follow should NOT affect world map auto-walk (world map has fixed camera)

## Codebase Analysis Progress

### GATE: Tier 1 MUST be complete before Phase 2 (Framework)!

- [x] 1.1 Structure overview (namespaces, singletons) → documented in game-api.md
- [x] 1.2 Input system — ALL game key bindings documented in game-api.md "Game Key Bindings"
- [x] 1.2 Input system — Safe mod keys identified and listed in game-api.md "Safe Mod Keys"
- [x] 1.3 UI system (base classes, text access patterns — TextMeshPro, UIPresenterBase)
- [x] 1.4 State management — singleton pattern, task-based input architecture documented
- [x] 1.5 Localization: English only — SKIPPED (single language)

### GATE: Relevant Tier 2 items MUST be done before implementing each feature!

- [ ] 1.6 Game mechanics (analyzed as needed per feature)
- [ ] 1.7 Status/feedback systems
- [ ] 1.8 Event system / Harmony patch points
- [ ] 1.9 Results documented in `docs/game-api.md`
- [ ] 1.10 Tutorial analysis (when relevant)

## Game Key Bindings (Original)

<!-- CRITICAL: Fill this during Tier 1 analysis! Every key the game uses.
Without this list, mod keys WILL conflict with game controls. -->

- (not yet documented — MUST be done before Phase 2)

## Implemented Features

- **Config menu announcements** (`ConfigMenuHandler.cs`)
  - "Config, N of Total: Category" when the config category menu opens or focus moves
  - "[Setting]: [Value], N of Total" when a submenu opens or focus moves
  - "[Value]" announced alone when left/right adjusts a setting value
  - Unavailable/button-only items announced without value
  - Label source: Strategy 2 — GameText inside each selector's own hierarchy (not sibling walk)

- **Title menu announcements** (`TitleMenuHandler.cs`)
  - "Press any button to start" when the title screen appears
  - "[Item]" when the menu opens or focus moves (no prefix)
  - Adds ", unavailable" for greyed-out items (e.g. Load Game with no save)

- **Gamepad binding menu announcements** (`GamepadMenuHandler.cs`)
  - "[Action]: [Button], N of Total" on navigation up/down
  - "[Action]: unassigned, N of Total" if no button assigned
  - "Press a button to assign." when confirm pressed on an action
  - Re-announces current item after button assignment
  - Button names read from `icon` GameText (sprite tag stripped of controller-type prefix)
  - Handles PS4/PS5/Xbox/Switch/PC controller types automatically

- **Keyboard binding menu announcements** (`KeyboardMenuHandler.cs`)
  - "[Action]: [Key], N of Total" on navigation up/down
  - "[Action]: unassigned, N of Total" if no key assigned
  - "Press a key to assign." when confirm pressed on an action
  - Re-announces current item after left/right category change

- **Load game menu** (`LoadGameHandler.cs`)
  - "Load game." announced when the screen opens
  - "[Slot label]. [Hero], Level [N], [Difficulty]. [Location]. Play time: [time]. [N] of [total]." on navigation
  - "Auto save. ..." for auto-save slots
  - "[Slot label]. Empty. [N] of [total]." for empty slots
  - Data read from UISaveLoadListItemData fields (pre-formatted strings from the game)

- **NPC/story dialogue** (`DialogueHandler.cs`)
  - "[Name]: [text]" when a dialogue line appears (NPC has a name shown)
  - "[text]" when no speaker name is present (narration, anonymous lines)
  - Fires on each new page as player advances through dialogue
  - Hooks: UIConversationPresenter.SetMessage(message, talkerName, voiceID, isWait, ref Rect)
  - TMP markup tags stripped before announcing

- **Tutorial boxes** (`NotificationHandler.cs`) ✓ TESTED
  - "Tutorial. [title]. [description]. Controls: [operation]" on each tutorial page
  - Button sprite tags converted to readable names (e.g. `<sprite name=PS4_Cross>` → "Cross")
  - Operation/controls text from data.operation field appended when present
  - Hooks: UITutorialInformationPresenter.SetInformation(UITutorialInformationData)

- **Dialog popups** (`NotificationHandler.cs`)
  - Yes/no and OK dialogs: "[question] [initial choice]" on open, "[choice]" on navigation
  - Description popups (e.g. acquired battle art) announced as "[name]. [description]"
  - Hooks: UIDialogPresenter.Setup + UIDialogPresenter.SelectChoices + UIDialogWindow.SetupDescription
  - Setup and SelectChoices coordinated via flag to avoid SelectChoices cutting off the question

- **New game settings screen** (`NewGameSettingsHandler.cs`)
  - "New game settings." on screen open
  - "[Label]: [Value]" on up/down navigation (e.g. "Difficulty: Galaxy")
  - New value announced alone on left/right change
  - "Editing name. Type your name and press Enter." when Name row is confirmed
  - Fallback labels if presenter text is empty
  - Hooks: UITitleSelectVoiceSelector.Show, OnUp, OnDown, UpdateCurrentPresenter, OnDecision

- **Protagonist selection screen** (`HeroSelectHandler.cs`)
  - "Protagonist selection." when the screen opens
  - "[Name]. [Description]" on open (initial focus) and left/right navigation
  - Description text read from heroDescription GameText field
  - Hooks: UITitleSelectHeroSelector.Show + UITitleSelectHeroSelector.OnSelected

- **Camp menu root announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - "Camp menu." when the camp opens
  - "[Item], N of total." when navigating the root menu (Status, Item, Equip, BattleSkill, Formation, etc.)
  - Greyed-out items announced with ", unavailable"
  - Re-announces current item when returning from a sub-screen
  - Root menu type: UICampMenuSelector (field menuSelector on UICampWindow)
  - Item data: UICampMenuItemData.menuItem (UIDefine.CampMenuItem enum), canDecisioned (availability)
  - Approach: polling currentIndex from Main.UpdateHandlers() — navigation is native-only, no Harmony hook fires
  - Item names currently use enum.ToString() (e.g. "BattleSkill") — can be refined with Loc entries

- **Camp item sub-screen announcements** (`CampMenuHandler.Items.cs`) ✓ TESTED — UPDATED
  - Now reads: Name x[quantity]. Effect. Description. Factor info. Position.
  - Effect text from UIItemInformationData.itemEffectInformation (what the item actually does)
  - Factor name + description for crafted/enhanced items
  - Quantity shown as "x5" instead of bare number
  - Double period fix: AppendSentence strips trailing periods from game text
  - Hook: UIItemInformationPresenter.Set caches effect/factor data for polling

- **Post-battle result announcements** (`BattleResultHandler.cs`) ✓ RETESTED (2026-03-21)
  - Announces SP and BSP totals after EXP/Fol
  - Level-ups include per-character BSP gained and learned battle skills
  - Learned skills now announce with description: "Learned Fire Bolt: Unleashes a fiery projectile."
    (via UICommon.CreateBattleSkillInformationData; falls back to name-only if description unavailable)
  - Bonus announcements: chain bonus (after totals), per-character Training and Open Eyes bonuses ✓ TESTED
  - Skill names resolved via ParameterManager → TextManager chain

- **Battle target announcements** (`BattleTargetHandler.cs`) ✓ TESTED
  - Hold L2 to enter target change mode; announces current enemy info
  - Cycles targets with directional input; each new target announced
  - Single-enemy battles: L2 re-reads current target's info (TargetChangeMode state detection)
  - Announces: name, HP %, shield %, leader type, active buffs/debuffs
  - HP shown as exact values if Spectacles item used on that enemy (IsSeeThroughEnemy check)
  - Enemy names resolved via ConstEnemyParameter.charaNameID → ParseCharaNameID fallback
  - Duplicate enemy names numbered (e.g. "Lizardaxe 1", "Lizardaxe 2")
  - Detection: SetControlPlayerTarget hook (CallerCount 7) + polling as backup
  - Spectacles is the ONLY see-through mechanism (no Analyze spell in this game)
  - R2 ally switching: announces controlled ally name, HP, MP, buffs/debuffs ✓ TESTED
  - Polls controlPlayerIndex; ControlPlayerChangeMode (state 6) detects first R2 press
  - Index silently seeded at battle start to avoid unwanted announcement

- **Camp equip sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - Slot list reads category before item name: "Weapon: Swift sword, 1 of 7."
  - Empty slots read "Greaves: None, 5 of 7." instead of being silent
  - Fixed: item list detection now uses currentState instead of activeInHierarchy

- **Camp skills sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED

- **Save game screen detection** (`LoadGameHandler.cs`) ✓ TESTED

- **Shop menu announcements** (`ShopHandler.cs`) ✓ TESTED
  - "Shop." when shop opens, root menu reads Buy/Sell/Cancel with position
  - Item browsing reads name + Fol price + description + position (buy and sell modes)
  - Quantity selection reads count + total Fol on change
  - Item details: description, equipment category, non-zero stats (ATK/DEF/INT/STM/LCK/POW/GUTS/HIT/EVD/CRT), factor effects
  - Descriptions sourced from UIItemInformationPresenter.Set hook (game doesn't populate itemDescription on shop list data)
  - Equipment stats from ParameterManager.GetItemParameter(itemID), factors from GetFactorParameter/GetFactorMessage

- **Item acquisition popups** (`NotificationHandler.cs`) ✓ TESTED
  - Treasure chest and quest reward popups now read aloud
  - Announces the game's message text plus each item name and count
  - Hook: UIOverflowItemPresenter.SetItem (CallerCount 3, fires when popup is populated)

- **Battle dodge warning audio cue** (`BattleCounterHandler.cs`, `AudioCuePlayer.cs`) ✓ TESTED
  - Plays Dodge.wav when an enemy is about to hit the player (dodge warning)
  - Hook: BattleCharacter.DoAttackNotify postfix — the game's own visual flash trigger
  - Only fires when target.IsControlPlayer() — ignores attacks on party members
  - Audio: WAV loaded from UserData/SO2RAccess/Sounds/Dodge.wav via winmm.dll (unmanaged memory)
  - Settings: ModSettings.DodgeSoundEnabled (on/off) and DodgeSoundVolume (0.0-1.0, default 0.8)
  - Volume-adjusted WAV cached in unmanaged memory, rebuilt only when volume setting changes
  - Refactored: shared TryParseWav() and ScalePcmSamples() helpers (dodge + save sound use same code)

- **Enemy proximity audio cue** (`EnemyProximityHandler.cs`, `SpatialAudioPlayer.cs`) ✓ TESTED
  - Looping spatial WAV cue warns of nearby field enemies
  - Volume scales with distance: full at 3 units, silent at 25 units
  - Stereo panning based on enemy direction relative to player's facing
  - Tracks closest enemy only; scans every ~60 frames via FindObjectsOfType<FieldEnemy>()
  - Audio engine: waveOut API (winmm.dll) with double-buffered stereo output
  - Separate from AudioCuePlayer (no conflict with dodge warning)
  - WAV file loaded from disk (UserData/SO2RAccess/Sounds/Enemy_proximity.wav) — swappable
  - UserVolume property ready for future mod settings menu
  - Deadlock bug fixed: Stop() sets _playing=false before waveOutReset to prevent callback deadlock
  - WAVEHDR_FLAGS offset corrected (24, not 16 on x64)
  - Stops on: battle, camp, shop, scene change. Resumes automatically on field.

- **Game over (battle loss) menu** (`GameOverHandler.cs`) ✓ TESTED
  - "Game over." announced when the battle loss screen appears
  - "Retry, 1 of 2." / "Title, 2 of 2." as player navigates up/down
  - Polling-based (native navigation, same pattern as shop/camp)
  - FindObjectOfType<UIGameOverWindow> with IsOpened polling

- **Battle command menu (Triangle)** (`BattleMenuHandler.cs`) ✓ TESTED
  - "Battle menu." announced when menu opens via Triangle during battle
  - Root menu: "Items, 1 of 4.", "Spells, unavailable, 2 of 4.", "Strategy, 3 of 4.", "Escape, 4 of 4."
  - Items sub-menu: Recovery/Combat tabs, item name + count + effect description + position
    - Effect text read from itemInformationPresenter's GameText (direct UI read fallback)
    - Item count from ItemManager.GetItemCount(itemID)
  - Spells sub-menu: per-character tabs, spell name + MP cost + range + effect + position
    - Character name resolved via ParameterManager → TextManager chain
  - Target selection: enemy/ally targeting after skill/item pick
    - Enemy: name + HP% (or exact with Spectacles) + position; reuses BattleTargetHandler helpers
    - Ally: name + HP/MP + position; self-targeting detected (single entry)
    - AoE: "All enemies" / "All allies" announced once
  - Strategy/Tactics sub-menu: character list + operation selection
    - Character: name + current operation assignment + position
    - Operation: name read from UICommonListItemPresenter.textMesh + "Currently set" indicator + position
  - Phase detection: UIStackSelectorWindowBase.GetPeekSelector() (OpenBattleState does NOT change for sub-screens)
  - All selectors have activeInHierarchy=True permanently — peek-based detection required
  - 4 hooks: SpellInfoData, EffectRange, UseDescription, OperationInfo
  - Tactics operation selector also matched in IdentifyPhase (may be pushed onto stack separately)

- **Battle status announcements** (`BattleStatusHandler.cs`) — PARTIALLY TESTED (damage dealt + ally HP warnings confirmed working; ailments need more game progress)
  - Ally health below 50%: "[Name], health below 50 percent." (queued, non-interrupting) ✓ TESTED
  - Ally health below 25%: "[Name], health critical." (queued) ✓ TESTED
  - Ally knocked out: "[Name], knocked out." (queued) ✓ TESTED
  - Ally negative status ailment: "[Name], [ailment]." (queued, e.g. "Claude, Poison.") — PENDING (needs more game progress)
  - Player damage dealt: "[N] damage." per hit by the controlled character (queued) ✓ TESTED
  - HP threshold tracking: only announces downward transitions (not on healing)
  - Ailment tracking: per-ally set, cleared on removal so re-application announces again
  - Hooks: BattleCharacter.DoCollisionReceiveAction (CallerCount 2, prefix+postfix), CharacterParameter.SetBuffDebuffState (CallerCount 19, postfix)
  - CRASH FIX: original DoDamage hook used ref DamageResult (IL2CPP value type) which corrupted Harmony trampolines. Replaced with DoCollisionReceiveAction — attacker obtained via attackCollision.OwnerCharacter.
  - All 3 features toggled independently in mod settings menu (F4 / L1+L3)
  - Settings: AllyHealthWarningEnabled, AllyStatusAilmentEnabled, PlayerDamageDealtEnabled (all default On)

- **Battle pause menu** (`BattlePauseHandler.cs`) ✓ TESTED
  - "Battle status." announced when pause menu opens (Start/Options during battle)
  - Tiered info system: basic (auto), weaknesses, resistances, status, equipment, cooking, music, leader
  - Keyboard: NumPad 8/2 tier cycling, NumPad 4/6 character cycling
  - Gamepad: R1/L1 tier cycling, D-pad native character cycling (all directions, polling announces)
  - Allies: "Name. HP X of Y. MP X of Y. N of Total."
  - Enemies: "Name. HP X of Y." (with Spectacles) or "HP unknown." (without)
  - Empty tiers auto-skipped; tier resets on character change
  - Hooks: SetHp, SetMp, SetElemental, SetAllBuffList, SetTargetName on UIBattlePauseCharacterPresenter
  - HP/MP read directly from CharacterParameter (not hook caches — fixes timing bug)
  - Ally name: ParameterManager chain (BattlePlayerParameter → charaNameID → TextManager) as primary
  - RefreshPauseUI called synchronously before BuildTiers (fixes stale cache)
  - Buff categorization via icon sprite matching (GetIconSprite → category map)
  - BattleTargetHandler helpers reused (enemy names, spectacles, status conditions)
  - Bugs fixed (2026-03-04):
    - HP/MP all zeros: direct CharacterParameter reads instead of hook caches
    - Ally name empty: ParameterManager chain resolves "Claude" via charaNameID
    - D-pad conflict: game uses ALL D-pad directions for character cycling natively;
      tier cycling moved to L1/R1 shoulder buttons (free during pause)

- **Camp status talents sub-screen** (`CampMenuHandler.cs`) ✓ TESTED
  - Hook: UITalentPresenter.Set(List<UITalentData>) — CallerCount(1)
  - Announces "Talents." heading + comma-separated talent names
  - Hook fires on status open (page 0), data CACHED; announced when pageIndex changes to 1
  - If character changes while on talent page, hook fires and announces immediately
  - Stats announcement gated to page 0 (prevents stats reading on talent page)

- **Location discovery notifications** (`NotificationHandler.cs`) ✓ TESTED
  - Hook: UIFieldLocationPointPresenter.Set(string name, string description) — CallerCount(1)
  - Announces "Discovered [name]. [description]" when a location marker popup appears
  - Rewards now handled separately by stacked field notification queue (no longer inline)

- **Map name announcement on area change** (`NavigationHandler.cs`) ✓ TESTED
  - Polls FieldManager.Instance.currentFieldmapID each frame
  - When fieldmap changes, resolves name via ParameterManager/TextManager and announces
  - Skips first detection (game load) to avoid announcing on initial scene
  - Reuses existing ResolveMapName logic (overrides, game data, fallback)

- **Stacked field reward notifications** (`NotificationHandler.cs`) ✓ TESTED
  - Hook: UIFieldInformationStackSelector.ShowInformation — CallerCount(15)
  - Queues rapid-fire notifications (EXP, Fol, items, level-ups, talents, etc.)
  - Announces all queued messages combined after 0.5s delay to prevent interruption
  - Supports item-style notifications with getText/count/unit fields

- **Reward announcements for managed-code rewards** (`NotificationHandler.cs`)
  - Hook: GameManager.GiveRewardWithWindow — CallerCount(6)
  - Announces EXP/Fol/SP/BP/items when rewards given via managed code (missions, etc.)
  - Does NOT fire for location point rewards (native-only flow)

- **Dialogue choice menus** (`DialogueChoiceHandler.cs`) ✓ TESTED
  - Choice menus during private actions, story events, and dialogue sequences now announced
  - Hook: UISelectChoiceSelector.ShowSelectChoiceMessage (CallerCount 5) — captures menu open
  - Announces prompt/title text + initial choice with position on open
  - Polling: selectChoiceIndex tracked each frame (native-only navigation, no Harmony hooks fire)
  - Navigation announces current choice text + "N of total" position
  - Choice text read from UISelectChoicePresenter.choicePresenterList[i].message.text
  - Deactivates when presenter goes inactive (choice confirmed or cancelled)

- **Save notification and audio cue** (`SaveNotificationHandler.cs`, `AudioCuePlayer.cs`, `ModSettings.cs`) ✓ TESTED
  - Hook: UIDialogWindow.SetupAutoSaveAnnounce (CallerCount 2) — reads new game save notification dialog
  - Hook: GameSaveManager.Save prefix (CallerCount 3) — detects manual save start
  - Hook: GameSaveManager.OnSaveSuccess postfix (CallerCount 1) — detects save completion
  - Polling: GameSaveManager.IsSaving() as backup (auto-saves)
  - Audio: plays Save_sound.wav from UserData/SO2RAccess/Sounds/ via winmm.dll (unmanaged memory)
  - Settings: ModSettings.SaveSoundEnabled (on/off) and SaveSoundVolume (0.0-1.0, default 0.5)
  - Settings persisted to UserData/SO2RAccess/settings.json (created automatically)
  - Ready for future mod settings menu integration

- **Fol readout** (`Main.cs`)
  - F3 (keyboard) or L1+R3 (gamepad) announces current Fol
  - Uses EventManager.Instance.GetMoney() to retrieve current money
  - Works anywhere in the game (field, menus, battle)

## In-Progress / Pending Test

- **Camp status sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - Hook-driven detection: both activeInHierarchy and root-menu-hidden approaches failed;
    now uses UICampStatusSelector.UpdatePresenter hook as trigger

- **Navigation key remap + gamepad support** (`NavigationHandler.cs`, `Main.cs`) ✓ TESTED
  - Keyboard: NumPad 5 open/close, NumPad 1 auto-walk/cancel, F5 no longer used
  - Gamepad: L1 hold opens nav, D-pad Up/Down=category, Left/Right=items, LStick up=auto-walk
  - L1 suppresses D-pad directions + ShortCut actions + FieldCameraLeft while held in field
  - L1 does not activate in camp menu, battle, or dialogue (IsFieldFree check)
  - D-pad auto-repeat while held (400ms initial, 150ms interval)
  - Steam Input must be disabled for gamepad detection to work
  - Bug fixed: field shortcuts (Quick Heal) now blocked — ShortCut actions (39-42) added to suppression list

- **Navigation Events category** (`NavigationHandler.cs`) ✓ TESTED
  - New "Events" category added to nav list (5th category after Markers)
  - Scans FieldEventCollision objects, filtered by IsEventActivate() (only active triggers shown)
  - Classified as "Story event", "Private action", or "Side event" (generic events dropped — no content)
  - PAs and sub-events with isDisableIcon=true skipped (game hides them)
  - Side events annotated with hints: "(reward)", "(battle)", or "(reward, battle)" when applicable
  - Plain "Side event" (no hint) = needs user testing to determine relevance
  - Numbered by label type in distance order (e.g. "Story event 1", "Side event (reward) 2")
  - NavMesh reachability filter applied; static transforms (LiveTransform = null)
  - PA NPCs (code names starting with `pa_`) also listed under Events as "Private action (Name)"
  - Name parsed from code name last segment (not dialogue-derived, which can be wrong speaker)

- **Navigation Enemies category** (`NavigationHandler.cs`) — TESTED, WORKING
  - New "Enemies" category added to nav list (7th category)
  - Uses FindObjectsOfType<FieldEnemy>() to scan field enemy symbols
  - Enemies excluded from NPC category via TryCast<FieldEnemy>() filter
  - Name resolution: EncountID → encounter params → partyID → enemy params → charaNameID
  - TextManager doesn't resolve enemy names on field (not loaded); falls back to parsed charaNameID
  - e.g. CHARA_LIZARDAXE → "Lizardaxe", shown as "Lizardaxe, medium 1"
  - Difficulty from EnemySymbolType: weak, medium, strong, raid
  - Sorted by distance, NavMesh reachability filtered, duplicate labels numbered
  - Live transform tracking for auto-walk to enemies

- **Navigation Save Points category** (`NavigationHandler.cs`) ✓ TESTED
  - New "Save Points" category added to nav list (6th category)
  - Uses FieldManager.Instance.FieldSavePointList (game-managed list)
  - Labels: "Save point" or "Recovery save point" based on IsRecovery property
  - Numbered when multiples of the same type exist (e.g. "Save point 1", "Recovery save point 2")
  - NavMesh reachability filter applied; live transform tracking for auto-walk

- **Navigation Stairs category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Stairs" category added to nav list (8th category)
  - Uses FieldManager.Instance.FieldStairsList (game-managed list)
  - Labels: "Stairs up" or "Stairs down" based on isUpperStage property
  - Numbered when multiples of the same direction exist (e.g. "Stairs up 1", "Stairs down 2")
  - NavMesh reachability filter applied; static transforms (LiveTransform = null)

- **Navigation Doors category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Doors" category added to nav list (9th category)
  - Uses FieldManager.Instance.FieldDoorList, filtered to StoneDoor type only
  - Labels: "Stone door, open" or "Stone door, closed" based on doorState
  - AutoDoor and Default door types excluded (auto-doors are ambient, not useful as nav targets)
  - Numbered when multiples of same state exist
  - NavMesh reachability filter applied; static transforms

- **Navigation Warp Points category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Warp Points" category added to nav list (10th category)
  - Source: FieldManager.Instance.FieldGimmickManager.FieldGimmickList
  - Identifies 3 gimmick types via TryCast:
    - FieldGimmick09 (warp panels) → "Warp panel"
    - FieldGimmick17 (magic circles) → "Magic circle" (filtered by IsEnable + not disabled)
    - FieldGimmick03 (moving platforms) → "Platform"
  - Each type numbered separately if multiples exist
  - NavMesh reachability filter applied; static transforms

- **Enhance menu sub-screen announcements** (`CampMenuHandler.BattleSkill.cs`, `CampMenuHandler.Formation.cs`) ✓ TESTED
  - Camp → Enhance shows 3 sub-items: Skill, CombatPoint, BattleSkillPoint
  - Gate checks expanded from "BattleSkill" to also accept "BattleSkillPoint" and "CombatPoint"
  - Hook-based deferred detection: activeInHierarchy always true, so heading + inner selector
    caching deferred to UIBattleSkillInformationPresenter.Set hook on first fire
  - Combat skills (CombatPoint): BP balance/cost per skill, max level indicator, toggle mode (Square)
  - Battle skills (BattleSkillPoint): BP balance/cost per skill, max level indicator
  - Skills (Skill): SP balance/cost per skill, max level indicator
  - Balance shown per-skill as "BP: 28 / 5" or "SP: 100 / 5" (not on heading)
  - Toggle mode (Square button): announces "Toggle mode", skill active/inactive status on navigate and confirm
  - Double punctuation fix: AppendSentence helper strips trailing periods from game text

- **Camp formation sub-screen announcements** (`CampMenuHandler.cs`) — NOT TESTED (needs more party members)

- **Camp operations child screens** (`CampMenuHandler.cs`)
  - Operations root menu reads its items (Formation, Party Formation, Assist Formation, Tactics) ✓
  - Formation: announces formation name, effect, sphere count, bonus details ✓ TESTED
  - Party Formation: cursor tracking via cursorTarget position matching, per-slot data from SetData hook ✓ TESTED
  - Assist Formation: polls UICampAssistSettingSelector (Equip slots + character picker) — NOT TESTED (needs more party members)
  - Tactics: polls UICampOperationSelector (character + operation states), hook for operation info ✓ TESTED

- **Equipment Wizard handler** (`EquipWizardHandler.cs`) — PENDING TEST (needs equipment wizard trigger)
  - New polling handler: FindObjectOfType<UISystemWindow>, polls IsShowingEquipWizard
  - Announces heading + description text + equipment comparison (old → new for changed slots)
  - Yes/No/Reject All menu navigation with position
  - Tracks equipWizardDataIndex for multi-character wizard advances
  - Loc keys added: equip_wizard_heading, equip_wizard_change, equip_wizard_position, menu options

- **First-item fix across all camp menus** (2026-03-07) — PENDING TEST
  - Root cause: Harmony hooks fire during game's Update, but polling flags gating them are set in
    OnLateUpdate — always one frame too late for the first item in any list.
  - Fix: replaced stale polling flags with live game state reads or removed redundant gates.
  - Files changed: CampMenuHandler.Equip.cs, CampMenuHandler.Formation.cs,
    CampMenuHandler.BattleSkill.cs, CampMenuHandler.Party.cs
  - Equip first item confirmed working by user. Other menus pending test.

- **Double-period fix** (2026-03-07) — PENDING TEST
  - Game text fields (item names, descriptions, skill names) often end with periods.
    Manual `.Append(". ")` created double periods. Fixed by using AppendSentence() helper
    which strips trailing periods before appending ". ".
  - Files changed: CampMenuHandler.Equip.cs, CampMenuHandler.Party.cs,
    CampMenuHandler.Formation.cs, CampMenuHandler.BattleSkill.cs
  - Equip item names confirmed fixed by user. Other menus pending test.

## In-Progress Features

- **Field Navigation — Phase 2 (audio list + auto-run)** (`NavigationHandler.cs`) ✓ COMPLETE AND TESTED
  - F5: open/close navigation list; also cancels auto-run if active
  - NumPad 8/2: navigate up/down within category
  - NumPad 4/6: switch category (NPCs, Chests, Exits, Markers, Events, Save Points, Enemies, Stairs, Doors, Warp Points, Locations)
  - NumPad 5: auto-run to selected item; press again to stop following
  - Items sorted by distance (closest first) within each category
  - Party members filtered (dist < 2 units)
  - NPC names: parsed from ConstNpcParameter code name; functional NPCs qualified e.g. "Equipment shop (Hahn)"
  - Chests: numbered by type in distance order (Unopened chest 1, Unopened chest 2 etc.)
  - Exits: labelled by icon type + game map name (e.g. "Building entrance to Arlia Village", "Town gate to Overworld")
  - **NavMesh pathfinding** (Phase 2.5): auto-walk uses NavMesh.CalculatePath() for wall-respecting paths instead of straight-line movement. Unreachable targets filtered from nav list on F5. Path recalculates every 1.5s for moving NPCs. Terrain height followed via waypoint Y interpolation. Falls back gracefully if no NavMesh in scene.
  - Auto-run: NavMesh waypoint-following at player's actual run speed (GetMoveSpeed(true) = 6.5); live transform tracking so wandering NPCs are followed
  - Run animation + footsteps: Harmony prefix blocks game from resetting Run to Unique/Idle each frame; PlayMoveAnimation(Run) called once at start
  - Auto-run NPC arrival: proximity-lock mode — player held 1 unit from NPC, facing them, until NumPad 5 pressed
  - Auto-run static arrival (exits, markers): stops and announces "Arrived"
  - Scene change cancels auto-run silently

## Pending Tests (Camp Item Sub-screen — updated format)

- [x] Camp item screen: "Items." announced when opening item screen ✓
- [x] Camp item screen: quantity reads as "x5" (not bare number) ✓
- [x] Camp item screen: effect text reads (e.g., "Restores a small amount of HP") ✓
- [x] Camp item screen: description reads after effect ✓
- [x] Camp item screen: no double period at end of description ✓
- [x] Camp item screen: factor info reads for crafted/enhanced items (if available) ✓
- [x] Camp item screen: returning to root menu re-announces root item ✓
- [x] Camp item screen: no stale announcement on camp re-open ✓

## Completed Tests (Camp Menu Root)

- [x] Camp menu: "Camp menu." announced when opening the menu ✓
- [x] Camp menu: "[Item name], N of total." announced on up/down navigation ✓
- [x] Camp menu: position count is correct ✓



- [x] Title menu navigation — all passing ✓
- [x] Config menu categories — all passing ✓
- [x] Config submenu settings (sliders + options) — all passing ✓
- [x] Keyboard menu: action names read correctly ✓
- [x] Keyboard menu: "Press a key to assign." announced on confirm ✓
- [x] Keyboard menu: assigned key name reads correctly ✓
- [x] Hero select: "Protagonist selection." announced on screen open ✓
- [x] Hero select: "Claude. [description]" announced on open (default selection) ✓
- [x] Hero select: "Rena. [description]" announced when navigating to Rena ✓
- [x] Hero select: description text is meaningful (not empty) ✓
- [x] New game settings: "New game settings." announced on open ✓
- [x] New game settings: "[Label]: [Value]" on up/down navigation ✓
- [x] New game settings: new value announced on left/right change ✓
- [x] New game settings: "Editing name." announced when Name row confirmed ✓
- [x] Gamepad menu: button assignments read correctly for all actions ✓
- [x] Gamepad menu: "Press a button to assign." announced on confirm ✓
- [x] Gamepad menu: updated button announced after assignment ✓
- [x] Load game: "Load game." announced on screen open ✓
- [x] Load game: slot details announced on navigation ✓
- [x] Load game: empty slots announced correctly ✓
- [x] NPC dialogue: text reads on each new line ✓
- [x] NPC dialogue: speaker name prepended when present ✓
- [x] Tutorial boxes: title and description announced ✓
- [x] Tutorial boxes: each page announces on navigation ✓
- [x] Description popup (Phase Gun Art): name and description announced ✓
- [x] Yes/no dialogs: question + initial choice announced on open ✓
- [x] Yes/no dialogs: navigating between options reads each option ✓
- [x] Navigation Phase 1: F5 scan announces NPC/chest/exit/marker counts ✓
- [x] Navigation Phase 1: NPC type labels resolve (Item shop, Innkeeper, NPC etc.) ✓
- [x] Navigation Phase 1: Chest opened/unopened status correct ✓
- [x] Navigation Phase 1: Exit destination map codes visible in debug log ✓
- [x] Navigation Phase 1: Distances plausible ✓
- [x] Navigation Phase 2: list opens/closes with F5 ✓
- [x] Navigation Phase 2: NumPad 8/2/4/6 navigation works ✓
- [x] Navigation Phase 2: NPC names parsed from code (Girl 1, Grandfather 2 etc.) ✓
- [x] Navigation Phase 2: Chests numbered by type and distance ✓
- [x] Navigation Phase 2: Exits show destination code suffix ✓
- [x] Navigation Phase 2: Auto-walk reaches stationary NPCs, player faces them ✓
- [x] Navigation Phase 2: Proximity-lock keeps player next to wandering NPCs ✓
- [x] Navigation Phase 2: NPC nav-list name matches dialogue name ✓ (shop NPCs shown as "Equipment shop (Hahn)" etc.)
- [x] Navigation Phase 2: Auto-run has run animation and footstep sounds ✓

## Completed Tests (Camp Status Sub-screen)

- [x] No false HP/MP announcement on camp root open (old stale bug fixed) ✓
- [x] "Status." announced when opening the status screen ✓
- [x] Character stats announced ✓
- [x] Age announced on page 0 ✓
- [x] Elemental affinities announced (or "No elemental affinities") ✓
- [x] Friendship levels announced with correct character names ✓
- [x] Up/Down virtual cursor navigates individual stat lines ✓
- [x] No stale announcements on camp reopen or character/page switch ✓
- [ ] Favorite food: only displays after food is discovered in-game (untested — needs gameplay progress)

## Pending Tests (Navigation Improvements — 2026-03-08)

- [ ] Field stuck detection: auto-walk into a corner or dead-end, verify it cancels after ~4s with "Path blocked" announcement
- [ ] Field stuck detection: normal auto-walk to NPC/chest still works (no false stuck triggers)
- [ ] Linecast filter: open nav list on a map with walls, check F12 debug log for "linecast blocked" messages
- [ ] Linecast filter: all expected NPCs/chests/exits still appear (no false removals)
- [ ] Floor labels: open nav list on a multi-floor map (e.g. inn), items on other floors show "(above)" or "(below)"
- [ ] Floor labels: items on the same floor have no suffix
- [ ] Regression: auto-walk to NPCs, chests, exits, counter NPCs all still work normally

## Pending Tests (Camp Formation Sub-screen)

- [ ] Not yet testable — area inaccessible in current game progress

## Pending Tests (Operations Child Screens — need more party members)

- [ ] Operations → Formation: announces formation name + effect on navigation
- [x] Operations → Party Formation: announces character name, level, HP/MP, role, position on navigation
- [ ] Operations → Assist Formation (Equip): announces button slot + assigned character/skill
- [ ] Operations → Assist Formation (Character picker): announces character names
- [x] Operations → Tactics (character list): announces character + current tactic ✓
- [ ] Operations → Tactics (operation picker): announces operation name + description

## Dialogue Voice Mode Toggle — TESTED (2026-03-07)

Voice detection fix: replaced broken PlayVoice Harmony hook (native IL2CPP calls
bypass managed stubs) with polling UIConversationSelector.currentVoiceController.IsPlaying().

- [x] F2 toggles dialogue voice mode
- [x] NameOnlyWhenVoiced: voiced cutscene lines announce speaker name only
- [x] NameOnlyWhenVoiced: unvoiced lines read full text
- [x] AlwaysReadFull: all lines read name + text regardless

## Pending Tests (Battle Status Announcements) — ALL TESTED (2026-03-05)

- [x] Enter battle, take damage until ally drops below 50% HP — hear "[Name], health below 50 percent." ✓
- [x] Continue taking damage below 25% — hear "[Name], health critical." ✓
- [x] Ally gets knocked out — hear "[Name], knocked out." ✓
- [x] Ally healed above 50%, then damaged below 50% again — hear warning again (threshold resets on heal) ✓
- [x] Ally gets poisoned or paralyzed — hear "[Name], Poison." or "[Name], Paralyze." ✓
- [x] Same ailment re-applied after wearing off — hear announcement again ✓
- [x] Attack an enemy as the player character — hear "[N] damage." per hit ✓
- [x] Multi-hit combo — damage announcements queue without interrupting each other ✓
- [x] Open mod settings (F4), find "Ally health warnings" — toggle Off, verify no HP warnings in battle ✓
- [x] Toggle "Ally status ailments" Off — verify no ailment announcements ✓
- [x] Toggle "Player damage dealt" Off — verify no damage numbers announced ✓
- [x] All three settings persist in settings.json after game restart ✓

## Pending Tests (Mod Settings Menu) — MOSTLY TESTED (2026-03-05)

- [x] F4 opens menu, hear "Mod settings menu. Save sound: On. Item 1 of 10." ✓ (10 items, not 7 — 3 battle settings added)
- [x] Up/Down arrow keys navigate items, hear label, value, and position ✓ (tested via gamepad)
- [x] Left/Right on toggle item flips On/Off ✓
- [x] Left/Right on volume item changes by 10% (0% to 100%) ✓
- [x] Left/Right on dialogue mode cycles Full text / Name only when voiced ✓
- [x] Escape or F4 again closes menu, hear "Settings saved. Menu closed." ✓ (tested via gamepad B button)
- [x] Gamepad: L1+L3 opens menu ✓
- [x] Gamepad: D-pad Up/Down navigates, D-pad Left/Right changes values ✓
- [x] Gamepad: Circle/B closes menu ✓
- [x] Settings persist after closing and reopening menu ✓
- [x] Settings persist in settings.json after game restart ✓
- [x] Nav overlay does NOT activate while mod menu is open ✓ (nav opened before menu, menu took over)
- [x] Game input is blocked while mod menu is open (no character movement, no other menus) ✓ (fixed: SuppressAllGameInput flag on GameInputManager hooks)

## Known Issues / Future Work

- **Bug: Enemy proximity sound ignores mod settings** — FIXED & CONFIRMED (2026-03-05):
  Fix: added enabled check + per-frame volume sync. Changed sound to Enemynearby.wav. Tested working.

- **Bug: Game crashes when player uses a battle skill** — FIXED (2026-03-05):
  DoDamage hook had `ref DamageResult` parameter — DamageResult is an IL2CPP value type
  (`sealed class : Il2CppSystem.ValueType`) which corrupted Harmony's trampoline marshaling.
  Fix: replaced with DoCollisionReceiveAction hook (CallerCount 2, no ref value types).
  Attacker obtained via `attackCollision.OwnerCharacter` instead of direct parameter.
  **Rule: NEVER hook IL2CPP methods with `ref` value type parameters (extends Il2CppSystem.ValueType).**

- **Bug: Battle skill menu triggers stale announcement on next camp open** — FIXED:
  All sub-screen gates now preserve their `_xxxWasActive` and `_xxxLastIndex` state when
  the root menu cursor moves away, preventing stale re-activation announcements.
  Same fix also resolved stale item announcements on shop open and camp menu scrolling.

- **Bug: Equip screen missing category name when slot is empty** — FIXED:
  Slot list now reads category before item name (e.g. "Weapon: Swift sword").
  Empty slots read "Category: None". Root cause was _equipItemListActive using
  activeInHierarchy (always true) — slot polling never ran. Fixed by using
  UICampEquipSelector.currentState (State.EquipType vs State.Item) instead.

- **Camp status detection** — FIXED: Both activeInHierarchy and root-menu-hidden detection
  failed (root menu selector also stays activeInHierarchy=true in sub-screens). Now fully
  hook-driven via UICampStatusSelector.UpdatePresenter (fires on open + character tab change).

- **Bug: Auto-walk arrival interrupts tutorial/notification speech** — FIXED:
  AnnounceArrival() checks if something was spoken in the last 0.5s via
  ScreenReader.GetRecentMessage(). If so, combines arrival first + interrupted
  message second into one announcement so the user hears both.

- **Bug: L1 nav blocked after camp menu** — FIXED: Camp closure detection used
  gameObject.activeInHierarchy which stays true after camp closes. Changed to
  WindowComponent.IsOpened property which properly reflects open/closed state.

- **Bug: Nav menu opened during camp menu via L1** — FIXED: IsOpened returns false
  during the camp window's opening animation (~36ms), causing IsCampOpen to be
  cleared immediately. Added 1-second grace period after Open postfix fires.
  Also added IsCampOpen gate in Main.ProcessGamepad (L1 press) and
  IsFieldFree check in NavigationHandler.ToggleNavList (keyboard NumPad 5).

- **NPC functional role + name combining** — shop/inn/guild NPCs now shown as e.g.
  "Equipment shop (Hahn)". Needs more in-game testing as more NPCs are encountered.

- **Counter NPC NavMesh fix** — FIXED: Functional NPCs (shops, inns, guilds, collectors,
  facilities) behind counters were filtered out by the NavMesh reachability check because
  no walkable path exists through the counter. Now these NPC types skip the reachability
  filter. Auto-walk uses partial NavMesh path to walk the player to the counter, then
  announces arrival. Player faces the NPC and can press action to interact.

- **Map exit names** — FIXED: Now resolved from game data at runtime via
  ParameterManager.GetFieldParameter(fieldmapID).FieldmapNameID → TextManager.GetMessage().
  Buildings show real names instead of codes like "22A". Results cached per session.

- **Gamepad nav menu** — IMPLEMENTED AND TESTED. L1 hold-to-open with D-pad navigation.
  See Key Bindings (Mod) section above for full control scheme.

- **Auto-walk exit compass direction** — IMPLEMENTED AND TESTED (2026-03-07): When auto-walking
  to an exit-type target (Exits, Stairs, Doors, Warp Points), the arrival message now includes a
  camera-relative compass direction so the player knows which way to walk to pass through the exit.
  E.g. "Arrived at Building entrance to Arlia. Exit is to the North East." Directions are computed
  relative to the camera orientation (North = stick forward/up), not world axes.

- **World map navigation** — OVERHAUL IN PROGRESS (2026-03-22):
  - **Architecture (completed 2026-03-21):**
    - NavigationHandler.Worldmap.cs fully separated from field map logic (no shared Update code)
    - Movement: stick injection via GetPlayerControlStick postfix (GetLeftStick doesn't work on world map — native pipeline)
    - GetPlayerControlStick CallerCount(0) but Harmony patches still intercept native calls (proven pattern)
  - **WorldmapPathfinder.cs — REWRITTEN (2026-03-22), NEEDS TESTING:**
    - **Two-layer walkability system:**
      - Layer 1: FieldManager.CanMove(x, y) — game's baked 1m walkability grid (terrain, ocean, cliffs)
      - Layer 2: Physics.OverlapSphere on layers 22/23 — Col_Obstacle colliders projected onto grid
    - Both layers combined give complete obstacle knowledge at 1m resolution
    - Binary heap A* priority queue — handles 200K+ cell grids efficiently
    - Grid: 1m cells (Stride=1), 300-cell padding, max 800x800 dimension
    - Snap-to-walkable: 30 cell radius (locations like Krosse City sit on non-walkable cells)
    - Stuck detection: 2s interval, diagnostic logging of colliders at stuck position
  - **Key discoveries (2026-03-22):**
    - FieldManager.CanMove(x, y) — game's own walkability grid at 1m resolution!
      Uses WorldGridData.alightFlag. CallerCount(3), safe to call.
    - GetWorldGridDataGridPosition(ref Vector3) — world-to-grid conversion
    - GetWorldGridDataPosition(int x, int y) — grid-to-world conversion
    - IsExistWorldGridData() — checks if grid is loaded
    - Game grid cell size is exactly 1.0m in both X and Z
    - CanMove tracks terrain/ocean/cliffs but NOT Col_Obstacle colliders
    - Col_Obstacle (layers 22/23) DO block player on world map (unlike field maps)
    - Col_Obstacle colliders are NOT stored in any game data structure — only exist as live Unity physics objects
    - CalcHeight with different layer masks shows NO difference (obstacles invisible to all CalcHeight variants)
    - CalcHeight with ref tag returns "Untagged" or "Rock" — not useful for obstacle detection
    - WorldGridData fields: encountIDList, footstepType, continentID, survivalAreaID, alightFlag, fishingWaterPlaceID, locationID (NO obstacle data)
    - Previous CalcHeight-only approach failed because it couldn't see Col_Obstacle physical barriers
    - Previous OverlapSphere-only approach (without CanMove) blocked too many cells (5193 obstacles)
    - Combined approach (CanMove + OverlapSphere) is the correct architecture
  - **Other fixes completed (2026-03-21):**
    - IsFieldFree grace period: tolerates 10 frames of EventManager.IsRunning flicker at terrain transitions
    - CheckFloorChange: uses FieldManager.IsWorldmap() directly instead of _isWorldmap flag
    - _autoWalkDifferentFloor: distance guard prevents premature arrival on stairs (field maps)
    - Arrival radii: chests 1.3m, enemies 1.8m, locations 10m fallback via TryEnterWorldmapLocation
  - **Next steps (testing needed):**
    - Test Salva → Arlia (short distance, previously worked)
    - Test Salva → Krosse City (long distance, previously failed)
    - If Col_Obstacle blocking is too aggressive again (no path), may need to reduce padding or
      use ClosestPoint checks instead of bounding box projection for obstacle marking
    - If path found but character still gets stuck, investigate if specific Col_Obstacles don't
      actually block movement (some may be passable like on field maps)
    - Future: Psynard (flying mount) support
    - Future: use pathfinder for nav list reachability filtering

- **Floor change announcements** — IMPLEMENTED AND TESTED (2026-03-07):
  - Polls player Y position each frame in CheckFloorChange()
  - Announces "Went upstairs." / "Went downstairs." when Y changes by 2+ units
  - 1.5 second cooldown prevents rapid-fire on long staircases
  - Resets on map change to avoid false triggers between areas
  - Auto-walk now accepts partial NavMesh paths for targets on different floors
    (Y difference > 2 units) instead of saying "Cannot reach"
  - Floor-aware arrival logic (2026-03-08): arrival proximity check skipped for
    different-floor targets (prevents false "arrived" when directly above/below).
    At partial path end, announces "Target is above/below you — look for stairs"
    instead of running endlessly. Tested — NavMesh sometimes finds full path
    including stairs (works perfectly), fix is safety net for partial paths.
  - Dynamic floor re-evaluation (2026-03-08): _autoWalkDifferentFloor is now cleared
    each frame if player reaches the same floor as target — prevents infinite walk
    when player goes upstairs to reach NPC but proximity check stayed disabled.
  - Floor-aware NavMesh sampling (2026-03-08): SampleNavMeshFloorAware() tries tight
    radius (1.0) first to stay on correct floor, then falls back to full radius (5.0).
    Y-override removed (2026-03-08): previously overrode sampled Y back to original
    when floor difference exceeded threshold, but this created positions off the NavMesh
    surface causing PathInvalid. Now uses sampled NavMesh position as-is and trusts
    CalculatePath to determine connectivity. Fixes Krosse Castle exit and Overworld
    town gate being falsely filtered as unreachable. ✓ TESTED
  - NOTE: Krosse Guild exit shows PathPartial (genuinely disconnected NavMesh) —
    may become accessible later in story progression. Monitor on revisit.

- **World map fast travel menu** — IMPLEMENTED AND TESTED (2026-03-07):
  - WorldMapHandler.cs: polling-based (same pattern as shop/camp — native-only navigation)
  - Detects UIWorldMapWindow via FindObjectOfType, polls IsOpened for open/close
  - Three-level hierarchy: Location (cities/dungeons with tabs) → Sub-areas → Fast travel points
  - Point selector uses two data types: UIWorldMapLocationListItemData (sub-areas) and UIWorldMapLocationListItemFastTravelData (destinations) — both handled via dual TryCast
  - Unavailable items announced with suffix. Tab changes (City/Dungeon) announced.

- **Bug: First item not announced in camp sub-screen lists** — FIXED (2026-03-07):
  Harmony hooks fire during game's Update (before MelonLoader OnLateUpdate), but the polling
  flags gating them were only set in OnLateUpdate — always one frame too late. Fixed by
  replacing stale flags with live game state reads. Applied to: equip items, formation,
  skills, battle skills (leveling + setting), tactics operations. Equip confirmed working.

- **Bug: Double periods in equip item names and other game text** — FIXED (2026-03-07):
  Game text fields already end with periods. Manual `.Append(". ")` created "Swift sword.. "
  Fixed by using AppendSentence() helper across all hooks that handle raw game text.

- **Bug: Enhance menu shows wrong data when switching between CombatPoint/BattleSkillPoint** — FIXED:
  When navigating between CombatPoint and BattleSkillPoint within the Enhance sub-menu, both passed
  the same IsEnhanceBattleSkillMenu() gate, so _battleSkillWasActive stayed true and inner selectors
  were never re-cached. Combat skills showed missing level/BP on first visit; battle skills showed
  the last combat skill's BP cost. Fix: track _lastBattleSkillMenuItem and re-cache when it changes.

- **Private action notification** — IMPLEMENTED AND TESTED (2026-03-07):
  - PrivateActionHandler.cs: polls ParameterManager.GetLocalityParameter(FieldmapID).IsPrivateAction
  - Plays PrivateAction.wav + screen reader "Private action available. Press Square." once per town visit
  - Volume slider in mod settings menu (0% = off, default 70%)
  - Game has NO native audio cue for PA availability — purely visual icon only

- **Dialogue choice menus** — IMPLEMENTED (2026-03-08), PENDING TEST:
  - DialogueChoiceHandler.cs: announces Yes/No and multi-choice menus during NPC conversations
  - Polling-based activation (finds UIConversationWindow.selectChoiceSelector, detects presenter visibility)
  - Hooks on ShowSelectChoiceMessage capture title text when available (bonus — not relied upon)
  - Index polling for navigation (native-only cursor movement, same pattern as camp menus)
  - Inn Yes/No uses ShowSelectChoiceDirectMessage (native-only call chain) — hook alone missed it
  - Loc keys: dialogue_choice_open_with_title, dialogue_choice_open, dialogue_choice_item

- **Database sub-menu accessibility** — IMPLEMENTED AND TESTED (2026-03-08):
  - CampMenuHandler.Database.cs: partial class with all 6 Database sub-screen handlers
  - Tutorial: browse with name/New/position, locked says "Locked", confirm reads title+description
  - Enemy Picture Book: browse with name/position, locked says "Unknown enemy", confirm reads full stats (HP/EXP/Fol/drops/habitat/boss)
  - Item Picture Book: browse with name/position, locked says "Unknown item", confirm reads name+description
  - Fish Picture Book: browse with name/position, locked says "Unknown fish", confirm reads full details (rare/crown/shadow/habitat/caught/length)
  - Location Picture Book: browse with name/position, locked says "Undiscovered", confirm reads name+discovered by+description
  - Player Data: virtual cursor (no native list selector) — Up/Down steps through 24 stats across 3 categories (Battle Data, Collection Data, Other Data), no wrapping, no position indicator
  - All gates use specific root menu item names (Tutorial, EnemyList, ItemPictureBook, FishPictureBook, Location, PlayerData)
  - Stale-seed pattern prevents spurious announcements on camp open

## Code Cleanup (2026-03-01)

Cleanup branch `claude-mod-cleanup` merged to master. Key changes:

- **File splitting:** CampMenuHandler split into 7 partial files (core, BattleSkill, Equip, Formation, Items, Party, Status). NavigationHandler split into 4 (core, AutoWalk, Build, Patches).
- **New shared utilities:** `TextUtil.cs` (StripTags consolidation), `FieldState.cs` (IsFieldFree consolidation)
- **Config slider bug fixed:** gauges now read `currentIndex` instead of animated `value.text` — values are correct immediately
- **Silent catches fixed:** 6 bare `catch {}` blocks replaced with proper logging
- **Dead code removed:** unused hook registration, dead localization key, commented-out hotkey code
- **Helpers extracted:** `SortAndFilterUnreachable()` (replaced 8 copies), `SuppressNavInput()`, `AppendSkillInfo()`
- **Hardcoded strings moved to Loc.Get():** GameOverHandler retry/title, CampMenuHandler.Party position/status
- **OnSceneChanged added to CampMenuHandler:** prevents stale IsCampOpen if scene changes while camp is open
- **IsFieldFree now checks ShopHandler.IsShopOpen** in NavigationHandler (was missing before)
- **DIAG logs removed:** ~30 unconditional MelonLogger.Msg("DIAG:...") lines removed from ConfigMenuHandler
- **Deferred (low priority):** UpdateXxx polling pattern helper, StripControllerPrefix consolidation across NotificationHandler/GamepadMenuHandler

## Code Cleanup (2026-03-04)

Stale-open check helper consolidation. Key changes:

- **New shared utility:** `SubScreenState.cs` — consolidates _wasActive/_suppressHeading/_lastIndex pattern into reusable class with CheckEntry(), SeedOnOpen(), SuppressNextHeading(), Reset() methods
- **9 sub-screens refactored:** Items, Equip, Formation, Skill, BattleSkillSetting, Party Formation, Assist Formation, Tactics — each replaced 2-3 repeated fields with a single SubScreenState instance
- **Open postfix simplified:** 7 identical try-catch stale-suppress blocks replaced with StaleSuppressIfActive() helper; Equip and BattleSkillSetting blocks expanded to seed child selector indices
- **Bug fixed: camp close announced root menu item** — _menuSelector now nulled on window close (prevented stale "Item, 1 of 10" announcement)
- **Bug fixed: sub-screen content announced on root menu highlight** — Equip slot list and BattleSkillSetting slot list indices now seeded in Open postfix (prevented spurious child announcements when just highlighting root item)
- **Not changed:** BattleSkill main handler (hook-driven), Status (hook-driven), ShopHandler, BattleMenuHandler, GameOverHandler

## Architecture Decisions

- (none yet)

## Key Bindings (Mod)

### Keyboard
- F1: Help
- F2: Toggle dialogue voice mode (full text / name only when voiced)
- NumPad 5: Open/close navigation list (also cancels auto-walk)
- NumPad 8 / 2: Navigate up/down in nav list
- NumPad 4 / 6: Switch category in nav list
- NumPad 1: Auto-walk to selected item / cancel auto-walk / stop following
- F12: Toggle debug mode

### Gamepad
- Hold L1: Open navigation list (field only, not in menus/battle)
- D-pad Up/Down (while L1 held): Switch category
- D-pad Left/Right (while L1 held): Navigate previous/next item
- Left stick up (while L1 held): Auto-walk to highlighted item
- Release L1: Close navigation list
- L1 press during auto-walk: Cancel auto-walk and reopen nav list

## Architecture Notes

- Runtime: net6, Unity 2021.3.22f1, IL2CPP, 64-bit
- Game uses Unity NEW Input System — use Keyboard.current[Key.Fx].wasPressedThisFrame (NOT Input.GetKeyDown)
- Game singletons all use ClassName.Instance pattern (SingletonMonoBehaviour)
- Game code namespace: Il2CppGame — must add `using Il2CppGame;` to access game classes
- Required csproj references for IL2CPP: Il2Cppmscorlib.dll + Il2CppInterop.Runtime.dll

## Notes for Next Session

### Skill development fix awaiting test (2026-03-19)
- Fix deployed. User reported: specialty SP costs showed "1" always, some skills showed max when not.
- Log confirmed: Scouting cost 20 SP (125→105) but displayed "SP: 125 / 1".
- Fix uses `UICommon.CalcNeedSpecialSkillForLevelUp()` for fresh specialty costs.
- Knowledge skill max verified against `ConstSkillParameter.levelupSp.Count`.
- See "Skill Development Screen Fix" section above for full test checklist.
- If test fails: check MelonLoader log for errors in `SkillInfoPresenter_Set_Postfix`.
  The `CalcNeedSpecialSkillForLevelUp` call (CallerCount 2) should be safe from managed code.

### Audio clip ready for integration
- **File:** `E:\StarOcean\audio_cue.wav` — 10-second clip (PCM WAV, 44100 Hz, 16-bit mono, ~861 KB)
- **Source:** YouTube clip trimmed from 5s to 15s
- **User has a specific use in mind** — to be implemented in a future session

### Navigation improvements (2026-03-08, late session)
- **Architecture review:** Thoroughly analyzed NavMesh pathfinding, game's AIPathFinder A*,
  NavMeshAgent, and OnMove() alternatives. Conclusion: current approach (NavMesh.CalculatePath
  for field maps, game A* for world map) is optimal. No rewrite needed.
- **Field map stuck detection:** Added 2-second interval check (FieldStuckMinMove=0.5 units).
  Two-strike system: first stuck → recalculate path; still stuck → cancel + announce
  "Path blocked to [target]. Auto-walk stopped." PENDING TEST
- **Physics.Linecast POI filtering:** Secondary filter after NavMesh reachability. Fires
  linecast at eye height, removes items blocked by non-trigger colliders (solid walls).
  Counter NPCs skip this check. Errors default to keeping item. PENDING TEST
- **Floor labels in nav list:** Items with Y difference > FloorChangeThreshold (2.0 units)
  get "(above)" or "(below)" appended to their label. Applied to all categories. PENDING TEST

### Current work (2026-03-08)
- NavMesh reachability fix: removed Y-override from SampleNavMeshFloorAware. The override
  created positions off the NavMesh surface causing PathInvalid, which falsely filtered exits
  like Krosse Castle gate (trigger Y=8.8, NavMesh Y=6.6). Now uses sampled NavMesh position
  as-is and trusts CalculatePath connectivity check. ✓ TESTED
- Krosse Guild exit: PathPartial (genuinely disconnected NavMesh), likely story-gated. Monitor.
- Auto-walk multi-floor NavMesh fix: floor-aware sampling (SampleNavMeshFloorAware) prevents
  NavMesh.SamplePosition from snapping to wrong floor in multi-story buildings (inn Tourist bug).
  Tight radius (1.0) tried first, falls back to full (5.0). ✓ TESTED
- Auto-walk dynamic floor re-evaluation: _autoWalkDifferentFloor cleared each frame once player
  reaches same Y level as target. Prevents infinite walk after going upstairs. ✓ TESTED
- DialogueChoiceHandler: rewritten to polling-based activation (was hook-only, hooks don't fire
  for native-only call chains like inn ShowSelectChoiceDirectMessage). Now detects presenter
  visibility via UIConversationWindow.selectChoiceSelector. PENDING TEST for inn Yes/No.
- Auto-walk field exit fix: auto-walk uses transform.position which bypasses Unity trigger
  colliders, so FieldMapjumpCollision (building doors, gates) never fired. Added TryEnterFieldExit()
  — calls ChangeFieldmap() directly on the nearest exit trigger, same approach as world map entry.
  Now announces "Entering [building]" instead of stopping outside. ✓ TESTED

### Current work (2026-03-07)
- R2 ally switching in battle: BattleTargetHandler now announces controlled ally on R2 press ✓ TESTED
  - Polls controlPlayerIndex + ControlPlayerChangeMode state (6) for first-press detection
  - Announces: name, HP (exact), MP (exact), active buffs/debuffs
  - Index silently seeded at battle start to avoid unwanted announcement
- Equipment Wizard handler: new EquipWizardHandler.cs — polls UISystemWindow.IsShowingEquipWizard,
  announces heading + description + equipment comparison + Yes/No/Reject All menu. Pending test.
- First-item fix: all camp menu hooks now use live game state reads instead of stale polling flags.
  Equip confirmed working. Formation, skills, battle skills, tactics pending test.
- Double-period fix: AppendSentence() applied to all raw game text in hook string builders.
  Equip confirmed. Other menus pending test.
- FieldState.IsFieldFree() hardened: added PauseManager.IsPause + EventManager.IsRunning checks
  - Dialogues, cutscenes, notifications, tutorials now block nav menu from opening
  - Auto-walk cancels immediately when any of these trigger mid-walk
  - All handlers using IsFieldFree() benefit (navigation + enemy proximity)
- Navigation distance label changed from "units" to "meters" (Unity 1 unit = 1 meter)

### Current work (2026-03-05)
- BattleStatusHandler: battle status announcements ✓
  - New file: BattleStatusHandler.cs (~310 lines)
  - Hook: BattleCharacter.DoCollisionReceiveAction (CallerCount 2, prefix+postfix) — HP tracking + damage dealt
  - Hook: CharacterParameter.SetBuffDebuffState (CallerCount 19, postfix) — status ailment detection
  - CRASH FIX: DoDamage had ref DamageResult (IL2CPP ValueType) that crashed Harmony trampolines; replaced with DoCollisionReceiveAction + attackCollision.OwnerCharacter for attacker
  - Ally HP below 50%, below 25%, knocked out — queued announcements, downward transitions only
  - Ally negative status ailments (poison, paralyze, petrify, confusion, silence, faint, death, stop, swallowed, controlled)
  - Player-controlled character damage dealt — announces damage amount per hit
  - All announcements use SayQueued (non-interrupting queue)
  - 3 new ModSettings toggles: AllyHealthWarningEnabled, AllyStatusAilmentEnabled, PlayerDamageDealtEnabled
  - 3 new mod menu items added to ModMenuHandler
  - 5 new Loc strings + 3 menu label strings
- ModMenuHandler: screen-reader-driven mod settings menu ✓
  - New file: ModMenuHandler.cs (~250 lines)
  - Keyboard: F4 to open/close, arrow keys to navigate/change, Escape to close
  - Gamepad: L1+L3 to open/close, D-pad to navigate/change, Circle to close
  - 10 settings: 3 sound toggles, 3 volume sliders (10% steps), dialogue voice mode, 3 battle announcement toggles
  - All input blocked while menu open (keyboard + gamepad)
  - Auto-saves to settings.json on close
  - Loc keys added, help text updated

### Previous work (2026-03-21)
- Super Specialty menu accessibility (CampMenuHandler.SuperSpecialty.cs) ✓ TESTED
  - New file: CampMenuHandler.SuperSpecialty.cs (~300 lines, partial class)
  - Context A: IC tab 2 ("Super Special Skills") — polls currentIndex, reads skillName/skillDescription
    from UISpecialSkillInformationPresenter GameText fields, reads conditions from
    superSpecialSkillLearningPresenter sub-presenter
  - Context B: Enhance → Skill → R2 (Skill Learning) — completely separate menu system
    using UICampSkillLearningSelector (on UICampSkillSelector.learningSelector)
    Polls currentDataList items (UISkillLearningListItemData: skillName, level),
    reads info from UISkillLearningInformationPresenter
  - Both contexts share AppendLearningConditions() for condition1/condition2 text
  - Loc keys: ss_screen, ss_not_learned, ss_requires, ss_position
- BattleResultHandler: bonus announcements (chain, Training, Open Eyes) confirmed working

### Previous work (2026-03-04)
- SubScreenState helper: stale-open check consolidation ✓
  - New file: SubScreenState.cs
  - 9 sub-screens refactored to use helper
  - Two bugs found and fixed during refactor (camp close + root highlight)
- BattlePauseHandler: all bugs fixed and tested ✓
  - Ally name: ParameterManager chain (charaNameID → TextManager) — shows "Claude"
  - HP/MP: direct CharacterParameter reads — shows real values
  - Gamepad tier cycling: moved from D-pad (conflicts with game's native character cycling
    on ALL directions) to L1/R1 shoulder buttons
  - Status conditions tier confirmed working (Stun on enemy)

### Previous work (2026-03-03)
- BattleMenuHandler: battle command menu (Triangle) fully implemented and tested
  - New file: BattleMenuHandler.cs (~1000 lines)
  - Phases: root menu, items, spells, target selection, tactics/strategy
- BattlePauseHandler: initial implementation with tiered info system
  - New file: BattlePauseHandler.cs (~500 lines)

### Previous work (2026-03-02)
- BattleResultHandler: learned skills now announce with description (UICommon.CreateBattleSkillInformationData)
- BattleResultHandler: bonus announcements added (chain, Training, Open Eyes) ✓ TESTED (2026-03-21)
- Old Loc key `battle_result_learned_skills` replaced with `battle_result_learned_skill` (name + desc)
  and `battle_result_learned_skill_noDesc` (name only fallback)

### Previous session (2026-03-01)
- BattleTargetHandler: L2 target cycling announces enemy name, HP%, shield%, leader, buffs/debuffs ✓ TESTED
- SaveNotificationHandler: save sound cue on manual/auto save ✓ TESTED
- ModSettings: JSON persistence for sound toggle/volume settings
- AudioCuePlayer: refactored to file-based WAV (dodge + save sounds from disk)
- TextUtil: shared ParseCharaNameID (was duplicated in NavigationHandler)
- Combat skill enhance: fixed level display (was 0/0), reordered to Name/Level/BP/Desc/Upgrade ✓ TESTED

### Battle skill / combat skill menu separation (2026-03-01)
- **Root battle skills** (Camp → BattleSkill): NEW detailed tactical readout
  - Format: Name. MP. Type. Target. Element. Range. Effect. Description. Level.
  - Target type resolved from ParameterManager.GetBattleSkillParameter(battleSkillID)
  - ✓ TESTED — root battle skills reading correctly
- **Enhance battle skills** (Camp → Enhance → BattleSkillPoint): upgrade-focused readout
  - Format: Name. MP. Level. SP balance/cost. Effect. Description. Upgrade: bonuses.
  - ✓ TESTED — working, user confirmed "rest works fine"
- **Enhance combat skills** (Camp → Enhance → CombatPoint): upgrade-focused readout
  - Format: Name. Level X of Y. BP balance/cost. Description. Upgrade: effect.
  - ✓ TESTED — combat skill level now read from UICampCombatSkillListItemData.skillLevel
    (UIBattleSkillInformationData.skillLevel is always 0 for combat skills)
  - Max level derived from ConstCombatSkillParameter.levelupBp.Count via ParameterManager
  - effectDescription used as upgrade label ("Upgrade: Effect chance up")
  - Duplicate text suppressed (e.g. Body Control where effect == description)
  - Combat skills have no MP cost (naturally skipped)
- **Code separation**: IsBattleSkillRelatedMenu() split into IsRootBattleSkillMenu() + IsEnhanceBattleSkillMenu()
- **Assignment screen**: unchanged, still uses AppendSkillInfo() (root only)
- Files changed: CampMenuHandler.BattleSkill.cs (rewritten), Loc.cs (new strings), CampMenuHandler.cs (RuntimeHelpers)

### Battle target lessons learned
- ShowSelectedTargetEnemy (CallerCount 3) does NOT fire for L2 target switching — likely for skill targeting
- SetControlPlayerTarget (CallerCount 7) is the correct hook for L2 target changes
- CharacterParameter.CharacterName is empty for battle enemies — use ConstEnemyParameter.charaNameID fallback
- BattleManager.stateMachine.currentState == 5 detects TargetChangeMode (for single-enemy re-reads)
- Spectacles is the ONLY see-through mechanism; no Analyze spell exists
- Elemental resistances only shown in pause menu (not during active combat)

### Previous test results (still valid)
- All nav, camp, shop, battle result, skills, save, status features working as documented above

### Key lesson learned
- Camp menu root selector activeInHierarchy stays true even when sub-screens are open.
  This means activeInHierarchy-based detection fails for ALL camp sub-screens.
  Hook-driven detection (used for status) is the reliable alternative when polling fails.

### Camp menu architecture (critical for sub-screen work)
- Root: UICampMenuSelector (menuSelector field on UICampWindow) — DONE
- Sub-screens are separate selector classes, all fields on UICampWindow:
  - itemSelector (UICampItemSelector)
  - statusSelector (UICampStatusSelector)
  - equipSelector (likely UICampEquipSelector)
  - battleSkillSelector, operationSelector, skillSelector, formationSelector
- Each sub-screen needs its own polling loop or hook
- Pattern: access the selector via UICampWindow field, poll currentIndex
- Item names: UICampMenuItemData.menuItem is UIDefine.CampMenuItem enum (toString gives
  e.g. "Status", "Item", "Equip", "BattleSkill", "Formation") — consider adding friendly
  Loc entries (e.g. "BattleSkill" → "Battle Skills") in a follow-up

### CRITICAL: Camp menu patching lessons learned
- Camp navigation is driven entirely from native C++ — NO Harmony hook fires for navigation
  (tested: UpdatePresenter, OnMoveCursor, OnUp, OnDown, Show, UICanSelectedListItemPresenterBase.OnSelected — all failed)
- Methods with CallerCount(0) called only from native code are NOT interceptable via Harmony
- Polling currentIndex from Main.UpdateHandlers() is the correct approach for this menu
- GetComponentInChildren<T>() fails for camp selectors — use the named fields on UICampWindow
- UICampCommandSelector is NOT the root menu — it may be unused in the demo or is a sub-selector

### Animation system notes (for future reference)
- Game uses FieldBillboardObject.PlayMoveAnimation(FieldAnimationKind) as the animation trigger
- CharacterAnimationAccessor.LateUpdate() (MonoBehaviour) resets animation to "Unique" each frame when no input
- MelonLoader OnLateUpdate fires BEFORE game MonoBehaviour LateUpdates — cannot override there
- Solution: Harmony prefix on PlayMoveAnimation blocks non-Run calls on the player during approach
- Player run speed: GetMoveSpeed(true) = 6.5 units/second

### Next feature candidates
- Navigation: Enemies — DONE ✓ (parsed names, TextManager doesn't resolve on field)
- Navigation: Events — DONE ✓ (tested)
- Navigation: Save points — DONE ✓
- Navigation: Stairs — DONE (pending dungeon test)
- Navigation: Doors (stone only) — DONE (pending dungeon test)
- Navigation: Warp Points (panels, circles, platforms) — DONE (pending dungeon test)
- Navigation: Flavor chat triggers (FieldFlavorChatCollision) — party banter spots
- Operations child screens: Party Formation ✓, Formation ✓, Assist Formation — pending test (need more party members)
- Camp sub-screen: skill learning (UICampSkillLearningSelector — complex, deferred)
- Battle pause menu handler (detailed enemy info: element resistances, buffs, HP when spectacled)
- Battle status announcements (player HP/MP during combat)

### Notes
- Build command: `dotnet build SO2RAccess.csproj` (auto-copies to Mods folder)
- Map exit names now resolved from game data automatically (ConstFieldParameter + TextManager)
