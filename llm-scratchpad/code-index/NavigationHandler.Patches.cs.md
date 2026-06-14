# NavigationHandler.Patches.cs (217 lines)

Partial class fragment of NavigationHandler — Harmony patches for input injection and fishing result announcements.
namespace: SO2RAccess (line 9)
usings: HarmonyLib, Il2CppGame, MelonLoader, UnityEngine, UnityEngine.AI

## partial class NavigationHandler (line 12)

fields/properties (declaration order):
- _staticIsAutoWalking : bool (line 17)  [— static; true while auto-walk input injection is active]
- _staticAutoWalkStickDir : Vector2 (line 24)  [— static; synthetic left stick direction injected during auto-walk]
- _staticCameraStickX : float (line 31)  [— static; synthetic camera right-stick X injected during auto-walk for camera follow]
- _lastFishingAnnouncement : string (line 148)  [— static; last fishing result announcement text for dedup]
- _lastFishingAnnouncementTime : float (line 150)  [— static; timestamp of last fishing announcement for dedup]

methods (declaration order):
- static void GetLeftStick_Postfix(ref Vector2 __result) (line 39)
  - note: Harmony postfix on GameInputManager.GetLeftStick(). Replaces returned stick value with _staticAutoWalkStickDir when auto-walking and not in direct-move mode.
- static void GetPlayerControlStick_Postfix(ref Vector2 __result) (line 52)
  - note: Harmony postfix on GameInputManager.GetPlayerControlStick(). Same injection as GetLeftStick; required separately because world map native pipeline reads this method instead of GetLeftStick.
- static void GetFieldCameraRightStick_Postfix(ref Vector2 __result) (line 63)
  - note: Harmony postfix on GameInputManager.GetFieldCameraRightStick(). Injects _staticCameraStickX to rotate camera toward walking direction during auto-walk.
- static bool SuppressNavInput(GameInputManager.InputAction inputAction, ref bool __result) (line 76)
  - note: Shared logic for IsDown/IsRepeat prefixes. Blocks all input when ModMenuHandler.SuppressAllGameInput; when gamepad nav active blocks D-pad (Up=11, Down=12, Left=13, Right=14), ShortCut directions (39-42), and FieldCameraLeft (56).
- static bool IsDown_Prefix(GameInputManager.InputAction inputAction, ref bool __result) (line 112)
  - note: Harmony prefix on GameInputManager.IsDown(). Delegates to SuppressNavInput.
- static bool IsRepeat_Prefix(GameInputManager.InputAction inputAction, ref bool __result) (line 121)
  - note: Harmony prefix on GameInputManager.IsRepeat(). Mirrors IsDown suppression for held D-pad auto-repeat.
- static bool GetDPad_Prefix(ref Vector2 __result) (line 133)
  - note: Harmony prefix on GameInputManager.GetDPad(). Returns Vector2.zero (skips original) when mod menu or gamepad nav is active so D-pad analog input doesn't move the player.
- static void FishingResultSet_Postfix(Il2CppSystem.Collections.Generic.List<UIFieldFishingResultListItemData> fishingDataList) (line 157)
  - note: Harmony postfix on UIFieldFishingResultPresenter.Set(). Announces each caught fish/item with name, size, record/new flags via ScreenReader. Deduplicates repeated calls within 2 seconds.
