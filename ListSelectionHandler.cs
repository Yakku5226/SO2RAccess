using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Universal list-selection safety net (game-api.md section 19).
    ///
    /// The game routes almost every list row through
    /// <c>UICanSelectedListItemPresenterBase.OnSelected</c>, and unlike native
    /// command-menu cursor movement this method DOES fire Harmony hooks. This handler
    /// posts a postfix on the base method so that any list screen without a dedicated
    /// handler still speaks the focused row's visible text instead of being silent.
    ///
    /// Design (deliberately conservative):
    /// - Rows whose concrete presenter type is already announced by a dedicated
    ///   handler (camp, shop, battle, config, quests, ...) are suppressed here so the
    ///   validated polling code stays the single voice — no double speech.
    /// - Generic row types (UICommonTextListItemPresenter etc.) are also suppressed
    ///   while the camp or shop screens are open, because those screens' sub-lists
    ///   are covered by polling; outside them (e.g. the guild counter's command
    ///   menu) they are spoken.
    /// - The row is read ~0.15s after the selection event so the game has finished
    ///   populating its texts (pattern verified by the reference-mod analysis).
    /// - In debug mode every event logs its concrete type and the decision, so an
    ///   unknown screen can be identified from one visit to it.
    /// </summary>
    public class ListSelectionHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        /// <summary>Seconds to wait after OnSelected before reading the row.</summary>
        private const float ReadDelay = 0.15f;
        /// <summary>Window in which an identical announcement is considered a duplicate.</summary>
        private const float DedupeWindow = 0.6f;
        /// <summary>Maximum number of text fragments joined into one announcement.</summary>
        private const int MaxFragments = 6;

        // Latest pending selection — overwritten by newer events, read from Update().
        private static UICanSelectedListItemPresenterBase _pending;
        private static string _pendingTypeName;
        private static float _pendingTime;

        private static string _lastSpoken = "";
        private static float _lastSpokenTime = -999f;

        // Last OnSelected time per concrete presenter type — lets other handlers
        // verify a list REALLY has focus (e.g. GuildHandler vs. the stale-woken
        // quest selector). Updated for every fire, including suppressed ones.
        private static readonly Dictionary<string, float> _lastSelectedByType =
            new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// Concrete presenter types whose screens are already announced by a dedicated
        /// handler — the universal hook stays silent for these. Grouped by owner.
        /// </summary>
        private static readonly HashSet<string> _coveredTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            // CampMenuHandler (root, items, equip, skills, formation, operations,
            // item creation, status, database picture books, quests, missions)
            "UICampMenuItemPresenter",
            "UICampCommandListItemPresenter",
            "UICampItemListItemPresenter",
            "UIItemCharacterStatusItemPresenter",
            "UICharacterStatusListItemPresenter",
            "UIEquipListItemPresenter",
            "UICampBattleSkillSettingEquipListItemPresenter",
            "UICampSkillListItemPresenter",
            "UISkillLearningListItemPresenter",
            "UICampFormationListItemPresenter",
            "UICampOperationCharacterListItemPresenter",
            "UICampAssistSettingCharacterListItemPresenter",
            "UISpecialSkillConsumeListItemPresenter",
            "UISpecialSkillCreationListItemPresenter",
            "UICampSpecialSkillResultListItemPresenter",
            "UICampEnemyPictureBookListItemPresenter",
            "UICampFishPictureBookListItemPresenter",
            "UICampLocationPictureBookListItemPresenter",
            "UICommonBookListItemPresenter",
            "UIQuestListItemPresenter",   // also GuildHandler's quest list poll
            "UIMissionListItemPresenter",

            // ShopHandler
            "UIShopItemListItemPresenter",

            // FishCollectorHandler
            "UIFishCollectorMenuListItemPresenter",
            "UIFishCollectorSelectFishListItemPresenter",
            "UIFishCollectorExchangeListItemPresenter",
            "UIFishCollectorCheckRewardListItemPresenter",

            // Battle handlers (menu, pause, target, status)
            "UIBattleMenuItemPresenter",
            "UIBattleSkillListItemPresenter",
            "UIBattleSpellListItemPresenter",
            "UIBattleItemListItemPresenter",
            "UIBattleStatusListItemPresenter",
            "UIBattleEnemyParameterListItemPresenter",
            "UIBattleOperationListItemPresenter",
            "UIBattleSkillDialogSelectItemPresenter",
            "UIBattlePauseCharacterListItemPresenter",
            "UIBattlePauseBuffDebuffListItemPresenter",
            "UIBattlePauseBonusListItemPresenter",

            // ConfigMenuHandler / KeyboardMenuHandler
            "UIConfigMenuItemPresenter",
            "UIConfigKeyboardListItemPresenter",

            // LoadGameHandler / save screens
            "UISaveLoadListItemPresenter",

            // TitleMenuHandler / GameOverHandler
            "UITitleMenuItemPresenter",
            "UITitleSelectLanguageListItemPresenter",
            "UIGameOverListItemPresenter",

            // WorldMapHandler (fast travel)
            "UIWorldMapLocationListItemPresenter",

            // Fishing minigame handlers
            "UIFieldFishingBaitListItemPresenter",
            "UIFieldFishingTargetListItemPresenter",
            "UIFieldFishingResultListItemPresenter",

            // Equip wizard / overflow flows
            "UIItemDiscardListItemPresenter",
        };

        #endregion

        #region Patches

        /// <summary>
        /// Hooks UICanSelectedListItemPresenterBase.OnSelected — the universal
        /// selection event every list row in the game routes through.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UICanSelectedListItemPresenterBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ListItemDataBase).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UICanSelectedListItemPresenterBase),
                        nameof(UICanSelectedListItemPresenterBase.OnSelected),
                        new Type[] { typeof(ListItemDataBase) }),
                    postfix: new HarmonyMethod(typeof(ListSelectionHandler),
                        nameof(OnSelected_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("[LISTSEL] Universal OnSelected patch applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LISTSEL] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Fires on every list-row selection game-wide. Filters covered screens and
        /// stores the rest as the pending row for the delayed read in Update().
        /// </summary>
        private static void OnSelected_Postfix(UICanSelectedListItemPresenterBase __instance)
        {
            if (__instance == null) return;

            try
            {
                string typeName = __instance.GetIl2CppType()?.Name ?? "unknown";

                _lastSelectedByType[typeName] = Time.unscaledTime;

                if (_coveredTypes.Contains(typeName))
                {
                    DebugLogger.LogState($"ListSelection: {typeName} suppressed (dedicated handler owns it).");
                    return;
                }

                // Camp and shop sub-lists are polled by their own handlers even when the
                // row type is generic — never compete with them on their screens.
                if (CampMenuHandler.IsCampOpen)
                {
                    DebugLogger.LogState($"ListSelection: {typeName} suppressed (camp open).");
                    return;
                }
                if (ShopHandler.IsShopOpen)
                {
                    DebugLogger.LogState($"ListSelection: {typeName} suppressed (shop open).");
                    return;
                }

                _pending = __instance;
                _pendingTypeName = typeName;
                _pendingTime = Time.unscaledTime;
                DebugLogger.LogState($"ListSelection: {typeName} pending generic read.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ListSelection.OnSelected_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Reads and announces the pending row once the settle delay has elapsed.
        /// Called every frame from Main.UpdateHandlers().
        /// </summary>
        public void Update()
        {
            if (_pending == null) return;
            if (Time.unscaledTime - _pendingTime < ReadDelay) return;

            var presenter = _pending;
            string typeName = _pendingTypeName;
            _pending = null;

            try
            {
                if (presenter.gameObject == null || !presenter.gameObject.activeInHierarchy)
                {
                    DebugLogger.LogState($"ListSelection: {typeName} dropped (row no longer visible).");
                    return;
                }

                string text = ReadPresenterText(presenter);
                if (string.IsNullOrEmpty(text))
                {
                    DebugLogger.LogState($"ListSelection: {typeName} dropped (no readable text found).");
                    return;
                }

                // The same row can re-fire on screen refreshes — don't repeat it.
                if (text == _lastSpoken && Time.unscaledTime - _lastSpokenTime < DedupeWindow)
                {
                    DebugLogger.LogState($"ListSelection: {typeName} dropped (duplicate within {DedupeWindow}s).");
                    return;
                }
                _lastSpoken = text;
                _lastSpokenTime = Time.unscaledTime;

                ScreenReader.Say(text);
                DebugLogger.LogGameValue("ListSelection.spoke", $"[{typeName}] {text}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ListSelection.Update: {ex.Message}");
            }
        }

        /// <summary>Clears pending state on scene change.</summary>
        public void OnSceneChanged()
        {
            _pending = null;
            _pendingTypeName = null;
            _lastSpoken = "";
            _lastSpokenTime = -999f;
            _lastSelectedByType.Clear();
        }

        /// <summary>
        /// True if a row of the given concrete presenter type fired OnSelected within
        /// the last <paramref name="window"/> seconds. Lets polling handlers confirm a
        /// list really has input focus — a selector woken with stale data (e.g. the
        /// guild counter waking the quest selector) never fires OnSelected.
        /// </summary>
        public static bool WasRecentlySelected(string typeName, float window)
        {
            return _lastSelectedByType.TryGetValue(typeName, out float t)
                && Time.unscaledTime - t <= window;
        }

        #endregion

        #region Text Reading

        /// <summary>
        /// Collects the visible text fragments from the row's active GameText children
        /// and joins them into one announcement (name, value, state, ...).
        /// </summary>
        private static string ReadPresenterText(UICanSelectedListItemPresenterBase presenter)
        {
            var texts = presenter.GetComponentsInChildren<GameText>(false);
            if (texts == null || texts.Length == 0) return null;

            var fragments = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var gt in texts)
            {
                if (gt == null) continue;
                string raw = ((Il2CppTMPro.TMP_Text)gt)?.text;
                string cleaned = TextUtil.StripTags(raw);
                if (IsPlaceholder(cleaned)) continue;
                if (!seen.Add(cleaned)) continue;

                fragments.Add(cleaned);
                if (fragments.Count >= MaxFragments) break;
            }

            return fragments.Count == 0 ? null : string.Join(", ", fragments);
        }

        /// <summary>
        /// True for texts that carry no information for the player: empty strings,
        /// lone dashes, zero-width characters, and the game's "0000" dummy fills.
        /// </summary>
        private static bool IsPlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            return text == "-" || text == "—" || text == "\u200b" || text == "0000";
        }

        #endregion
    }
}
