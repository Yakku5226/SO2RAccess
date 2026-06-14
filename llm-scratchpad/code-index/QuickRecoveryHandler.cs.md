# QuickRecoveryHandler.cs (353 lines)

Announces the field Quick Recovery ("quick heal") overlay (D-pad Right). Reads the
Yes/No cursor (polled), on-demand party HP/MP status (NumPad 0 or L3), and the
recovery result after GameManager.QuickRecovery executes. Navigation is native-only
(CallerCount 0), so the cursor is polled. The execution hook is shared with the camp
quick-recovery variant and is guarded by a freshness window on the field snapshot.

namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, UnityEngine.InputSystem

## sealed class MemberSnap (line 33)  [private, nested in QuickRecoveryHandler]
Projected per-member outcome captured while the menu is open.

fields/properties (declaration order):
- Name : string (line 35)
- Hp : int (line 36)
- HpMax : int (line 36)
- ChangeHp : int (line 36)
- Mp : int (line 36)
- MpMax : int (line 36)
- ChangeMp : int (line 36)

## class QuickRecoveryHandler (line 29)
Announces the field Quick Recovery menu; polls cursor and announces heal results.

fields/properties (declaration order):
- _selector : UIFieldQuickRecoverySelector (line 38)
- _wasActive : bool (line 39)
- _lastChoice : UIDefine.DialogChoices (line 40)
- _nextFindTime : float (line 41)
- _snapshot : List\<MemberSnap\> (line 43)
- _snapshotTime : float (line 44)
- _healExecuted : static bool (line 47)
- _healExecutedTime : static float (line 48)
- _patchesApplied : static bool (line 49)
- SnapshotFreshWindow : const float = 2f (line 53)  — max age (seconds) for snapshot to be considered field (not camp) recovery

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 63)
  - note: Patches GameManager.QuickRecovery with a postfix only. Safe to call multiple times.
- static void GameManager_QuickRecovery_Postfix() (line 88)
  - note: Postfix for GameManager.QuickRecovery. Sets _healExecuted=true and records _healExecutedTime.
- void Update() (line 102)
  - note: Processes pending heal result first (fires even after menu closes), then lazily finds/re-validates _selector, captures fresh snapshot, announces heading on open, reads party status on NumPad 0 or L3 (requires L1 not held), and polls Yes/No cursor.
- static string ChoiceText(UIDefine.DialogChoices choice) (line 223)  [private]
  - note: Returns localized "Yes" or "No" label via Loc.Get.
- static string ResolveName(PlayerID playerID) (line 231)  [private]
  - note: Resolves member name via ParameterManager.UserParameter.GetCharacterParameter; falls back to title-casing the enum string.
- void CaptureSnapshot() (line 249)  [private]
  - note: Reads recoveryDataList and builds _snapshot of MemberSnap; updates _snapshotTime.
- void AnnouncePartyStatus() (line 282)  [private]
  - note: Reads recoveryDataList; for each member announces HP/MP and projected recovery amounts. Uses changeHp - hp for HP gain, changeMp < mp for MP spent.
- void TryAnnounceResult() (line 326)  [private]
  - note: Announces recovery result from _snapshot. Skips if snapshot is stale relative to _healExecutedTime (guards against camp recovery triggering field result announcement).
