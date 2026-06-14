# SO2R Decompiled Source — Structural Index for Accessibility Modding

**Purpose:** A finder's index so an AI assistant can quickly locate game classes, understand
the source layout, and know what `docs/game-api.md` already covers vs. what it doesn't.
This document is a complement to game-api.md, not a replacement.

**Last updated:** 2026-06-14

---

## How to Search This Codebase

### Where classes live

- `decompiled/Assembly-CSharp/Il2CppGame/` — 2,701 files. All game logic lives here.
- `decompiled/Assembly-CSharp/Il2CppCommon/` — 121 files. Engine/framework layer:
  singletons, task/state machine base, animation, camera, save sub-modules, sound controllers.
  Rarely need to read these in full; useful when tracing a base class.
- `decompiled/Assembly-CSharp/Il2Cpp/` — 23 files. Unity asset/rendering utilities and
  anonymous compiler types. Ignore unless debugging rendering.
- MoonSharp (Lua interpreter) files are scattered in Il2CppGame but are irrelevant to
  game logic — skip anything under `MoonSharp.*` namespaces.

### Naming conventions (use these as grep/glob patterns)

- `*Manager.cs` — 50 singletons. Access via `ClassName.Instance`.
- `UI*.cs` — 1,086 UI classes. Grouped by prefix (see UI section below).
- `UIListSelectorBase` subclasses — navigable menus; all have `currentIndex` (int) and
  `currentDataList` (List<ListItemDataBase>). These are the prime hook/poll targets.
- `UICanSelectedListItemPresenterBase` subclasses — individual list items with
  `OnSelected(ListItemDataBase)` — hookable for navigation announcements.
- `UIPresenterBase` subclasses — all UI panels; have `Show()` / `Hide()` / `ForceHide()`.
- `Const*Parameter.cs` — 223 data table classes (the game's read-only data).
  Access via `ParameterManager.Instance.GetXxxParameter(...)`.
- `Scriptable*Parameter.cs` — 232 Unity ScriptableObject wrappers for the same data tables.
  These ARE the `Const*Parameter` data at runtime; `ParameterManager` returns `Const*` views.
- `Field*.cs` — 255 field/exploration classes (player, NPCs, enemies, collision, AI, gimmicks).
- `Battle*.cs` — 306 battle system classes (characters, AI, skills, effects, tasks).
- `*ID.cs` — 39 enum files for typed IDs (PlayerID, ShopID, QuestID, FieldmapID, etc.).
- `*Task.cs` — state-machine task nodes used everywhere. Usually not a hook target.
- `*State*.cs` / `*StateMachine*.cs` — finite state machines per subsystem.

### Quick grep recipes

```powershell
# Find all classes that inherit from UIListSelectorBase
Select-String -Path "Il2CppGame\*.cs" -Pattern "UIListSelectorBase" -SimpleMatch

# Find all public methods on FieldManager
Select-String -Path "Il2CppGame\FieldManager.cs" -Pattern "public "

# Find callers of a specific method
Select-String -Path "Il2CppGame\*.cs" -Pattern "ShowFieldIcon" -SimpleMatch

# Find where an enum value is used
Select-String -Path "Il2CppGame\*.cs" -Pattern "NpcType.INN" -SimpleMatch
```

---

## Major Class Clusters

### 1. Managers / Singletons (50 total)

All accessed via `.Instance`. Defined in `Il2CppGame/` unless noted otherwise.

**Core engine (all documented in game-api.md):**
- `GameManager` — core game state machine
- `GameInputManager` — all input; `InputAction` enum; `GetLeftStick()`, `IsDown()`, etc.
- `GameUIManager` — central UI management; window registration
- `TextManager` — text/message lookup; `GetMessage(key, MessageType)`
- `ParameterManager` — all game data tables; ~32K lines; central access point for Const*
- `EventManager` — cutscene/dialogue event scripting; ~18K lines
- `GameSaveManager` — save/load operations
- `GameSoundManager` — BGM/SE playback
- `GameResourceManager` — asset loading
- `ItemManager` — item inventory and item data
- `PartyManager` — party composition

**Field / overworld (partially documented):**
- `FieldManager` — field state, entity lists, current map ID (documented in game-api.md)
- `FieldEnemyManager` — enemy symbol spawn/despawn on field (inferred from name)
- `FieldEnemyScoutManager` — scout/hide enemy mechanics (inferred)
- `FieldGimmickManager` — puzzle/gimmick object management (inferred)
- `FieldFishingManager` — fishing session state (see `docs/` memory: fishing-system.md)
- `FieldFishingPerformanceManager` — fishing animation/performance (inferred)
- `FieldFishShadowManager` / `FieldFishShadowThinkManager` — fish shadow AI (inferred)
- `FieldEnvironmentManager` — environment lighting/weather (inferred)
- `FieldMinimapManager` — minimap tile and icon management (inferred)
- `FieldCollisionManager` — field collision registration (inferred)
- `FieldCullingManager` / `CullingManager` — object visibility culling (inferred)
- `FlavorChatManager` — ambient "flavor" NPC chat triggers (inferred)
- `FieldPhotoModeManager` — photo mode state (inferred)
- `FieldEnemyMusicPopupManager` / `FieldEnemySpecialPopupManager` — enemy music/popup toasts (inferred)

**Battle:**
- `BattleManager` — battle state, character lists, result data; ~5K lines
- `BattleCollisionManager` — attack/hit collision registration (inferred)

**World map:**
- `MinimapManager` / `MinimapOnWorldMapManager` / `WorldMinimapManager` / `WorldPartialMinimapManager` — minimap layers (inferred)
- `WorldMapSilhouetteObjectManager` — world map silhouette visuals (inferred)

**Minigames:**
- `BunnyRaceManager` / `BunnyRaceCameraManager` — bunny race minigame
- `ColiseumManager` — coliseum battle management
- `CookingMasterManager` / `CookingMasterRhythmGameManager` — cooking minigame

**Rendering / misc:**
- `GameRenderManager` — layer masks, render settings (partially documented)
- `GameMovieManager` — FMV cutscene playback (inferred)
- `AstarNodeManager` — A* pathfinding node graph (used by field AI)
- `OnCameraUpdatedEventManager` — camera update event dispatch (inferred)
- `JobFinalizeAfter01Manager` / `JobFinalizeBefore01Manager` — Unity job system hooks (inferred)
- `DistanceLODManager` — LOD distance management (inferred)
- `FieldGrassManager` / `FieldCharacterLightManager` — grass/light rendering (inferred)

---

### 2. Field / Exploration (255 classes total)

**Documented in game-api.md (Sections 16, 18):**
- `FieldManager` — entity lists, player access, map ID
- `FieldPlayer` — player transform, movement
- `FieldNpcCharacter` — NPC runtime instance; NpcType enum
- `FieldEnemy` — enemy symbol on field; EnemySymbolType (Weak/Medium/Strong/Raid); encountID chain for name
- `FieldTreasureBox` — chest open state (use `IsAcquired` property)
- `FieldMapjumpCollision` — map exit/transition triggers
- `FieldLocationPoint` — discoverable location markers
- `FieldDoor` / `FieldStairs` — door and stair objects on field
- `FieldSavePoint` — save point objects

**Field AI (not yet documented):**
- `FieldAIController` / `FieldAIBehavior` — base field AI
- `FieldAIEnemyBehavior` / `FieldAIEnemyChaseBehavior` / `FieldAIEnemyDiscoveryBehavior` — enemy chase/discovery logic
- `FieldAIFollowLeaderBehavior` / `FieldAIFollowNpcBehavior` — party follow AI
- `FieldAIWanderingBehavior` / `FieldAINullBehavior` — idle and null behaviors
- `FieldAIMoveController` / `FieldAIMoveAvoidance` — movement with obstacle avoidance

**Field gimmicks (18 numbered gimmick types, not yet documented):**
- `FieldGimmick01` through `FieldGimmick17` + their controllers/doors/switches
- `FieldGimmickBase` / `FieldGimmickControllerBase` — base classes

**Field character tasks (state machine nodes, not hook targets):**
- `FieldCharacter` (base class; FieldNpcCharacter and FieldPlayer inherit)
- `FieldCharacterMoveTask` / `FieldCharacterJumpTask` / `FieldCharacterIdlingTask`
- `FieldCharacterFishingTask` + fishing sub-tasks
- `FieldCharacterLadderTask` / `FieldCharacterConversationTask`

**Field events and collisions:**
- `FieldEventCollision` — event trigger zones
- `FieldFlagCollision` — flag-triggered events
- `FieldWallCollision` — wall/barrier collisions
- `FieldFlavorChatCollision` — ambient chat trigger zones
- `EventCollision` (base class in Il2CppGame)

**Field states (high-level FSM, not direct hook targets):**
- `FieldStateMachine` + `FieldState*` variants for each mode: `FieldStatePlayable`,
  `FieldStateConversation`, `FieldStateFishing`, `FieldStateEncountEnemy`, `FieldStateMapjump`,
  `FieldStateLocationPoint`, `FieldStateBunny`, `FieldStatePsynard`, `FieldStatePhotoMode`, etc.

**Other field objects:**
- `FieldPsynard` — Psynard (flying creature) field representation
- `FieldBunny` / `FieldRaceBunny` — bunny/race bunny field objects
- `FieldArea` — area zone definitions
- `FieldPhotoSpot` — photo spot locations

---

### 3. Battle System (306 classes total)

**Documented in game-api.md (MEMORY sections):**
- `BattleManager` — battle state singleton
- `BattleCharacter` — base battle character (players and enemies share this base)
- `BattlePlayer` — player character in battle; `BattlePlayerParameter` for stats
- `BattleEnemy` — enemy in battle; `BattleEnemyParameter`; `CharacterName` is often empty in battle (use charaNameID fallback from field)
- `BattleAssistCharacter` — assist/partner mechanic

**Per-character player classes (one per named character, not documented):**
- `BattleCharacterPlayerClaude`, `BattleCharacterPlayerRena`, `BattleCharacterPlayerCeline`,
  `BattleCharacterPlayerAshton`, `BattleCharacterPlayerBowman`, `BattleCharacterPlayerChisato`,
  `BattleCharacterPlayerDias`, `BattleCharacterPlayerEdge`, `BattleCharacterPlayerErnest`,
  `BattleCharacterPlayerFidel`, `BattleCharacterPlayerLaeticia`, `BattleCharacterPlayerLeon`,
  `BattleCharacterPlayerNoel`, `BattleCharacterPlayerOpera`, `BattleCharacterPlayerPrecis`,
  `BattleCharacterPlayerRaymond`, `BattleCharacterPlayerWelch`

**Per-enemy-type classes (one per enemy species, not documented):**
- `BattleCharacterEnemyLizard`, `BattleCharacterEnemyWolf`, `BattleCharacterEnemySheep`, etc.
  (30+ entries — contain per-species special AI overrides)

**Battle AI:**
- `BattleAIController` / `BattleAIBehavior` — base AI
- `BattleAIPlayerBehavior` / `BattleAIEnemyBehavior` — player/enemy branches
- `BattleAIAttackerBehavior` / `BattleAIRecoveryBehavior` / `BattleAIProtectBehavior`
- `BattleAISpellCasterBehavior` / `BattleAITacticsBehavior`
- `BattleAIMoveController` / `BattleAIMoveAvoidance`

**Battle tasks / actions (state machine, not direct hook targets):**
- `BattleCharacterActionTask` / `BattleCharacterMoveTask` / `BattleCharacterAttackNotifyTask`
- `BattleCharacterDamageTask` / `BattleCharacterDeadTask` / `BattleCharacterReviveTask`
- `BattleCharacterStepAvoidTask` / `BattleCharacterGuardTask`
- `BattleAttackCollision` — the collision object carrying attack data; has `OwnerCharacter`
- `DamageResult` — value type; DO NOT use as ref/out in Harmony hooks (IL2CPP limitation)

**Battle state / result data:**
- `BattleCharacterState` — per-character state enum
- `BattleCharacterHistoryParameter` — battle statistics per character
- `BattleCharacterResumeParameter` — resume-after-pause data

---

### 4. UI Classes (1,086 total)

**Base class hierarchy (important for Harmony hooks):**
- `UIPresenterBase` — all UI panels (Show/Hide/ForceHide/SetActive)
- `UIAnimationPresenterBase` — panels with open/close animations
- `UIListSelectorBase` — navigable lists; `currentIndex` (int), `currentDataList` (List)
- `UIScrollListSelectorBase` — scrollable variant of UIListSelectorBase
- `UIVariableListSelectorBase` — variable-height variant
- `UICanSelectedListItemPresenterBase` — individual list items with `OnSelected`
- `UIListItemPresenterBase` — simpler non-selectable item presenters
- `UISelectorBase` — selection widget base
- `UISelectItemSelectorBase` / `UISelectItemPresenterBase` — item-picker variants
- `UIStackSelectorWindowBase` — window that stacks selectors (used by UIGameOverWindow)
- `UICharacterTabListSelectorBase` — tabbed character list base
- `UIItemListSelectorBase` — item-list specialization
- `UIControllerBase` → `UIBattleController` / `UIFieldController` — the top-level HUD controllers

**Camp menu (233 classes; partially documented in game-api.md sections 17+MEMORY):**
- `UICampWindow` — top-level camp window; holds all sub-selectors as fields
- `UICampMenuSelector` — the root camp menu (NOT UICampCommandSelector; see MEMORY)
- `UICampCommandSelector` — command list within root menu (documented)
- `UICampCommandListItemData` / `UICampCommandListItemPresenter` — individual command items
- `UICampItemSelector` / `UICampItemListSelector` / `UICampItemListItemPresenter` — items sub-screen (documented)
- `UICampStatusSelector` / `UICampStatusPresenter` / `UICampStatusParameterData` — status sub-screen (documented)
- `UICampEquipSelector` / `UICampEquipItemListSelector` — equipment sub-screen
- `UICampBattleSkillSelector` / `UICampBattleSkillListSelector` — battle skill sub-screen
- `UICampSkillSelector` / `UICampSkillListItemData` — skill/talent sub-screen
- `UICampFormationSelector` / `UICampFormationListItemData` — formation sub-screen
- `UICampOperationSelector` / `UICampOperationSelectListSelector` — operation sub-screen
- `UICampSpecialSkillGlobalSelector` — entry point for all special skills (item creation)
- `UICampSpecialSkill*Selector` / `UICampSpecialSkill*ActionSelector` — one pair per craft type:
  Alchemy, Art, BlackSmith, Cooking, Craft, Customize, Duplicate, Familiar, Fishing,
  Machinery, MasterShef, Mixing, Music, OpenEyes, Oracle, Orchestra, PickPocket, Publishing,
  Remake, ReverseSide, Scout, Survival, Training, Writing, ComeonBunny, CutIn, SuperAppraisal
- `UICampSpecialSkillResultSelector` / `UICampSpecialSkillResultListItemData` — creation result screen
- `UICampSpecialSkillSelectMaterialSelector` / `UICampSpecialSkillItemListSelector` — material pickers
- `UICampBattleMemberListSelector` / `UICampBattleMemberSelectItemData` — character tab bar
- `UICampPartyMemberSelector` / `UICampPartyMemberSelectItemData` — party member picker
- `UICampQuickRecoverySelector` — quick heal (documented in MEMORY)
- `UICampEnemyPictureBookSelector` / `UICampFishPictureBookSelector` / `UICampLocationPictureBookSelector` / `UICampItemPictureBookSelector` — picture book / compendium sub-screens
- `UICampSpeechBalloonPresenter` — speech balloon UI in camp (inferred)
- `UICampTutorialListSelector` — tutorial list within camp (inferred)
- `UICampSelectCharacterSelector` / `UICampSelectSpecialSkillSelector` — nested pickers
- `UICampAssistSettingSelector` / `UICampAssistEquipListItemData` — assist settings sub-screen

**Battle UI (148 classes; partially documented in game-api.md/MEMORY):**
- `UIBattleController` — top-level battle HUD controller
- `UIBattleWindow` — battle window component
- `UIBattleMenuSelector` / `UIBattleMenuItemData` / `UIBattleMenuItemPresenter` — battle pause menu
- `UIBattleItemSelector` / `UIBattleItemListItemData` — item use in battle
- `UIBattleSpellSelector` / `UIBattleSpellItemData` — spell use in battle
- `UIBattleSelectEnemySelector` — enemy targeting selector
- `UIBattleSelectCharacterSelector` — character selection in battle
- `UIBattleStatusSelector` / `UIBattleStatusListItemData` — battle status list
- `UIBattlePauseSelector` / `UIBattlePauseCharacterListPresenter` — pause screen
- `UIBattleTacticsSelector` / `UIBattleTacticsOperationListSelector` — tactics (AI instructions)
- `UIBattleResultSelector` / `UIBattleResultCharacterData` / `UIBattleResultLevelUpData` — battle result screen
- `UIBattleSkillDialogSelector` — skill equip dialog in battle
- `UIBattleItemSpectaclesSelector` / `UIBattleItemSpectaclesData` — Spectacles item usage
- `UIBattleEnemyHPPresenter` / `UIBattleEnemyBreakGaugeSelector` — enemy HP/break UI
- `UIBattlePlayerStatusPresenter` / `UIBattleGaugePresenter` — player HP/MP gauges
- `UIBattleAssistPresenter` / `UIBattleAssistGaugePresenter` — assist system UI
- `UIBattleSphereBonusPresenter` — sphere bonus UI
- `UIBattleDamagePresenter` / `UIBattleDamageSelector` — damage number display
- `UIBattleEncountPresenter` — encounter entry animation
- `UIBattleOperationPresenter` / `UIBattleOperationSelector` — in-battle operation menu
- `UIBattleChangeControlPlayerPresenter` — control character switch UI
- `UIBattleOffScreenTargetSelector` — off-screen target indicator
- `UIBattleMinimapSelector` — minimap during battle

**Field HUD UI (55 classes; partially documented in game-api.md Section 18):**
- `UIFieldController` — central field notification controller (documented)
- `UIFieldWindow` — field window component
- `UIFieldOperationPresenter` — button prompt (e.g., "X Jump"); best hook: `Set(...)` [CallerCount(7)]
- `UIFieldLabelOperationPresenter` — labeled button prompt variant
- `UIFieldIconSelector` / `UIFieldIconPresenter` — world-space icons (LocationPoint/Fishing only)
- `UIFieldLocationPointPresenter` — location discovery; `Set(...)` hookable
- `UIFieldSymbolNamePresenter` / `UIFieldSubSymbolNamePresenter` — area/sub-area name banner
- `UIFieldModePresenter` — mode banner (e.g., stealth label)
- `UIFieldEmotionSelector` / `UIFieldEmotionPresenter` — NPC emotion bubbles
- `UIFieldShortcutPresenter` — shortcut HUD
- `UIFieldAutoSavePresenter` — auto-save indicator
- `UIFieldInformationStackSelector` — stacked info toasts queue
- `UIFieldItemInformationStackPresenter` — item-acquired toast
- `UIFieldFavorabilityInformationPresenter` — favorability change toast
- `UIFieldPickPocketSelector` — pickpocket choice UI
- `UIFieldQuickRecoverySelector` — field quick recovery selector
- `UIFieldFishingBaitSelector` / `UIFieldFishingTargetPresenter` / `UIFieldFishingResultPresenter` — fishing UI
- `UIFieldFishingWaterPlaceInformationSelector` — fishing spot info
- `UIFieldCookingPresenter` — cooking in-field UI (inferred)
- `UIFieldPhotoSpotSelector` / `UIFieldPhotoSpotGuestListSelector` — photo spot UI

**Dialogue & popup (19 classes; documented in game-api.md Sections 13-15):**
- `UIConversationWindow` / `UIConversationPresenter` / `UIConversationSelector` — dialogue system
- `UIConversationLogSelector` / `UIConversationLogListItemData` — dialogue log/backlog
- `UIDialogWindow` / `UIDialogPresenter` / `UIDialogDescriptionPresenter` / `UIDialogOnOffPresenter` — popups
- `UITutorialWindow` / `UITutorialInformationPresenter` / `UITutorialPopupSelector` — tutorial

**Shop UI (11 classes; partially documented in MEMORY):**
- `UIShopWindow` — shop top window
- `UIShopMenuSelector` / `UIShopMenuListItemData` — buy/sell menu
- `UIShopItemListSelector` / `UIShopItemListItemData` — item list
- `UIShopMoneyPresenter` — money display
- `UIShopCharacterStatusPresenter` / `UIShopEquipChangePresenter` — equip-change preview

**Save/Load UI (7 classes; documented in game-api.md Section 12):**
- `UISaveLoadWindow` / `UISaveLoadSelector` / `UISaveLoadListItemData` — documented
- `UISaveLoadWarningPresenter` — overwrite warning popup (inferred)
- `UISaveLoadPresenter` / `UISaveLoadListItemPresenter` / `UISaveLoadSelectorData`

**Quest & Mission UI (25 classes; not yet documented):**
- `UIQuestWindow` / `UIQuestSelector` / `UIQuestMenuSelector` — quest menu
- `UIQuestListItemData` / `UIQuestListItemPresenter` — quest list items
- `UIQuestDescriptionPresenter` / `UIQuestRequesterPresenter` / `UIQuestRewardElementPresenter`
- `UIMissionWindow` / `UIMissionListSelector` / `UIMissionListItemData` / `UIMissionListItemPresenter`
- `UIMissionInformationSelector` — mission detail view
- `UIMissionReceiveAchievementSelector` / `UIMissionReceiveRewardScrollPresenter` — reward claim
- `UIMissionStatePresenter` / `UIMissionRewardDescriptionPresenter`

**World Map UI (21 classes; not yet documented):**
- `UIWorldMapWindow` / `UIWorldMapPresenter` — world map window
- `UIWorldMapFastTravelSelector` / `UIWorldMapFastTravelSelectorData` — fast travel menu
- `UIWorldMapCurrentLocationSelector` — current location display
- `UIWorldMapLocationSelector` / `UIWorldMapLocationListItemData` — location list
- `UIWorldMapIconPresenter` / `UIWorldMapIconItemData` — map icon display
- `UIWorldMapFieldNamePresenter` — field name display on world map
- `UIWorldMapPointSelector` — point-of-interest selector

**Title screen UI (36 classes; partially documented in game-api.md):**
- `UITitleWindow` / `UITitlePresenter` / `UITitleMenuSelector` — title screen root
- `UITitleSelectHeroSelector` / `UITitleSelectVoiceSelector` — hero/voice selection
- `UITitleSelectDifficultySelector` / `UITitleSelectLanguageSelector` — difficulty/language
- `UITitleNewGameSelector` — new game flow
- `UITitleVoiceGallerySelector` / `UITitleVoiceGalleryCharaListSelector` — voice gallery
- `UITitleOriginalStaffSelector` — original staff credits
- `UITitleLicenseSelector` / `UITitlePressAnyButtonSelector`

**Config / settings UI (37 classes; partially documented in game-api.md):**
- `UIConfigWindow` / `UIConfigMenuSelector` — config root
- `UIConfigSystemSelector` / `UIConfigBattleSelector` / `UIConfigDisplaySelector`
- `UIConfigGraphicsSelector` / `UIConfigBrightnessSelector` / `UIConfigSoundVolumeSelector`
- `UIConfigGamePadSelector` / `UIConfigKeyboardSelector` / `UIConfigKeyboardListSelector`
- `UIConfigEveryCharacterVoiceSelector` / `UIConfigVoiceLanguageSelector`
- `UIConfigGroupSelectorBase` / `UIConfigGroupSelectItemSelector` — group config pattern
- `UIConfigGroupGaugeSelectItemSelector` — slider config item

**Minigame UI:**
- Bunny Race (15): `UIBunnyRaceWindow`, `UIBunnyRaceBetSelector`, `UIBunnyRaceMedalShopSelector`, etc.
- Coliseum (5): `UIColiseumWindow`, `UIColiseumCharacterSelector`, `UIColiseumReadyCheckSelector`, etc.
- Cooking Master (40): `UICookingMasterWindow`, `UICookingMasterRhythmGameSelector`, etc.

**Minimap UI (5 classes):**
- `UIMinimapPresenter` / `UIMinimapPresenterBase` — field minimap
- `UIMinimapOnWorldMapPresenter` — minimap overlay on world map
- `UIMinimapIconPresenter` / `UIMinimapOnWorldMapIconPresenter`

**Game Over (5 classes; documented in MEMORY):**
- `UIGameOverWindow` / `UIGameOverSelector` / `UIGameOverListItemData` / `UIGameOverListItemPresenter`

**Common / shared UI (18 classes):**
- `UICommonListPresenter` / `UICommonListItemData` / `UICommonListItemPresenter` — generic list
- `UICommonSelectTextPresenter` — text-swap animator (see game-api.md Known Issues)
- `UICommonSelectCharacterListSelector` — shared character picker
- `UICommonAnimationPresenter` / `UICommonCountPresenter`
- `UICommonBookListItemData` / `UICommonBookListItemPresenter` — shared book/compendium rows

**Other notable UI classes:**
- `UIGameOverWindow` — extends `UIStackSelectorWindowBase` (pattern for polled-only windows)
- `UIItemCreationInformationPresenter` / `UIItemCreationLevelPresenter` — item creation info panels
- `UIAssistDialogSelector` — assist dialog (inferred)
- `UIAchievementSelector` — achievement popup (inferred)
- `UIChallengeBattleSelector` / `UIChallengeBattleRuleSelector` — challenge battle
- `UICaptionSelector` — caption/subtitle selector (inferred)

---

### 5. Data Tables — Const*Parameter (223 classes)

All accessed via `ParameterManager.Instance.GetXxxParameter(...)`. Each `Const*` has a
matching `Scriptable*` asset. Only the most accessibility-relevant ones are listed here.

**Documented in game-api.md:**
- `ConstNpcParameter` — NPC data: Name, position, NpcType, shopID, conversationDistance
- `ConstFieldmapEncountParameter` — enemy encounter: enemyPartyID link for field enemy naming
- `ConstEnemyParameter` — enemy data: charaNameID (name key), stats, battleSkills
- `ConstEnemyPartyParameter` / `ConstEnemyPartyMemberParameter` — enemy party composition
- `ConstLocationPointParameter` — discovery marker: locationNameID, rewardID

**High value, not yet documented:**
- `ConstItemParameter` — item data: name, description, category, price, usability
- `ConstBattleSkillParameter` / `ConstBattleSkillLevelParameter` — battle skill data, SP costs
- `ConstSkillParameter` / `ConstSkillSetParameter` — field/special skills
- `ConstSpecialSkillParameter` / `ConstSuperSpecialSkillParameter` — item creation skills
- `ConstQuestParameter` / `ConstMissionParameter` — quest/mission text and rewards
- `ConstShopParameter` / `ConstShopProductParameter` / `ConstShopCommonProductParameter` — shop inventory
- `ConstMapjumpParameter` / `ConstMapjumpGroupParameter` — map exit data (destination names)
- `ConstFastTravelParameter` — fast travel destination list
- `ConstPlayerParameter` — player character base stats and names
- `ConstRewardParameter` — reward definitions (EXP, Fol, items)
- `ConstLevelupParameter` — level-up thresholds and rewards
- `ConstLearningBattleSkillParameter` — which skills are learned at which level
- `ConstFactorParameter` / `ConstFactorProbabilityParameter` — item factor (enchantment) data
- `ConstBuffDebuffParameter` — status effect data: name, description, effect
- `ConstFormationParameter` — formation data
- `ConstSavePointParameter` — save point data (position, map)
- `ConstTreasureBoxParameter` — chest contents by ID
- `ConstFlavorChatParameter` — ambient NPC chat text
- `ConstPrivateActionEventGroupParameter` / `ConstPrivateActionEventSequenceParameter` — Private Action events

**Item creation data:**
- `ConstAlchemyParameter` / `ConstCraftParameter` / `ConstMixingParameter` / `ConstMixingCombinationParameter`
- `ConstBlackSmithParameter` / `ConstMachineryParameter` / `ConstCookingParameter`
- `ConstCustomizeParameter` / `ConstRemakeParameter` / `ConstAppraisalParameter`
- `ConstCreationManualParameter` / `ConstCreationSettingsParameter` / `ConstCreationSuccessSettingsParameter`

**Picture books / compendiums:**
- `ConstEnemyBookParameter` / `ConstFishBookParameter` / `ConstItemBookParameter`

---

### 6. ID Enums (39 files)

These are pure enum files; their values are used everywhere as typed IDs.
Relevant ones for accessibility mods:

- `PlayerID` — identifies which party member (used in UIFieldController toasts, battle)
- `FieldmapID` — map identifiers (technical codes like `MF_0001_01A`; no human-readable table)
- `MapjumpID` — map exit identifiers (codes only)
- `ShopID` — shop identifiers
- `QuestID` / `MissionID` — quest and mission identifiers
- `LocationPointID` — discovery marker identifiers
- `BattleSkillID` / `SkillID` / `SpecialSkillID` / `SuperSpecialSkillID` — skill identifiers
- `BuffDebuffID` — status effect identifiers
- `TalentID` — talent identifiers
- `NpcType` (not a separate file; inside FieldNpcCharacter) — INVALID, NORMAL, INN, SHOP_EQUIPMENT, SHOP_ITEM, GUILD, CHECK, OTHER, FACILITY, FISH_COLLECTOR, SHOP_FOOD, BED, PSYNARD, MAX
- `FactorID` — item factor (enchantment) identifiers
- `ElementID` — elemental attribute identifiers
- `FormationID` — formation identifiers
- `AssistID` — assist character identifiers
- `FastTravelID` / `WorldmapID` — world map travel identifiers
- `TutorialID` — tutorial entry identifiers

---

### 7. Save Data Architecture

**Core save classes (not documented in game-api.md):**
- `GameSaveManager` — singleton; handles load/save triggers
- `GameSaveDataCreator` / `GameSaveDataCreatorBase` — builds save data object
- `GameSaveDataLoader` / `GameSaveDataLoaderBase` — reads save data; multiple version-dated subclasses:
  `GameSaveDataLoader_20220303`, `_20220921`, `_20221027`, `_20230127`, `_20230210`, etc. — versioned migration chain
- `GameSaveDataHeader` / `GameSaveDataHeaderLoader` + versioned header loaders — save file header
- `GameRetryDataCreator` / `GameRetryDataLoader` — battle retry save subset

**In Il2CppCommon (base layer):**
- `SaveManager` / `SaveSubModule` / `WindowsSaveSubModule` / `SteamSaveSubModule`

---

### 8. Event / Dialogue Architecture

**Documented in game-api.md and MEMORY:**
- `EventManager` — script execution engine; ~18K lines
- `UIConversationPresenter` — dialogue text display; best hook: private `SetMessage(string, string, string, bool, ref Rect)` postfix

**Not yet documented:**
- `EventState` / `EventStateMachine` / `EventStateRun` / `EventStateLoadScript` — event state machine
- `EventCollision` — trigger zone base class
- `EventPlacementData` / `EventPlacementParameter` — event object placement data
- `EventMoveObjectTask` / `EventCameraTask` / `EventShakeCameraTask` — cutscene task nodes (many variants)
- `EventUtility` — utility methods for events (worth reading for discovery)
- `EventDefine` — event-related enum definitions
- `FlavorChatManager` / `ConstFlavorChatParameter` — ambient NPC chat separate from main dialogue
- `ConstPrivateActionEventGroupParameter` / `ConstPrivateActionEventSequenceParameter` — Private Action (PA) event data

---

### 9. Item Creation / Special Skills

The camp "Special Skills" menu hosts all crafting/creation types. Not yet documented.

**Core:**
- `SpecialSkill` — base special skill class
- `SuperSpecialSkill` — super special skill variant
- `ItemCreation` / `ItemCreationResult` / `ItemCreationResultType` — item creation outcome data

**Per-craft-type SpecialSkill data parameters (Const* tables):**
- Alchemy, Art, BlackSmith, Cooking, Craft, Customize, Duplicate, Familiar, Machinery,
  MasterShef, Mixing, Music, OpenEyes, Oracle, Orchestra, PickPocket, Publishing,
  Remake, ReverseSide, Scout, Survival, Training, Writing

**Key UI entry points for each type:**
- `UICampSpecialSkillGlobalSelector` — the hub selector that routes to each type
- `UICampSpecialSkill[Type]Selector` — per-type material selection
- `UICampSpecialSkill[Type]ActionSelector` — per-type action/confirm step
- `UICampSpecialSkillResultSelector` — final result display (universal)

**Appraisal (item identification):**
- `ConstAppraisalParameter` / `ConstSpecialAppraisalParameter`
- `UICampSpecialSkillAppraisalSelector` / `UICampSpecialSkillAppraisalActionSelector`
- `UIItemCreationInformationPresenter` — shows result info

---

### 10. Quest & Guild System

**Not documented in game-api.md (MEMORY notes Guild as "native wall"):**
- `QuestUtility` / `MissionUtility` — utility functions for quest/mission lookups
- `QuestCategory` / `MissionCategory` / `MissionType` — categorization enums
- `ConstQuestParameter` — quest data: text, requirements, rewards
- `ConstMissionParameter` — mission (guild task) data
- `UIQuestWindow` / `UIQuestSelector` / `UIQuestMenuSelector` — quest browsing UI
- `UIMissionWindow` / `UIMissionListSelector` / `UIMissionInformationSelector` — guild mission UI
- `UIMissionReceiveAchievementSelector` — achievement reward claim

---

### 11. Il2CppCommon Framework Layer

Key base classes that Il2CppGame classes inherit from:

- `SingletonMonoBehaviour<T>` — the `.Instance` singleton base; most managers use this
- `SingletonBasicMonoBehaviour<T>` — lightweight singleton variant (TextManager uses this)
- `BaseMonoBehaviour` — standard MonoBehaviour with common utilities
- `TaskBase` / `TaskComponent` — the task/state-machine node base
- `GameSceneBase` — scene base class; each scene type inherits
- `StateMachine<T>` — generic FSM used by field, battle, event state machines
- `SaveManager` / `SaveSubModule` — base save I/O layer
- `SoundManager` / `SoundController` / `SeController` — audio system base
- `InputManager` / `KeyboardManager` — low-level input (wrapped by GameInputManager)
- `FadeManager` — screen fade transitions
- `EffectManager` — particle/effect pooling
- `ObjectPool` — generic object pool
- `UIManager` — base UI manager (GameUIManager extends this)
- `GameSceneManager` — scene loading/transition

---

## Gaps vs. docs/game-api.md

The following areas exist in the decompiled source but are NOT documented in game-api.md.
These are the most valuable to document for future accessibility features:

### Gap 1: Quest & Mission Menu (HIGH PRIORITY)
- MEMORY notes the Guild menu is a "native wall" but doesn't document the Quest system.
- `UIQuestWindow`, `UIQuestSelector`, `UIQuestMenuSelector`, `UIQuestListItemData`,
  `UIQuestDescriptionPresenter`, `ConstQuestParameter`, `QuestUtility` — none in game-api.md.
- Quest notifications probably flow through `UIFieldController` info toasts.

### Gap 2: Battle Result Screen (HIGH PRIORITY)
- `UIBattleResultSelector`, `UIBattleResultCharacterData`, `UIBattleResultLevelUpData`,
  `UIBattleResultExperiencePresenter`, `UIBattleResultBattleSkillPresenter` — not documented.
- The result screen after battle is the primary feedback moment for blind players.
- MEMORY documents `DoCollisionReceiveAction` for damage tracking but the results screen itself is undocumented.

### Gap 3: Shop System (MEDIUM PRIORITY)
- `UIShopWindow`, `UIShopMenuSelector`, `UIShopItemListSelector`, `UIShopItemListItemData`,
  `ConstShopParameter`, `ConstShopProductParameter` — not documented.
- MEMORY mentions shop polling but no field-level documentation exists.

### Gap 4: Item Creation / Special Skills (MEDIUM PRIORITY)
- 28 crafting types each with Selector + ActionSelector + Const* data — none documented.
- `UICampSpecialSkillGlobalSelector` as the hub, `UICampSpecialSkillResultSelector` as the
  universal result screen, and `ItemCreation` / `ItemCreationResult` as the outcome objects.
- Current MEMORY only covers the "going silent" bug fix (commit 88defb6).

### Gap 5: World Map UI (MEDIUM PRIORITY)
- `UIWorldMapWindow`, `UIWorldMapFastTravelSelector`, `UIWorldMapLocationListItemData`,
  `ConstFastTravelParameter` — not documented in game-api.md.
- World map auto-walk is documented in MEMORY but the UI layer for fast travel is not.

### Gap 6: Save Data Versioning Architecture (LOW PRIORITY for mods)
- 12+ versioned `GameSaveDataLoader_*` classes show the save format migration chain.
- Not needed for accessibility but relevant if the mod ever needs to read save data.

### Gap 7: FieldmapID / MapjumpID Human-Readable Names (KNOWN GAP)
- game-api.md Section 16 notes this limitation but does not document the lookup approach.
- `ConstMapjumpParameter` and `ConstFastTravelParameter` likely contain destination name IDs.
- These could be used to resolve exit names without a custom lookup table.

### Gap 8: ConstItemParameter — Item Name/Description Lookup (HIGH PRIORITY)
- Items appear in many contexts (chests, shops, battle items, crafting results) but
  `ConstItemParameter` fields (name, description, category, price) are not documented.
- `ParameterManager.GetItemParameter(itemID)` access pattern not yet documented.

### Gap 9: Field Gimmick System (LOW PRIORITY)
- 18 numbered gimmick types (`FieldGimmick01` through `FieldGimmick17`) — none documented.
- Relevant if adding accessibility cues for puzzle elements.

### Gap 10: Private Action (PA) System (LOW PRIORITY)
- `ConstPrivateActionEventGroupParameter` / `ConstPrivateActionEventSequenceParameter`,
  `FieldPrivateActionAlphaTask`, `FieldPrivateActionMoveTask` — PA events not documented.
- Could be relevant for announcing when a PA opportunity is available.

---

## Quick Reference: Which File to Read First by Task

- "I need to hook a menu navigation" → read the `UI*Selector.cs` for that menu + `UIListSelectorBase.cs`
- "I need item data (name, description)" → `ConstItemParameter.cs` + grep `ParameterManager` for GetItemParameter
- "I need to know what's in a chest" → `ConstTreasureBoxParameter.cs` + `FieldTreasureBox.cs`
- "I need quest/mission text" → `ConstQuestParameter.cs` / `ConstMissionParameter.cs` + their `UI*ListItemData.cs`
- "I need shop inventory" → `ConstShopParameter.cs` + `ConstShopProductParameter.cs` + `UIShopItemListItemData.cs`
- "I need enemy name in battle" → `ConstEnemyParameter.cs` (`charaNameID` field) — see MEMORY for full chain
- "I need fast travel destinations" → `ConstFastTravelParameter.cs` + `UIWorldMapFastTravelSelector.cs`
- "I need level-up / skill-learn data" → `ConstLevelupParameter.cs` + `ConstLearningBattleSkillParameter.cs`
- "I need status effect name" → `ConstBuffDebuffParameter.cs`
- "I need formation info" → `ConstFormationParameter.cs` + `UICampFormationSelector.cs`
