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

                var sb = new StringBuilder();

                string name = infoPresenter.skillName?.text;
                if (!string.IsNullOrEmpty(name))
                    sb.Append(name);

                string desc = infoPresenter.skillDescription?.text;
                if (!string.IsNullOrEmpty(desc))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(desc);
                }

                // Super specialty conditions from the sub-presenter.
                AppendLearningConditions(sb, infoPresenter.superSpecialSkillLearningPresenter);

                // Position.
                sb.Append(". ").Append(Loc.Get("ss_position", idx + 1, count));

                ScreenReader.Say(sb.ToString());
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

                var sb = new StringBuilder();

                // Try to read from currentDataList items (UISkillLearningListItemData).
                try
                {
                    var dataItem = listBase.currentDataList[idx];
                    var skillData = dataItem?.TryCast<UISkillLearningListItemData>();
                    if (skillData != null)
                    {
                        string skillName = skillData.skillName;
                        if (!string.IsNullOrEmpty(skillName))
                            sb.Append(skillName);

                        int level = skillData.level;
                        if (level > 0)
                            sb.Append(", ").Append(Loc.Get("ic_skill_level", level));
                        else
                            sb.Append(", ").Append(Loc.Get("ss_not_learned"));
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampSL: data read error: {ex.Message}");
                }

                // Read description and conditions from the info presenter.
                try
                {
                    var infoPresenter = _slSelector.informationPresenter;
                    if (infoPresenter != null)
                    {
                        string desc = infoPresenter.skillDescription?.text;
                        if (!string.IsNullOrEmpty(desc))
                        {
                            if (sb.Length > 0) sb.Append(". ");
                            sb.Append(desc);
                        }

                        AppendLearningConditions(sb,
                            infoPresenter.superSpecialSkillLearningPresenter);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampSL: info read error: {ex.Message}");
                }

                // Position.
                sb.Append(". ").Append(Loc.Get("ss_position", idx + 1, count));

                string message = sb.ToString();
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
        /// Shared between context A (IC tab 2) and context B (skill learning).
        /// </summary>
        private static void AppendLearningConditions(StringBuilder sb,
            UISuperSpecialSkillLearningPresenter learningPresenter)
        {
            if (learningPresenter == null) return;

            try
            {
                string cond1 = learningPresenter.condition1Skill?.text;
                string cond1Desc = learningPresenter.condition1Description?.text;
                string cond2 = learningPresenter.condition2Skill?.text;
                string cond2Desc = learningPresenter.condition2Description?.text;

                var conditions = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(cond1))
                {
                    string c = string.IsNullOrEmpty(cond1Desc)
                        ? cond1 : $"{cond1} {cond1Desc}";
                    conditions.Add(c);
                }
                if (!string.IsNullOrEmpty(cond2))
                {
                    string c = string.IsNullOrEmpty(cond2Desc)
                        ? cond2 : $"{cond2} {cond2Desc}";
                    conditions.Add(c);
                }
                if (conditions.Count > 0)
                    sb.Append(". ").Append(Loc.Get("ss_requires",
                        string.Join(", ", conditions)));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampSS: conditions error: {ex.Message}");
            }
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
