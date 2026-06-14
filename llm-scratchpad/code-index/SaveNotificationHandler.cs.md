# SaveNotificationHandler.cs (217 lines)

Handles save-related accessibility announcements:
1. Reads the auto-save notification dialog at new game start
   (UIDialogWindow.SetupAutoSaveAnnounce postfix).
2. Detects saves via three methods (debounced to 2s):
   - Harmony prefix on GameSaveManager.Save (CallerCount 3, managed saves).
   - Harmony postfix on GameSaveManager.OnSaveSuccess (CallerCount 1, completion backup).
   - Per-frame polling of GameSaveManager.IsSaving() (backup for auto-saves).
namespace: SO2RAccess (line 7)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, System.Runtime.CompilerServices

## class SaveNotificationHandler (line 20)
Save detection and announcement. Uses RuntimeHelpers.RunClassConstructor before patching
to ensure IL2CPP types are initialized.

fields/properties (declaration order):
- _patchesApplied : bool (line 27)
- _wasSaving : bool (line 28)  — previous-frame IsSaving() result for edge detection
- _lastSaveAnnouncedTime : static float (line 31)  — debounce timestamp; static so both static hooks and instance Update() share it; initialized to -10f

methods (declaration order):

- void ApplyPatches(HarmonyLib.Harmony harmony) (line 38)
  - note: Idempotent (guards with _patchesApplied). Runs class constructors for UIDialogWindow and GameSaveManager before patching. Applies three patches: SetupAutoSaveAnnounce postfix, Save prefix, OnSaveSuccess postfix.

- static void SetupAutoSaveAnnounce_Postfix(UIDialogWindow __instance) (line 92)
  - note: Postfix for UIDialogWindow.SetupAutoSaveAnnounce. Reads presenter.message.text, strips tags, announces via ScreenReader. Falls back to Loc.Get("save_autosave_announce_fallback") if text unavailable.

- static void Save_Prefix() (line 125)
  - note: Prefix for GameSaveManager.Save. Delegates to OnSaveDetected().

- static void OnSaveSuccess_Postfix() (line 142)
  - note: Postfix for GameSaveManager.OnSaveSuccess. Delegates to OnSaveDetected(). Catches saves not intercepted by Save_Prefix (e.g. auto-saves that bypass managed Save()).

- void Update() (line 162)
  - note: Called each frame from Main.UpdateHandlers(). Polls GameSaveManager.IsSaving(); calls OnSaveDetected() on rising edge (_wasSaving false → true).

- static void OnSaveDetected() (line 192)
  - note: Core debounce+announce. Skips if < 2s since last announcement. If ModSettings.SaveSoundEnabled, calls AudioCuePlayer.PlaySaveCue(). (Note: no ScreenReader.Say here — audio cue only.)

- void OnSceneChanged() (line 209)  — resets _wasSaving to false on scene change
