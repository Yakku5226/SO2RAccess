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
            Add("help",         "Keyboard: F1 Help. NumPad 5 open or close navigation list. NumPad 8 and 2 move up and down. NumPad 4 and 6 change category. NumPad 1 walk to selected item or cancel walk. F12 toggle debug mode. Gamepad: hold L1 to open navigation, D-pad up and down for category, left and right for items. Push left stick up to walk.");

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
            Add("nav_open",           "Navigation. {0}. {1}, {2} units.");
            Add("nav_close",          "Navigation closed.");
            Add("nav_item",           "{0}, {1} units.");
            Add("nav_category",       "{0}. {1}, {2} units.");
            Add("nav_category_empty",   "{0}. None.");
            Add("nav_autowalk_start",      "Walking to {0}.");
            Add("nav_autowalk_arrived",    "Arrived at {0}.");
            Add("nav_autowalk_arrived_npc","Arrived at {0}. Press action button to interact. NumPad 1 or L1 to stop following.");
            Add("nav_autowalk_cancelled",  "Auto-walk cancelled.");
            Add("nav_autowalk_unreachable","Cannot reach {0}.");
            Add("nav_autowalk_no_navmesh", "No navigation data available.");
            Add("nav_autowalk_lost_path",  "Lost path to {0}.");
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
            Add("nav_event_story",     "Story event");
            Add("nav_event_pa",        "Private action");
            Add("nav_event_side",      "Side event");
            Add("nav_event_generic",   "Event");
            Add("nav_event_story_n",   "Story event {0}");
            Add("nav_event_pa_n",      "Private action {0}");
            Add("nav_event_side_n",    "Side event {0}");
            Add("nav_event_generic_n", "Event {0}");
            Add("nav_save",            "Save point");
            Add("nav_save_n",          "Save point {0}");
            Add("nav_save_recovery",   "Recovery save point");
            Add("nav_save_recovery_n", "Recovery save point {0}");

            // Navigation — enemies
            Add("nav_enemy_named",   "{0}, {1}");
            Add("nav_enemy_typed",   "{0} enemy");
            Add("nav_enemy_unknown", "Enemy");
            Add("nav_enemy_weak",    "weak");
            Add("nav_enemy_medium",  "medium");
            Add("nav_enemy_strong",  "strong");
            Add("nav_enemy_raid",    "raid");

            // Camp menu — root
            Add("camp_menu_screen",           "Camp menu.");
            Add("camp_menu_item",             "{0}, {1} of {2}.");
            Add("camp_menu_item_unavailable", "{0}, unavailable, {1} of {2}.");

            // Camp menu — item sub-screen
            Add("camp_item_screen",          "Items.");
            Add("camp_item_entry",           "{0}, {1}. {2}. {3} of {4}.");
            Add("camp_item_entry_nodesc",    "{0}, {1}. {2} of {3}.");

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

            // Camp menu — battle skill leveling sub-screen (also via Enhance → BattleSkillPoint)
            // Hook-driven: UIBattleSkillInformationPresenter.Set fires on every navigation.
            Add("camp_battleskill_screen",   "Battle skills.");
            Add("camp_combatskill_screen",   "Combat skills.");
            Add("camp_battleskill_level",    "Level {0} of {1}");
            Add("camp_battleskill_mp",       "MP: {0}");
            Add("camp_battleskill_position", "{0} of {1}.");

            // Camp menu — battle skill assignment sub-screen
            // Equip state: polled — "[Button]: [Skill]" or "[Button]: no skill assigned".
            // SelectBattleSkill state: hook-driven — "Assigning to [button]: [skill details]".
            Add("camp_battleskill_setting_screen",     "Battle skill assignment.");
            Add("camp_battleskill_setting_slot",       "{0}: {1}. {2} of {3}.");
            Add("camp_battleskill_setting_slot_empty", "{0}: no skill assigned. {1} of {2}.");
            Add("camp_battleskill_setting_assigning",  "Assigning to {0}");

            // Camp menu — formation sub-screen
            // Hook-driven: UICampFormationInformationPresenter.Set fires on every navigation.
            Add("camp_formation_screen",    "Formation.");
            Add("camp_formation_position",  "{0} of {1}.");

            // Camp menu — skills sub-screen (field/IC skills, NOT battle skills)
            // Hook-driven: UISkillInformationPresenter.Set fires on every navigation.
            Add("camp_skill_screen",        "Skills.");
            Add("camp_skill_level",         "Level {0}");
            Add("camp_skill_position",      "{0} of {1}.");

            // Camp menu — party formation sub-screen (character selection grid)
            Add("camp_party_formation_screen", "Party formation.");
            Add("camp_party_formation_char",   "{0}, Level {1}. {2}. {3} of {4}.");

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
            Add("shop_item_buy",        "{0}. {1} Fol. {2} of {3}.");
            Add("shop_item_sell",       "{0}. Sell: {1} Fol. Own: {2}. {3} of {4}.");
            Add("shop_item_quantity",   "Quantity: {0}. Total: {1} Fol.");

            // Item acquisition popups (treasure chests, quest rewards, etc.)
            Add("overflow_item",            "{0}");
            Add("overflow_item_multi",      "{0} x{1}");

            // Battle results
            Add("battle_result_heading",    "Battle complete.");
            Add("battle_result_exp",        "{0} EXP.");
            Add("battle_result_fol",        "{0} Fol.");
            Add("battle_result_sp",         "{0} SP.");
            Add("battle_result_bsp",        "{0} Battle Skill Points.");
            Add("battle_result_levelup",    "{0} leveled up to {1}.");
            Add("battle_result_levelup_sp", "Gained {0} SP.");
            Add("battle_result_levelup_bsp","Gained {0} Battle Skill Points.");
            Add("battle_result_learned_skills", "Learned: {0}.");
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

            // Battle target (L2 target change mode)
            Add("battle_target_hp_pct",         "HP {0}%.");
            Add("battle_target_hp_exact",       "HP {0} of {1}.");
            Add("battle_target_shield_pct",     "Shield {0}%.");
            Add("battle_target_shield_broken",  "Shield broken.");
            Add("battle_target_defeated",       "Defeated.");
            Add("battle_target_leader",         "Leader: {0}.");
            Add("battle_target_status",         "{0}.");
        }

        #endregion
    }
}
