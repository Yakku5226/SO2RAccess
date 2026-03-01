# Code Index: CampMenuHandler.cs

## Top-Level Comments (lines 11–61)

Class-level XML summary on `CampMenuHandler` describes:
- The purpose: announces camp menu navigation to the screen reader
- All Harmony patches applied and what they target
- The sub-screen hierarchy for each camp screen (root, item, status, equip, battle skill,
  battle skill assignment, formation, skills)
- The polling approach: navigation is native C++ only, so `Update()` polls `currentIndex`
  each frame and announces on change

---

## Class: CampMenuHandler (line 62)

### Fields

Root menu:
- `private bool _patchesApplied` (line 66)
- `private static readonly Regex _spriteNameExtractor` (line 69) — extracts name from `<sprite name=X>` tags
- `private static readonly Regex _tagStripper` (line 72) — strips all remaining rich text tags
- `private static UICampMenuSelector _menuSelector` (line 78)
- `private static int _lastIndex` (line 79)
- `private static bool _wasActive` (line 80)
- `public static bool IsCampOpen` (line 86) — property; used by NavigationHandler to block gamepad nav overlay
- `private static float _campOpenTime` (line 93) — guards against IsOpened returning false during opening animation
- `private static UICampWindow _campWindow` (line 96)

Item sub-screen:
- `private static UICampItemSelector _itemSelector` (line 101)
- `private static UIListSelectorBase _itemListSelectorBase` (line 102)
- `private static int _itemLastIndex` (line 103)
- `private static bool _itemWasActive` (line 104)
- `private static bool _itemSuppressHeading` (line 107) — suppresses "Items." if selector was already active on camp open (stale state)

Equip sub-screen:
- `private static UICampEquipSelector _equipSelector` (line 116)
- `private static bool _equipWasActive` (line 117)
- `private static bool _equipSuppressHeading` (line 118)
- `private static UIListSelectorBase _equipSlotListBase` (line 121)
- `private static int _equipSlotLastIndex` (line 122)
- `private static bool _equipSlotWasActive` (line 123)
- `private static string[] _equipSlotCategoryNames` (line 126) — friendly names for slot indices (Weapon, Armor, etc.)
- `private static UIListSelectorBase _equipItemListBase` (line 129)
- `private static bool _equipItemListActive` (line 130)

Battle skill leveling sub-screen:
- `private static UICampBattleSkillSelector _battleSkillOuterSelector` (line 138)
- `private static UISelectBattleSkillSelector _battleSkillInnerSelector` (line 139)
- `private static UIListSelectorBase _battleSkillListBase` (line 140)
- `private static bool _battleSkillWasActive` (line 141)
- `private static bool _battleSkillSuppressHeading` (line 142)

Status sub-screen:
- `private static UICampStatusSelector _statusSelector` (line 152)
- `private static bool _statusScreenOpen` (line 153)
- `private static int _statusLastIndex` (line 154)
- `private static UICampStatusParameterData _statusParamData` (line 155)
- `private static UICampStatusLevelData _statusLevelData` (line 156)
- `private static string _statusPlayerName` (line 157)
- `private static int _statusLastPageIndex` (line 158)
- `private static string _cachedTalentAnnouncement` (line 164) — built by UITalentPresenter.Set hook on page 0; announced when user switches to talent page
- `private static string _lastRootMenuItemName` (line 166) — tracks which root menu item is highlighted; used as gating signal for all sub-screens

Battle skill assignment sub-screen:
- `private static UICampBattleSkillSettingSelector _battleSkillSettingSelector` (line 178)
- `private static UICampBattleSkillEquipListSelector _battleSkillEquipListSel` (line 179)
- `private static UIListSelectorBase _battleSkillEquipListBase` (line 180)
- `private static UIListSelectorBase _battleSkillPickerListBase` (line 181)
- `private static int _battleSkillEquipLastIndex` (line 182)
- `private static bool _battleSkillSettingWasActive` (line 183)
- `private static bool _battleSkillSettingSuppressHeading` (line 184)

Formation sub-screen:
- `private static UICampFormationSelector _formationSelector` (line 190)
- `private static bool _formationWasActive` (line 191)
- `private static bool _formationSuppressHeading` (line 192)

Skills sub-screen:
- `private static UICampSkillSelector _skillSelector` (line 199)
- `private static bool _skillWasActive` (line 200)
- `private static bool _skillSuppressHeading` (line 201)

Party formation sub-screen:
- `private static UICampSelectCharacterSelector _selectCharSelector` (line 207)
  Note: extends UISelectorBase, NOT UIListSelectorBase; uses GetCurrentIndex() method
- `private static int _selectCharLastIndex` (line 208)
- `private static bool _selectCharWasActive` (line 209)
- `private static bool _selectCharSuppressHeading` (line 210)
- `private static Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData> _selectCharDataList` (line 211)

Assist formation sub-screen:
- `private static UICampAssistSettingSelector _assistSelector` (line 216)
- `private static bool _assistWasActive` (line 217)
- `private static bool _assistSuppressHeading` (line 218)
- `private static UIListSelectorBase _assistEquipListBase` (line 219)
- `private static int _assistEquipLastIndex` (line 220)
- `private static UIListSelectorBase _assistCharListBase` (line 221)
- `private static int _assistCharLastIndex` (line 222)
- `private static int _assistLastState` (line 223) — tracks state 0=Equip vs 1=SelectAssistCharacter

Tactics sub-screen:
- `private static UICampOperationSelector _operationSelector` (line 230)
- `private static bool _operationWasActive` (line 231)
- `private static bool _operationSuppressHeading` (line 232)
- `private static int _operationCharLastIndex` (line 233)
- `private static UIListSelectorBase _operationSelectListBase` (line 234)
- `private static int _operationSelectLastIndex` (line 235)
- `private static int _operationLastState` (line 236) — tracks state 0=SelectCharacter vs 1=SelectOperation

### Methods

#### Patch Application region (lines 240–431)

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 247)
  Note: Calls RuntimeHelpers.RunClassConstructor on every IL2CPP type used before patching,
  then registers all postfix patches. Safe to call multiple times — guarded by `_patchesApplied`.

#### Update (Polling) region (lines 433–1625)

- `public void Update()` (line 439)
  Note: Called every frame from Main.UpdateHandlers(). Checks for camp window closure via
  IsOpened (not activeInHierarchy), then delegates to all sub-screen poll methods.

- `private void UpdateRootMenu()` (line 481)
  Note: Polls `_menuSelector.currentIndex`; announces menu item name and availability.
  Also resets status screen state when the root menu index changes (user returned from status).

- `private void UpdateItemSelector()` (line 563)
  Note: Gated on `_lastRootMenuItemName == "Item"` — sub-screen activeInHierarchy is permanently
  true. Announces item name, quantity, description. Handles stale-open suppression.

- `private void UpdateStatusSelector()` (line 680)
  Note: Despite the XML doc saying "no longer polls," it does poll `pageIndex` to detect L1/R1
  page switches. On switch to page 1 (talents), announces the cached talent string. The main
  status detection is hook-driven via Diag_StatusSelector_UpdatePresenter.

- `private static void AnnounceStatusCharacter(int index, int total)` (line 722)
  Note: Reads from three separately-captured static fields (`_statusPlayerName`,
  `_statusLevelData`, `_statusParamData`) and builds a single combined announcement covering
  name, level, HP, MP, EXP, all combat stats, and all base attributes.

- `private static void CacheEquipSlotCategories()` (line 784)
  Note: Hardcodes the seven equipment slot friendly names in EquipType enum order.

- `private void UpdateEquipSelector()` (line 801)
  Note: Gated on `_lastRootMenuItemName == "Equip"`. Uses `currentState` (not activeInHierarchy)
  to distinguish slot-list vs item-list mode. Delegates slot polling to UpdateEquipSlotList().

- `private void UpdateEquipSlotList()` (line 891)
  Note: Polls the equipment slot list (what is currently equipped in each slot). Announces
  slot category name, equipped item name, and availability. Called only when item list is not open.

- `private void UpdateBattleSkillSelector()` (line 967)
  Note: Gated on `_lastRootMenuItemName == "BattleSkill"`. Only tracks open/close state and
  caches inner selector references. Actual skill announcements are hook-driven.

- `private void UpdateBattleSkillSettingSelector()` (line 1038)
  Note: Gated on `_lastRootMenuItemName == "BattleSkill"`. In Equip state, delegates to
  UpdateBattleSkillEquipSlotList(). In SelectBattleSkill state, the hook handles announcements.

- `private void UpdateBattleSkillEquipSlotList()` (line 1114)
  Note: Polls button-slot list in the skill assignment screen. Reads slot data via
  GetCurrentData() on the typed selector (not via currentDataList cast).

- `private void UpdateFormationSelector()` (line 1156)
  Note: Gated on `_lastRootMenuItemName == "Formation"`. Only tracks open/close and announces
  heading. Actual formation detail announcements are hook-driven.

- `private void UpdateSkillSelector()` (line 1214)
  Note: Gated on `_lastRootMenuItemName == "Skill"`. Only tracks open/close and announces
  heading. Actual skill detail announcements are hook-driven.

- `private void UpdatePartyFormationSelector()` (line 1272)
  Note: Gated on `_lastRootMenuItemName == "PartyFormation"`. Uses GetCurrentIndex() (not
  currentIndex property) because this selector extends UISelectorBase, not UIListSelectorBase.
  Character data comes from the CharacterStatusPresenter_SetStatus_Postfix hook cache.

- `private void UpdateAssistSettingSelector()` (line 1349)
  Note: Gated on `_lastRootMenuItemName == "AssistFormation"`. Has two polling branches based
  on currentState: Equip (0) polls equip slot list; SelectAssistCharacter (1) polls character
  list. Lazily caches the sub-selector references on first use in each state.

- `private void UpdateTacticsSelector()` (line 1507)
  Note: Gated on `_lastRootMenuItemName == "Tactics"`. SelectCharacter state (0) polls the
  operation selector directly cast to UIListSelectorBase. SelectOperation state (1) polls
  position only — actual tactic name/description come from the OperationInfoPresenter hook.

#### Harmony Patch Methods region (lines 1627–2326)

- `private static void CampWindow_Open_Postfix(UICampWindow __instance)` (line 1633)
  Note: Sets IsCampOpen, records open timestamp, caches all sub-selector references from the
  window instance, and handles stale-active detection for each sub-screen. Long method (~310
  lines) — one sequential block per sub-screen.

- `private static void ItemInfoPresenter_Set_Postfix(UIItemInformationData data)` (line 1957)
  Note: Fires on every equip item navigation. Gated on equip screen active AND item list
  active. Announces name, description, battle effect, five combat stats (only non-zero),
  factor name, factor description, and list position.

- `private static void BattleSkillInfoPresenter_Set_Postfix(UIBattleSkillInformationData data)` (line 2033)
  Note: Shared hook for both the leveling screen and the assignment screen's skill picker.
  Branches on which screen is active. In the assignment screen, only fires in SelectBattleSkill
  state; reads current button name from the equip slot selector to prefix the announcement.

- `private static void StatusParamPresenter_Setup_Postfix(UICampStatusParameterData data)` (line 2145)
  Note: Only stores data into `_statusParamData`. The actual announcement is triggered later
  by Diag_StatusSelector_UpdatePresenter.

- `private static void FormationInfoPresenter_Set_Postfix(string formationName, string effectDescription)` (line 2157)
  Note: Gated on formation screen active. Reads list position by casting the selector to
  UIListSelectorBase. Announces name, effect, and position.

- `private static void SkillInfoPresenter_Set_Postfix(UISkillInformationData data)` (line 2202)
  Note: Gated on skill screen active. Announces skill name, level (if > 0), description, and
  list position via cast to UIListSelectorBase.

- `private static void CharacterStatusPresenter_SetStatus_Postfix(Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData> dataList)` (line 2253)
  Note: Only stores the data list into `_selectCharDataList`. Does not announce anything;
  polling in UpdatePartyFormationSelector reads from this cache.

- `private static void OperationInfoPresenter_Set_Postfix(string name, string description, string prefabPath)` (line 2276)
  Note: Gated on tactics screen active AND SelectOperation state (state == 1). Reads
  UIOperationListItemData.isSetting to append "Currently set." if the tactic is already active.

#### Status Screen Hooks region (lines 2328–2471)

- `private static void Diag_StatusSelector_UpdatePresenter(int index, int difference, bool isDelay)` (line 2336)
  Note: Despite "Diag_" prefix, this is the primary status announcement trigger, not a
  diagnostic method. Fires last in the hook chain after UpdateName and LevelPresenter.Setup
  have already populated their data fields. Announces heading on first open, then calls
  AnnounceStatusCharacter. Only announces on the stats page (page 0).

- `private static void Diag_StatusSelector_UpdateName(PlayerID playerID, ConstPlayerParameter playerParam)` (line 2386)
  Note: Despite "Diag_" prefix, this captures the player name into `_statusPlayerName` for
  use by AnnounceStatusCharacter. Uses ParameterManager.GetCharacterFirstName; falls back to
  playerID.ToString() on error.

- `private static void Diag_StatusSelector_UpdateStatusLevel(CharacterParameter charaParam)` (line 2405)
  Note: Despite "Diag_" prefix, this is a no-op hook — it only logs a debug message. The
  actual level data is captured by Diag_StatusLevelPresenter_Setup instead.

- `private static void Diag_StatusLevelPresenter_Setup(UICampStatusLevelData data)` (line 2414)
  Note: Despite "Diag_" prefix, this stores the level/HP/MP/EXP data into `_statusLevelData`
  for use by AnnounceStatusCharacter. Fires before UpdatePresenter in the hook chain.

- `private static void TalentPresenter_Set_Postfix(Il2CppSystem.Collections.Generic.List<UITalentData> dataList)` (line 2427)
  Note: Fires on status screen open (page 0 initialization), not on page switch. Builds and
  caches the full talent list announcement. If already on the talent page when called (e.g.
  after a character tab change while viewing talents), announces immediately.

#### Helpers region (lines 2474–2489)

- `private static string StripTags(string text)` (line 2481)
  Note: Two-pass cleaner. First extracts sprite tag names (e.g. `<sprite name=R1>` becomes
  `R1`), then strips all remaining HTML-like tags. Used for button labels in the battle skill
  assignment screen where category names contain sprite tags.
