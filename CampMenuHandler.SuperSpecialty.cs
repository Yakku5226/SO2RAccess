using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Super Specialty sub-screen accessibility.
    ///
    /// Super Specialty skills (Master Chef, Orchestra, Bunny Call, etc.) appear in
    /// two different menu systems:
    ///
    ///   Context A — Tab 2 of the IC skill selection list (UICampSelectSpecialSkillSelector).
    ///     Accessed via: Camp → ItemCreation → R1/R1 to "Super Special Skills" tab,
    ///     or Field D-pad Down → IC shortcut → R1/R1.
    ///     Navigation is list-based (UIListSelectorBase). Info panel text is read
    ///     from the existing UISpecialSkillInformationPresenter's GameText fields.
    ///     Handled by TryPollSuperSpecialtyTab() called from UpdateICSkillSelection.
    ///
    ///   Context B — Skill Learning selector (UICampSkillLearningSelector).
    ///     Accessed via: Camp → Enhance → Skill → R2.
    ///     Completely separate menu system for improving/learning skills.
    ///     List-based (UIListSelectorBase), accessed via _skillSelector.learningSelector.
    ///     Has its own UISkillLearningInformationPresenter with skillName, skillDescription,
    ///     and superSpecialSkillLearningPresenter for conditions.
    ///     Data items: UISkillLearningListItemData (skillName, level).
    /// </summary>
    public partial class CampMenuHandler
    {
        #region Skill Learning Fields (Context B)

        /// <summary>Cached skill learning selector from UICampSkillSelector.learningSelector.</summary>
        private static UICampSkillLearningSelector _slSelector;
        private static readonly SubScreenState _slState = new SubScreenState();

        #endregion

        #region Skill Learning Caching

        /// <summary>
        /// Caches the skill learning selector. Called from CampWindow_Open_Postfix
        /// via CacheItemCreationSelectors (reusing the call site).
        /// </summary>
        private static void CacheSuperSpecialtySelector()
        {
            _slState.Reset();
            _slSelector = null;

            try
            {
                if (_skillSelector != null)
                {
                    _slSelector = _skillSelector.learningSelector;
                    if (_slSelector != null)
                    {
                        DebugLogger.LogState("CampSL: learning selector cached.");
                        if (_slSelector.gameObject.activeInHierarchy)
                        {
                            var listBase = _slSelector.TryCast<UIListSelectorBase>();
                            if (listBase != null)
                                _slState.SeedOnOpen(listBase.currentIndex);
                            else
                                _slState.SuppressNextHeading();
                            DebugLogger.LogState("CampSL: stale on open — heading suppressed.");
                        }
                    }
                    else
                    {
                        DebugLogger.LogState("CampSL: learningSelector is null.");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSL: cache error: {ex.Message}");
            }
        }

        #endregion

        #region Context A — Tab 2 Polling (IC skill selection list)

        /// <summary>
        /// Polls the existing UISpecialSkillInformationPresenter's GameText fields
        /// when on tab 2 of the IC skill selection list. The game's native code uses
        /// a second Set overload to update the SAME info presenter for super specialties.
        /// We read skillName.text, skillDescription.text, and the
        /// superSpecialSkillLearningPresenter's condition GameText fields.
        /// Called from UpdateICSkillSelection when _icLastTab == 2.
        /// </summary>
        private void TryPollSuperSpecialtyTab()
        {
            try
            {
                var listBase = _icSkillSelector?.TryCast<UIListSelectorBase>();
                if (listBase == null) return;

                int idx = listBase.currentIndex;
                if (idx == _icSkillState.LastIndex) return;
                _icSkillState.LastIndex = idx;

                int count = listBase.currentDataList?.Count ?? 0;
                if (count <= 0) return;

                // Read from the SAME informationPresenter that tabs 0/1 use.
                var infoPresenter = _icSkillSelector.informationPresenter;
                if (infoPresenter == null)
                {
                    ScreenReader.Say(Loc.Get("ss_position", idx + 1, count));
                    DebugLogger.LogState($"CampSS tab2: no info presenter, idx={idx}/{count}");
                    return;
                }

                var fragments = new System.Collections.Generic.List<string>();
                var sb = new StringBuilder();

                string name = infoPresenter.skillName?.text;
                fragments.Add(name);
                fragments.Add(infoPresenter.skillDescription?.text);

                // Super specialty requirements. The shared sub-presenter
                // (superSpecialSkillLearningPresenter) lags one navigation behind for the
                // skill NAMES (its Set updates only those, a frame late), so it announced a
                // neighbor's requirement for every entry. Instead, map the fresh displayed
                // name to a SuperSpecialSkillID and build the game's own requirement data
                // object from that id — its condition skill names are computed on demand and
                // cannot lag. Pair them with the static count/level descriptions from the
                // presenter (those never change, so they don't lag). Fall back to the
                // presenter only if the lookup/construction fails.
                try
                {
                    var learn = infoPresenter.superSpecialSkillLearningPresenter;
                    if (!AppendConditionsFromGameData(sb, name, learn, "CampSS tab2"))
                    {
                        // Unknown skill — fall back to the (lagging) presenter read.
                        AppendLearningConditions(sb, learn);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampSS tab2: requirement error: {ex.Message}");
                    AppendLearningConditions(sb, infoPresenter.superSpecialSkillLearningPresenter);
                }
                fragments.Add(sb.ToString());

                string message = TextUtil.JoinSentences(fragments)
                    + ". " + Loc.Get("ss_position", idx + 1, count);
                ScreenReader.Say(message);
                DebugLogger.LogState($"CampSS tab2: {name}, idx={idx}/{count}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSS tab2: poll error: {ex.Message}");
            }
        }

        #endregion

        #region Context B — Skill Learning Update (Enhance → Skill → R2)

        /// <summary>
        /// Polls the skill learning selector for navigation changes.
        /// Called from Update() unconditionally (Enhance path, not IC).
        /// </summary>
        private void UpdateSkillLearning()
        {
            if (_slSelector == null) return;

            bool isActive;
            try { isActive = _slSelector.gameObject.activeInHierarchy; }
            catch { return; }

            bool shouldPoll = _slState.CheckEntry(
                isActive,
                () => ScreenReader.Say(Loc.Get("ss_screen")),
                "CampSL",
                onHidden: () => { });

            if (!shouldPoll) return;

            try
            {
                var listBase = _slSelector.TryCast<UIListSelectorBase>();
                if (listBase == null) return;

                int idx = listBase.currentIndex;
                if (idx == _slState.LastIndex) return;
                _slState.LastIndex = idx;

                int count = listBase.currentDataList?.Count ?? 0;
                if (count <= 0) return;

                var fragments = new System.Collections.Generic.List<string>();
                string skillName = null;

                // Name + level from the row data (UISkillLearningListItemData).
                try
                {
                    var dataItem = listBase.currentDataList[idx];
                    var skillData = dataItem?.TryCast<UISkillLearningListItemData>();
                    if (skillData != null)
                    {
                        skillName = skillData.skillName;
                        int level = skillData.level;
                        string levelText = level > 0
                            ? Loc.Get("ic_skill_level", level)
                            : Loc.Get("ss_not_learned");
                        fragments.Add(string.IsNullOrEmpty(skillName)
                            ? levelText : $"{skillName}, {levelText}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampSL: data read error: {ex.Message}");
                }

                // Description + learning conditions from the info presenter.
                try
                {
                    var infoPresenter = _slSelector.informationPresenter;
                    if (infoPresenter != null)
                    {
                        fragments.Add(infoPresenter.skillDescription?.text);

                        var sb = new StringBuilder();
                        AppendSkillLearningConditions(sb, infoPresenter, skillName);
                        fragments.Add(sb.ToString());
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampSL: info read error: {ex.Message}");
                }

                string message = TextUtil.JoinSentences(fragments)
                    + ". " + Loc.Get("ss_position", idx + 1, count);
                if (!string.IsNullOrEmpty(message))
                {
                    ScreenReader.Say(message);
                    DebugLogger.LogState($"CampSL: {message}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSL: poll error: {ex.Message}");
            }
        }

        #endregion

        #region Shared Helpers

        /// <summary>
        /// Appends condition text from a UISuperSpecialSkillLearningPresenter.
        /// Used by context B (skill learning). Context A reads from row data instead
        /// (the presenter lags there); both funnel into the string-based overload.
        /// </summary>
        private static void AppendLearningConditions(StringBuilder sb,
            UISuperSpecialSkillLearningPresenter learningPresenter)
        {
            if (learningPresenter == null) return;

            try
            {
                AppendLearningConditions(sb,
                    learningPresenter.condition1Skill?.text,
                    learningPresenter.condition1Description?.text,
                    learningPresenter.condition2Skill?.text,
                    learningPresenter.condition2Description?.text);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSS: conditions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends "Requires: ..." from two skill/description condition pairs.
        /// Each pair is joined as "Skill Description" (e.g. "Music 2 people are at Lv 4"),
        /// followed by "met" / "not met" when the game's achievement flag is known.
        /// Empty conditions are skipped; nothing is appended when both are empty.
        /// </summary>
        private static void AppendLearningConditions(StringBuilder sb,
            string cond1Skill, string cond1Desc, string cond2Skill, string cond2Desc,
            bool? met1 = null, bool? met2 = null)
        {
            var conditions = new System.Collections.Generic.List<string>();
            AddCondition(conditions, cond1Skill, cond1Desc, met1);
            AddCondition(conditions, cond2Skill, cond2Desc, met2);

            if (conditions.Count > 0)
                sb.Append(Loc.Get("ss_requires", string.Join(", ", conditions)));
        }

        /// <summary>Formats one learning condition ("Skill description, met") into the list.</summary>
        private static void AddCondition(System.Collections.Generic.List<string> conditions,
            string skill, string description, bool? met)
        {
            if (string.IsNullOrEmpty(skill)) return;
            string text = string.IsNullOrEmpty(description) ? skill : $"{skill} {description}";
            if (met.HasValue)
                text = Loc.Get(met.Value ? "ss_condition_met" : "ss_condition_not_met", text);
            conditions.Add(text);
        }

        /// <summary>
        /// Appends the learning conditions for the super specialty called
        /// <paramref name="displayedName"/>, built from the game's own requirement data
        /// (condition skill names + achievement flags computed on demand from the id, so
        /// they can never lag or show prefab placeholders). The count/level wording comes
        /// from the presenter's static description texts, which never change per entry.
        /// Returns false when the name is unknown and nothing was appended.
        /// </summary>
        private static bool AppendConditionsFromGameData(StringBuilder sb, string displayedName,
            UISuperSpecialSkillLearningPresenter learn, string logTag)
        {
            var ssid = ResolveSuperSpecialSkillIdByName(displayedName);
            if (ssid == SuperSpecialSkillID.INVALID)
            {
                DebugLogger.LogState($"{logTag}: '{displayedName}' is not a known super specialty name.");
                return false;
            }

            var data = new UISkillLearningSuperSpecialSkillInformationData(ssid);
            string cond1 = data.condition1SkillName;
            string cond2 = data.condition2SkillName;
            if (string.IsNullOrEmpty(cond1) && string.IsNullOrEmpty(cond2))
            {
                DebugLogger.LogState($"{logTag}: {ssid} has no condition skill names in game data.");
                return false;
            }

            AppendLearningConditions(sb,
                cond1, learn?.condition1Description?.text,
                cond2, learn?.condition2Description?.text,
                data.isAchievementCondition1, data.isAchievementCondition2);
            DebugLogger.LogState($"{logTag}: {ssid} conditions: '{cond1}' met={data.isAchievementCondition1}, "
                + $"'{cond2}' met={data.isAchievementCondition2}");
            return true;
        }

        /// <summary>
        /// Context B (skill learning screen): appends what the game's info panel shows for
        /// the current entry. Unlearned super specialties show the learning conditions; those
        /// are built from game data because the condition sub-presenter keeps its prefab
        /// placeholder ("取得スキル" + the fixed template wording) and only its static
        /// count/level wording is trustworthy. Learned ones hide that panel and show the
        /// growth info instead: the specialty whose party total drives the level, that
        /// total, and "+N until Lvl Up" (hidden at max level). Every panel text is logged
        /// (shown/hidden) so a debug log explains what was and wasn't spoken.
        /// </summary>
        private static void AppendSkillLearningConditions(StringBuilder sb,
            UISkillLearningInformationPresenter infoPresenter, string skillName)
        {
            var learn = infoPresenter.superSpecialSkillLearningPresenter;
            bool conditionsShown = learn != null && learn.gameObject.activeInHierarchy;

            LogSkillLearningPanel(infoPresenter, learn, conditionsShown);

            if (conditionsShown)
            {
                if (!AppendConditionsFromGameData(sb, skillName, learn, "CampSL"))
                    AppendLearningConditions(sb, learn);
                return;
            }

            AppendGrowthInfo(sb, infoPresenter);
        }

        /// <summary>
        /// Appends the growth info the game shows for a learned super specialty:
        /// "Grows with Replication, total level 30. +3 until Lvl Up." Each part is included
        /// only while its text is shown on screen; the until-level-up line is the game's own
        /// (already localized) wording.
        /// </summary>
        private static void AppendGrowthInfo(StringBuilder sb,
            UISkillLearningInformationPresenter infoPresenter)
        {
            string growthSkill = ShownText(infoPresenter.levelUpSkill);
            string totalLevel = ShownText(infoPresenter.totalLevel);
            string untilLevelUp = ShownText(infoPresenter.levelUpDescription);

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(growthSkill))
            {
                parts.Add(string.IsNullOrEmpty(totalLevel)
                    ? Loc.Get("ss_grows_with", growthSkill)
                    : Loc.Get("ss_grows_with_total", growthSkill, totalLevel));
            }
            parts.Add(untilLevelUp);

            sb.Append(TextUtil.JoinSentences(parts));
            DebugLogger.LogState($"CampSL: growth info: skill='{growthSkill}' total='{totalLevel}' untilLevelUp='{untilLevelUp}'");
        }

        /// <summary>The text of a GameText while it is visible on screen, else null.</summary>
        private static string ShownText(GameText t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return null;
            string s = TextUtil.StripTags(t.text);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>
        /// Debug-only dump of the skill learning info panel: which parts the game shows and
        /// what text they hold. Evidence for deciding what else this screen should announce.
        /// </summary>
        private static void LogSkillLearningPanel(UISkillLearningInformationPresenter info,
            UISuperSpecialSkillLearningPresenter learn, bool conditionsShown)
        {
            if (!Main.DebugMode) return;
            try
            {
                string Part(string label, GameText t) => t == null
                    ? $"{label}=<null>"
                    : $"{label}[{(t.gameObject.activeInHierarchy ? "shown" : "hidden")}, id='{t.messageId}']='{t.text}'";

                DebugLogger.LogState("CampSL panel: "
                    + $"conditions={(conditionsShown ? "shown" : "hidden")} | "
                    + Part("cond1Skill", learn?.condition1Skill) + " | "
                    + Part("cond1Desc", learn?.condition1Description) + " | "
                    + Part("cond2Skill", learn?.condition2Skill) + " | "
                    + Part("cond2Desc", learn?.condition2Description) + " | "
                    + Part("levelUpSkill", info.levelUpSkill) + " | "
                    + Part("levelUpDesc", info.levelUpDescription) + " | "
                    + Part("totalLevel", info.totalLevel) + " | "
                    + $"specialSkillParent={(info.specialSkillParent?.activeInHierarchy)} "
                    + $"growLayoutParent={(info.growLayoutParent?.activeInHierarchy)}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSL panel: dump error: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps a super specialty's displayed name to its SuperSpecialSkillID. The IC tab-2
        /// list rows are generic UICommonListItemData (name text only, no id) and the
        /// selector's cacheData id stays INVALID, so the displayed name is the only reliable
        /// per-row key. The map is built once from parameter data and matches the same name
        /// the list shows. Returns INVALID when the name is unknown.
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, SuperSpecialSkillID> _ssNameToId;

        private static SuperSpecialSkillID ResolveSuperSpecialSkillIdByName(string name)
        {
            string key = NormalizeSsName(name);
            if (string.IsNullOrEmpty(key)) return SuperSpecialSkillID.INVALID;

            EnsureSuperSpecialtyNameMap();
            if (_ssNameToId != null && _ssNameToId.TryGetValue(key, out var id))
                return id;
            return SuperSpecialSkillID.INVALID;
        }

        private static void EnsureSuperSpecialtyNameMap()
        {
            if (_ssNameToId != null) return;

            var map = new System.Collections.Generic.Dictionary<string, SuperSpecialSkillID>();
            try
            {
                var pm = ParameterManager.Instance;
                var tm = TextManager.Instance;
                if (pm != null && tm != null)
                {
                    for (int i = (int)SuperSpecialSkillID.INVALID + 1; i < (int)SuperSpecialSkillID.MAX; i++)
                    {
                        var id = (SuperSpecialSkillID)i;
                        try
                        {
                            var param = pm.GetSuperSpecialSkillParameter(id);
                            string nameId = param?.superSpecialSkillNameID;
                            if (string.IsNullOrEmpty(nameId)) continue;

                            string resolved = ResolveSkillText(tm, nameId);
                            string key = NormalizeSsName(resolved);
                            if (!string.IsNullOrEmpty(key))
                                map[key] = id;
                        }
                        catch { /* skip this id */ }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSS: name map build error: {ex.Message}");
            }

            _ssNameToId = map;
            DebugLogger.LogState($"CampSS: super specialty name map built, {_ssNameToId.Count} entries.");
        }

        /// <summary>Resolves a text key trying each MessageType the game uses.</summary>
        private static string ResolveSkillText(TextManager tm, string key)
        {
            TextManager.MessageType[] types =
            {
                TextManager.MessageType.Skill,
                TextManager.MessageType.System,
                TextManager.MessageType.Item,
            };
            foreach (var mt in types)
            {
                try
                {
                    string s = tm.GetMessage(key, mt);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch { /* try next */ }
            }
            return null;
        }

        private static string NormalizeSsName(string s)
        {
            s = TextUtil.StripTags(s);
            return s?.Trim();
        }

        #endregion

        #region Super Specialty Hook (kept for potential future use)

        /// <summary>
        /// Postfix hook for UISuperSpecialSkillInformationPresenter.Set.
        /// CallerCount(1) but the caller is native C++ — this hook may never fire.
        /// If it does fire, announce immediately as a bonus.
        /// </summary>
        private static void SuperSpecialSkillInfoPresenter_Set_Postfix(
            string skillName, string skillDescription, string learnSkill,
            Il2CppSystem.Collections.Generic.List<string> needSkillList)
        {
            if (string.IsNullOrEmpty(skillName)) return;

            var sb = new StringBuilder();
            sb.Append(skillName);

            if (!string.IsNullOrEmpty(learnSkill))
                sb.Append(", ").Append(learnSkill);

            if (!string.IsNullOrEmpty(skillDescription))
                sb.Append(". ").Append(skillDescription);

            if (needSkillList != null && needSkillList.Count > 0)
            {
                try
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < needSkillList.Count; i++)
                    {
                        string skill = needSkillList[i];
                        if (!string.IsNullOrEmpty(skill))
                            parts.Add(skill);
                    }
                    if (parts.Count > 0)
                        sb.Append(". ").Append(Loc.Get("ss_requires", string.Join(", ", parts)));
                }
                catch { /* ignore */ }
            }

            try
            {
                var listBase = _icSkillSelector?.TryCast<UIListSelectorBase>();
                if (listBase != null)
                {
                    int idx = listBase.currentIndex;
                    int count = listBase.currentDataList?.Count ?? 0;
                    if (count > 0)
                        sb.Append(". ").Append(Loc.Get("ss_position", idx + 1, count));
                }
            }
            catch { /* ignore */ }

            ScreenReader.Say(sb.ToString());
            DebugLogger.LogState($"CampSS hook: {skillName}, {learnSkill}");
        }

        #endregion
    }
}
