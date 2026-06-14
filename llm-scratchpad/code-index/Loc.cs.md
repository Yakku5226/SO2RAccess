# Loc.cs (803 lines)

Centralized localization for the accessibility mod. All screen reader strings go through Loc.Get(). No hardcoded strings permitted. Single English locale only; keys serve as fallback when a key is missing.
namespace: SO2RAccess (line 4)
usings (non-System / notable only): (none beyond System, System.Collections.Generic)

## static class Loc (line 14)

fields/properties (declaration order):
- _initialized : bool (line 18)
- _strings : Dictionary<string, string> (line 19)

methods (declaration order):
- void Initialize() (line 28)
  - note: idempotent — guards with _initialized flag; calls InitializeStrings() then sets flag
- string Get(string key) (line 39)
  - note: returns key itself as fallback when key not found (helps spot missing strings)
- string Get(string key, params object[] args) (line 48)
  - note: formats template via string.Format; logs warning and returns raw template on FormatException
- void Add(string key, string english) (line 66)  [private helper — sets _strings[key]]
- void InitializeStrings() (line 75)  [private; populates _strings with ~300 entries across all feature areas]
  - note: string table covers: General, Title, Config, Keyboard/Gamepad binding, Protagonist selection, New game settings, Load/Save game, Dialogue/NPC, Tutorials, Dialog popups, Field navigation (nav_*), Navigation cross-island (nav_island_*), Fishing, World map, Shop (~15 category names), Guild, Item acquisition, Equipment wizard, Battle results, Enemy proximity audio, Location discovery, Rewards, Save notifications, Game over, Mod settings menu (mod_menu_*), Field jump prompt, Battle target/ally/pause/menu/bonus gauge/status, Database sub-screens (db_tutorial_*, db_enemy_*, db_item_*, db_fish_*, db_location_*, db_playerdata_*), Camp menu root/items/status/equip/elemental/battle skill/enhance/formation/skills/party/assist/tactics (camp_*), Item Creation (ic_*), Quick Heal (quickheal_*), Super Specialty (ss_*), Pickpocket, Quest/Mission lists, Dialogue choice menus
