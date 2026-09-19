using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Log-only diagnostics for the open questions of docs/review-2026-09-19.md.
    /// Nothing here speaks or changes behaviour; every line is written through
    /// <see cref="DebugLogger"/> (F12 debug mode) with a "[Probe:...]" tag so the
    /// answers can be found with one search of Latest.log.
    ///
    ///   [Probe:ICPanel]  A1 — does the item creation info panel show an owned count
    ///                    on the Writing / Publication screens?
    ///   [Probe:ICLevel]  A2 — does the IC skill panel show a level for a learned
    ///                    super specialty, and which Set overload fills it?
    ///   [Probe:Cutin]    A6 — reward numbers behind a field auto-kill.
    ///   [Probe:SP]       A6 — any change of a party member's SP / BP outside battle.
    ///   [Probe:Pickup]   C  — is a pickup's "x20" the amount gained or the new stock?
    ///   [Probe:Gauge]    A4 / B2 — break directing cross-check, bonus rows per level.
    ///   [Probe:ICResult] B3 — the game's own cursor on the IC result screen.
    ///   [Probe:SkillBook] B2 — what the game shows after a skill book is used.
    ///
    /// Remove this file (and its three call sites) once the questions are answered.
    /// </summary>
    public static class ReviewProbes
    {
        #region Fields

        private const float SpPollInterval = 0.5f;

        /// <summary>
        /// Delays (seconds) after a skill book use at which the visible text is logged.
        /// Emptied 2026-09-19: each dump froze the game ~3 s (1404 texts) and the three
        /// dumps were identical — the game shows no new text after a skill book.
        /// </summary>
        private static readonly float[] _skillBookDumpDelays = System.Array.Empty<float>();

        private static bool _patchesApplied;

        private static readonly Dictionary<PlayerID, int> _lastSp = new Dictionary<PlayerID, int>();
        private static readonly Dictionary<PlayerID, int> _lastBp = new Dictionary<PlayerID, int>();
        private static float _nextSpPollTime;

        private static int _pickupStockBefore = -1;

        private static int _lastResultCursor = int.MinValue;

        private static float _skillBookUseTime = -1f;
        private static int _skillBookDumpsDone;

        #endregion

        #region Patches

        /// <summary>
        /// Installs the probe hooks. Each hook is patched on its own so one missing
        /// game method cannot lose the others; the outcome is always logged.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;
            _patchesApplied = true;

            RuntimeHelpers.RunClassConstructor(typeof(UIItemCreationInformationPresenter).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UISpecialSkillInformationPresenter).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(UIFieldController).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(ItemSkillLevelup).TypeHandle);

            var listOfString = typeof(Il2CppSystem.Collections.Generic.List<string>);
            var listOfConditions = typeof(Il2CppSystem.Collections.Generic.List<UIConditionGroupData>);

            Patch(harmony, "ICPanel", typeof(UIItemCreationInformationPresenter), "Set",
                new[] { typeof(UIItemCreationInformationData) },
                postfix: nameof(CreationInfoPresenter_Set_Postfix));

            Patch(harmony, "ICLevel(list overload)", typeof(UISpecialSkillInformationPresenter), "Set",
                new[] { typeof(string), typeof(string), listOfString, listOfConditions,
                        typeof(string), typeof(bool), typeof(int) },
                postfix: nameof(SkillInfoPresenter_SetList_Postfix));

            Patch(harmony, "ICLevel(two-condition overload)", typeof(UISpecialSkillInformationPresenter), "Set",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string),
                        typeof(bool), typeof(bool), typeof(string), typeof(bool), typeof(int) },
                postfix: nameof(SkillInfoPresenter_SetTwoConditions_Postfix));

            Patch(harmony, "Cutin", typeof(UIFieldController), "AddCutinResult",
                new[] { typeof(BattleResultInfo) },
                prefix: nameof(AddCutinResult_Prefix));

            Patch(harmony, "Pickup", typeof(UIFieldController), "ShowItemInformation",
                new[] { typeof(int), typeof(int), typeof(FactorID) },
                prefix: nameof(ShowItemInformation_Prefix),
                postfix: nameof(ShowItemInformation_Postfix));

            Patch(harmony, "Gauge(break directing)", typeof(BattleManager), "StartBonusGaugeBreakDirecting",
                Type.EmptyTypes,
                postfix: nameof(StartBonusGaugeBreakDirecting_Postfix));

            Patch(harmony, "SkillBook", typeof(ItemSkillLevelup), "OnProcess",
                Type.EmptyTypes,
                prefix: nameof(ItemSkillLevelup_OnProcess_Prefix),
                postfix: nameof(ItemSkillLevelup_OnProcess_Postfix));
        }

        /// <summary>
        /// Patches one method and reports the outcome in the normal log (not debug-only:
        /// F12 is usually pressed long after start-up, which hid the bonus gauge hook's
        /// confirmation in the 2026-09-19 log).
        /// </summary>
        private static void Patch(HarmonyLib.Harmony harmony, string probe, Type type, string method,
            Type[] args, string prefix = null, string postfix = null)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    MelonLogger.Warning($"[Probe:{probe}] {type.Name}.{method} not found, probe inactive.");
                    return;
                }

                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(ReviewProbes), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(ReviewProbes), postfix) : null);
                MelonLogger.Msg($"[Probe:{probe}] hook applied on {type.Name}.{method}.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Probe:{probe}] patching {type.Name}.{method} failed: {ex.Message}");
            }
        }

        #endregion

        #region Polling

        /// <summary>
        /// Per-frame part of the probes: the SP / BP watcher and the timed text dumps
        /// after a skill book use. Does nothing unless debug mode is on.
        /// </summary>
        public static void Update()
        {
            if (!Main.DebugMode) return;

            float now = Time.unscaledTime;

            if (now >= _nextSpPollTime)
            {
                _nextSpPollTime = now + SpPollInterval;
                PollSkillPoints();
            }

            if (_skillBookUseTime >= 0f)
                PollSkillBookDumps(now);
        }

        /// <summary>
        /// A6: logs every change of a party member's SP or BP. Together with the
        /// [Probe:Cutin] line this shows whether a field auto-kill grants SP silently.
        /// </summary>
        private static void PollSkillPoints()
        {
            try
            {
                var members = PartyManager.Instance?.GetPartyMembers();
                var user = ParameterManager.Instance?.UserParameter;
                if (members == null || user == null) return;

                bool inBattle = (BattleManager.Instance?.battlePlayerList?.Count ?? 0) > 0;

                foreach (var id in members)
                {
                    if (id == PlayerID.INVALID || id == PlayerID.MAX) continue;
                    var cp = user.GetCharacterParameter(id);
                    if (cp == null) continue;

                    LogPointChange("SP", id, _lastSp, cp.SkillPoint, inBattle);
                    LogPointChange("BP", id, _lastBp, cp.CombatSkillPoint, inBattle);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:SP] read error: {ex.Message}");
            }
        }

        private static void LogPointChange(string label, PlayerID id, Dictionary<PlayerID, int> last,
            int value, bool inBattle)
        {
            if (last.TryGetValue(id, out int previous) && previous != value)
            {
                DebugLogger.LogState($"[Probe:SP] {id} {label} {previous} -> {value} "
                    + $"({value - previous:+0;-0}), inBattle={inBattle}.");
            }
            last[id] = value;
        }

        /// <summary>B2: logs all visible text at fixed delays after a skill book was used.</summary>
        private static void PollSkillBookDumps(float now)
        {
            if (_skillBookDumpsDone >= _skillBookDumpDelays.Length)
            {
                _skillBookUseTime = -1f;
                return;
            }

            float delay = _skillBookDumpDelays[_skillBookDumpsDone];
            if (now < _skillBookUseTime + delay) return;

            _skillBookDumpsDone++;
            DebugLogger.LogState($"[Probe:SkillBook] visible text {delay:F1} s after use:");
            int found = SubtitleHandler.LogVisibleText("Probe:SkillBook");
            DebugLogger.LogState($"[Probe:SkillBook] {found} visible text object(s).");
        }

        #endregion

        #region Probes called from handlers

        /// <summary>
        /// B3: logs the game's own cursor on the item creation result screen whenever it
        /// moves, so an arrival on a non-first row can be told apart from a mod race.
        /// </summary>
        public static void TrackResultCursor(int gameCursor, int rowCount, bool selectorActive)
        {
            if (!Main.DebugMode) return;
            if (!selectorActive)
            {
                _lastResultCursor = int.MinValue;
                return;
            }
            if (gameCursor == _lastResultCursor) return;

            string from = _lastResultCursor == int.MinValue ? "(screen shown)" : _lastResultCursor.ToString();
            _lastResultCursor = gameCursor;
            DebugLogger.LogState($"[Probe:ICResult] game cursor {from} -> {gameCursor} of {rowCount} rows.");
        }

        /// <summary>
        /// B2: logs the bonus rows the game holds for the current formation (level,
        /// type, value) and every cached bonus value, at the moment a level settles.
        /// Shows whether "Bonus level N" with no new bonus TYPE was a stronger value.
        /// </summary>
        public static void LogBonusRows(BattleManager bm, int level, IEnumerable<BonusBuffType> allTypes)
        {
            if (!Main.DebugMode) return;
            try
            {
                var rows = new List<string>();
                var list = PartyManager.Instance?.GetBattleSphereBonusList();
                if (list != null)
                {
                    // Index loop: foreach over an IL2CPP list of this type throws
                    // "type initializer for 'Enumerator'" (log 2026-09-19 14:21).
                    for (int i = 0; i < list.Count; i++)
                    {
                        var row = list[i];
                        if (row == null) continue;
                        rows.Add($"L{row.Level} {row.BonusBuffType}={row.EffectValue:0.###} msg='{row.specialEffectsMessageID}'");
                    }
                }

                var cached = new List<string>();
                foreach (var bt in allTypes)
                {
                    float v = bm.GetSphereBonusBuffValueCache(bt);
                    if (v > 0f) cached.Add($"{bt}={v:0.###}");
                }

                DebugLogger.LogState($"[Probe:Gauge] level {level}: rows=[{string.Join("; ", rows)}], "
                    + $"cache=[{string.Join(", ", cached)}].");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:Gauge] bonus row read error: {ex.Message}");
            }
        }

        #endregion

        #region Hook Callbacks

        /// <summary>A1: what the creation info panel holds and shows for the highlighted row.</summary>
        private static void CreationInfoPresenter_Set_Postfix(
            UIItemCreationInformationPresenter __instance, UIItemCreationInformationData data)
        {
            if (!Main.DebugMode || __instance == null || data == null) return;
            try
            {
                int itemId = data.itemID;
                int owned = itemId > 0 ? (ItemManager.Instance?.GetItemCount(itemId) ?? -1) : -1;
                int rows = data.dataList?.Count ?? 0;

                DebugLogger.LogState($"[Probe:ICPanel] category='{data.categoryName}' rows={rows} "
                    + $"isItem={data.isItem} itemID={itemId} ownedByItemManager={owned} | "
                    + $"itemName={Describe(__instance.itemName)} itemCount={Describe(__instance.itemCount)} "
                    + $"itemCountLabel={Describe(__instance.itemCountLabel)} "
                    + $"itemDataParent.active={IsActive(__instance.itemDataParent)} "
                    + $"itemParent.alpha={Alpha(__instance.itemParent)} "
                    + $"creationParent.alpha={Alpha(__instance.creationParent)}.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:ICPanel] error: {ex.Message}");
            }
        }

        /// <summary>A2: list overload of the IC skill panel (the one the mod already hooks).</summary>
        private static void SkillInfoPresenter_SetList_Postfix(
            UISpecialSkillInformationPresenter __instance, string skillName, int level)
        {
            LogSkillPanel("list", __instance, skillName, level);
        }

        /// <summary>A2: two-condition overload of the IC skill panel (never hooked before).</summary>
        private static void SkillInfoPresenter_SetTwoConditions_Postfix(
            UISpecialSkillInformationPresenter __instance, string skillName,
            bool isClearCondition1, bool isClearCondition2, int level)
        {
            LogSkillPanel($"two-condition (met1={isClearCondition1}, met2={isClearCondition2})",
                __instance, skillName, level);
        }

        private static void LogSkillPanel(string overload, UISpecialSkillInformationPresenter presenter,
            string skillName, int level)
        {
            if (!Main.DebugMode || presenter == null) return;
            try
            {
                var lp = presenter.levelPresenter;
                var learning = presenter.superSpecialSkillLearningPresenter;

                DebugLogger.LogState($"[Probe:ICLevel] overload={overload} skill='{skillName}' levelArg={level} | "
                    + $"levelPresenter.active={IsActive(lp?.gameObject)} "
                    + $"levelValue={Describe(lp?.levelValue)} levelLabel={Describe(lp?.levelLabel)} "
                    + $"normalText={Describe(lp?.normalText)} maxLevelValue={Describe(lp?.maxLevelValue)} | "
                    + $"requirementsPanel.active={IsActive(learning?.gameObject)} "
                    + $"conditionLabel={Describe(presenter.conditionLabel)}.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:ICLevel] error: {ex.Message}");
            }
        }

        /// <summary>A6: the reward record behind a field auto-kill cut-in.</summary>
        private static void AddCutinResult_Prefix(BattleResultInfo battleResultInfo)
        {
            if (!Main.DebugMode || battleResultInfo == null) return;
            try
            {
                var perCharacter = new List<string>();
                var list = battleResultInfo.CharacterDataList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        if (c == null) continue;
                        perCharacter.Add($"{c.playerID}: exp+{c.increaseExp} preSP={c.preSkillPoint} "
                            + $"BP {c.preCombatSkillPoint}->{c.afterCombatSkillPoint} (+{c.increaseCombatSkillPoint})");
                    }
                }

                DebugLogger.LogState($"[Probe:Cutin] exp={battleResultInfo.Exp} money={battleResultInfo.Money} "
                    + $"skillPoint={battleResultInfo.SkillPoint} (base {battleResultInfo.skillPointBase}) "
                    + $"battlePoint={battleResultInfo.CombatSkillPoint} (base {battleResultInfo.combatSkillPointBase}) "
                    + $"characters=[{string.Join("; ", perCharacter)}]. "
                    + "Watch for [Probe:SP] lines within a second of this one.");

                // Poll SP at once so a grant made just before the cut-in is attributed to it.
                _nextSpPollTime = 0f;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:Cutin] error: {ex.Message}");
            }
        }

        /// <summary>C: stock before the pickup popup is built.</summary>
        private static void ShowItemInformation_Prefix(int itemID)
        {
            if (!Main.DebugMode) return;
            _pickupStockBefore = ReadStock(itemID);
        }

        /// <summary>
        /// C: the count the game passes for the popup next to the real stock. If
        /// "count" equals the stock while the stock barely moved, the spoken "x20" is
        /// the stock and not the amount gained.
        /// </summary>
        private static void ShowItemInformation_Postfix(int itemID, int count, FactorID factorID)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"[Probe:Pickup] itemID={itemID} countArg={count} factor={factorID} "
                + $"stockAtPrefix={_pickupStockBefore} stockAtPostfix={ReadStock(itemID)}. "
                + "Compare countArg with the 'FieldInfoStack(item)' count that follows.");
        }

        /// <summary>A4: independent confirmation that the game started a gauge break.</summary>
        private static void StartBonusGaugeBreakDirecting_Postfix(BattleManager __instance)
        {
            if (!Main.DebugMode || __instance == null) return;
            try
            {
                DebugLogger.LogState($"[Probe:Gauge] StartBonusGaugeBreakDirecting fired: "
                    + $"level={__instance.sphereBonusBuffLevel}, "
                    + $"ratio={__instance.GetBattleSphereBonusCurrentLevelRatio():F2}. "
                    + "A 'BonusGauge.Break:' line should sit next to this one.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[Probe:Gauge] break directing read error: {ex.Message}");
            }
        }

        /// <summary>B2: a skill book is about to be applied.</summary>
        private static void ItemSkillLevelup_OnProcess_Prefix(ItemSkillLevelup __instance)
        {
            if (!Main.DebugMode || __instance == null) return;
            DebugLogger.LogState($"[Probe:SkillBook] use begins: {DescribeSkillBook(__instance)}.");
        }

        /// <summary>B2: a skill book was applied; arms the timed visible-text dumps.</summary>
        private static void ItemSkillLevelup_OnProcess_Postfix(ItemSkillLevelup __instance)
        {
            if (!Main.DebugMode || __instance == null) return;
            DebugLogger.LogState($"[Probe:SkillBook] use done: {DescribeSkillBook(__instance)}.");
            _skillBookUseTime = Time.unscaledTime;
            _skillBookDumpsDone = 0;
        }

        #endregion

        #region Helpers

        /// <summary>Skill of the book plus every party member's current level in it.</summary>
        private static string DescribeSkillBook(ItemSkillLevelup effect)
        {
            try
            {
                var skillId = effect.skillID;
                var levels = new List<string>();
                var members = PartyManager.Instance?.GetPartyMembers();
                var user = ParameterManager.Instance?.UserParameter;
                if (members != null && user != null)
                {
                    foreach (var id in members)
                    {
                        if (id == PlayerID.INVALID || id == PlayerID.MAX) continue;
                        var cp = user.GetCharacterParameter(id);
                        if (cp != null) levels.Add($"{id}={cp.GetSkillLevel(skillId)}");
                    }
                }
                return $"skill={skillId} maxLevel={effect.maxSkllLevel} levels=[{string.Join(", ", levels)}]";
            }
            catch (Exception ex)
            {
                return $"(read error: {ex.Message})";
            }
        }

        private static int ReadStock(int itemID)
        {
            try { return ItemManager.Instance?.GetItemCount(itemID) ?? -1; }
            catch { return -1; }
        }

        /// <summary>Text of a label plus whether it is really on screen.</summary>
        private static string Describe(GameText text)
        {
            if (text == null) return "(null)";
            return $"'{text.text}' (active={IsActive(text.gameObject)})";
        }

        private static string IsActive(GameObject go)
        {
            return go == null ? "(null)" : go.activeInHierarchy.ToString();
        }

        private static string Alpha(CanvasGroup group)
        {
            return group == null ? "(null)" : group.alpha.ToString("0.##");
        }

        #endregion
    }
}
