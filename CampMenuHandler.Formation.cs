using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

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
                    () => ScreenReader.Say(Loc.Get("camp_skill_screen")),
                    "CampSkill");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateSkillSelector: {ex.Message}");
                _skillSelector = null;
                _skillState.Reset();
            }
        }

        /// <summary>
        /// Postfix for UICampFormationInformationPresenter.Set(...).
        /// Fires whenever the formation information panel updates — on each navigation
        /// in the formation list. Announces formation name, effect description, and position.
        /// </summary>
        private static void FormationInfoPresenter_Set_Postfix(
            string formationName, string effectDescription)
        {
            if (_formationSelector == null) return;
            if (_lastRootMenuItemName != "Formation") return;

            try
            {
                DebugLogger.LogGameValue("CampFormation.info",
                    $"name='{formationName}' effect='{effectDescription}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(formationName))
                    AppendSentence(sb, formationName);
                if (!string.IsNullOrEmpty(effectDescription))
                    AppendSentence(sb, effectDescription);

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

                var itemList = _skillSelector.itemDataList;
                int itemCount = itemList?.Count ?? 0;
                if (itemCount > 0 && idx >= 0 && idx < itemCount)
                {
                    var itemData = itemList[idx];
                    spCost = itemData.consumeSP;
                    isMax  = itemData.isLevelMax;
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
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.SkillInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }
    }
}
