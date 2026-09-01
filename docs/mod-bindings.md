# SO2RAccess — Default Input Bindings

`ModKeys.cs` is the single source of truth for every binding below. All
keyboard bindings can be changed in the mod settings menu (F4 → Key bindings);
user overrides are stored in `UserData/SO2RAccess/settings.json` and applied on
startup. The keys listed here are the shipped defaults.

Verification: with debug mode on (F12), the mod logs the game's live keyboard
and gamepad bindings and a FREE/CLASHES verdict for every mod key
(`InputBindingDump.cs`). Run it after a game update or after rebinding game
keys in the game's own config.

## Keyboard

### Always available

- F1 — speak the help text (all bindings)
- F2 — toggle dialogue voice mode (full text / name only when voiced)
- F3 — read current Fol (money)
- F4 — open or close the mod settings menu
- F12 — toggle debug mode (also runs the binding dump when turning on)

### Navigation (field and world map, modeless)

The navigation list lives in the background — there is no open/close. Each key
builds or refreshes the list automatically when needed. When a menu, dialogue,
or battle is up, these keys do nothing and pass through to the game.

- Minus — previous category (also refreshes the list if it is older than 10 seconds)
- Equals — next category (same refresh rule)
- Left bracket — previous item in the category
- Right bracket — next item in the category
- Backslash — walk to the selected item; press again while walking to cancel

Moving manually (W A S D or arrows) silently cancels an auto-walk.

### Battle pause menu (while open)

Same physical keys as navigation, different context — no conflict.

- Minus — info tier down
- Equals — info tier up
- Left bracket — previous character
- Right bracket — next character

### Context keys

- Apostrophe — story hint (only while the camp menu is open)
- P — party status (only while the Quick Recovery overlay is open)

### Mod settings menu (while open)

- Up and Down arrows — previous / next setting
- Left and Right arrows — decrease / increase the value
- Enter — open a submenu (currently only Key bindings)
- Escape or F4 — close and save

These menu keys are fixed — rebinding never changes them.

### Key bindings submenu (last item of the settings menu)

- Up and Down arrows — previous / next action (all mod actions except the
  debug-only hotkeys (F5–F11 and Semicolon), then "Reset all keys to defaults" and
  "Save and go back")
- Enter on an action — capture mode: the next key pressed becomes the
  pending binding; Escape cancels the capture
- Enter on "Save and go back" — apply the pending bindings, write them to
  `settings.json`, return to the settings list
- Escape — discard all pending changes and return to the settings list

Clash warnings are passive: a captured key that the game also uses (checked
against the live binding dump) or that another mod action holds in an
overlapping context is announced as a warning but still accepted. Capturing
a debug-only key warns that it is reserved for debugging and only works while
debug mode is off — it is still accepted. Gamepad
in the submenu: D-pad navigates, Cross/A activates, Circle/B discards and
goes back; pad buttons cannot be captured — rebinding is keyboard-only.

### Debug only (F12 mode on)

F5 obstacle scan, F6 collision streaming trace, F7 route auditor,
F8 CharaWall scan, F9 grid bake, F10 travel mask diagnostics,
F11 pathfinding diagnostics, Semicolon on-screen text dump (logs every visible
text with its object path — use it to find text the mod is not reading).
These keys are fixed — they do not appear in the key bindings submenu.

## Gamepad

The mod's modifier is L2 (left trigger). L1 is not touched — it belongs to the
game (pickpocket on the field, battle arts).

- Hold L2 — open the navigation overlay (field only)
- L2 held, D-pad Up / Down — previous / next category
- L2 held, D-pad Left / Right — previous / next item
- L2 held, push left stick up — walk to the selected item
- Release L2 — close the overlay silently
- L2 plus L3 — open or close the mod settings menu
- L2 plus R3 — read current Fol
- L3 alone (camp menu open) — story hint
- L3 alone (Quick Recovery open) — party status
- Battle pause menu: L1 — info tier up, R1 — info tier down (these are free
  while the pause menu is open; character cycling is the game's own D-pad)
- Mod settings menu open: D-pad navigates and changes values, Circle/B closes

While L2 is held, the mod suppresses the game actions bound to L2 (looked up
live from the game's bindings) plus D-pad movement and shortcuts, so holding
the overlay never triggers game functions. Moving the left stick (without L2)
cancels an auto-walk silently.

## The game's own battle keys (keyboard, from the live dump 2026-08-30)

Not mod bindings — recorded here because they are documented nowhere else.
These are the game's defaults; they change if rebound in the game's config.

- F — normal attack (and confirm)
- C — dodge / step avoid (and cancel)
- Q and E — battle skill 1 and 2 (the L1/R1 special art slots)
- Tab — battle menu (items, spells, strategy, escape)
- R — change command (the quick strategy/operations list)
- T — battle pause (the status screen the mod reads in tiers)
- Left Ctrl — switch which character you control
- Left Shift — target change mode
- Left Alt — target lock
- Digits 1 to 4 — assault actions
- Space — skip effects

## Design rules

- NumPad is never used — the mod must work on numpadless keyboards.
- Keys are physical QWERTY positions (Unity Input System `Key` values); on
  other layouts the printed symbol may differ from the spoken name.
- Same key in two contexts that can never be active together (navigation vs
  battle pause) is deliberate reuse, not a clash.
- A mod key is only "safe" if the live binding dump says FREE — the mod cannot
  consume keys away from the game, so a clash means both react.
