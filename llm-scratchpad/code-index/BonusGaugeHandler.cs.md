# BonusGaugeHandler.cs (362 lines)

Announces bonus gauge progress and break events during battle. Sound cues play at 25%
(1 beep), 50% (2 beeps), 75% (3 beeps) via AudioCuePlayer.PlayGaugeFillCue. On break
(100%): 4 beeps + screen reader announcement of new level and newly granted buff.
Detection: polling GetBattleSphereBonusCurrentLevelRatio each frame, plus a Harmony
prefix+postfix on BattleManager.BreakBonusGauge (CallerCount 3) for break events.

namespace: SO2RAccess (line 12)
usings (non-System / notable only): Il2CppGame, HarmonyLib, MelonLoader, UnityEngine

## class BonusGaugeHandler (line 21)
Announces bonus gauge progress and break events during battle.

fields/properties (declaration order):
- _patchesApplied : bool (line 24)
- _lastLevel : static int (line 27)
- _announcedThresholds : static readonly HashSet\<int\> (line 28)  — tracks which of {25, 50, 75} beep thresholds have fired for the current level
- _wasInBattle : static bool (line 29)
- _lastAnnouncedGaugeBucket : static int (line 33)  — highest 5% bucket already spoken; -1 = none. Independent of beep thresholds.
- GaugePercentStep : const int = 5 (line 36)  — step in percent between spoken gauge percentages
- _preBreakValues : static readonly Dictionary\<BonusBuffType, float\> (line 39)  — buff snapshot taken in prefix, compared in postfix to find newly granted buff
- _preBreakLevel : static int (line 41)  — captured in prefix to detect spurious BreakBonusGauge initialization calls
- GaugeFillRepeatGap : const float = 0.15f (line 44)  — gap in seconds between repeated beeps in the coroutine
- _allBuffTypes : static readonly BonusBuffType[] (line 47)  — all valid BonusBuffType values iterated for buff detection

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 69)
  - note: Registers BattleManager and BonusBuffType IL2CPP types. Patches BattleManager.BreakBonusGauge(bool) with both prefix and postfix. Method lookup is guarded with a null check; missing method logs a warning rather than throwing.
- void Update() (line 119)
  - note: Bails if BattleManager.Instance is null or battlePlayerList is empty. On battle entry, seeds _announcedThresholds and _lastAnnouncedGaugeBucket from the current ratio so stale gauge progress from the previous battle is not re-announced. Polls ratio each frame for beep thresholds (25/50/75) and spoken percentage (every GaugePercentStep %, capped below 100).
- static void BreakBonusGauge_Prefix(BattleManager __instance) (line 222)
  - note: Prefix for BattleManager.BreakBonusGauge. Captures _preBreakLevel, marks all three beep thresholds as announced (prevents duplicate sounds if polling races the hook), and snapshots all active buff values into _preBreakValues.
- static void BreakBonusGauge_Postfix(BattleManager __instance) (line 260)
  - note: Postfix for BattleManager.BreakBonusGauge. Skips if postLevel <= _preBreakLevel (spurious call). Plays 4 beeps, finds the newly granted buff by comparing post-break buff values against _preBreakValues snapshot, announces level and buff name via ScreenReader.SayQueued.
- static IEnumerator PlayGaugeFillCoroutine(int count) (line 328)
  - note: Plays gauge fill sound 'count' times with GaugeFillRepeatGap seconds between plays. Launched via MelonCoroutines.Start.
- void OnSceneChanged() (line 345)
  - note: Calls Reset() to clear all tracking state.
- static void Reset() (line 350)  [private]
  - note: Clears _wasInBattle, _lastLevel, _preBreakLevel, _lastAnnouncedGaugeBucket, _announcedThresholds, and _preBreakValues.
