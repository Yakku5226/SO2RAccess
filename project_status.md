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
**Currently working on:** Phase 3 — stale announcement bugs fixed (shop + camp)
**Blocked by:** Nothing — framework fully working in-game

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

- **Tutorial boxes** (`NotificationHandler.cs`)
  - "Tutorial. [title]. [description]" on each tutorial page
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

- **Camp item sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED

- **Post-battle result announcements** (`BattleResultHandler.cs`) ✓ TESTED
  - Some stat-change popups may not read — user to test further

- **Camp equip sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED

- **Camp skills sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED

- **Save game screen detection** (`LoadGameHandler.cs`) ✓ TESTED

- **Shop menu announcements** (`ShopHandler.cs`) ✓ TESTED
  - "Shop." when shop opens, root menu reads Buy/Sell/Cancel with position
  - Item browsing reads name + Fol price + position (buy and sell modes)
  - Quantity selection reads count + total Fol on change

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

- **Navigation Events category** (`NavigationHandler.cs`) — NOT TESTED
  - New "Events" category added to nav list (5th category after Markers)
  - Scans FieldEventCollision objects, filtered by IsEventActivate() (only active triggers shown)
  - Classified as "Story event", "Private action", "Side event", or generic "Event"
  - Numbered by type in distance order (e.g. "Story event 1", "Private action 2")
  - NavMesh reachability filter applied; static transforms (LiveTransform = null)

- **Navigation Save Points category** (`NavigationHandler.cs`) ✓ TESTED
  - New "Save Points" category added to nav list (6th category)
  - Uses FieldManager.Instance.FieldSavePointList (game-managed list)
  - Labels: "Save point" or "Recovery save point" based on IsRecovery property
  - Numbered when multiples of the same type exist (e.g. "Save point 1", "Recovery save point 2")
  - NavMesh reachability filter applied; live transform tracking for auto-walk

- **Camp formation sub-screen announcements** (`CampMenuHandler.cs`) — NOT TESTED (area inaccessible in current game progress)

- **Camp operations sub-screen** (`CampMenuHandler.cs`) — NOT YET BUILT
  - Operations menu opens and reads its sub-menu items (e.g. "Formation", "Tactics")
  - But the child screens (Party Formation, Tactics) do NOT read when opened
  - Needs implementation for Operations → Party Formation and Operations → Tactics

## In-Progress Features

- **Field Navigation — Phase 2 (audio list + auto-run)** (`NavigationHandler.cs`) ✓ COMPLETE AND TESTED
  - F5: open/close navigation list; also cancels auto-run if active
  - NumPad 8/2: navigate up/down within category
  - NumPad 4/6: switch category (NPCs, Chests, Exits, Markers, Events, Save Points)
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

## Pending Tests (Camp Item Sub-screen)

- [x] Camp item screen: "Items." announced when opening item screen ✓
- [x] Camp item screen: name and description announced on navigation ✓
- [ ] Camp item screen: item count verified (need multiple of same item to test)
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

## Pending Tests (Camp Formation Sub-screen)

- [ ] Not yet testable — area inaccessible in current game progress

## Known Bug: Operations Child Screens

- Operations root menu reads its items (Formation, Tactics) ✓
- Operations → Party Formation: silent (not yet built)
- Operations → Tactics: silent (not yet built)

## Known Issues / Future Work

- **Bug: Battle skill menu triggers stale announcement on next camp open** — FIXED:
  All sub-screen gates now preserve their `_xxxWasActive` and `_xxxLastIndex` state when
  the root menu cursor moves away, preventing stale re-activation announcements.
  Same fix also resolved stale item announcements on shop open and camp menu scrolling.

- **Bug: Equip screen missing category name when slot is empty** —
  On the equipment screen, when nothing is equipped in a slot, the equipment category
  (e.g. "Weapon", "Armor", "Boots") does not read. Should announce e.g. "Weapon. None."
  instead of being silent or only saying "None".

- **Camp status detection** — FIXED: Both activeInHierarchy and root-menu-hidden detection
  failed (root menu selector also stays activeInHierarchy=true in sub-screens). Now fully
  hook-driven via UICampStatusSelector.UpdatePresenter (fires on open + character tab change).

- **Bug: L1 nav blocked after camp menu** — FIXED: Camp closure detection used
  gameObject.activeInHierarchy which stays true after camp closes. Changed to
  WindowComponent.IsOpened property which properly reflects open/closed state.

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

## Architecture Decisions

- (none yet)

## Key Bindings (Mod)

### Keyboard
- F1: Help
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

### Current work
Navigation menu — fixed counter NPC filtering bug. Tested and working.
Still pending: Doors, Stairs, Gimmicks, Flavor chat triggers (see next feature candidates).

### Test results (2026-02-27)
- Nav key remap: ✓ NumPad 5 open/close, NumPad 1 auto-walk/cancel
- Gamepad L1 nav: ✓ open/close, D-pad navigation, LStick auto-walk
- Gamepad D-pad suppression: ✓ Quick Heal and other shortcuts blocked while L1 held
- Battle results: ✓ working (some stat popups may not read — re-test later)
- Skills: ✓ working
- Equip: ✓ working
- Save detection: ✓ working
- Status: ✓ working (fixed: hook-driven detection via UpdatePresenter)
- Formation: not testable (area inaccessible)
- Operations child screens (Party Formation, Tactics): not yet built
- Events category in nav list: NOT YET TESTED
- Counter NPC fix (Hahn/shops): ✓ working — functional NPCs behind counters now appear in nav list

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
- In-game test: Navigation Events category (does FieldEventCollision show active triggers?)
- Navigation: Save points (FieldSavePoint via FieldManager.FieldSavePointList) — DONE ✓
- Navigation: Doors (FieldDoor via FieldManager.FieldDoorList) — dungeon doors, open/close state
- Navigation: Stairs (FieldStairs via FieldManager.FieldStairsList) — vertical transitions
- Navigation: Gimmicks (FieldGimmick01-18) — dungeon puzzle objects (warp gates, switches, etc.)
- Navigation: Flavor chat triggers (FieldFlavorChatCollision) — party banter spots
- Operations child screens: Party Formation + Tactics (not yet built)
- Camp sub-screen: skill learning (UICampSkillLearningSelector — complex, deferred)
- Battle status announcements
- Known bugs to fix: battle skill stale announcement on camp reopen, equip empty slot missing category name

### Notes
- Build command: `dotnet build SO2RAccess.csproj` (auto-copies to Mods folder)
- Map exit names now resolved from game data automatically (ConstFieldParameter + TextManager)
