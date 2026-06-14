# GameOverHandler.cs (186 lines)

Announces game over (battle loss) menu navigation to the screen reader.
Features: "Game over." heading on screen appear; "Retry, 1 of 2." / "Title, 2 of 2."
on navigation. Detection: UIGameOverWindow found via FindObjectOfType (lazy/throttled).
All menu navigation is native C++ — polling is required (no Harmony hooks fire).
namespace: SO2RAccess (line 6)
usings (non-System / notable only): Il2CppGame, MelonLoader

## class GameOverHandler (line 22)
Polling-based game over menu handler; no Harmony hooks needed for navigation.

fields/properties (declaration order):
- _patchesApplied : bool (line 25)
- _window : static UIGameOverWindow (line 27)
- _isOpen : static bool (line 28)
- _findCooldown : static int (line 29)
- _selector : static UIGameOverSelector (line 31)
- _selectorBase : static UIListSelectorBase (line 32)
- _lastIndex : static int (line 33)
- _menuNames : static string[] (line 37)  — lazy-resolved from Loc; index matches UIGameOverSelector.MenuType enum (Retry=0, Title=1)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 47)
  - note: No Harmony patches applied. Only calls RuntimeHelpers.RunClassConstructor for UIGameOverWindow, UIGameOverSelector, UIGameOverSelectorData to ensure IL2CPP type registration.
- void OnSceneChanged() (line 69)
- void Update() (line 83)
  - note: Called every frame from Main.UpdateHandlers(). Calls DetectWindow(), then UpdateMenu() if open.
- void DetectWindow() (line 99)
  - note: Lazy FindObjectOfType with 60-frame cooldown. Polls _window.IsOpened; on open captures _selector, casts to UIListSelectorBase, announces Loc.Get("gameover_screen"); on close resets state.
- void UpdateMenu() (line 155)
  - note: Polls _selectorBase.currentIndex each frame. Announces Loc.Get("gameover_menu_item") with name and position when index changes. Lazily initializes _menuNames from Loc on first call.
