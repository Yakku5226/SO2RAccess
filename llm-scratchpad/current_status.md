# Cleanup Session — Current Status

## Working branch
`claude-mod-cleanup-2` (branched from `master`)

> Note: a stale `claude-mod-cleanup` branch already existed from a March 2026
> cleanup session. It is fully merged into master (0 unique commits, 33 behind),
> so it was left untouched and a fresh branch name was used.

## Mod under cleanup
- **Name:** SO2RAccess — accessibility mod for *Star Ocean: The Second Story R*
- **Engine:** Unity IL2CPP, 64-bit; **Loader:** MelonLoader
- **Source:** 63 `.cs` files in repo root; decompiled game source under
  `decompiled/Assembly-CSharp/Il2CppGame/` (~2,701 files).

## Prompts already run
- [x] prompts/sanity-checks-setup.md — sanity checks passed, branch + scratchpad created
- [x] prompts/information-gathering-and-checking.md — docs gathered & synthesized (see below)
- [x] prompts/code-directory-construction.md — code index built for all 63 source files

## Prompts up next
- [x] prompts/large-file-handling.md  DONE (deletion + splits committed, smoke test PASSED 2026-06-14)
- [x] prompts/input-handling.md  DONE. Verdict: input is NOT spaghetti — centralized in
  Main.ProcessHotkeys/ProcessGamepad, modal if-cascade, only QuickRecoveryHandler polls
  directly. No framework needed. ACTION TAKEN (user-approved): extracted F5/F8-F11 debug
  hotkeys into DebugHotkeys.cs, DELETED vestigial F6/F7 experiments. Main.cs 978->697.
  Committed. (Smoke test deferred — debug-only keys, build-verified.)
- [x] prompts/string-builder.md  DONE. Verdict: NOT a string-builder mod (user chose skip the
  optional small tidy). Details below.
- [~] prompts/low-level-cleanup.md  IN PROGRESS. User chose to execute ALL bundles thoroughly.
  Per-file subagent scan -> llm-scratchpad/cleanup-candidates.md. Committed so far (each its own commit):
  * Bundle A (dead code) DONE: gauge-break audio + Is*SoundLoaded; WorldmapPathfinder dead methods;
    LogWorldmapMapjumpColliders; _wmPathFinder cache chain; ModMenuItemType; ScreenReader.Stop;
    12 dead Loc keys; GridGenerator IsWalkable/solidWallCount/EXPEL-NEDE; write-only fields
    (BattleMenu caches, IC pending, _playerDataLastIndex, LIDAR consts/dirs, GameOver _selector);
    unused usings.
  * Bundle B (stale debug scaffolding) DONE: LogResultDiag, LogActiveActionSelectorsDiag, IC
    first-open DIAG dump, DialogueChoice DIAG block (+ their fields). Kept FieldPrompt catalog.
  * Bundle C (dedup) PARTIAL — DONE: (#2) StripControllerPrefix -> TextUtil; (#5) BattlePause
    CycleCharacterLeft/Right -> CycleCharacter(delta); (#1) charaNameID resolution -> 
    TextUtil.ResolveCharaNameKey (5 sites); (#3) DpadRepeater shared by Main+ModMenu;
    (#8) TextUtil.AppendPosition (8 sites).
    (#4) PollPictureBook shared helper for the 5 camp database picture-book screens — ATTEMPTED
    then REVERTED (commit efb9cce reverted): caused a HARD NATIVE CRASH opening the Fish picture
    book (confirmed regression; book worked pre-refactor). No managed exception logged; refactored
    code was structurally identical to the original, so cause is an unexplained IL2CPP/runtime
    interaction (suspected per-frame delegate allocation). Standalone Update* methods restored.
    LESSON: avoid per-frame method-group/lambda delegate allocation in IL2CPP polling hot paths;
    if retrying, gate BEFORE building delegates and/or cache delegates as static fields.
    (#6) UiFinder.TryGetActiveOverlay for Pickpocket+QuickRecovery overlay detection (Guild/Shop
    left — different frame-counter cadence) — KEPT.
    NOT DONE — (#7) Build* duplicate-label-numbering: deliberately SKIPPED. The builders use
    distinct localized _n Loc keys per sub-type (nav_save_n, nav_save_recovery_n, etc.); unifying
    to the generic append-number form would drop localization / change wording. "Not safely fixable."
  * Bundle D (consistency) DONE: DIAG logs -> DebugLogger; ic_unknown_item key; stale comments
    (ResolveLocationRewards, SaveNotification); relocated MapState NPC-type doc. (Left ", unavailable"
    hardcoded — routing via ic_unavailable would change capitalization = wording change, out of scope.)
  ALL builds 0/0. Bundles A/B/C(all but #7)/D complete. AWAITING final smoke test (esp. camp
  database picture-book screens + pickpocket + quick recovery, which #4/#6 touched), then
  proceed to prompts/finalization.md.
  --- string-builder detail ---
  Localization-first (571 Loc.Get format-string calls), string.Join for lists, StringBuilder
  mostly in debug/algorithm files (WorldmapDiagnostics 87, GridGenerator 35, Pathfinder 26 =
  log text, not announcements). Manual space-separator pattern only ~22 sites (BattleResultHandler
  12 = main offender). Per prompt -> move on; offered optional small tidy to user.
  - PRE-STEP DONE (smoke test PASSED 2026-06-14, committed e9a711c): deleted the dead island/multi-segment
    navigation subsystem before splitting (it was self-referential dead code the
    project notes already flagged for removal — splitting it would be wasted work).
    Removed: IslandNavigator.cs, IslandScanner.cs, NavMeshIslandDiagnostics.cs
    (1901 lines) + dead methods/state in NavigationHandler.cs (CheckIslandCrossing,
    CheckDeferredIslandScan, route-segment/crossing state, ExitZone steering),
    AutoWalk.cs (StartMultiSegmentWalk/CheckSegmentTransition/StartNextSegment/
    StartFinalSegment/GetExitIslandSet/CacheCrossingExitZones/AvoidExitZones/
    IsNearRouteWaypoint/FlatSqrDistance), Build.cs (dead hasIslandGraph branch in
    SortAndFilterUnreachable), and 10 nav_island_* Loc keys. KEPT (live): the hard
    map-exit barrier (PathCrossesMapExit / MapExitBarrierMargin / _autoWalkAllowExit).
    Build 0/0. Line counts: NavigationHandler.cs 2050→1883, AutoWalk.cs 1434→965,
    Build.cs 1493→1470, Loc.cs 803→792. ~2570 lines removed total.
  - SPLITS DONE (autonomous mode, build-verified 0/0 + committed each, awaiting final smoke test):
    Verbatim partial-class extractions (no logic change). New files + resulting sizes:
    * NavigationHandler.cs 1883->1214; +List.cs (299), +MapState.cs (401)
    * NavigationHandler.Build.cs 1470->835; +Build.Npcs.cs (279), +Build.Enemies.cs (186),
      +Build.Worldmap.cs (218)
    * NavigationHandler.Worldmap.cs 1288->733; +Worldmap.Pathfinding.cs (570)
    * CampMenuHandler.ItemCreation.cs 1684->683; +ItemCreation.ActionList.cs (551),
      +ItemCreation.Result.cs (174), +ItemCreation.Material.cs (313)
    * CampMenuHandler.cs 1062->320; +CampMenuHandler.Patches.cs (375), +CampMenuHandler.Open.cs (393)
    * BattleMenuHandler.cs (made partial) 1077->503; +ItemSpell.cs (275), +TargetTactics.cs (324)
  - DELIBERATELY NOT SPLIT (cohesive single-concern or one dominant method — splitting would
    scatter logic): WorldmapDiagnostics.cs (1443, LIVE debug tooling via F8/F11 — NOT dead,
    keep), NavigationHandler.cs (1214, dominated by ~730-line Update loop), WorldmapGridGenerator
    (1103), BattlePauseHandler (992), Main.cs (978), AutoWalk.cs (965), CampMenuHandler.BattleSkill
    (897), WorldmapPathfinder (830), Loc.cs (792), Database (780), AudioCuePlayer (744),
    NotificationHandler (710), ShopHandler (636), etc.
  - DELETION CANDIDATES FLAGGED FOR USER (not done — need owner sign-off, not clearly dead):
    (1) ApplyWorldmapMovement_Lidar in NavigationHandler.Worldmap.cs (~156 lines) — dead (no
    callers) but author-marked "preserved for future use". (2) WorldmapDiagnostics.TraceRoutesToGaps
    has hardcoded gap offsets from one recorded session (stale one-off scaffolding); whole
    WorldmapDiagnostics file could go if worldmap nav is considered fully settled (removes F8/F11
    debug capability).
  - Code index: per-file .md entries under llm-scratchpad/code-index are now STALE re: file
    boundaries (content accurate, but methods relocated to the new partials above). Regenerate
    if needed; the map above is authoritative for this session.
- [ ] then prompts/input-handling.md

## Code index note
- 3 orphaned code-index .md files (Island*) deleted. The indexes for
  NavigationHandler.cs / .AutoWalk.cs / .Build.cs are now partially stale (dead
  methods removed); they'll be regenerated/updated as those files are split.

## Code index
- `llm-scratchpad/code-index/<file>.cs.md` — one index per source file (classes/methods/
  fields + line numbers, no bodies). 63 files, all present, verified non-empty.
- Files >2000 lines: NavigationHandler.cs (2050). Next-largest: CampMenuHandler.ItemCreation.cs
  (1684), NavigationHandler.Build.cs (1493), WorldmapDiagnostics.cs (1443),
  NavigationHandler.AutoWalk.cs (1434), NavigationHandler.Worldmap.cs (1288).

## Docs produced this session
- `llm-docs/game-model.md` — conceptual model of the game (screens, controls, mechanics)
- `llm-docs/api-index.md` — finder's index of the decompiled source + gaps vs game-api.md
- `llm-docs/CLAUDE.md` — index/overview of llm-docs (progressive disclosure)
- Root `CLAUDE.md` — fixed build command (`dotnet build SO2RAccess.csproj`), added Game
  Overview section, added llm-docs references. All factoids verified valid.

## Documentation gaps noted for later (from api-index.md)
Not yet in docs/game-api.md: ConstItemParameter (item lookup), Battle Result screen,
Quest system, Shop system data layer, World Map fast-travel UI data layer.

## Scratchpad file directory
- `current_status.md` — this file (session tracking)
  (intermediate subagent artifacts were promoted to llm-docs or removed)

## Open questions for the user — RESOLVED (clarified by author 2026-06-14)
- Keyboard defaults: Enter=confirm, Backspace=cancel, M=map, Tab=camp (~). Folded in.
- Saving: world map anywhere; towns/dungeons only at save points; quicksave autosave on
  map transitions (single slot). Folded in.
- Contraband: counterfeit-money crafting, lowers party affection (author-confirmed).
- Remaking: Factor add/upgrade on gear (via secondary AI source, marked approximate).
  All folded into llm-docs/game-model.md.

## Notes
- Treat built-in memory tools as READ-ONLY during this process (per llm-entrypoint.md).
  Stage all working context here in llm-scratchpad instead.
