# BattleStatusHandler.cs (437 lines)

namespace: SO2RAccess (line 9)
usings (non-System / notable only): Il2CppGame, HarmonyLib, MelonLoader, UnityEngine

## class BattleStatusHandler (line 25)
Announces combat status events: ally HP below 50%/25%/KO, ally negative status ailments, player-controlled character damage dealt. All announcements via ScreenReader.SayQueued() (non-interrupting). Each feature independently toggled via ModSettings.
Detection: DoCollisionReceiveAction (CallerCount 2) prefix+postfix for HP/damage; SetBuffDebuffState (CallerCount 19) postfix for ailments.

fields/properties (declaration order):
- _patchesApplied : bool (line 28)
- _allyHpState : static readonly Dictionary<IntPtr, int> (line 32)  — HP threshold state per ally (keyed by BattleCharacter.Pointer); 0=above 50%, 1=below 50%, 2=below 25%, 3=KO
- _allyAilments : static readonly Dictionary<IntPtr, HashSet<BuffDebuffID>> (line 36)  — announced ailments per ally (keyed by CharacterParameter.Pointer); cleared on removal so re-application re-announces
- _preDamageHp : static int (line 39)  — HP snapshot captured in prefix, compared in postfix
- _preDamageVictimPtr : static IntPtr (line 40)  — BattleCharacter.Pointer for the victim in the current prefix/postfix pair
- _negativeAilments : static readonly HashSet<BuffDebuffID> (line 43)  — POISON, PARALYZE, PETRIFACTION, CONFUSION, SILENCE, FAINT, DEATH, STOP, SWALLOWED, CONTROLLED

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 65)
  - note: Registers prefix+postfix on DoCollisionReceiveAction(BattleAttackCollision, Vector3) and postfix on SetBuffDebuffState(BuffDebuffID, bool, float, float, float). DamageResult intentionally NOT loaded — ref IL2CPP value type crashes Harmony trampolines. Each patch wrapped in its own try/catch to allow partial success.
- static void DoCollisionReceiveAction_Prefix(BattleCharacter __instance, BattleAttackCollision attackCollision) (line 154)
  - note: Harmony prefix. Snapshots __instance.BattleCharacterParameter.CharacterParameter.HitPoint and __instance.Pointer into _preDamageHp/_preDamageVictimPtr before damage is applied.
- static void DoCollisionReceiveAction_Postfix(BattleCharacter __instance, BattleAttackCollision attackCollision) (line 177)
  - note: Harmony postfix. Computes actualDamage from pre/post HP diff. If attacker is control player and target is not player: announces damage dealt (Feature 5, guarded by ModSettings.PlayerDamageDealtEnabled). If target is player character: calls CheckAllyHealthThreshold (Features 1-3, guarded by ModSettings.AllyHealthWarningEnabled).
- static void SetBuffDebuffState_Postfix(CharacterParameter __instance, BuffDebuffID buffDebuffID, bool flag) (line 235)
  - note: Harmony postfix. On removal (flag=false): clears ailment from _allyAilments tracking. On application (flag=true): checks ModSettings.AllyStatusAilmentEnabled, searches BattleManager.battlePlayerList for the matching CharacterParameter, deduplicates via _allyAilments HashSet, announces via Loc.Get("battle_status_ailment").
- static void CheckAllyHealthThreshold(BattleCharacter ally, int hp, int hpMax) (line 305)
  - note: Computes current HP state, compares to _allyHpState[ptr]. Announces only on downward transitions (health worsening). Updates _allyHpState.
- static int GetHpThresholdState(int hp, int hpMax) (line 342)
  - note: Returns 3 if hp<=0, 2 if <25%, 1 if <25-50%, 0 otherwise.
- internal static string ResolveAllyName(BattleCharacter ally) (line 358)
  - note: Tries CharacterParameter.CharacterName, then BattlePlayerParameter.PlayerParameter.charaNameID via TextManager, then TextUtil.ParseCharaNameID(), then falls back to "Ally".
- static string ResolveBuffDebuffName(BuffDebuffID id) (line 398)
  - note: Calls id.ToMessageID() then TextManager.GetMessage(); falls back to id.ToString().
- void OnSceneChanged() (line 427)
  - note: Clears _allyHpState, _allyAilments, resets _preDamageHp and _preDamageVictimPtr.
