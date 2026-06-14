# BattleTargetHandler.cs (544 lines)

Announces enemy information when the player cycles targets during L2 target
change mode in battle: name, HP percentage (exact if Spectacles used),
shield/break gauge percentage, leader type, and active buffs/debuffs.
Detection: Harmony postfix on BattleManager.SetControlPlayerTarget (CallerCount 7)
plus per-frame polling. Both paths debounced via shared _lastAnnouncedPtr.
Also handles ally control player switching (R2) via controlPlayerIndex polling.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): Il2CppGame, HarmonyLib, MelonLoader, UnityEngine

## class BattleTargetHandler (line 23)

fields/properties (declaration order):
- _patchesApplied : bool (line 25)
- _lastTargetPtr : IntPtr (line 28)  — polling state; tracks last announced target by IL2CPP pointer
- _wasInTargetChangeMode : bool (line 31)
- _lastControlPlayerIndex : int (line 34)
- _controlPlayerSeeded : bool (line 35)
- _lastAnnouncedPtr : IntPtr (line 38)  — static; shared debounce between hook and polling
- BATTLE_STATE_TARGET_CHANGE : int = 5 (line 40)  — const
- BATTLE_STATE_CONTROL_PLAYER_CHANGE : int = 6 (line 41)  — const
- _wasInControlPlayerChangeMode : bool (line 44)

methods (declaration order):

- void ApplyPatches(HarmonyLib.Harmony harmony) (line 49)
  - note: runs RuntimeHelpers.RunClassConstructor for 10 IL2CPP types, then patches BattleManager.SetControlPlayerTarget with SetControlPlayerTarget_Postfix.

- void SetControlPlayerTarget_Postfix(BattleCharacter target) (line 96)
  - note: static Postfix on BattleManager.SetControlPlayerTarget(BattleCharacter, bool). Skips player characters and already-announced pointers. Calls AnnounceTarget.

- void Update() (line 121)
  - note: instance method called each frame from Main.UpdateHandlers(). Three polling cases: (1) target pointer changed, (2) entered TargetChangeMode but same pointer (single-enemy battle), (3) target cleared. Also handles R2 ally switch via controlPlayerIndex; seeds index on first frame without announcing.

- void OnSceneChanged() (line 227)
  - note: resets all tracking state (_lastTargetPtr, _lastAnnouncedPtr, _wasInTargetChangeMode, _lastControlPlayerIndex, _controlPlayerSeeded, _wasInControlPlayerChangeMode).

- void AnnounceTarget(BattleCharacter target) (line 241)
  - note: static; resolves name (ResolveEnemyName + ResolveDuplicateName), HP (exact if spectacled else percent), shield/durability, leader type, buffs/debuffs. Builds parts list and calls ScreenReader.Say. Handles defeated check first.

- void AnnounceControlPlayer(BattleCharacter ally) (line 317)
  - note: static; reads HP, MP from CharacterParameter; resolves ally name via BattleStatusHandler.ResolveAllyName; builds message with/without statusStr.

- string ResolveEnemyName(BattleParameterBase battleParam, CharacterParameter charParam) (line 348)
  - note: internal static; tries CharacterParameter.CharacterName first; falls back to ConstEnemyParameter.charaNameID via TextManager (may work in battle), then TextUtil.ParseCharaNameID ("CHARA_LIZARDAXE" → "Lizardaxe"); final fallback "Enemy".

- bool IsEnemySpectacled(BattleParameterBase battleParam) (line 398)
  - note: internal static; casts to BattleEnemyParameter, gets EnemyID, calls UIBattlePauseSelector.IsSeeThroughEnemy. Returns false on any exception.

- string ResolveLeaderType(BattleParameterBase battleParam) (line 424)
  - note: internal static; casts to BattleEnemyParameter, gets LeaderType, calls ToSystemMessageID(), resolves via TextManager; falls back to enum.ToString(). Returns null if INVALID.

- string ResolveBuffDebuffs(CharacterParameter charParam) (line 456)
  - note: internal static; calls charParam.GetBuffDebuffList(BuffDebuffID.INVALID) to get all active statuses; skips BREAK (already covered by shield gauge) and INVALID; resolves each via ToMessageID() + TextManager; returns comma-separated Loc string or null if none.

- string ResolveDuplicateName(BattleCharacter target, string baseName) (line 497)
  - note: internal static; collects all alive enemies sharing baseName from BattleManager.battleEnemyList, appends " N" suffix for disambiguation. Returns baseName unchanged if only one.
