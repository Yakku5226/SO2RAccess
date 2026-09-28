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
    /// Log-only survey of the Fun City minigame screens (bunny race, coliseum,
    /// cooking master). Nothing here speaks or changes behaviour; every line goes
    /// through <see cref="DebugLogger"/> (F12 debug mode) with a "[Trace:...]" tag.
    ///
    /// Purpose: these windows mix list screens (already logged by
    /// <see cref="ListSelectionHandler"/>) with digit entries, choice pickers and
    /// info panels that fire no list event and therefore leave no trace today. One
    /// debug-mode visit to each minigame with this file active shows, per screen:
    ///
    ///   [Trace:Windows]  which minigame windows exist in the loaded UI scenes.
    ///   [Trace:Screen]   window + stack state + concrete selector whenever the
    ///                    top selector changes, and when the window closes.
    ///   [Trace:Text]     every visible text of the open window whenever any of it
    ///                    changes (what a sighted player sees, cursor moves included).
    ///   [Trace:*]        targeted hooks that confirm which game methods are
    ///                    hookable for a real handler (medal count, paddock bunny,
    ///                    choice picker, rank / challenge info, cooking signals).
    ///
    /// Remove this file (and its three call sites in Main.cs) once the minigame
    /// handlers exist.
    /// </summary>
    public static class MiniGameTrace
    {
        #region Fields

        private const float PollInterval = 0.25f;
        private const float WindowRefreshInterval = 5f;
        private const int MaxFragments = 40;

        /// <summary>Stack windows this survey follows; all other windows are ignored.</summary>
        private static readonly HashSet<string> _tracedWindows = new HashSet<string>(StringComparer.Ordinal)
        {
            "UIBunnyRaceWindow",
            "UIColiseumWindow",
            "UICookingMasterWindow",
        };

        private static bool _patchesApplied;
        private static float _nextPollTime;
        private static float _nextWindowRefreshTime;

        private static readonly List<UIStackSelectorWindowBase> _windows = new List<UIStackSelectorWindowBase>();
        private static string _lastWindowSet = "";

        /// <summary>Last logged "state=.. selector=.." per window type name.</summary>
        private static readonly Dictionary<string, string> _lastScreenByWindow =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Last logged visible-text snapshot per window type name.</summary>
        private static readonly Dictionary<string, string> _lastTextByWindow =
            new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Patches

        /// <summary>
        /// Installs the targeted hooks. Each hook is patched on its own so one missing
        /// game method cannot lose the others; the outcome is always logged.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;
            _patchesApplied = true;

            RuntimeHelpers.RunClassConstructor(typeof(UIStackSelectorWindowBase).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRaceMedalShopSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRacePaddockPresenter).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRaceBetSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UISelectChoicePresenter).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIColiseumCharacterSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIDuelBattleSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UISurvivalBattleSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIChallengeBattleSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterCookListSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterSignalPresenter).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterRhythmGameSelector).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterFoodActionPresenter).TypeHandle);

            var signalState = typeof(UICookingMasterSignalSelector.SignalState);

            // Bunny race
            Patch(harmony, "MedalShop", typeof(UIBunnyRaceMedalShopSelector), "UpdateMedalCount",
                new[] { typeof(int), typeof(bool) }, nameof(UpdateMedalCount_Postfix));
            Patch(harmony, "Paddock", typeof(UIBunnyRacePaddockPresenter), "ShowBunnyInfoAt",
                new[] { typeof(int) }, nameof(ShowBunnyInfoAt_Postfix));
            Patch(harmony, "BetChara", typeof(UIBunnyRaceBetSelector), "SelectCharaInfo",
                new[] { typeof(bool) }, nameof(SelectCharaInfo_Postfix));

            // Choice pickers outside the conversation window (coliseum character,
            // cooking master opponent / cook)
            Patch(harmony, "ChoiceSet", typeof(UISelectChoicePresenter), "Set",
                new[] { typeof(string), typeof(Il2CppSystem.Collections.Generic.List<UIChoiceData>) },
                nameof(SelectChoicePresenter_Set_Postfix));
            Patch(harmony, "ColiseumChara", typeof(UIColiseumCharacterSelector), "UpdatePresenter",
                Type.EmptyTypes, nameof(ColiseumCharacter_UpdatePresenter_Postfix));
            Patch(harmony, "CookList", typeof(UICookingMasterCookListSelector), "UpdatePresenter",
                Type.EmptyTypes, nameof(CookList_UpdatePresenter_Postfix));

            // Coliseum rule screens
            Patch(harmony, "Duel", typeof(UIDuelBattleSelector), "UpdatePresenter",
                Type.EmptyTypes, nameof(Duel_UpdatePresenter_Postfix));
            Patch(harmony, "Survival", typeof(UISurvivalBattleSelector), "SetChallenger",
                new[] { typeof(string), typeof(int), typeof(PlayerID) }, nameof(Survival_SetChallenger_Postfix));
            Patch(harmony, "Challenge", typeof(UIChallengeBattleSelector), "UpdatePresenter",
                Type.EmptyTypes, nameof(Challenge_UpdatePresenter_Postfix));

            // Cooking master
            Patch(harmony, "Signal(one)", typeof(UICookingMasterSignalPresenter), "Set",
                new[] { typeof(string), signalState }, nameof(SignalSetOne_Postfix));
            Patch(harmony, "Signal(pair)", typeof(UICookingMasterSignalPresenter), "Set",
                new[] { typeof(string), typeof(string), signalState }, nameof(SignalSetPair_Postfix));
            Patch(harmony, "Rhythm", typeof(UICookingMasterRhythmGameSelector), "SetupCookingResult",
                new[] { typeof(int), typeof(int), typeof(int), typeof(NotesResultType) },
                nameof(SetupCookingResult_Postfix));
            Patch(harmony, "FoodCount", typeof(UICookingMasterFoodActionPresenter), "SetCreateCount",
                new[] { typeof(int) }, nameof(SetCreateCount_Postfix));
        }

        /// <summary>
        /// Patches one method with a postfix and reports the outcome in the normal log
        /// (not debug-only: F12 is usually pressed long after start-up).
        /// </summary>
        private static void Patch(HarmonyLib.Harmony harmony, string tag, Type type, string method,
            Type[] args, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    MelonLogger.Warning($"[Trace:{tag}] {type.Name}.{method} not found, hook inactive.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(typeof(MiniGameTrace), postfix));
                MelonLogger.Msg($"[Trace:{tag}] hook applied on {type.Name}.{method}.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Trace:{tag}] patching {type.Name}.{method} failed: {ex.Message}");
            }
        }

        #endregion

        #region Hook bodies

        private static void UpdateMedalCount_Postfix(UIBunnyRaceMedalShopSelector __instance,
            int newMedalCount, bool isUp)
        {
            if (!Main.DebugMode) return;
            try
            {
                var p = __instance.medalShopPresenter;
                DebugLogger.LogState($"[Trace:MedalShop] count={newMedalCount} up={isUp} digit={__instance.digit} "
                    + $"buyMedalCount={__instance.buyMedalCount} | label='{Text(p?.buyMedalLabelText)}' "
                    + $"buy='{Text(p?.buyMedalCountText)}' total='{Text(p?.totalMoneyCountText)}' "
                    + $"medals='{Text(p?.retainedMedalText)}' fol='{Text(p?.retainedMoneyText)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:MedalShop] read error: {ex.Message}"); }
        }

        private static void ShowBunnyInfoAt_Postfix(UIBunnyRacePaddockPresenter __instance, int index)
        {
            if (!Main.DebugMode) return;
            try
            {
                bool condition = __instance.conditionObj != null && __instance.conditionObj.activeInHierarchy;
                DebugLogger.LogState($"[Trace:Paddock] index={index} "
                    + $"number='{Text(__instance.bunnyIndexText)}' name='{Text(__instance.bunnyNameText)}' "
                    + $"speed='{Text(__instance.bunnySpeedText)}' stamina='{Text(__instance.bunnyStaminaText)}' "
                    + $"personality='{Text(__instance.bunnyPersonalityText)}' conditionShown={condition}");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:Paddock] read error: {ex.Message}"); }
        }

        private static void SelectCharaInfo_Postfix(UIBunnyRaceBetSelector __instance, bool isRight)
        {
            if (!Main.DebugMode) return;
            try
            {
                DebugLogger.LogState($"[Trace:BetChara] right={isRight} bunnyIndex={__instance.bunnyIndex} "
                    + $"last={__instance.lastSelectedBunnyIndex} race='{Text(__instance.raceName)}' "
                    + $"medals='{Text(__instance.medalText)}' listIndex={__instance.CurrentIndex}/{__instance.DataCount}");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:BetChara] read error: {ex.Message}"); }
        }

        private static void SelectChoicePresenter_Set_Postfix(UISelectChoicePresenter __instance,
            string title, Il2CppSystem.Collections.Generic.List<UIChoiceData> dataList)
        {
            if (!Main.DebugMode) return;
            try
            {
                var items = new List<string>();
                int count = dataList?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    var d = dataList[i];
                    if (d == null) continue;
                    items.Add($"{TextUtil.StripTags(d.message)}{(d.canDecision ? "" : " (locked)")}");
                }
                DebugLogger.LogState($"[Trace:ChoiceSet] title='{TextUtil.StripTags(title)}' "
                    + $"parent={__instance.transform?.parent?.name} choices={count}: {string.Join(" | ", items)}");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:ChoiceSet] read error: {ex.Message}"); }
        }

        private static void ColiseumCharacter_UpdatePresenter_Postfix(UIColiseumCharacterSelector __instance)
        {
            if (!Main.DebugMode) return;
            try
            {
                DebugLogger.LogState($"[Trace:ColiseumChara] index={__instance.selectChoiceIndex} "
                    + $"rule={__instance.currentRule} title='{Text(__instance.titleLabel)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:ColiseumChara] read error: {ex.Message}"); }
        }

        private static void CookList_UpdatePresenter_Postfix(UICookingMasterCookListSelector __instance)
        {
            if (!Main.DebugMode) return;
            try
            {
                DebugLogger.LogState($"[Trace:CookList] index={__instance.selectChoiceIndex} state={__instance.selectState}");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:CookList] read error: {ex.Message}"); }
        }

        private static void Duel_UpdatePresenter_Postfix(UIDuelBattleSelector __instance)
        {
            if (!Main.DebugMode) return;
            try
            {
                var info = __instance.rankInformationPresenter;
                DebugLogger.LogState($"[Trace:Duel] index={__instance.CurrentIndex}/{__instance.DataCount} "
                    + $"state={__instance.currentState} header='{Text(__instance.headerText)}' "
                    + $"challenger='{Text(__instance.challengerName)}' lv='{Text(__instance.challengerLevel)}' | "
                    + $"rank='{Text(info?.rank)}' recLv='{Text(info?.recommendLevel)}' "
                    + $"desc='{Text(info?.rankDescription)}' reward='{Text(info?.rewardItemName)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:Duel] read error: {ex.Message}"); }
        }

        private static void Survival_SetChallenger_Postfix(UISurvivalBattleSelector __instance,
            string challengerName, int level, PlayerID playerID)
        {
            if (!Main.DebugMode) return;
            try
            {
                var info = __instance.rewardInformationPresenter;
                DebugLogger.LogState($"[Trace:Survival] challenger='{challengerName}' lv={level} id={playerID} "
                    + $"maxWins='{Text(info?.maxConsecutiveWinCount)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:Survival] read error: {ex.Message}"); }
        }

        private static void Challenge_UpdatePresenter_Postfix(UIChallengeBattleSelector __instance)
        {
            if (!Main.DebugMode) return;
            try
            {
                var info = __instance.battleInformationPresenter;
                DebugLogger.LogState($"[Trace:Challenge] index={__instance.CurrentIndex}/{__instance.DataCount} | "
                    + $"name='{Text(info?.battleName)}' recLv='{Text(info?.recommendLevel)}' "
                    + $"party='{Text(info?.partyRule)}' rule='{Text(info?.battleRule)}' "
                    + $"desc='{Text(info?.description)}' reward1='{Text(info?.firstRewardName)}' "
                    + $"reward2='{Text(info?.secondRewardName)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:Challenge] read error: {ex.Message}"); }
        }

        private static void SignalSetOne_Postfix(string signal, UICookingMasterSignalSelector.SignalState type)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"[Trace:Signal] '{TextUtil.StripTags(signal)}' type={type}");
        }

        private static void SignalSetPair_Postfix(string left, string right,
            UICookingMasterSignalSelector.SignalState type)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"[Trace:Signal] left='{TextUtil.StripTags(left)}' right='{TextUtil.StripTags(right)}' type={type}");
        }

        private static void SetupCookingResult_Postfix(int notesNumber, int cookedItemID, int addScore,
            NotesResultType resultType)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"[Trace:Rhythm] note={notesNumber} item={cookedItemID} "
                + $"'{TextUtil.ResolveItemName(cookedItemID)}' score+={addScore} result={resultType}");
        }

        private static void SetCreateCount_Postfix(UICookingMasterFoodActionPresenter __instance, int count)
        {
            if (!Main.DebugMode) return;
            try
            {
                DebugLogger.LogState($"[Trace:FoodCount] count={count} text='{Text(__instance.createCount)}'");
            }
            catch (Exception ex) { DebugLogger.LogState($"[Trace:FoodCount] read error: {ex.Message}"); }
        }

        #endregion

        #region Polling

        /// <summary>
        /// Every 0.25 s in debug mode: refreshes the traced window list, then logs the
        /// top selector and the visible text of each open minigame window when they
        /// change. Called every frame from Main.UpdateHandlers().
        /// </summary>
        public static void Update()
        {
            if (!Main.DebugMode) return;

            float now = Time.unscaledTime;
            if (now < _nextPollTime) return;
            _nextPollTime = now + PollInterval;

            if (now >= _nextWindowRefreshTime)
            {
                _nextWindowRefreshTime = now + WindowRefreshInterval;
                RefreshWindows();
            }

            for (int i = 0; i < _windows.Count; i++)
                PollWindow(_windows[i]);
        }

        /// <summary>Forgets cached windows and snapshots on scene change.</summary>
        public static void OnSceneChanged()
        {
            _windows.Clear();
            _lastWindowSet = "";
            _lastScreenByWindow.Clear();
            _lastTextByWindow.Clear();
            _nextWindowRefreshTime = 0f;
        }

        /// <summary>Finds the traced stack windows, inactive ones included, and logs the set once.</summary>
        private static void RefreshWindows()
        {
            try
            {
                _windows.Clear();
                var found = UnityEngine.Object.FindObjectsOfType<UIStackSelectorWindowBase>(true);
                if (found == null) return;

                var names = new List<string>();
                for (int i = 0; i < found.Length; i++)
                {
                    var w = found[i];
                    if (w == null) continue;
                    string name = w.GetIl2CppType()?.Name;
                    if (name == null || !_tracedWindows.Contains(name)) continue;
                    _windows.Add(w);
                    names.Add(name);
                }

                names.Sort(StringComparer.Ordinal);
                string set = string.Join(", ", names);
                if (set != _lastWindowSet)
                {
                    _lastWindowSet = set;
                    DebugLogger.LogState($"[Trace:Windows] found: {(set.Length == 0 ? "(none)" : set)}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Trace:Windows] refresh error: {ex.Message}");
            }
        }

        /// <summary>Logs the window's top selector and visible text when either changes.</summary>
        private static void PollWindow(UIStackSelectorWindowBase window)
        {
            string wName;
            bool opened;
            try
            {
                wName = window.GetIl2CppType()?.Name ?? "unknown";
                opened = window.IsOpened;
            }
            catch
            {
                return; // destroyed with its scene; the next refresh drops it
            }

            if (!opened)
            {
                if (_lastScreenByWindow.Remove(wName))
                {
                    _lastTextByWindow.Remove(wName);
                    DebugLogger.LogState($"[Trace:Screen] {wName} closed.");
                }
                return;
            }

            string selectorName = "(none)";
            int state = -1;
            try
            {
                var peek = window.GetPeekSelector();
                state = window.GetCurrentState();
                if (peek != null) selectorName = peek.GetIl2CppType()?.Name ?? "unknown";
            }
            catch (Exception ex)
            {
                selectorName = $"(error: {ex.Message})";
            }

            string screen = $"state={state} selector={selectorName}";
            if (!_lastScreenByWindow.TryGetValue(wName, out string lastScreen) || lastScreen != screen)
            {
                _lastScreenByWindow[wName] = screen;
                _lastTextByWindow.Remove(wName);
                DebugLogger.LogState($"[Trace:Screen] {wName} {screen}");
            }

            string text = ReadVisibleText(window);
            if (!_lastTextByWindow.TryGetValue(wName, out string lastText) || lastText != text)
            {
                _lastTextByWindow[wName] = text;
                DebugLogger.LogState($"[Trace:Text] {wName}: {text}");
            }
        }

        #endregion

        #region Text helpers

        /// <summary>All distinct visible texts under the window, joined for one log line.</summary>
        private static string ReadVisibleText(Component root)
        {
            try
            {
                var texts = root.GetComponentsInChildren<GameText>(false);
                if (texts == null || texts.Length == 0) return "(no text)";

                var fragments = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < texts.Length; i++)
                {
                    string cleaned = Text(texts[i]);
                    if (string.IsNullOrWhiteSpace(cleaned) || !seen.Add(cleaned)) continue;
                    fragments.Add(cleaned);
                    if (fragments.Count >= MaxFragments) { fragments.Add("..."); break; }
                }
                return fragments.Count == 0 ? "(no text)" : string.Join(" | ", fragments);
            }
            catch (Exception ex)
            {
                return $"(read error: {ex.Message})";
            }
        }

        /// <summary>Tag-stripped text of a GameText, or an empty string when absent.</summary>
        private static string Text(GameText gt)
        {
            if (gt == null) return "";
            string raw = ((Il2CppTMPro.TMP_Text)gt)?.text;
            return TextUtil.StripTags(raw) ?? "";
        }

        #endregion
    }
}
