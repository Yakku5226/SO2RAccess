# SO2RAccess — Accessibility Mod for Star Ocean: The Second Story R

SO2RAccess makes **Star Ocean: The Second Story R** (Steam, Windows) playable for blind and visually impaired players. It reads menus, dialogue, and game state aloud through your screen reader, plays audio cues for events you would otherwise only see, and provides guided navigation so you can walk to NPCs, shops, dungeons, and towns without sighted assistance.

The mod is built on [MelonLoader](https://melonwiki.xyz/) and uses the [Tolk](https://github.com/dkager/tolk) library to talk to screen readers (NVDA, JAWS, and others, with SAPI as a fallback).

## Features

- **Full menu narration** — the camp menu and all of its sub-screens (items, equipment, battle skills, status, formation, tactics, item creation, specialties, operations, missions), shops, guilds, save screens, and the game-over menu.
- **Dialogue readout** — conversation text is read aloud, including unvoiced lines the game only shows on screen.
- **Field navigation** — a navigation menu lists nearby NPCs, exits, treasure chests, save points, and interactable objects; pick one and the mod walks you there automatically.
- **World map navigation** — pathfinding auto-walk to towns, dungeons, and fishing spots across the world map, with honest feedback when a destination cannot be reached on foot.
- **Battle accessibility** — target announcements, enemy proximity cues, dodge notifications, and status readouts during real-time battles.
- **Fishing support** — navigate to fishing spots and get an audio cue the moment you can cast.
- **Audio cues** — distinct sounds for events like a nearby enemy, a dodge window, save points, private actions, and bonus gauge progress. Sound and speech output can be toggled independently.
- **Localization-ready** — all spoken strings go through a localization layer, so translations can be added without code changes.

## Requirements

- Star Ocean: The Second Story R (Steam version, Windows, 64-bit) — the free demo works too; install into the demo's folder the same way
- A screen reader (NVDA, JAWS, or another Tolk-supported reader; Windows SAPI works as a fallback)
- [MelonLoader](https://melonwiki.xyz/) (see installation below)

## Installation

1. **Install MelonLoader.**
   Download the MelonLoader installer from the official site or GitHub releases:
   - Website: <https://melonwiki.xyz/>
   - Installer download: <https://github.com/LavaGang/MelonLoader/releases/latest>

   Run the installer, choose *Star Ocean The Second Story R* (point it at the game's `.exe` in your Steam folder, typically `...\Steam\steamapps\common\STAR OCEAN THE SECOND STORY R`), and install the latest MelonLoader version for 64-bit games.

2. **Run the game once.**
   Start the game after installing MelonLoader and quit again. This lets MelonLoader finish its setup and create the `Mods` and `UserData` folders inside the game directory.

3. **Install the mod.**
   From the mod release download:
   - Copy `SO2RAccess.dll` into the game's `Mods` folder.
   - Copy `Tolk.dll` and `nvdaControllerClient64.dll` into the main game folder (the one containing the game's `.exe`).
   - Copy the `Sounds` folder to `UserData\SO2RAccess\Sounds` inside the game directory, so that the `.wav` files end up directly in that folder.

4. **Play.**
   Start your screen reader, then start the game. The mod announces itself once it has loaded. If you hear nothing, check the MelonLoader console/log for errors — the most common cause is `Tolk.dll` not being in the game folder.

### Navigation data (no action needed)

The navigation features rely on two sets of pre-recorded data, and both are **built into `SO2RAccess.dll` itself** — there is nothing extra to install:

- The **world map walkability scan**, used by the world map pathfinder. On first launch the mod automatically extracts it to `UserData\SO2RAccess\worldmap_expel.grid`.
- **Walk-route recordings ("breadcrumbs")** for towns, dungeons, and fields, used for guided walking in those areas. These are read directly from inside the DLL.

The mod also creates a few files of its own in `UserData` as you play (settings, learned NPC names, and any new walk routes it records). If you ever generate your own local copies of the navigation data, those take priority over the versions shipped in the DLL.

## Controls

All mod keys work on any keyboard — the NumPad is never required. The mod does
not take keys away from the game; its defaults were chosen to avoid every key
the game uses. Press **F1** in game at any time to hear this list read aloud.

### Keyboard

- **F1** — help (reads the full control list)
- **F2** — toggle dialogue voice mode (full text, or name only when a line is voiced)
- **F3** — read your current Fol (money)
- **F4** — open or close the mod settings menu
- **Minus and Equals** — previous / next navigation category
- **Left bracket and Right bracket** — previous / next item in the category
- **Backslash** — walk to the selected item; press again while walking to stop
- **Apostrophe** (while the camp menu is open) — read the current story hint
- **P** (while the Quick Recovery prompt is open) — read party status

Navigation needs no opening or closing: press any navigation key on a field or
the world map and the mod scans your surroundings automatically. Moving on your
own (W A S D or the arrow keys) quietly stops an auto-walk.

In the battle pause menu the same four navigation keys change what you hear:
**Minus and Equals** switch the amount of detail (info tier), and the
**brackets** switch between characters.

In the mod settings menu: **Up and Down arrows** move between settings,
**Left and Right arrows** change the value, **Enter** opens a submenu, and
**Escape** or **F4** closes and saves.

The last item, **Key bindings**, opens a submenu where every mod key can be
changed: arrows move through the actions, **Enter** on an action asks for the
new key — the next key you press becomes the binding. If the new key is already
used by the game or by another mod action, you hear a warning but the key is
still accepted. **F5** through **F11** are reserved for the mod's debugging
hotkeys — they are not listed in the submenu, and binding one of them to an
action gives a warning that the key will only work while debug mode is off.
Changes only take effect when you activate **Save and go back**
at the bottom of the list; **Escape** leaves the submenu and discards them.
There is also a **Reset all keys to defaults** item. The menu's own keys
(arrows, Enter, Escape) always stay the same and cannot be rebound.

### Gamepad

The mod lives on **L2** — hold it like a shift key. L1 stays untouched for the
game's own pickpocketing.

- **Hold L2** — open the navigation overlay (on a field or the world map)
- **L2 held, D-pad Up / Down** — previous / next category
- **L2 held, D-pad Left / Right** — previous / next item
- **L2 held, push the left stick up** — walk to the selected item
- **Release L2** — close the overlay
- **L2 + L3** (click the left stick) — open or close the mod settings menu
- **L2 + R3** (click the right stick) — read your current Fol
- **L3 alone** — story hint in the camp menu; party status in Quick Recovery
- **Battle pause menu: L1 / R1** — more / less detail about the selected character

Moving the left stick (without L2) quietly stops an auto-walk. In the mod
settings menu the D-pad navigates and changes values, **Cross/A** opens a
submenu or activates an item, and **Circle/B** goes back or closes. Key
rebinding itself is keyboard-only — the gamepad layout is fixed.

## Known issues

See [KNOWN_ISSUES.md](KNOWN_ISSUES.md) for current limitations and quirks.

## Credits

- **Yakku** — project direction, design, and testing.
- **[MelonLoader](https://github.com/LavaGang/MelonLoader)** — the mod loader this project runs on.
- **[Tolk](https://github.com/dkager/tolk)** by Davy Kager — screen reader abstraction library.
- **Gemioli / Square Enix** — Star Ocean: The Second Story R. This is an unofficial fan-made accessibility mod and is not affiliated with or endorsed by Square Enix.

### Sound credits

The notification sounds are sourced from [Freesound](https://freesound.org/) under Creative Commons licenses. The full listing also ships with the mod in `Sounds\Game sound license directory.txt`.

- Triple beep (dodge notification) by **andersmmg** — <https://freesound.org/people/andersmmg/sounds/511491/>
- Menu Beep (save sound) by **CogFireStudios** — <https://freesound.org/s/531511/> — License: Creative Commons 0
- Dangerous City (enemy approach) by **pholosho_seloane** — <https://freesound.org/s/548162/> — License: Creative Commons 0
- Mobile Phone Notification Sound (private action notification) by **TheArbuzikYT** — <https://freesound.org/s/840284/> — License: Creative Commons 0
- Menu Beep (bonus gauge fill) by **DrMrSir** — <https://freesound.org/s/529560/> — License: Attribution 4.0
- bubble_big (fishing spot arrival) by **cdonahueucsd** — <https://freesound.org/s/337133/> — License: Attribution 4.0

## License

This mod is released under the [MIT License](LICENSE). You are free to use, modify, and redistribute it — including in your own projects — as long as you keep the copyright notice, which credits the original author. See the `LICENSE` file for the full text.

The bundled sounds keep their own licenses as listed under [Sound credits](#sound-credits).
