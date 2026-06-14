# FieldState.cs (47 lines)

Shared field-state queries used by multiple handlers. Centralises checks so all handlers agree on when the field is usable.
namespace: SO2RAccess (line 5)
usings (non-System / notable only): Il2CppCommon, Il2CppGame

## static class FieldState (line 11)
Shared field-state queries — centralises checks so all handlers agree on when the field is usable.

fields/properties (declaration order):
(none)

methods (declaration order):

- static bool IsFieldFree() (line 19)
  - note: returns true only when FieldManager exists, player exists, PauseManager.IsPause is false, EventManager.IsRunning is false, CampMenuHandler.IsCampOpen is false, ShopHandler.IsShopOpen is false; catches all exceptions and returns false
