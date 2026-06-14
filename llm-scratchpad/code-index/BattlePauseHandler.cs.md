# BattlePauseHandler.cs (992 lines)

Announces battle pause menu information via screen reader in a tiered system.
When Start/Options is pressed during battle, detects the pause menu, polls character
selection, and announces info in tiers (Tier 0: basic; Tier 1+: weaknesses,
resistances, conditions, equipment, cooking, music, leader). Tier cycling via
D-pad up/down or NumPad 8/2; character cycling via D-pad left/right or NumPad 4/6.
Detection: polling GameUIManager.IsShowingBattlePause() + currentCharacterIndex.
Data capture: Harmony postfixes on UIBattlePauseCharacterPresenter hooks.

namespace: SO2RAccess (line 9)
usings (non-System / notable only): Il2CppGame, HarmonyLib, MelonLoader, UnityEngine

## class BattlePauseHandler (line 26)
Announces battle pause menu character info to the screen reader using a tiered system.

fields/properties (declaration order):
- _patchesApplied : bool (line 30)
- _battleWindow : UIBattleWindow (line 33)
- _pauseSelector : UIBattlePauseSelector (line 34)
- _findCooldown : int (line 35)
- _isPauseOpen : bool (line 38)
- IsPauseOpen : bool (line 41)  — public read-only property
- _lastCharIndex : int (line 44)
- _characterChangePending : bool (line 45)
- _currentTier : int (line 48)
- _tiers : List<TierData> (line 49)
- _iconCategoryMap : Dictionary<IntPtr, string> (line 52)  — sprite pointer → category name; built once per pause open
- _cachedHp : static int (line 55)
- _cachedHpMax : static int (line 55)
- _cachedIsEnemy : static bool (line 56)
- _cachedMp : static int (line 57)
- _cachedMpMax : static int (line 57)
- _cachedElementals : static List<CachedElemental> (line 58)
- _cachedAllBuffs : static List<CachedBuff> (line 59)
- _cachedTargetName : static string (line 60)

## struct TierData (line 62)  [private, nested in BattlePauseHandler]
- Label : string (line 64)
- Content : string (line 65)

## struct CachedElemental (line 68)  [private, nested in BattlePauseHandler]
- Resistance : string (line 70)
- Type : ElementResistanceType (line 71)

## struct CachedBuff (line 74)  [private, nested in BattlePauseHandler]
- Description : string (line 76)
- IconPtr : IntPtr (line 77)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony) (line 88)
  - note: Registers Harmony postfixes on UIBattlePauseCharacterPresenter.SetHp, SetMp, SetElemental, SetAllBuffList, SetTargetName. Runs RuntimeHelpers.RunClassConstructor for all relevant types before patching.

- static void SetHp_Postfix(int hp, int hpMax, bool isEnemy) (line 171)
  - note: Postfix for UIBattlePauseCharacterPresenter.SetHp. Caches hp, hpMax, isEnemy into static fields.

- static void SetMp_Postfix(int mp, int mpMax) (line 185)
  - note: Postfix for UIBattlePauseCharacterPresenter.SetMp. Caches mp, mpMax.

- static void SetElemental_Postfix(Il2CppSystem.Collections.Generic.List<UIElementalData> dataList) (line 198)
  - note: Postfix for UIBattlePauseCharacterPresenter.SetElemental. Copies IL2CPP list to managed List<CachedElemental>.

- static void SetAllBuffList_Postfix(Il2CppSystem.Collections.Generic.List<UIBattlePauseAllBuffData> dataList) (line 225)
  - note: Postfix for UIBattlePauseCharacterPresenter.SetAllBuffList. Copies IL2CPP list to managed List<CachedBuff>, capturing icon pointer.

- static void SetTargetName_Postfix(string name) (line 253)
  - note: Postfix for UIBattlePauseCharacterPresenter.SetTargetName (CallerCount 0, called from native). Caches displayed name.

- void OnSceneChanged() (line 272)
  - note: Resets all instance and static state on scene change; calls ClearHookCaches().

- static void ClearHookCaches() (line 286)
  - note: Zeroes all static hook-cached values (_cachedHp/Mp/Elementals/Buffs/TargetName).

- void Update() (line 306)
  - note: Called every frame from Main.UpdateHandlers(). Four-step: (1) throttled UIBattleWindow find, (2) detect pause open/close, (3) handle 1-frame _characterChangePending delay, (4) poll currentCharacterIndex for changes.

- void BuildIconCategoryMap() (line 424)
  - note: Maps UIDefine.PauseBuffDebuffIcon sprite pointers to category strings ("equipment", "cooking", "music", "leader", "conditions"). Called once on pause open.

- void MapIcon(UIDefine.PauseBuffDebuffIcon, string) (line 455)
  - note: Resolves sprite via _pauseSelector.GetIconSprite and stores pointer → category in _iconCategoryMap.

- void BuildTiers() (line 479)
  - note: Builds _tiers list for current character: always adds Tier 0 (basic), then conditionally adds weaknesses, resistances, conditions, and buff-category tiers if non-empty.

- string BuildBasicTier(BattleCharacter, bool, int, int) (line 546)
  - note: Builds Tier 0 announcement. Reads HP/MP from CharacterParameter directly. For enemies, checks spectacles. For allies, calls ResolveAllyName. Returns defeated/ally/enemy/unknown Loc string.

- string ResolveAllyName(BattleCharacter, CharacterParameter) (line 622)
  - note: Four-fallback name resolution: (1) _cachedTargetName hook, (2) CharacterParameter.CharacterName, (3) ParameterManager → BattlePlayerParameter → constPlayer.charaNameID → TextManager, (4) "Ally".

- string BuildWeaknessesTier() (line 681)
  - note: Filters _cachedElementals for DOUBLE resistance type; joins as comma list.

- string BuildResistancesTier() (line 697)
  - note: Filters _cachedElementals for HALF/DISABLE/ABSORB; joins as comma list.

- string BuildStatusConditionsTier(BattleCharacter) (line 729)
  - note: Reads CharacterParameter.GetBuffDebuffList(INVALID), resolves names via TextManager, skips BREAK/INVALID entries.

- void BuildBuffCategoryTiers() (line 777)
  - note: Groups _cachedAllBuffs by icon pointer → category map into equipment/cooking/music/leader lists; adds non-empty lists as tiers.

- void TierUp() (line 852)
  - note: Decrements _currentTier (no wrap); calls AnnounceTier().

- void TierDown() (line 863)
  - note: Increments _currentTier (no wrap); calls AnnounceTier().

- void AnnounceTier() (line 878)
  - note: Tier 0 announces content directly; other tiers use "Label: Content. X of Y." Loc format.

- void CycleCharacterLeft() (line 909)
  - note: Wraps from first to last character. Sets currentCharacterIndex, clears caches, calls RefreshPauseUI + BuildTiers + AnnounceTier.

- void CycleCharacterRight() (line 939)
  - note: Wraps from last to first character. Sets currentCharacterIndex, clears caches, calls RefreshPauseUI + BuildTiers + AnnounceTier.

- void RefreshPauseUI() (line 969)
  - note: Calls _pauseSelector.UpdateCharacterPresenter(), .UpdateBuffDebuffList(), .UpdateBonusEffect() to force game UI refresh after programmatic index change.
