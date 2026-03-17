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
**Currently working on:** Fishing — arrival facing diagnostic (needs test)
**Blocked by:** Awaiting test of fishing spot arrival facing fix (diagnostic build deployed)
**Last completed:** Fishing — navigation + catch result announcements (2026-03-17)

### Fishing Accessibility (2026-03-17) — PARTIALLY WORKING

- **What works (tested 2026-03-17):**
  - Fishing spots appear in Interactables nav category via `FindObjectsOfType<FieldFishingWaterPlace>()`
  - Auto-walk navigates player to the water's edge (path exhausts at shore)
  - Catch result announcements: Harmony postfix on `UIFieldFishingResultPresenter.Set()` (CallerCount 1)
    announces "Caught: [fish name], [size], [new record/max size/new]."
  - "Fish got away" already caught by existing dialogue system
  - Game's built-in audio/vibration cues are sufficient for the minigame itself (no custom cues needed)
  - User completed Fishing Mission 1 successfully
- **What's NOT working (needs fix):**
  - **Arrival facing:** Player does not face the water correctly after auto-walk to fishing spot.
    User has to manually turn to interact. Three approaches tried so far:
    1. `Position = NavMesh shore point` → player faced along shore, not toward water
    2. `Position = BoxCollider center` → path exhaustion triggered too far (2m), player still not facing water correctly
    3. `Position = shore point, LiveTransform = col.transform` → `_autoWalkTarget` updates to collider position each frame, path exhaustion faces `targetDir` toward collider. Still not working per user report.
  - **Diagnostic build deployed:** Added `facing=` and `targetDir=` vectors to path exhaustion log.
    Next session: have user walk to fishing spot, check log to see if facing is correct but
    overridden by game, or if direction calculation is wrong.
  - **Collider data (Krosse area):** center=(-63.70, -1.00, -0.73), bounds=(1.50, 1.20, 1.50),
    walkTarget=(-64.89, -1.50, -0.51), player typically ends at X≈-65, Z≈0
- **Files:**
  - `NavigationHandler.Build.cs` — `BuildFishingSpots()` method
  - `NavigationHandler.cs` — `BuildFishingSpots()` call in scan, fishing result hook in ApplyPatches
  - `NavigationHandler.Patches.cs` — `FishingResultSet_Postfix()` for catch announcements
  - `Loc.cs` — 6 keys: nav_fishing, nav_fishing_n, fish_caught, fish_new_record, fish_new, fish_max_size

### Item Creation Sub-screen (2026-03-17) — WORKING

- **What works (tested 2026-03-17):**
  - Skill selection: skill name, description, level, tab switching — all working
  - Action list: category name, creation hook, character tab — working
  - "????" item names: fixed, now says "Unknown" (SanitizeItemName helper)
  - Create mode: after selecting a material (e.g. Silver), announces "Create [count].
    Success rate: [X] percent." Count changes announced as user adjusts with D-pad.
    Detection via `actionPresenter.currentCreateCount` (-1 = inactive, >0 = Create visible).
  - Result screen: fully working — item name, success/failure, position
  - Stale suppression: all IC sub-screens (skill, action, result) properly seed
    LastIndex and tab values on camp open. Scrolling past IC in root menu is silent.
- **What's NOT yet accessible (future work):**
  - **Material selection screen** (`UICampSpecialSkillAddMaterialSelector`):
    ALL sub-selectors have stale `activeInHierarchy=true`. The `Set` hook (CallerCount 1)
    does NOT fire (native-only call). The `currentState` field stays at `Normal` (never
    transitions). This screen likely only appears for Compounding/Customization at higher
    skill levels. Hook + polling code is dormant, ready when encountered.
- **Files:**
  - `CampMenuHandler.ItemCreation.cs` — all IC logic (skill, action, create mode, result)
  - `CampMenuHandler.cs` — selector caching in Open postfix, 3 Harmony patches, Update call
  - `Loc.cs` — 17 localization keys (ic_screen, ic_tab_*, ic_skill_*, ic_action_*, ic_result_*, ic_unknown_item)

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
   to `chest.IsAcquired` (property, calls native getter). Distant chests no longer flip
   between Opened/Unopened.
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

- **Post-battle result announcements** (`BattleResultHandler.cs`) ✓ RETESTED (2026-03-05)
  - Announces SP and BSP totals after EXP/Fol
  - Level-ups include per-character BSP gained and learned battle skills
  - Learned skills now announce with description: "Learned Fire Bolt: Unleashes a fiery projectile."
    (via UICommon.CreateBattleSkillInformationData; falls back to name-only if description unavailable)
  - Bonus announcements: chain bonus (after totals), per-character Training and Open Eyes bonuses
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

- **World map navigation** — IMPLEMENTED AND TESTED (2026-03-07):
  - World map has no Unity NavMesh — uses game's custom A* pathfinder instead
  - Reachability: CalcHeight path sampling (10 points along line to target) detects ocean barriers
  - Distance caps: chests max 200m, enemies max 150m (reduces 50+ items to nearby handful)
  - Locations category: cities/dungeons from ConstWorldmapSymbolParameter, scenario-progress filtered
  - Location names resolved via localityID -> GetLocalityParameter -> localityNameID -> TextManager
  - No reachability filter on locations (false negatives would hide targets permanently for blind users)
  - Auto-walk: per-frame WorldmapFindPath (game's A* pathfinder), navigates around terrain
  - Stuck detection: cancels if player moves < 2 units in 3 seconds
  - Coordinate wrapping handled: fresh positions each frame (stored waypoints go stale)
  - Arrival radius: 15m (vs 1.8m for field maps) due to larger world map objects
  - Full technical documentation: docs/worldmap-pathfinding.md

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
- BattleResultHandler: bonus announcements added (chain, Training, Open Eyes) — PENDING TEST
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
