# Translating SO2RAccess

Everything the mod speaks comes from one JSON file of key-and-text pairs. To translate the mod you copy the English file, translate the text values, and drop your copy next to it. No coding, no rebuilding — a text editor is enough.

## Where the files live

Inside the game directory, the mod keeps its language files in:

`UserData\SO2RAccess\lang`

On every launch the mod writes a fresh `en.json` there — the complete, current English text as a reference template. Because it is regenerated every time, never edit `en.json` itself; your changes would be overwritten. Your own translation file is never touched by the mod.

## Getting started

1. Open a command prompt in the lang folder, or use any file manager, and copy the template. For German, for example: `copy en.json de.json`
2. Open your copy in a text editor and translate the `language_name` entry first — set it to the language's own name in that language, for example "Deutsch" or "Français". This is the name the settings menu speaks for your file.
3. Translate the rest of the values — always the text after the colon, never the key before it. Keys like `nav_autowalk_start` must stay exactly as they are.
4. Save the file as UTF-8. Windows Notepad saves UTF-8 by default; if your editor asks, do not pick "Unicode" (that is UTF-16 and the file will be rejected with a log message).

## File naming and automatic language selection

The mod can follow the game's own text language automatically. For that to work, your file must be named with the matching code:

- ja.json — Japanese
- ko.json — Korean
- zh-Hant.json — Traditional Chinese
- zh-Hans.json — Simplified Chinese
- fr.json — French
- it.json — Italian
- de.json — German
- es.json — Spanish

Languages the game does not offer (for example Russian as `ru.json`, or Polish as `pl.json`) work too — they just cannot be picked automatically. Players select them by hand in the settings menu.

## Selecting a language in the game

Open the mod settings menu (F4, or L2 + L3 on a gamepad) and find the Speech language setting. Left and Right arrows cycle through Automatic, English, and every translation file found in the lang folder. The change applies instantly — the menu re-announces itself in the new language — and is saved when the menu closes. Automatic follows the game's text language, even when it is changed mid-session in the game's own config menu.

The setting is also stored in `UserData\SO2RAccess\settings.json` as `"Language"`, where `"auto"` means Automatic and any other value is a file name without the `.json` ending.

## Rules for the text

Keys starting with an underscore, like `_section_title_menu`, are section comments carried over from the source code. They are never spoken; they exist to tell you which screen the following strings belong to. You can translate them for your own orientation or leave them in English — it makes no difference to the mod.

Many strings contain placeholders like `{0}` and `{1}`. The mod fills these with live values — names, numbers, key names — when it speaks. You may move a placeholder to wherever your language's word order needs it, but never delete one, never add one, and never change the number inside the braces. For example, "Walking to {0}." may become "{0} wird angesteuert." — the `{0}` moved, but it is still there.

One string deserves special care: `help`, the F1 help text. It uses sixteen placeholders, `{0}` through `{15}`, each filled with a specific key name. The entry `_section_key_names_are_filled_in_live` directly above it documents which placeholder is which key. Keep all sixteen.

Two characters need escaping inside JSON text: a double quote must be written as `\"` and a backslash as `\\`. If the file has a syntax error anywhere — a missing comma, a stray quote — the whole file is rejected: the mod stays in English and the reason is written to the log.

## What happens with missing or extra keys

You do not have to translate everything, and a mod update may add new keys your file does not have yet. Any key missing from your file simply falls back to English — the rest of your translation keeps working. When your file loads, the log states how many keys fell back, so you can check the freshly regenerated `en.json` for what is new. An empty value ("") counts as untranslated and falls back too. Unknown keys (for example ones removed in an update) are ignored.

## Testing your translation

1. Place your file in the lang folder and start the game.
2. Pick your language in the mod settings menu, or set it in `settings.json` before starting.
3. Play a little — menus, navigation, a battle — and listen.
4. Check the log at `MelonLoader\Latest.log` in the game folder. Look for lines starting with `[LOC]`: they say whether your file loaded, and how many keys fell back to English.

Two things to know:

- Your screen reader's voice must support the target language — the mod hands the text over as-is; it does not switch voices or synthesizers.
- The very first spoken line at startup (the welcome message) can still be English when the language is on Automatic: it fires before the game is loaded enough for the mod to ask which language the game is set to. Everything after follows your translation.

## Sharing your translation

A translation is just your single JSON file — share it however you like, and players drop it into `UserData\SO2RAccess\lang`. If you would like it considered for bundling with the mod itself, open an issue or pull request on the project's GitHub page.
