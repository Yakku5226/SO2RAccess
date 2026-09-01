# SO2RAccess — Accessibility Mod for Star Ocean: The Second Story R

SO2RAccess makes Star Ocean: The Second Story R (Steam, Windows) playable for blind and visually impaired players. It reads menus, dialogue, and game state aloud through your screen reader, plays audio cues for events you would otherwise only see, and provides guided navigation so you can walk to NPCs, shops, dungeons, and towns without sighted assistance.

The mod is built on [MelonLoader](https://melonwiki.xyz/) and uses the [Tolk](https://github.com/dkager/tolk) library to talk to screen readers (NVDA, JAWS, and others, with SAPI as a fallback).

## Features

- Full menu narration — the camp menu and all of its sub-screens (items, equipment, battle skills, status, formation, tactics, item creation, specialties, operations, missions), shops, guilds, save screens, and the game-over menu.
- Dialogue readout — conversation text is read aloud, including unvoiced lines the game only shows on screen.
- Cutscene subtitles — the subtitle line under movie cutscenes and the caption text events show above characters are read as they appear. Can be turned off in the mod settings menu (F4).
- Field navigation — a navigation menu lists nearby NPCs, exits, treasure chests, save points, and interactable objects; pick one and the mod walks you there automatically.
- World map navigation — pathfinding auto-walk to towns, dungeons, and fishing spots across the world map, with honest feedback when a destination cannot be reached on foot.
- Battle accessibility — target announcements, enemy proximity cues, dodge notifications, and status readouts during real-time battles.
- Fishing support — navigate to fishing spots and get an audio cue the moment you can cast.
- Audio cues — distinct sounds for events like a nearby enemy, a dodge window, save points, private actions, and bonus gauge progress. Sound and speech output can be toggled independently.
- Community translations — all spoken text lives in a plain JSON file; anyone can translate the mod without code changes, and the mod can follow the game's own language setting automatically. See [TRANSLATING.md](TRANSLATING.md).

## Requirements

- Star Ocean: The Second Story R (Steam version, Windows, 64-bit) — the free demo works too; install into the demo's folder the same way
- A screen reader (NVDA, JAWS, or another Tolk-supported reader; Windows SAPI works as a fallback)
- [MelonLoader](https://melonwiki.xyz/) (see installation below)

## Installation

1. Install MelonLoader.
   Download the MelonLoader installer from the official site or GitHub releases:
   - Website: <https://melonwiki.xyz/>
   - Installer download: <https://github.com/LavaGang/MelonLoader/releases/latest>

   Run the installer, choose Star Ocean The Second Story R (point it at the game's `.exe` in your Steam folder, typically `...\Steam\steamapps\common\STAR OCEAN THE SECOND STORY R`), and install the latest MelonLoader version for 64-bit games.

2. Run the game once.
   Start the game after installing MelonLoader and quit again. This lets MelonLoader finish its setup and create the `Mods` and `UserData` folders inside the game directory.

3. Install the mod.
   From the mod release download:
   - Copy `SO2RAccess.dll` into the game's `Mods` folder.
   - Copy `Tolk.dll` and `nvdaControllerClient64.dll` into the main game folder (the one containing the game's `.exe`).
   - Copy the `Sounds` folder to `UserData\SO2RAccess\Sounds` inside the game directory, so that the `.wav` files end up directly in that folder.
   - Optional: copy the `lang` folder to `UserData\SO2RAccess\lang` if you want the bundled starter translations (French, German, Swedish, Portuguese, Simplified Chinese). Skip it to play in English.

4. Play.
   Start your screen reader, then start the game. The mod announces itself once it has loaded. If you hear nothing, check the MelonLoader console/log for errors — the most common cause is `Tolk.dll` not being in the game folder.

All navigation data is built into `SO2RAccess.dll` — there is nothing extra to install. The mod creates a few files of its own in `UserData` as you play (settings, learned NPC names, and navigation data).

## Updating to a new release

You do not need to reinstall MelonLoader or repeat the full installation — updating only replaces the files that came from the mod download:

1. Quit the game.
2. Download the new release and unpack it.
3. Copy `SO2RAccess.dll` into the game's `Mods` folder, replacing the old file. This is the only step that is always required.
4. Copy the `lang` folder to `UserData\SO2RAccess\lang` again, replacing the old files. New versions usually add new spoken text, and a translation from an older release would read those new lines in English until it is updated. Skip this step only if you have edited a translation file yourself and want to keep your changes. The English template (`en.json`) never needs copying — the mod regenerates it automatically every time the game starts.
5. Only if the release notes mention new or changed sounds: copy the `Sounds` folder to `UserData\SO2RAccess\Sounds` again. Likewise, `Tolk.dll` and `nvdaControllerClient64.dll` almost never change between releases — copying them again is harmless but normally unnecessary.

Your personal data is safe during an update. Settings, key bindings, learned NPC names, and navigation data live in their own files under `UserData\SO2RAccess` and are not part of the release download, so replacing the files above never touches them.

After updating, start the game: the mod announces itself as usual, and the MelonLoader console/log lists the SO2RAccess version that loaded, so you can confirm the new version is active.

## Controls

The mod does not take keys away from the game; its defaults were chosen to
avoid every key the game uses. Press F1 in game at any time to hear this list
read aloud.

### Keyboard

- F1 — help (reads the full control list)
- F2 — toggle dialogue voice mode (full text, or name only when a line is voiced)
- F3 — read your current Fol (money)
- F4 — open or close the mod settings menu
- Minus and Equals — previous / next navigation category
- Left bracket and Right bracket — previous / next item in the category
- Backslash — walk to the selected item; press again while walking to stop
- Apostrophe (while the camp menu is open) — read the current story hint
- P (while the Quick Recovery prompt is open) — read party status

Navigation needs no opening or closing: press any navigation key on a field or
the world map and the mod scans your surroundings automatically. Moving on your
own (W A S D or the arrow keys) quietly stops an auto-walk.

In the battle pause menu the same four navigation keys change what you hear:
Minus and Equals switch the amount of detail (info tier), and the
brackets switch between characters.

In the mod settings menu: Up and Down arrows move between settings,
Left and Right arrows change the value, Enter opens a submenu, and
Escape or F4 closes and saves.

The last item, Key bindings, opens a submenu where every mod key can be
changed: arrows move through the actions, Enter on an action asks for the
new key — the next key you press becomes the binding. If the new key is already
used by the game or by another mod action, you hear a warning but the key is
still accepted. F5 through F11 are reserved for the mod's debugging
hotkeys — they are not listed in the submenu, and binding one of them to an
action gives a warning that the key will only work while debug mode is off.
Changes only take effect when you activate Save and go back
at the bottom of the list; Escape leaves the submenu and discards them.
There is also a Reset all keys to defaults item. The menu's own keys
(arrows, Enter, Escape) always stay the same and cannot be rebound.

### Gamepad

The mod lives on L2 — hold it like a shift key.

- Hold L2 — open the navigation overlay (on a field or the world map)
- L2 held, D-pad Up / Down — previous / next category
- L2 held, D-pad Left / Right — previous / next item
- L2 held, push the left stick up — walk to the selected item
- Release L2 — close the overlay
- L2 + L3 (click the left stick) — open or close the mod settings menu
- L2 + R3 (click the right stick) — read your current Fol
- L3 alone — story hint in the camp menu; party status in Quick Recovery
- Battle pause menu: L1 / R1 — more / less detail about the selected character

Moving the left stick (without L2) quietly stops an auto-walk. In the mod
settings menu the D-pad navigates and changes values, Cross/A opens a
submenu or activates an item, and Circle/B goes back or closes. Key
rebinding itself is keyboard-only — the gamepad layout is fixed.

## Translations

The mod speaks English out of the box, and community translations are plain JSON files — no code changes needed. To use one, drop the translation file (for example `de.json`) into `UserData\SO2RAccess\lang` inside the game directory, then pick the language in the mod settings menu (F4) under Speech language, or leave it on Automatic to follow the game's own text language. Any text a translation does not cover falls back to English automatically.

Starter translations for French (`fr.json`), German (`de.json`), Swedish (`sv.json`), Portuguese (`pt.json`), and Simplified Chinese (`zh-Hans.json`) ship with the mod. They are machine-assisted first drafts — corrections and improvements from native speakers are very welcome; open an issue or pull request on GitHub.

To create a translation yourself, see [TRANSLATING.md](TRANSLATING.md) — it walks through copying the always-current English template the mod places in that same lang folder.

## Known issues

See [KNOWN_ISSUES.md](KNOWN_ISSUES.md) for current limitations and quirks.

## Credits

- Yakku — project direction, design, and testing.
- [MelonLoader](https://github.com/LavaGang/MelonLoader) — the mod loader this project runs on.
- [Tolk](https://github.com/dkager/tolk) by Davy Kager — screen reader abstraction library.
- Gemioli / Square Enix — Star Ocean: The Second Story R. This is an unofficial fan-made accessibility mod and is not affiliated with or endorsed by Square Enix.

### Sound credits

The notification sounds are sourced from [Freesound](https://freesound.org/) under Creative Commons licenses. The full listing also ships with the mod in `Sounds\Game sound license directory.txt`.

- Dodge notification and jump prompt sounds by Yakku — made for this mod
- Menu Beep (save sound) by CogFireStudios — <https://freesound.org/s/531511/> — License: Creative Commons 0
- Dangerous City (enemy approach) by pholosho_seloane — <https://freesound.org/s/548162/> — License: Creative Commons 0
- Mobile Phone Notification Sound (private action notification) by TheArbuzikYT — <https://freesound.org/s/840284/> — License: Creative Commons 0
- Menu Beep (bonus gauge fill) by DrMrSir — <https://freesound.org/s/529560/> — License: Attribution 4.0
- bubble_big (fishing spot arrival) by cdonahueucsd — <https://freesound.org/s/337133/> — License: Attribution 4.0

## License

This mod is released under the [MIT License](LICENSE). You are free to use, modify, and redistribute it — including in your own projects — as long as you keep the copyright notice, which credits the original author. See the `LICENSE` file for the full text.

The bundled sounds keep their own licenses as listed under [Sound credits](#sound-credits).
