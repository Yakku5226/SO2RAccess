# BattleMenuHandler.cs (1077 lines)

Announces the in-battle command menu (Triangle) via screen reader. Covers four sub-screens:
Phase A: Root menu (Item / Battle Skill / Tactics / Escape); Phase B: Item sub-menu (Recovery / Combat tabs);
Phase C: Spell/skill sub-menu (per-character tabs); Phase D: Target selection; Phase E: Tactics.
All navigation is native C++ (CallerCount 0) — uses polling pattern. Data capture via Harmony postfixes.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): Il2CppGame, HarmonyLib, MelonLoader

## class BattleMenuHandler (line 23)
Announces the in-battle command menu sub-screens via polling and Harmony postfixes.

fields/properties (declaration order):
- _patchesApplied : bool (line 27)
- _battleWindow : UIBattleWindow (line 30)
- _menuSelector : UIBattleMenuSelector (line 31)
- _itemSelector : UIBattleItemSelector (line 32)
- _spellSelector : UIBattleSpellSelector (line 33)
- _targetSelector : UIBattleSelectCharacterSelector (line 34)
- _tacticsSelector : UIBattleTacticsSelector (line 35)
- _findCooldown : int (line 36)
- PHASE_NONE : const int = 0 (line 39)
- PHASE_MENU : const int = 1 (line 40)
- PHASE_ITEM : const int = 2 (line 41)
- PHASE_SPELL : const int = 3 (line 42)
- PHASE_TARGET : const int = 4 (line 43)
- PHASE_TACTICS : const int = 5 (line 44)
- PHASE_OTHER : const int = 99 (line 45)
- _lastPhase : int = -1 (line 46)
- _wasWindowOpen : bool (line 47)
- _lastMenuIndex : int = -1 (line 50)
- _lastItemIndex : int = -1 (line 53)
- _lastItemTab : int = -1 (line 54)
- _lastSpellIndex : int = -1 (line 57)
- _lastSpellTab : int = -1 (line 58)
- _lastTargetIndex : int = -1 (line 61)
- _lastTargetIsEnemy : bool (line 62)
- _lastTargetAllAnnounced : bool (line 63)
- _lastTacticsCharIndex : int = -1 (line 66)
- _lastTacticsState : int = -1 (line 67)
- _lastTacticsOpIndex : int = -1 (line 68)
- _tacticsOpListBase : UIListSelectorBase (line 69)
- _cachedInfoLabel : static string (line 72)  — written by SpellInfoData_Postfix
- _cachedInfoEffect : static string (line 73)
- _cachedInfoRange : static string (line 74)
- _cachedInfoValueLabel : static string (line 75)
- _cachedInfoValue : static int (line 76)
- _cachedOpName : static string (line 77)
- _cachedOpDesc : static string (line 78)
- _cachedRangeDesc : static string (line 79)
- _cachedEffectDesc : static string (line 80)
- _cachedUseDescTitle : static string (line 81)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 91)
  - note: Registers Harmony postfixes on UIBattleSpellInformationPresenter.Set, UIBattleSkillEffectRangePresenter.Set, UIBattleUseDescriptionPresenter.Set, UICampOperationInformationPresenter.Set; uses RuntimeHelpers.RunClassConstructor for 23 types.
- static void SpellInfoData_Postfix(UIBattleSpellInformationData data) (line 207)
  - note: Postfix for UIBattleSpellInformationPresenter.Set(UIBattleSpellInformationData). Caches label, effectDescription, rangeDescription, valueLabel, value.
- static void EffectRange_Postfix(string rangeDescription, string effectDescription) (line 224)
  - note: Postfix for UIBattleSkillEffectRangePresenter.Set(string, string, List<ElementID>). Caches range/effect desc.
- static void UseDescription_Postfix(string title) (line 237)
  - note: Postfix for UIBattleUseDescriptionPresenter.Set(string, ElementID, List<string>). Caches skill/item name shown during target selection.
- static void OperationInfo_Postfix(string name, string description) (line 249)
  - note: Postfix for UICampOperationInformationPresenter.Set(string, string, string). Caches tactics operation name + description.
- void OnSceneChanged() (line 267)
- void Update() (line 280)
  - note: Lazy-finds UIBattleWindow (60-frame cooldown), detects window open/close, dispatches to per-phase pollers.
- private void ResetAllState() (line 349)
- private void ResetPollingState() (line 356)
- private static void ClearHookCaches() (line 372)
- private int IdentifyPhase(UISelectorBase peekSelector) (line 394)
  - note: Compares top-of-stack selector against cached selector references; also checks tacticsSelector.operationListSelector for PHASE_TACTICS.
- private void HandlePhaseTransition(int newPhase, int oldPhase) (line 416)
  - note: Resets per-phase polling state and announces headings on phase entry. Root menu heading only on first open (oldPhase < 0).
- private static void ClearInfoCache() (line 457)
- private void PollRootMenu() (line 470)
- private void PollItemSelector() (line 504)
  - note: Detects tab changes (Recovery/Combat) first; announces tab heading then returns. Falls through to index change.
- private void AnnounceItem(int idx) (line 538)
  - note: Reads item name from hook cache → info presenter label text → ParameterManager fallback. Reads count from ItemManager.
- private static string ResolveItemName(int itemID) (line 614)
- private static int ResolveItemCount(int itemID) (line 629)
- private void PollSpellSelector() (line 644)
  - note: Detects tab (character) changes first; announces character name heading then returns. Falls through to index change.
- private void AnnounceSpell(int idx) (line 676)
  - note: Reads range/effect from _cachedRangeDesc/_cachedEffectDesc (hook) with _cachedInfoRange/_cachedInfoEffect fallback.
- private string ResolveSpellCasterName(int tabIndex) (line 725)
  - note: Resolves caster name via spellcasterPlayerIDList → ParameterManager.charaNameID → TextManager; falls back to TextUtil.ParseCharaNameID.
- private void PollTargetSelector() (line 767)
  - note: Handles AoE (isSelectedAll) announce-once, then dispatches to PollAllyTarget or PollEnemyTarget.
- private void PollEnemyTarget() (line 803)
  - note: Reads HP as exact (if spectacled), percent (if not), or 0 (if dead). Uses BattleTargetHandler for name resolution and duplicate disambiguation.
- private void PollAllyTarget() (line 863)
  - note: Self-targeting (list count == 1) uses a dedicated Loc key; otherwise announces name, HP, MP.
- private string ResolveUseDescTitle() (line 904)
  - note: Returns skill/item name from hook cache → useDescriptionPresenter.title text → _cachedInfoLabel fallback.
- private void PollTacticsSelector() (line 936)
  - note: Dispatches to PollTacticsCharacter (state 0) or PollTacticsOperation (state 1); resets sub-state on state change.
- private void PollTacticsCharacter() (line 965)
- private void PollTacticsOperation() (line 992)
  - note: Lazy-finds operation list via TryCast<UIListSelectorBase>. Reads op name from hook cache → UICommonListItemPresenter.textMesh fallback. Checks isSetting on UIOperationListItemData for "current" flag.
