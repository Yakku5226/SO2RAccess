# Low-Level Cleanup Candidates (from per-file subagent scan, 2026-06-14)

Validated highlights grouped into bundles. Line estimates approximate.

## Bundle A — Dead code deletions (low risk, high confidence) ~520 lines
- AudioCuePlayer: dead gauge-break AUDIO feature — LoadGaugeBreakSound/IsGaugeBreakSoundLoaded/
  PlayGaugeBreakCue + 8 backing fields. (VALIDATED dead. NOTE: distinct from the live
  BonusGaugeBreakAnnouncementEnabled *speech* setting — do NOT touch that.) ~95
- WorldmapPathfinder: dead methods (zero callers) — SimplifyPath (+hasClearance plumbing in
  FindPath), FindNearestReachableToTarget, RunDiagnostics, ScanPathLine. ~250 (+ using System.Text)
- NavigationHandler.Build.Worldmap: LogWorldmapMapjumpColliders — pure diag, one internal call. ~65
- NavigationHandler.Worldmap(.Pathfinding): dead cache chain GetWorldmapPathFinder/_wmPathFinder/
  ClearWorldmapCache (ocean checks use CalcHeight, not this). ~30
- ModMenuHandler: dead ModMenuItemType enum + write-only Type field + 18 assignments. ~22
- ScreenReader: dead Stop() + orphan Tolk_Silence P/Invoke. ~15
- AudioCuePlayer: unused Is*SoundLoaded props (Dodge/Jump/Save/PrivateAction; keep GaugeFill). ~12
- 12 dead Loc keys: nav_autowalk_enter_fail, nav_autowalk_arrived_above/below, battle_pause_none,
  battle_result_levelup_sp, camp_enhance_sp, camp_tactics_operation(+_current), db_playerdata_stat,
  ic_action_screen, save_saving, proximity_wav_missing. ~12
- WorldmapGridGenerator: dead CachedGrid.IsWalkable (~5); solidWallCount->bool (~3); collapse
  identical EXPEL/NEDE bounds branches (~10). ~18
- Write-only fields: BattleMenuHandler _cachedInfoValue/_cachedInfoValueLabel (~5) + _cachedOpDesc (~4);
  IC _icPendingSkillDesc/_icPendingSkillLevel/_icPendingCreationData (~10); Quest _questWindow (~4);
  Mission _campMissionWindow (~4); Database _playerDataLastIndex (~3); GameOverHandler _selector (~3);
  Nav LIDAR LidarWaypointBias/LidarCommitTime consts + write-only _lidarCommittedDir/_lidarSmoothedDir (~8).
- Unused usings: System.Text (CampMenuHandler.cs/Open.cs/Patches.cs), RegularExpressions
  (Status/Party), HarmonyLib+CompilerServices+RegularExpressions (Equip/Formation), Generic
  (Formation, BattleCounterHandler), DebugHotkeys Generic. ~15

## Bundle B — Stale debug scaffolding for SOLVED problems (low-med risk) ~140 lines
(Doc comments describe past-tense solved investigations.)
- CampMenuHandler.ItemCreation.ActionList: LogActiveActionSelectorsDiag (~60) + its line-28 call
- CampMenuHandler.ItemCreation.Result: LogResultDiag (~43) + its line-18 call
- CampMenuHandler.ItemCreation: first-open DIAG dump + _icDiagDone (~18)
- DialogueChoiceHandler: DIAG block + _diagCooldown (~22)
(KEEP: FieldPromptHandler debug catalog — still useful for uncovered prompt types.)

## Bundle C — Cross-file dedup refactors (med risk, high maintainability value) ~250 lines
- charaNameID -> ParameterManager -> TextManager -> ParseCharaNameID chain duplicated in 5 places
  (BattleTargetHandler, BattleStatusHandler, BattlePauseHandler, BattleMenuHandler.ItemSpell,
  NavigationHandler.Build.Enemies) -> shared TextUtil.ResolveCharaName. ~40-60
- Sprite-tag + controller-prefix stripping duplicated (GamepadMenuHandler vs NotificationHandler vs
  KeyboardMenuHandler) -> consolidate in TextUtil; delete dup + 2 public wrappers. ~25
- D-pad auto-repeat state machine duplicated (Main.cs vs ModMenuHandler) -> shared DpadRepeater. ~30
- Camp Database 5 picture-book Update* methods -> one generic browse helper. ~100
- BattlePauseHandler CycleCharacterLeft/Right -> merge into CycleCharacterTo(newIdx). ~25
- Lazy-find-with-cooldown pattern (Guild/Shop frame-counter; Pickpocket/QuickRecovery time-based)
  -> shared helper. ~30
- Build*.cs duplicate-label-numbering pattern (Save/Stairs/Doors/Warp/Enemies/Events) -> helper. ~40
- Inline position string sb.Append(". ").Append(idx+1)... (~9x IC + others) -> AppendPosition helper. ~15

## Bundle D — Consistency / correctness fixes (low risk, ~quality) ~few lines
- Hardcoded English strings bypassing Loc.Get: ActionList ", unavailable"; Result "Unknown";
  EquipWizard slotNames[]; Pickpocket " of " position.
- DIAG-prefixed always-on logs -> DebugLogger.LogState: KeyboardMenuHandler:75, GamepadMenuHandler:81,
  GameOverHandler:58.
- Stale comments: NotificationHandler ResolveLocationRewards refs (413,618); SaveNotificationHandler:188
  "Announces the save" (only plays cue now); WorldmapGridGenerator magic-string comments (WMGG vs WMGH).
- Orphan/misplaced XML doc comments: NavigationHandler.cs ~608; NavigationHandler.MapState.cs ~337.

## Notes / deferred
- AudioCuePlayer 5x duplicated Load/Play sound blocks -> SoundCue helper (~150-200 lines) = biggest
  single refactor but MED risk (HGlobal/GC lifetime). Deferred unless user wants it.
- WorldmapDiagnostics TraceRoutesToGaps hardcoded Salva offsets (~250 debug lines) — debug-only keep,
  flagged earlier; trim candidate if user wants.
- Main.cs handler-list consolidation via IModHandler interface (~60 lines) — med risk, behavior-sensitive.
