# BattleCounterHandler.cs (68 lines)

No file-level comment block.
namespace: SO2RAccess (line 3)
usings: Il2CppGame, HarmonyLib, MelonLoader, System.Runtime.CompilerServices

## class BattleCounterHandler (line 14)
Plays an audio dodge-warning cue when an enemy is about to hit the player.
Hooks BattleCharacter.DoAttackNotify — the game's own visual "incoming attack" flash trigger.

fields/properties (declaration order):
- _patchesApplied : bool (line 16)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony) (line 21)
  - note: Applies postfix Harmony patch on BattleCharacter.DoAttackNotify(BattleCharacter). Uses RuntimeHelpers.RunClassConstructor to warm up BattleManager and BattleCharacter before AccessTools.Method.
- static void DoAttackNotify_Postfix(BattleCharacter target) (line 51)
  - note: Postfix on BattleCharacter.DoAttackNotify(BattleCharacter target). Fires on the game's incoming-attack flash. Calls AudioCuePlayer.PlayDodgeWarningCue() only if target.IsControlPlayer() and ModSettings.DodgeSoundEnabled.
