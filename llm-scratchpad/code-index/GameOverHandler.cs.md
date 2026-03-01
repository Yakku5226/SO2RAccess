# Code Index: GameOverHandler.cs

## Top-level comments

- File is in namespace `SO2RAccess`.
- Class-level XML doc summary: announces game over (battle loss) menu navigation to the screen reader.
- Features: "Game over." heading on screen appear; "Retry, 1 of 2." / "Title, 2 of 2." on navigation.
- Detection: `UIGameOverWindow` found via `FindObjectOfType` (lazy init with throttle); `IsOpened` polled each frame.
- All navigation is native C++ — no Harmony hooks fire. Polling is the correct approach (same pattern as camp/shop menus).

---

## Class: GameOverHandler (line 21)

### Fields

- `private bool _patchesApplied` (line 25)
- `private static UIGameOverWindow _window` (line 27)
- `private static bool _isOpen` (line 28)
- `private static int _findCooldown` (line 29)
- `private static UIGameOverSelector _selector` (line 31)
- `private static UIListSelectorBase _selectorBase` (line 32)
- `private static int _lastIndex = -1` (line 33)
- `private static readonly string[] MenuNames = { "Retry", "Title" }` (line 36)
  Note: Names are ordered to match `UIGameOverSelector.MenuType` enum order (index 0 = Retry, index 1 = Title).

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 46)
  Note: Despite the name, no Harmony patches are actually registered. This method only runs `RuntimeHelpers.RunClassConstructor` to initialize IL2CPP types; all announcements are polling-based.

- `public void OnSceneChanged()` (line 68)
  Note: Resets all cached references and state flags to null/-1/false on scene change.

- `public void Update()` (line 86)
  Note: Called every frame from `Main.UpdateHandlers()`. Calls `DetectWindow()` then `UpdateMenu()` if open.

- `private void DetectWindow()` (line 98)
  Note: Lazily finds `UIGameOverWindow` via `FindObjectOfType` with a 60-frame (~1 second) cooldown between search attempts. Polls `IsOpened` to detect open/close transitions and announces "Game over." on open. Caches the selector reference on open. On exception, resets `_window` to null to allow re-discovery.

- `private void UpdateMenu()` (line 153)
  Note: Polls `_selectorBase.currentIndex` each frame and announces the focused item (Retry or Title) with its position only when the index changes. Uses `MenuNames` array for human-readable names.
