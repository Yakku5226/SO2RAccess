# ModMenuHandler.cs (388 lines)

Screen-reader-driven mod settings menu. Opened with F4 (keyboard) or L1+L3 (gamepad).
All navigation is purely audio — no game UI involved. Manages a flat list of
ModMenuItems (toggles, volumes, enums). Blocks all game input while open via
SuppressAllGameInput static flag read by GameInputManager Harmony prefixes.

namespace: SO2RAccess (line 6)
usings (non-System / notable only): MelonLoader, UnityEngine.InputSystem

## enum ModMenuItemType (line 12)
Types of settings items in the mod menu.

members:
- Toggle (line 13)
- Volume (line 14)
- Enum (line 15)

## class ModMenuHandler (line 22)
Screen-reader-driven mod settings menu opened with F4 or L1+L3.

fields/properties (declaration order):
- IsOpen : bool (line 27)  — public read-only; whether the mod menu is currently open
- SuppressAllGameInput : static bool (line 33)  — public read-only; checked by GameInputManager Harmony prefixes to block ALL game input while menu is open
- _currentIndex : int (line 35)
- _items : List<ModMenuItem> (line 36)
- _dpadRepeatDir : int (line 39)
- _dpadRepeatTimer : float (line 40)
- DpadRepeatInitial : const float (line 41)  — 0.4f
- DpadRepeatInterval : const float (line 42)  — 0.15f

methods (declaration order):
- void Open() (line 51)
  - note: Calls BuildItems(), resets index and D-pad state, sets IsOpen + SuppressAllGameInput, announces heading + first item.

- void Close() (line 69)
  - note: Clears IsOpen + SuppressAllGameInput, calls ModSettings.Save(), announces close.

- void Toggle() (line 80)

- bool ProcessKeyboard(Keyboard kb) (line 91)
  - note: Called from Main.ProcessHotkeys(). Escape/F4 closes; arrow keys navigate/change value; returns true (consumes) for all keys while open.

- bool ProcessGamepad(Gamepad gp) (line 130)
  - note: Called from Main.ProcessGamepad(). Circle/B closes; D-pad navigates/changes value with auto-repeat (DpadRepeatInitial=0.4s, DpadRepeatInterval=0.15s).

- void FireDpadAction(int dir) (line 178)
  - note: Dispatches dir (1=Up→Navigate(-1), 2=Down→Navigate(+1), 3=Left→ChangeValue(-1), 4=Right→ChangeValue(+1)).

- void Navigate(int delta) (line 189)
  - note: Wraps _currentIndex, announces FormatItem for new index.

- void ChangeValue(int delta) (line 200)
  - note: Calls item.Change(delta), announces label + new value.

- string FormatItem(int index) (line 212)
  - note: Returns Loc.Get("mod_menu_item", label, value, index+1, count).

- void BuildItems() (line 223)
  - note: Constructs _items list from current ModSettings. Items: SaveSoundEnabled, SaveSoundVolume, DodgeSoundEnabled, DodgeSoundVolume, EnemyProximitySoundEnabled, EnemyProximitySoundVolume, PrivateActionSoundVolume, DialogueVoiceMode, AllyHealthWarningEnabled, AllyStatusAilmentEnabled, PlayerDamageDealtEnabled, BonusGaugeSoundVolume, BonusGaugeBreakAnnouncementEnabled, BonusGaugePercentAnnounceEnabled, JumpPromptSoundEnabled, JumpPromptSpeechEnabled.

- static float ClampVolume(float v) (line 366)
  - note: Clamps to [0,1] and rounds to 1 decimal place.

## class ModMenuItem (line 378)  [private, nested in ModMenuHandler]
A single item in the mod settings menu.

fields/properties (declaration order):
- LabelKey : string (line 380)
- Type : ModMenuItemType (line 381)
- GetValue : Func<string> (line 382)
- Change : Action<int> (line 383)
