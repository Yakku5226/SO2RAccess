using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        // Formation sub-screen (formationSelector on UICampWindow)
        // UICampFormationSelector extends UIHelpListSelectorBase → UIListSelectorBase.
        // Info: UICampFormationInformationPresenter.Set hook fires on every formation change.
        // Announces formation name, effect description, and position.
        private static UICampFormationSelector _formationSelector = null;
        private static readonly SubScreenState _formationState = new SubScreenState();

        // Skills sub-screen — field/IC skills (skillSelector on UICampWindow)
        // UICampSkillSelector extends UICharacterTabListSelectorBase → UIHelpListSelectorBase
        //   → UIListSelectorBase. Has states: Skill, SpecialSkill, Learning.
        // Info: UISkillInformationPresenter.Set hook fires on every skill navigation.
        // Announces skill name, description, level, and position.
        private static UICampSkillSelector _skillSelector = null;
        private static readonly SubScreenState _skillState = new SubScreenState();

        // Deferred-flush state for skill announcements. The skill-info presenter fires
        // TWICE per navigation (~6ms apart): the first carries stale data (and, on an
        // L1/R1 switch, would carry the name) while the second carries fresh data but
        // no name and interrupts the first. So instead of announcing inside the
        // presenter, we cache the latest text and flush it once the burst settles
        // (SkillFlushDelay), prepending the character name only when the character
        // actually changed. This also collapses the routine double-announce.
        private const float SkillFlushDelay = 0.05f;
        private static string _skillPendingText = null;
        private static PlayerID _skillPendingPlayer;
        private static float _skillPendingTime;
        private static PlayerID _skillLastFlushedPlayer;
        private static bool _skillFlushedOnce = false;

        /// <summary>
        /// Polls the UICampFormationSelector for active state changes.
        /// Announces "Formation." when the screen opens.
        /// Formation detail announcements (name, effect, position) are handled by the
        /// UICampFormationInformationPresenter.Set hook.
        /// </summary>
        private void UpdateFormationSelector()
        {
            if (_formationSelector == null) return;

            if (_lastRootMenuItemName != "Formation") return;

            try
            {
                bool isActive = _formationSelector.gameObject.activeInHierarchy;

                _formationState.CheckEntry(
                    isActive,
                    () => ScreenReader.Say(Loc.Get("camp_formation_screen")),
                    "CampFormation");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateFormationSelector: {ex.Message}");
                _formationSelector = null;
                _formationState.Reset();
            }
        }

        /// <summary>
        /// Polls the UICampSkillSelector for active state changes.
        /// Announces "Skills." when the screen opens.
        /// Skill detail announcements (name, description, level, position) are handled by the
        /// UISkillInformationPresenter.Set hook.
        /// </summary>
        private void UpdateSkillSelector()
        {
            if (_skillSelector == null) return;

            if (_lastRootMenuItemName != "Skill") return;

            try
            {
                bool isActive = _skillSelector.gameObject.activeInHierarchy;

                _skillState.CheckEntry(
                    isActive,
                    () => { _skillPendingText = null; _skillFlushedOnce = false; ScreenReader.Say(Loc.Get("camp_skill_screen")); },
                    "CampSkill");

                if (!isActive)
                {
                    _skillPendingText = null;
                    return;
                }

                // Flush a pending skill announcement once the presenter's double-fire
                // burst has settled, so the named first fire isn't interrupted away.
                if (_skillPendingText != null
                    && UnityEngine.Time.time - _skillPendingTime >= SkillFlushDelay)
                {
                    FlushPendingSkill();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateSkillSelector: {ex.Message}");
                _skillSelector = null;
                _skillState.Reset();
                _skillPendingText = null;
            }
        }

        /// <summary>
        /// Postfix for UICampFormationInformationPresenter.Set(...).
        /// Fires whenever the formation information panel updates — on each navigation
        /// in the formation list. Announces formation name, effect description, sphere count,
        /// bonus count, individual bonus descriptions, and position.
        /// </summary>
        private static void FormationInfoPresenter_Set_Postfix(
            string formationName, string effectDescription, int currentSphereValue,
            int enableCount, Il2CppSystem.Collections.Generic.List<UIBonusBuffDescriptionData> bonusDescriptionList)
        {
            if (_formationSelector == null) return;
            if (_lastRootMenuItemName != "Formation") return;

            try
            {
                DebugLogger.LogGameValue("CampFormation.info",
                    $"name='{formationName}' effect='{effectDescription}' spheres={currentSphereValue} enabled={enableCount}");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(formationName))
                    AppendSentence(sb, formationName);
                if (!string.IsNullOrEmpty(effectDescription))
                    AppendSentence(sb, effectDescription);

                // Sphere count and active bonus count.
                AppendSentence(sb, Loc.Get("camp_formation_spheres",
                    currentSphereValue, enableCount));

                // Individual bonus descriptions.
                if (bonusDescriptionList != null)
                {
                    for (int i = 0; i < bonusDescriptionList.Count; i++)
                    {
                        var bonus = bonusDescriptionList[i];
                        if (bonus == null) continue;
                        string desc = bonus.description ?? "";
                        if (string.IsNullOrEmpty(desc)) continue;

                        if (bonus.enable)
                            AppendSentence(sb, Loc.Get("camp_formation_bonus_enabled", desc));
                        else
                            AppendSentence(sb, Loc.Get("camp_formation_bonus_disabled", desc));
                    }
                }

                // Read position from the selector (cast to UIListSelectorBase).
                var baseSel = _formationSelector.TryCast<UIListSelectorBase>();
                if (baseSel != null)
                {
                    int idx = baseSel.currentIndex;
                    var list = baseSel.currentDataList;
                    int total = list?.Count ?? 0;
                    if (total > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_formation_position", idx + 1, total));
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.FormationInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UISkillInformationPresenter.Set(UISkillInformationData).
        /// Fires whenever the skill information panel updates — on each navigation
        /// in the skills list. Announces skill name, level, SP cost, description, and position.
        /// </summary>
        private static void SkillInfoPresenter_Set_Postfix(UISkillInformationData data)
        {
            if (_skillSelector == null) return;
            if (_lastRootMenuItemName != "Skill") return;
            if (data == null) return;

            try
            {
                string name = data.skillName ?? "";
                string description = data.skillDescription ?? "";
                int level = data.skillLevel;

                var tabBase = _skillSelector.TryCast<UICharacterTabListSelectorBase>();

                // Look up list item data for SP cost, max level, and current balance.
                int spCost = 0;
                bool isMax = false;
                string balance = "";
                var baseSel = _skillSelector.TryCast<UIListSelectorBase>();
                int idx = baseSel?.currentIndex ?? -1;
                int total = 0;

                if (baseSel != null)
                {
                    var dataList = baseSel.currentDataList;
                    total = dataList?.Count ?? 0;
                }

                // Pick the data list that matches what's on screen. The specialty
                // filter (Square, then Triangle) narrows the visible list to
                // narrowDownItemDataList; currentIndex is relative to that narrowed
                // list. Indexing the full itemDataList while narrowed read the wrong
                // skill's SP cost (e.g. Piety reported Biology's 235 SP instead of its
                // own 24, since both sit at the same list position). narrowDownSpecialSkillID
                // is INVALID when no filter is active.
                var itemList = _skillSelector.itemDataList;
                if (_skillSelector.narrowDownSpecialSkillID != SpecialSkillID.INVALID)
                {
                    var narrowed = _skillSelector.narrowDownItemDataList;
                    if (narrowed != null && narrowed.Count > 0)
                        itemList = narrowed;
                }
                int itemCount = itemList?.Count ?? 0;
                if (itemCount > 0 && idx >= 0 && idx < itemCount)
                {
                    var itemData = itemList[idx];

                    // The game's itemDataList is stale for specialties after leveling —
                    // consumeSP and isLevelMax don't refresh. Compute fresh values.
                    if (itemData.specialSkillID != SpecialSkillID.INVALID)
                    {
                        // Specialty: call game API for fresh cost data.
                        if (tabBase != null)
                        {
                            var pm = ParameterManager.Instance;
                            var charaParam = pm?.UserParameter?.GetCharacterParameter(tabBase.currentPlayerID);
                            if (charaParam != null)
                            {
                                var levelUpList = UICommon.CalcNeedSpecialSkillForLevelUp(
                                    charaParam, itemData.specialSkillID);
                                if (levelUpList != null && levelUpList.Count > 0)
                                {
                                    spCost = 0;
                                    for (int i = 0; i < levelUpList.Count; i++)
                                        spCost += levelUpList[i].consumeSP;
                                    isMax = false;
                                }
                                else
                                {
                                    // Empty list = already at max level.
                                    spCost = 0;
                                    isMax = true;
                                }
                            }
                        }
                    }
                    else if (itemData.skillID != SkillID.INVALID)
                    {
                        // Knowledge skill: itemDataList is reliable for cost,
                        // but verify max level against game parameter data.
                        spCost = itemData.consumeSP;
                        var skillParam = ParameterManager.Instance?.GetSkillParameter(itemData.skillID);
                        if (skillParam != null)
                        {
                            var spList = skillParam.levelupSp;
                            isMax = (spList == null || level >= spList.Count);
                        }
                        else
                        {
                            isMax = itemData.isLevelMax;
                        }
                    }
                    else
                    {
                        // Fallback: use stale data.
                        spCost = itemData.consumeSP;
                        isMax = itemData.isLevelMax;
                    }
                }

                // ReadSkillPointBalance is defined in BattleSkill partial class.
                balance = ReadSkillPointBalance(_skillSelector);

                DebugLogger.LogGameValue("CampSkill.info",
                    $"name='{name}' lv={level} spCost={spCost} isMax={isMax} balance='{balance}' desc='{description}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))
                    AppendSentence(sb, name);
                if (level > 0)
                {
                    sb.Append(Loc.Get("camp_skill_level", level));
                    if (isMax) sb.Append(Loc.Get("camp_skill_max_level"));
                    sb.Append(". ");
                }
                if (spCost > 0 && !isMax && !string.IsNullOrEmpty(balance))
                    sb.Append(Loc.Get("camp_skill_sp_cost", balance, spCost)).Append(". ");
                if (!string.IsNullOrEmpty(description))
                    AppendSentence(sb, description);

                if (total > 0 && idx >= 0)
                    sb.Append(Loc.Get("camp_skill_position", idx + 1, total));

                string result = sb.ToString().Trim();
                if (string.IsNullOrEmpty(result)) return;

                // Cache for deferred flush (see SkillFlushDelay). Capture the character
                // so the flush can prepend the name only on a genuine L1/R1 switch.
                _skillPendingText = result;
                if (tabBase != null) _skillPendingPlayer = tabBase.currentPlayerID;
                _skillPendingTime = UnityEngine.Time.time;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.SkillInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks the cached skill announcement once the presenter's double-fire burst
        /// has settled. Prepends the character's first name when the character changed
        /// since the last flush (an L1/R1 switch), matching the Item Creation menu.
        /// </summary>
        private static void FlushPendingSkill()
        {
            string text = _skillPendingText;
            _skillPendingText = null;
            if (string.IsNullOrEmpty(text)) return;

            bool charChanged = _skillFlushedOnce && _skillPendingPlayer != _skillLastFlushedPlayer;
            _skillLastFlushedPlayer = _skillPendingPlayer;
            _skillFlushedOnce = true;

            if (charChanged)
            {
                string charName = null;
                try { charName = ParameterManager.Instance?.GetCharacterFirstName(_skillPendingPlayer); }
                catch { /* ignore */ }
                if (!string.IsNullOrEmpty(charName))
                    text = charName + ". " + text;
            }

            ScreenReader.Say(text);
        }
    }
}
