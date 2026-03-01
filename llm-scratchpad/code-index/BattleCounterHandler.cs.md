# Code Index: BattleCounterHandler.cs

## Top-level comments
- Class summary (lines 9-13): Plays an audio cue when an enemy is about to hit the player,
  giving time to press X and dodge. Hooks BattleCharacter.DoAttackNotify — the game's own
  visual "incoming attack" flash trigger.

---

## Class: BattleCounterHandler (line 14)

### Fields
- private bool _patchesApplied (line 16)

### Methods
- public void ApplyPatches(HarmonyLib.Harmony harmony) (line 21)
  Note: Patches BattleCharacter.DoAttackNotify with DoAttackNotify_Postfix. Uses
  RuntimeHelpers.RunClassConstructor on BattleManager and BattleCharacter before patching
  to ensure IL2CPP type constructors have run. Guards against double-patching via _patchesApplied.

- private static void DoAttackNotify_Postfix(BattleCharacter target) (line 51)
  Note: Harmony postfix on BattleCharacter.DoAttackNotify(BattleCharacter target). Fires
  whenever the game triggers its visual "incoming attack" flash on any battle character.
  Only acts when the target is the player (target.IsControlPlayer()). Calls
  AudioCuePlayer.PlayDodgeWarningCue() to play the dodge warning sound.
  Despite the "Counter" name in the class and log message, this is a dodge WARNING cue,
  not a counter-attack cue — the name predates a design change.
