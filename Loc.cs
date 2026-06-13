using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// Centralized localization for the accessibility mod.
    /// All screen reader strings must go through Loc.Get() — no hardcoded strings.
    ///
    /// Usage:
    ///   Loc.Get("key")              — get a string
    ///   Loc.Get("key", arg1, arg2)  — get a string with {0}, {1} placeholders
    /// </summary>
    public static class Loc
    {
        #region Fields

        private static bool _initialized = false;
        private static readonly Dictionary<string, string> _strings = new Dictionary<string, string>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes localization strings. Called once at mod startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            InitializeStrings();
            _initialized = true;
        }

        /// <summary>
        /// Returns the localized string for the given key.
        /// Falls back to the key itself if not found (helps spot missing strings).
        /// </summary>
        public static string Get(string key)
        {
            if (!_initialized) Initialize();
            return _strings.TryGetValue(key, out string value) ? value : key;
        }

        /// <summary>
        /// Returns the localized string with {0}, {1}, ... placeholders filled in.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Loc.Get format error for key '{key}': {ex.Message}");
                return template;
            }
        }

        #endregion

        #region Private Methods

        private static void Add(string key, string english)
        {
            _strings[key] = english;
        }

        /// <summary>
        /// All mod strings defined here. Add new strings as features are implemented.
        /// Convention: [handler]_[action], e.g. "inventory_opened", "battle_hp".
        /// </summary>
        private static void InitializeStrings()
        {
            // General
            Add("mod_loaded",   "SO2RAccess loaded. Press F1 for help.");
            Add("debug_on",     "Debug mode enabled.");
            Add("debug_off",    "Debug mode disabled.");
            Add("fol_amount",   "{0} Fol.");
            Add("help",         "Keyboard: F1 Help. F3 read Fol. F4 Mod settings. NumPad 5 open or close navigation list. NumPad 8 and 2 move up and down. NumPad 4 and 6 change category. NumPad 1 walk to selected item or cancel walk. F12 toggle debug mode. Gamepad: hold L1 to open navigation, D-pad up and down for category, left and right for items. Push left stick up to walk. L1 plus L3 for mod settings. L1 plus R3 read Fol.");

            // Title menu
            Add("title_press_any_button",      "Title screen. Press any button to start.");
            Add("title_menu_item",             "{0}");
            Add("title_menu_item_unavailable", "{0}, unavailable");

            // Config menu
            Add("config_menu_item",      "Config, {1} of {2}: {0}");
            Add("config_setting",        "{0}: {1}, {2} of {3}");
            Add("config_setting_no_value", "{0}, {1} of {2}");
            Add("config_value",          "{0}");

            // Keyboard binding menu
            Add("keyboard_item",        "{0}: {1}, {2} of {3}");
            Add("keyboard_item_no_key", "{0}: unassigned, {1} of {2}");
            Add("keyboard_press_key",   "Press a key to assign.");

            // Gamepad binding menu
            Add("gamepad_item",           "{0}: {1}, {2} of {3}");
            Add("gamepad_item_no_button", "{0}: unassigned, {1} of {2}");
            Add("gamepad_press_button",   "Press a button to assign.");

            // Protagonist selection screen
            Add("hero_select_screen", "Protagonist selection.");
            Add("hero_name_claude",   "Claude");
            Add("hero_name_rena",     "Rena");
            Add("hero_select_item",   "{0}. {1}");

            // New game settings screen
            Add("newgame_screen",              "New game settings.");
            Add("newgame_item_with_value",     "{0}: {1}");
            Add("newgame_editing_name",        "Editing name. Type your name and press Enter.");
            Add("newgame_label_name",          "Name");
            Add("newgame_label_voice_language","Voice Language");
            Add("newgame_label_voice_type",    "Voice Type");
            Add("newgame_label_difficulty",    "Difficulty");
            Add("newgame_label_bgm",           "BGM Version");
            Add("newgame_label_event_art",     "Display Event Art");
            Add("newgame_label_initialize",    "Return to defaults");
            Add("newgame_label_decision",      "Confirm");

            // Load game menu
            Add("load_game_screen",    "Load game.");
            Add("load_slot_label_auto","Auto save");
            Add("load_slot_with_data", "{0}. {1}, Level {2}, {3}. {4}. Play time: {5}. {6} of {7}.");
            Add("load_slot_empty",     "{0}. Empty. {1} of {2}.");

            // Dialogue / NPC text boxes
            Add("dialogue_no_name",   "{0}");
            Add("dialogue_with_name", "{0}: {1}");
            Add("dialogue_speaker_only", "{0}");
            Add("dialogue_mode_full",      "Dialogue mode: full text");
            Add("dialogue_mode_name_only", "Dialogue mode: name only when voiced");

            // Tutorial boxes
            Add("tutorial_page",          "Tutorial. {0}. {1}");
            Add("tutorial_page_no_title", "Tutorial. {0}");
            Add("tutorial_operation",     "Controls: {0}");

            // Dialog and description popups
            Add("dialog_message",              "{0}");
            Add("dialog_message_with_choice",  "{0} {1}");
            Add("dialog_description",          "{0}. {1}");
            Add("dialog_description_no_desc",  "{0}");
            Add("dialog_choice",               "{0}");

            // Field navigation — Phase 2 list
            Add("nav_not_in_field",   "Not in a field area.");
            Add("nav_no_items",       "No items found.");
            Add("nav_open",           "Navigation. {0}. {1}, {2} meters.");
            Add("nav_close",          "Navigation closed.");
            Add("nav_item",           "{0}, {1} meters.");
            Add("nav_category",       "{0}. {1}, {2} meters.");
            Add("nav_category_empty",   "{0}. None.");
            Add("nav_autowalk_start",      "Walking to {0}.");
            Add("nav_autowalk_arrived",    "Arrived at {0}.");
            Add("nav_autowalk_arrived_exit","Arrived at {0}. Exit is to the {1}.");
            Add("nav_autowalk_entering",   "Entering {0}.");
            Add("nav_autowalk_enter_fail", "Arrived near {0}. Could not enter automatically. Try fast travel instead.");
            Add("nav_autowalk_arrived_npc","Arrived at {0}. Press action button to interact. NumPad 1 or L1 to stop following.");
            Add("nav_autowalk_resuming", "Resuming walk to {0}.");
            Add("nav_autowalk_unreachable","Cannot reach {0}.");
            Add("nav_autowalk_route_exits","Cannot reach {0} without leaving the area. No safe route found.");
            Add("nav_autowalk_no_navmesh", "No navigation data available.");
            Add("nav_autowalk_lost_path",  "Lost path to {0}.");
            Add("nav_autowalk_stuck",      "Path blocked to {0}. Auto-walk stopped.");
            Add("nav_floor_up",            "Went upstairs.");
            Add("nav_floor_down",          "Went downstairs.");
            Add("nav_label_above",         "{0} (above)");
            Add("nav_label_below",         "{0} (below)");
            Add("nav_chest_unopened",   "Unopened chest");
            Add("nav_chest_opened",     "Opened chest");
            Add("nav_chest_unopened_n", "Unopened chest {0}");
            Add("nav_chest_opened_n",   "Opened chest {0}");
            Add("nav_exit_door",      "Building entrance");
            Add("nav_exit_gate",      "Town gate");
            Add("nav_exit_with_dest", "{0} to {1}");
            Add("nav_marker",         "Quest marker");
            Add("nav_marker_n",       "Quest marker {0}");
            Add("nav_npc_n",          "NPC {0}");
            Add("nav_event_story",              "Story event");
            Add("nav_event_pa",                 "Private action");
            Add("nav_autowalk_arrived_above",   "Arrived near {0}. Target is above you — look for stairs or a ramp.");
            Add("nav_autowalk_arrived_below",   "Arrived near {0}. Target is below you — look for stairs or a ramp.");
            // Honest "could not reach" messages — used when the path stops short
            // of the real target so we never falsely claim arrival.
            Add("nav_autowalk_cannot_reach",       "Could not reach {0}. Stopped {1} meters {2}.");
            Add("nav_autowalk_cannot_reach_above", "Could not reach {0}. It is above you, {1} meters {2} — look for stairs or a ramp.");
            Add("nav_autowalk_cannot_reach_below", "Could not reach {0}. It is below you, {1} meters {2} — look for stairs or a ramp.");

            // Navigation — cross-island routing
            Add("nav_island_route",             "Walking to {0}. Route crosses {1} levels.");
            Add("nav_island_exploring",         "No confirmed route to {0}. Exploring {1} unconfirmed connections.");
            Add("nav_island_no_route",          "Cannot reach {0}. No known route exists. Try exploring the area manually.");
            Add("nav_island_crossing",          "Crossing to next level.");
            Add("nav_island_crossing_attempt",  "Attempting unconfirmed crossing.");
            Add("nav_island_crossing_confirmed","Connection confirmed. Continuing.");
            Add("nav_island_crossing_blocked",  "Blocked. Cannot cross here. Cancelling auto-walk.");
            Add("nav_island_crossing_stuck",    "Stuck at crossing. Cancelling auto-walk.");
            Add("nav_island_continuing",        "Continuing to {0}. {1} crossings remaining.");
            Add("nav_island_final",             "Final approach to {0}.");
            Add("nav_event_side",               "Side event");
            Add("nav_event_side_reward",        "Side event (reward)");
            Add("nav_event_side_battle",        "Side event (battle)");
            Add("nav_event_side_reward_battle", "Side event (reward, battle)");
            Add("nav_save",            "Save point");
            Add("nav_save_n",          "Save point {0}");
            Add("nav_save_recovery",   "Recovery save point");
            Add("nav_save_recovery_n", "Recovery save point {0}");

            // Navigation — fishing spots
            Add("nav_fishing",         "Fishing spot");
            Add("nav_fishing_n",       "Fishing spot {0}");

            // Fishing results
            Add("fish_caught",         "Caught:");
            Add("fish_new_record",     "new record");
            Add("fish_new",            "new");
            Add("fish_max_size",       "max size");

            // Navigation — world map locations
            Add("nav_location_dungeon",  "{0} (Dungeon)");

            // World map fast travel menu
            Add("worldmap_open",         "Fast travel.");
            Add("worldmap_tab_city",     "Cities.");
            Add("worldmap_tab_dungeon",  "Dungeons.");
            Add("worldmap_unavailable",  "{0}, unavailable");

            // Navigation — enemies
            Add("nav_enemy_named",   "{0}, {1}");
            Add("nav_enemy_typed",   "{0} enemy");
            Add("nav_enemy_unknown", "Enemy");
            Add("nav_enemy_weak",    "weak");
            Add("nav_enemy_medium",  "medium");
            Add("nav_enemy_strong",  "strong");
            Add("nav_enemy_raid",    "raid");

            // Navigation — stairs
            Add("nav_stairs_up",             "Stairs up");
            Add("nav_stairs_down",           "Stairs down");
            Add("nav_stairs_up_n",           "Stairs up {0}");
            Add("nav_stairs_down_n",         "Stairs down {0}");

            // Navigation — doors (stone only)
            Add("nav_door_stone_open",       "Stone door, open");
            Add("nav_door_stone_closed",     "Stone door, closed");
            Add("nav_door_stone_open_n",     "Stone door, open {0}");
            Add("nav_door_stone_closed_n",   "Stone door, closed {0}");

            // Navigation — warp points
            Add("nav_warp_panel",            "Warp panel");
            Add("nav_warp_panel_n",          "Warp panel {0}");
            Add("nav_warp_circle",           "Magic circle");
            Add("nav_warp_circle_n",         "Magic circle {0}");
            Add("nav_warp_platform",         "Platform");
            Add("nav_warp_platform_n",       "Platform {0}");

            // Camp menu — root
            Add("camp_menu_screen",           "Camp menu.");
            Add("camp_menu_item",             "{0}, {1} of {2}.");
            Add("camp_menu_item_unavailable", "{0}, unavailable, {1} of {2}.");

            // Camp menu — item sub-screen
            Add("camp_item_screen",          "Items.");
            Add("camp_item_factor",          "Factor: {0}");
            Add("camp_item_position",        "{0} of {1}.");

            // Camp menu — status sub-screen
            // Full character data: name, level, HP/MP, EXP, combat stats, base attributes.
            Add("camp_status_screen",        "Status.");
            Add("camp_status_level_hp_mp",   "Level {0}. HP: {1} of {2}. MP: {3} of {4}.");
            Add("camp_status_exp",           "EXP: {0}, next level: {1}.");
            Add("camp_status_combat",        "Attack: {0}. Defence: {1}. Magic: {2}. Hit: {3}. Dodge: {4}. Critical: {5}.");
            Add("camp_status_attributes",    "Strength: {0}. Constitution: {1}. Dexterity: {2}. Agility: {3}. Intelligence: {4}. Luck: {5}.");
            Add("camp_status_stamina_guts",  "Stamina: {0}. Guts: {1}.");
            Add("camp_status_position",      "{0} of {1}.");
            Add("camp_status_talents_screen","Talents.");
            Add("camp_status_talents_none",  "No talents.");
            Add("camp_status_stat_attack",   "Attack");
            Add("camp_status_stat_defence",  "Defence");
            Add("camp_status_stat_magic",    "Magic");
            Add("camp_status_stat_hit",      "Hit");
            Add("camp_status_stat_dodge",    "Dodge");
            Add("camp_status_stat_critical", "Critical");
            Add("camp_status_stat_str",      "Strength");
            Add("camp_status_stat_con",      "Constitution");
            Add("camp_status_stat_dex",      "Dexterity");
            Add("camp_status_stat_agl",      "Agility");
            Add("camp_status_stat_int",      "Intelligence");
            Add("camp_status_stat_luc",      "Luck");
            Add("camp_status_stat_stamina",  "Stamina");
            Add("camp_status_stat_guts",     "Guts");
            Add("camp_status_age",           "Age: {0}.");
            Add("camp_status_favorite_food", "Favorite food: {0}.");
            Add("camp_status_elements",      "Elements.");
            Add("camp_status_elements_none", "No elemental affinities.");
            Add("camp_status_friendship",         "Friendship.");
            Add("camp_status_friendship_entry",   "{0}: {1}%");
            Add("camp_status_friendship_ending",  "{0}: {1}%, ending candidate");
            Add("camp_status_friendship_none",    "No friendship data.");

            // Camp menu — equip sub-screen
            // Slot list: each entry shows the item currently equipped in that slot.
            Add("camp_equip_screen",             "Equipment.");
            Add("camp_equip_slot",               "{0}: {1}, {2} of {3}.");
            Add("camp_equip_slot_empty",         "{0}: None, {1} of {2}.");
            Add("camp_equip_slot_unavailable",   "{0}: {1}, unavailable, {2} of {3}.");
            // Item list: announced by UIItemInformationPresenter.Set hook.
            Add("camp_equip_stat_attack",    "Attack: {0}");
            Add("camp_equip_stat_defence",   "Defence: {0}");
            Add("camp_equip_stat_magic",     "Magic: {0}");
            Add("camp_equip_stat_hit",       "Hit: {0}");
            Add("camp_equip_stat_avoidance", "Avoidance: {0}");
            Add("camp_equip_factor",         "Factor: {0}");
            Add("camp_equip_position",       "{0} of {1}.");

            // Camp menu — equip elemental resistances (Triangle button)
            Add("camp_equip_elemental_heading",  "Elemental resistances.");
            Add("camp_equip_elemental_weak",     "{0}: weak");
            Add("camp_equip_elemental_half",     "{0}: resistant");
            Add("camp_equip_elemental_immune",   "{0}: immune");
            Add("camp_equip_elemental_absorb",   "{0}: absorb");
            Add("camp_equip_elemental_none",     "No elemental resistances.");

            // Camp menu — root battle skill sub-screen (Camp → BattleSkill)
            // Detailed tactical readout: Name, MP, Type, Target, Element, Range, Effect, Description, Level.
            Add("camp_root_battleskill_screen",              "Battle skills.");
            Add("camp_root_battleskill_mp",                  "MP: {0}");
            Add("camp_root_battleskill_type_damage",         "Type: Damage");
            Add("camp_root_battleskill_type_durability",     "Type: Shield break");
            Add("camp_root_battleskill_type_damage_durability","Type: Damage and shield break");
            Add("camp_root_battleskill_type_support",        "Type: Support");
            Add("camp_root_battleskill_target_enemy",        "Target: Single enemy");
            Add("camp_root_battleskill_target_enemy_all",    "Target: All enemies");
            Add("camp_root_battleskill_target_player",       "Target: Single ally");
            Add("camp_root_battleskill_target_player_all",   "Target: All allies");
            Add("camp_root_battleskill_target_self",         "Target: Self");
            Add("camp_root_battleskill_element",             "Element: {0}");
            Add("camp_root_battleskill_range",               "Range: {0}");
            Add("camp_root_battleskill_level",               "Level: {0} of {1}");

            // Element names (used in root battle skill readout)
            Add("element_earth",     "Earth");
            Add("element_water",     "Water");
            Add("element_fire",      "Fire");
            Add("element_wind",      "Wind");
            Add("element_lightning", "Lightning");
            Add("element_star",      "Star");
            Add("element_negative",  "Negative");
            Add("element_light",     "Light");
            Add("element_dark",      "Dark");

            // Camp menu — enhance battle/combat skill sub-screen (Camp → Enhance → BattleSkillPoint/CombatPoint)
            // Upgrade-focused readout: Name, BP balance/cost, Level, Next level bonuses, MP.
            Add("camp_enhance_battleskill_screen", "Battle skills.");
            Add("camp_enhance_combatskill_screen", "Combat skills.");
            Add("camp_enhance_bp",                 "BP: {0} / {1}");
            Add("camp_enhance_sp",                 "SP: {0} / {1}");
            Add("camp_enhance_level",              "Level: {0} of {1}");
            Add("camp_enhance_level_max",          "Level: {0} of {1}, max");
            Add("camp_enhance_mp",                 "MP: {0}");
            Add("camp_enhance_next",               "Upgrade: {0}");
            Add("camp_enhance_bonus_damage_up",        "Damage up");
            Add("camp_enhance_bonus_hit_up",           "Hit up");
            Add("camp_enhance_bonus_critical_up",      "Critical up");
            Add("camp_enhance_bonus_range_expansion",  "Range expansion");
            Add("camp_enhance_bonus_heal_up",          "Heal up");
            Add("camp_enhance_bonus_grant_up",         "Grant up");
            Add("camp_enhance_bonus_add_attracting",   "Add attracting");
            Add("camp_enhance_bonus_stop_time_up",     "Stop time up");
            Add("camp_enhance_bonus_change_effect",    "Effect change");
            Add("camp_enhance_bonus_add_penetration",  "Add penetration");
            Add("camp_enhance_bonus_ignore_defence",   "Ignore defence");
            Add("camp_enhance_bonus_weakness_attack",  "Weakness attack");
            Add("camp_enhance_bonus_recover_abnormal", "Recover abnormal");

            // Shared — kept for battle skill assignment screen and skills sub-screen
            Add("camp_battleskill_level",    "Level {0} of {1}");
            Add("camp_battleskill_mp",       "MP: {0}");
            Add("camp_battleskill_position", "{0} of {1}.");

            // Enhance sub-menu — shared cost/balance strings (kept for skills sub-screen)
            Add("camp_skill_sp_cost",        "SP: {0} / {1}");
            Add("camp_skill_bp_cost",        "BP: {0} / {1}");
            Add("camp_skill_max_level",      ", max");

            // Combat skill toggle mode (Square button in combat skills screen)
            Add("camp_combatskill_toggle",   "Toggle mode.");
            Add("camp_combatskill_active",   "{0}, active. {1} of {2}.");
            Add("camp_combatskill_inactive", "{0}, inactive. {1} of {2}.");

            // Camp menu — battle skill assignment sub-screen
            // Equip state: polled — "[Button]: [Skill]" or "[Button]: no skill assigned".
            // SelectBattleSkill state: hook-driven — "Assigning to [button]: [skill details]".
            Add("camp_battleskill_setting_screen",     "Battle skill assignment.");
            Add("camp_battleskill_setting_slot",       "{0}: {1}. {2} of {3}.");
            Add("camp_battleskill_setting_slot_empty", "{0}: no skill assigned. {1} of {2}.");
            Add("camp_battleskill_setting_assigning",  "Assigning to {0}");

            // Camp menu — formation sub-screen
            // Hook-driven: UICampFormationInformationPresenter.Set fires on every navigation.
            Add("camp_formation_screen",         "Formation.");
            Add("camp_formation_position",       "{0} of {1}.");
            Add("camp_formation_spheres",        "Spheres: {0}. {1} bonuses active.");
            Add("camp_formation_bonus_enabled",  "{0}, active.");
            Add("camp_formation_bonus_disabled", "{0}.");

            // Camp menu — skills sub-screen (field/IC skills, NOT battle skills)
            // Hook-driven: UISkillInformationPresenter.Set fires on every navigation.
            Add("camp_skill_screen",        "Skills.");
            Add("camp_skill_level",         "Level {0}");
            Add("camp_skill_position",      "{0} of {1}.");

            // Camp menu — party formation sub-screen (character selection grid)
            Add("camp_party_formation_screen",      "Party formation.");
            Add("camp_party_formation_char",        "{0}, Level {1}. HP {2}/{3}, MP {4}/{5}. {6}. {7} of {8}.");
            Add("camp_party_formation_guest",       "Guest.");
            Add("camp_party_formation_unavailable", "Unavailable.");

            // Camp menu — assist formation sub-screen (assign assist characters to buttons)
            Add("camp_assist_screen",          "Assist formation.");
            Add("camp_assist_slot",            "{0}: {1}, {2}. {3} of {4}.");
            Add("camp_assist_slot_empty",      "{0}: None. {1} of {2}.");
            Add("camp_assist_char",            "{0}. {1} of {2}.");
            Add("camp_assist_char_current",    "{0}, currently set. {1} of {2}.");

            // Camp menu — tactics sub-screen (assign tactics to party members)
            Add("camp_tactics_screen",             "Tactics.");
            Add("camp_tactics_char",               "{0}: {1}. {2} of {3}.");
            Add("camp_tactics_operation",          "{0}. {1} of {2}.");
            Add("camp_tactics_operation_current",  "{0}, currently set. {1} of {2}.");
            Add("camp_tactics_currently_set",      "Currently set.");
            Add("camp_tactics_operation_position", "{0} of {1}.");

            // Save game (same UI as load, differentiated by SaveLoadState)
            Add("save_game_screen",         "Save game.");

            // Shop menu
            Add("shop_screen",          "Shop.");
            Add("shop_menu_item",       "{0}, {1} of {2}.");
            Add("shop_buy_heading",     "Buy.");
            Add("shop_sell_heading",    "Sell.");
            Add("shop_item_buy",        "{0}. {1} Fol. {2}{3} of {4}.");
            Add("shop_item_sell",       "{0}. Sell: {1} Fol. Own: {2}. {3}{4} of {5}.");
            Add("shop_item_quantity",   "Quantity: {0}. Total: {1} Fol.");

            // Shop item category names
            Add("shop_cat_sword",       "Sword");
            Add("shop_cat_twin_sword",  "Dual Swords");
            Add("shop_cat_wand",        "Wand");
            Add("shop_cat_knuckle",     "Knuckle");
            Add("shop_cat_punch",       "Fists");
            Add("shop_cat_book",        "Book");
            Add("shop_cat_whip",        "Whip");
            Add("shop_cat_gun_disk",    "Gun and Disc");
            Add("shop_cat_stungun",     "Stun Gun");
            Add("shop_cat_rod",         "Rod");
            Add("shop_cat_helmet",      "Helmet");
            Add("shop_cat_shield",      "Shield");
            Add("shop_cat_armor",       "Armor");
            Add("shop_cat_greave",      "Greave");
            Add("shop_cat_accessory",   "Accessory");

            // Guild mission menu
            // NOTE: Guild mission list is a native code wall — individual mission
            // names and cursor position cannot be read from managed code. Only window
            // open/close detection works. Dialogue system catches accept/provisions.
            Add("guild_screen",             "Guild.");

            // Item acquisition popups (treasure chests, quest rewards, etc.)
            Add("overflow_item",            "{0}");
            Add("overflow_item_multi",      "{0} x{1}");

            // Equipment wizard (auto-equip popup for new gear)
            Add("equip_wizard_heading",         "Equipment Wizard");
            Add("equip_wizard_change",          "{0}: {1} to {2}");
            Add("equip_wizard_menu_yes",        "Yes");
            Add("equip_wizard_menu_no",         "No");
            Add("equip_wizard_menu_reject_all", "Reject All");
            Add("equip_wizard_position",        "{0}, {1} of {2}.");

            // Battle results
            Add("battle_result_heading",    "Battle complete.");
            Add("battle_result_exp",        "{0} EXP.");
            Add("battle_result_fol",        "{0} Fol.");
            Add("battle_result_sp",         "{0} SP.");
            Add("battle_result_bsp",        "{0} Battle Skill Points.");
            Add("battle_result_levelup",    "{0} leveled up to {1}.");
            Add("battle_result_levelup_sp", "Gained {0} SP.");
            Add("battle_result_levelup_bsp","Gained {0} Battle Skill Points.");
            Add("battle_result_learned_skill", "Learned {0}: {1}.");
            Add("battle_result_learned_skill_noDesc", "Learned {0}.");
            Add("battle_result_skill_unknown", "new skill");
            Add("battle_result_bonus_chain", "Chain bonus active.");
            Add("battle_result_bonus_training", "{0} has Training bonus.");
            Add("battle_result_bonus_openeyes", "{0} has Open Eyes bonus.");
            Add("battle_result_item",       "Obtained {0}.");
            Add("battle_result_item_multi", "Obtained {0}, {1}.");

            // Enemy proximity audio
            Add("proximity_wav_missing",    "Enemy proximity sound file not found: {0}");

            // Location discovery notifications
            Add("location_discovered",      "Discovered {0}.");
            Add("location_discovered_desc", "Discovered {0}. {1}");

            // Reward notifications (location points, missions, etc.)
            Add("reward_exp",               "{0} EXP");
            Add("reward_fol",               "{0} Fol");
            Add("reward_sp",                "{0} SP");
            Add("reward_bp",                "{0} BP");
            Add("reward_item",              "{0}");
            Add("reward_item_multi",        "{0} x{1}");

            // Save notifications
            Add("save_saving",                      "Saving.");
            Add("save_autosave_announce_fallback",  "When the game is saving, a save icon will appear on screen.");

            // Game over (battle loss) menu
            Add("gameover_screen",      "Game over.");
            Add("gameover_menu_item",   "{0}, {1} of {2}.");
            Add("gameover_retry",       "Retry");
            Add("gameover_title",       "Title");

            // Mod settings menu (F4 / L1+L3)
            Add("mod_menu_open",                    "Mod settings menu.");
            Add("mod_menu_close",                   "Settings saved. Menu closed.");
            Add("mod_menu_item",                    "{0}: {1}. Item {2} of {3}.");
            Add("mod_menu_changed",                 "{0}: {1}.");
            Add("mod_menu_on",                      "On");
            Add("mod_menu_off",                     "Off");
            Add("mod_menu_label_save_sound",        "Save sound");
            Add("mod_menu_label_save_volume",       "Save sound volume");
            Add("mod_menu_label_dodge_sound",       "Dodge sound");
            Add("mod_menu_label_dodge_volume",      "Dodge sound volume");
            Add("mod_menu_label_proximity_sound",   "Enemy proximity sound");
            Add("mod_menu_label_proximity_volume",  "Enemy proximity sound volume");
            Add("mod_menu_label_pa_volume",         "Private action sound volume");
            Add("mod_menu_label_dialogue_mode",     "Dialogue mode");
            Add("mod_menu_dialogue_full",           "Full text");
            Add("mod_menu_dialogue_name_only",      "Name only when voiced");
            Add("mod_menu_label_ally_health",       "Ally health warnings");
            Add("mod_menu_label_ally_ailment",      "Ally status ailments");
            Add("mod_menu_label_player_damage",     "Player damage dealt");
            Add("mod_menu_label_gauge_volume",      "Bonus gauge sound volume");
            Add("mod_menu_label_gauge_break_announce", "Bonus gauge break announcement");
            Add("mod_menu_label_gauge_percent",     "Bonus gauge percentage announcement");
            Add("mod_menu_label_jump_sound",        "Jump prompt sound");
            Add("mod_menu_label_jump_speech",       "Jump prompt speech");

            // Field prompts (button guide above the player / interactables)
            Add("jump_prompt",            "Press {0} to jump down.");
            Add("jump_prompt_no_button",  "Jump down available.");

            // Battle target (L2 target change mode)
            Add("battle_target_hp_pct",         "HP {0}%.");
            Add("battle_target_hp_exact",       "HP {0} of {1}.");
            Add("battle_target_shield_pct",     "Shield {0}%.");
            Add("battle_target_shield_broken",  "Shield broken.");
            Add("battle_target_defeated",       "Defeated.");
            Add("battle_target_leader",         "Leader: {0}.");
            Add("battle_target_status",         "{0}.");

            // Battle ally switch (R2 control player change)
            Add("battle_ally_switch",           "{0}. HP {1} of {2}. MP {3} of {4}.");
            Add("battle_ally_switch_status",    "{0}. HP {1} of {2}. MP {3} of {4}. {5}.");

            // Battle pause menu (Start/Options during battle)
            Add("battle_pause_heading",         "Battle status.");
            Add("battle_pause_ally",            "{0}. HP {1} of {2}. MP {3} of {4}. {5} of {6}.");
            Add("battle_pause_enemy",           "{0}. HP {1} of {2}. {3} of {4}.");
            Add("battle_pause_enemy_unknown",   "{0}. HP unknown. {1} of {2}.");
            Add("battle_pause_defeated",        "{0}. Defeated. {1} of {2}.");
            Add("battle_pause_tier",            "{0}: {1}. {2} of {3}.");
            Add("battle_pause_weaknesses",      "Weaknesses");
            Add("battle_pause_resistances",     "Resistances");
            Add("battle_pause_conditions",      "Status conditions");
            Add("battle_pause_equipment",       "Equipment effects");
            Add("battle_pause_cooking",         "Cooking buffs");
            Add("battle_pause_music",           "Music effects");
            Add("battle_pause_leader",          "Leader effects");
            Add("battle_pause_elem_double",     "{0}, double damage");
            Add("battle_pause_elem_half",       "{0}, half damage");
            Add("battle_pause_elem_immune",     "{0}, immune");
            Add("battle_pause_elem_absorb",     "{0}, absorb");
            Add("battle_pause_none",            "None");

            // Battle menu (Triangle during combat)
            Add("battle_menu_heading",              "Battle menu.");
            Add("battle_menu_root_item",            "{0}, {1} of {2}.");
            Add("battle_menu_root_item_unavailable", "{0}, unavailable, {1} of {2}.");
            Add("battle_menu_items_recovery",       "Recovery.");
            Add("battle_menu_items_combat",         "Combat.");
            Add("battle_menu_items_detail",         "{0} x{1}. {2}. {3} of {4}.");
            Add("battle_menu_items_empty",          "No items.");
            Add("battle_menu_spell_heading",        "{0}. Skills.");
            Add("battle_menu_spell_detail",         "{0}. MP {1}. {2}. {3}. {4} of {5}.");
            Add("battle_menu_spell_unavailable",    "{0}. MP {1}. Unavailable. {2} of {3}.");
            Add("battle_menu_spell_empty",          "No skills.");
            Add("battle_menu_target_enemy",         "Using {0}. {1}. HP {2}%. {3} of {4}.");
            Add("battle_menu_target_enemy_exact",   "Using {0}. {1}. HP {2} of {3}. {4} of {5}.");
            Add("battle_menu_target_enemy_unknown",  "Using {0}. {1}. HP unknown. {2} of {3}.");
            Add("battle_menu_target_ally",          "Using {0}. {1}. HP {2} of {3}. MP {4} of {5}. {6} of {7}.");
            Add("battle_menu_target_all_enemies",   "Using {0}. All enemies.");
            Add("battle_menu_target_all_allies",    "Using {0}. All allies.");
            Add("battle_menu_target_self",          "Using {0}. Self.");

            // Battle menu — items without description
            Add("battle_menu_items_basic",          "{0} x{1}. {2} of {3}.");

            // Bonus gauge (during active combat)
            Add("bonus_gauge_break",                "Bonus level {0}, {1}.");
            Add("gauge_percent",                    "Gauge {0}.");
            Add("bonus_buff_unknown",               "Unknown buff");
            Add("bonus_buff_sphere_up",             "Sphere up");
            Add("bonus_buff_guts_up",               "Guts up");
            Add("bonus_buff_exp_up",                "EXP up");
            Add("bonus_buff_mp_cost_cut",           "MP cost cut");
            Add("bonus_buff_super_armer",           "Super armor");
            Add("bonus_buff_atk_up",                "Attack up");
            Add("bonus_buff_int_up",                "Intelligence up");
            Add("bonus_buff_def_up",                "Defence up");
            Add("bonus_buff_hit_up",                "Hit up");
            Add("bonus_buff_avd_up",                "Avoidance up");
            Add("bonus_buff_fol_up",                "Fol up");
            Add("bonus_buff_sphere_mp_recover",     "MP recovery");
            Add("bonus_buff_regist_abnormal",       "Abnormal resist");
            Add("bonus_buff_item_all_range",        "Item range up");
            Add("bonus_buff_item_recast_zero",      "Item recast zero");
            Add("bonus_buff_enemy_element_disable", "Enemy elements disabled");
            Add("bonus_buff_sphere_atk_up",         "Sphere attack up");
            Add("bonus_buff_item_not_consume",      "Items not consumed");
            Add("bonus_buff_hp_recover_ontime",     "HP recovery over time");
            Add("bonus_buff_mp_recover_ontime",     "MP recovery over time");
            Add("bonus_buff_atk_int_up_ontime",     "Attack and intelligence up over time");
            Add("bonus_buff_crt_up",                "Critical up");

            // Battle status announcements (during active combat)
            Add("battle_status_hp_below_50",        "{0}, health below 50 percent.");
            Add("battle_status_hp_below_25",        "{0}, health critical.");
            Add("battle_status_ko",                 "{0}, knocked out.");
            Add("battle_status_ailment",            "{0}, {1}.");
            Add("battle_status_damage_dealt",       "{0} damage.");

            // Battle menu — tactics/strategy
            Add("battle_menu_tactics_heading",      "Strategy.");
            Add("battle_menu_tactics_char",         "{0}. {1}. {2} of {3}.");
            Add("battle_menu_tactics_current",      "Currently set.");
            Add("battle_menu_position",             "{0} of {1}.");

            // Database — Tutorial sub-screen
            Add("db_tutorial_screen",       "Tutorial.");
            Add("db_tutorial_locked",       "Locked. {0} of {1}.");
            Add("db_tutorial_item",         "{0}. {1} of {2}.");
            Add("db_tutorial_item_new",     "{0}, New. {1} of {2}.");
            Add("db_tutorial_detail",       "{0}. {1}");

            // Database — Enemy Picture Book
            Add("db_enemy_screen",          "Enemy Picture Book.");
            Add("db_enemy_locked",          "Unknown enemy. {0} of {1}.");
            Add("db_enemy_item",            "{0}. {1} of {2}.");
            Add("db_enemy_item_new",        "{0}, New. {1} of {2}.");
            Add("db_enemy_boss",            "Boss.");
            Add("db_enemy_hp",              "HP: {0}.");
            Add("db_enemy_exp",             "EXP: {0}.");
            Add("db_enemy_fol",             "{0} Fol.");
            Add("db_enemy_drops",           "Drops: {0}.");
            Add("db_enemy_habitat",         "Habitat: {0}.");

            // Database — Item Picture Book
            Add("db_item_screen",           "Item Picture Book.");
            Add("db_item_locked",           "Unknown item. {0} of {1}.");
            Add("db_item_item",             "{0}. {1} of {2}.");
            Add("db_item_item_new",         "{0}, New. {1} of {2}.");

            // Database — Fish Picture Book
            Add("db_fish_screen",           "Fish Picture Book.");
            Add("db_fish_locked",           "Unknown fish. {0} of {1}.");
            Add("db_fish_item",             "{0}. {1} of {2}.");
            Add("db_fish_item_new",         "{0}, New. {1} of {2}.");
            Add("db_fish_rare",             "Rare.");
            Add("db_fish_crown",            "Crown.");
            Add("db_fish_shadow",           "Shadow: {0}.");
            Add("db_fish_habitat",          "Habitat: {0}.");
            Add("db_fish_caught",           "Caught: {0} times.");
            Add("db_fish_max_length",       "Max length: {0}.");

            // Database — Location Picture Book
            Add("db_location_screen",           "Location Picture Book.");
            Add("db_location_locked",           "Undiscovered. {0} of {1}.");
            Add("db_location_item",             "{0}. {1} of {2}.");
            Add("db_location_item_new",         "{0}, New. {1} of {2}.");
            Add("db_location_discovered_by",    "Discovered by: {0}.");

            // Quest list sub-screen (camp → Quests and Missions → Quests)
            Add("quest_screen",             "Quests.");
            Add("quest_empty",              "Empty. {0} of {1}.");
            // quest_item: {0}=name {1}=status {2}=position {3}=total
            Add("quest_item",               "{0}, {1}. {2} of {3}.");
            Add("quest_item_new",           "{0}, New, {1}. {2} of {3}.");
            Add("quest_status_available",   "Available");
            Add("quest_status_received",    "In progress");
            Add("quest_status_reportable",  "Ready to report");
            Add("quest_status_completed",   "Completed");
            Add("quest_status_not_achieved","Not achieved");
            Add("quest_rewards",            "Rewards: {0}");

            // Mission list sub-screen (camp → Quests and Missions → Missions)
            Add("mission_screen",              "Missions.");
            Add("mission_empty",               "Empty. {0} of {1}.");
            // mission_item: {0}=name {1}=status {2}=position {3}=total
            Add("mission_item",                "{0}, {1}. {2} of {3}.");
            Add("mission_category",            "{0}.");
            Add("mission_status_complete",     "Complete");
            Add("mission_status_achieved",     "Achieved");
            Add("mission_status_reportable",   "Ready to report");
            Add("mission_status_in_progress",  "In progress");
            Add("mission_status_incomplete",   "Incomplete");
            Add("mission_cat_beginner",        "Beginner");
            Add("mission_cat_expert",          "Expert");
            Add("mission_cat_specialist",      "Specialist");
            Add("mission_cat_legend",          "Legend");

            // Dialogue choice menus (private actions, story choices, etc.)
            // open_with_title: {0}=title {1}=total {2}=choiceText {3}=position
            Add("dialogue_choice_open_with_title", "{0}. Choice, {1} items. {2}, {3} of {1}.");
            // open: {0}=total {1}=choiceText {2}=position
            Add("dialogue_choice_open",            "Choice, {0} items. {1}, {2} of {0}.");
            // open_no_items: {0}=title {1}=total
            Add("dialogue_choice_open_no_items",   "Choice, {1} items.");
            Add("dialogue_choice_item",            "{0}, {1} of {2}.");

            // Item Creation sub-screen (Camp → Item Creation)
            Add("ic_screen",                  "Item Creation.");
            Add("ic_shortcut_screen",          "IC Specialty.");
            Add("ic_tab_itemcreation",        "Item Creation.");
            Add("ic_tab_specialskill",        "Special Skills.");
            Add("ic_tab_superspecialskill",   "Super Special Skills.");
            Add("ic_skill_level",             "Level {0}");
            Add("ic_skill_position",          "{0} of {1}.");
            Add("ic_action_screen",           "Creation.");
            Add("ic_action_position",         "{0} of {1}.");
            Add("ic_creates",                 "Creates: {0}");
            Add("ic_have_count",              "Have {0}");
            Add("ic_unavailable",             "Unavailable");
            Add("ic_factor",                  "Factor: {0}");
            Add("ic_result_heading",          "Creation result.");
            Add("ic_result_success",          "Success");
            Add("ic_result_failure",          "Failure");
            Add("ic_unknown_item",            "Unknown");
            Add("ic_material_screen",         "Material selection.");
            Add("ic_material_slots",          "Material slots.");
            Add("ic_material_itemlist",       "Choose item.");
            Add("ic_material_empty",          "Empty");
            Add("ic_material_slot",           "Slot {0}");
            Add("ic_material_create",         "Create");
            Add("ic_material_rate",           "Success rate: {0} percent.");

            // Train switch selector (toggle ON/OFF per party member)
            Add("ic_train_item",              "{0}, {1}. {2} of {3}.");
            Add("ic_train_on",                "ON");
            Add("ic_train_off",               "OFF");
            Add("ic_train_all_on",            "Turn all on. {0} of {1}.");
            Add("ic_train_all_off",           "Turn all off. {0} of {1}.");

            // Pickpocket field menu
            Add("pickpocket_heading",          "Pickpocket.");
            Add("pickpocket_rate",             "{0} percent");

            // Super Specialty (IC overlay)
            Add("ss_screen",                  "Super Specialty.");
            Add("ss_not_learned",             "Not learned");
            Add("ss_requires",                "Requires: {0}");
            Add("ss_position",                "{0} of {1}.");

            // Database — Player Data (virtual cursor navigation)
            Add("db_playerdata_screen",         "Player Data.");
            Add("db_playerdata_stat",           "{0}.");
            Add("db_playerdata_category_stat",  "{0}. {1}.");
            Add("db_playerdata_battle",         "Battle Data");
            Add("db_playerdata_collection",     "Collection Data");
            Add("db_playerdata_other",          "Other Data");
        }

        #endregion
    }
}
