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
        private static bool _formationWasActive = false;
        private static bool _formationSuppressHeading = false;

        // Skills sub-screen — field/IC skills (skillSelector on UICampWindow)
        // UICampSkillSelector extends UICharacterTabListSelectorBase → UIHelpListSelectorBase
        //   → UIListSelectorBase. Has states: Skill, SpecialSkill, Learning.
        // Info: UISkillInformationPresenter.Set hook fires on every skill navigation.
        // Announces skill name, description, level, and position.
        private static UICampSkillSelector _skillSelector = null;
        private static bool _skillWasActive = false;
        private static bool _skillSuppressHeading = false;

        /// <summary>
        /// Polls the UICampFormationSelector for active state changes.
        /// Announces "Formation." when the screen opens.
        /// Formation detail announcements (name, effect, position) are handled by the
        /// UICampFormationInformationPresenter.Set hook.
        /// </summary>
        private void UpdateFormationSelector()
        {
            if (_formationSelector == null) return;

            // Only poll when the root menu highlights "Formation".
            if (_lastRootMenuItemName != "Formation")
            {
                // Don't reset _formationWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "Formation" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _formationSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_formationWasActive)
                    {
                        _formationWasActive = false;
                        DebugLogger.LogState("CampFormation: selector hidden.");
                    }
                    return;
                }

                if (!_formationWasActive)
                {
                    _formationWasActive = true;

                    if (!_formationSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_formation_screen"));
                        DebugLogger.LogState("CampFormation: selector visible.");
                    }
                    else
                    {
                        _formationSuppressHeading = false;
                        DebugLogger.LogState("CampFormation: stale open — heading suppressed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateFormationSelector: {ex.Message}");
                _formationSelector = null;
                _formationWasActive = false;
                _formationSuppressHeading = false;
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

            // Only poll when the root menu highlights "Skill".
            if (_lastRootMenuItemName != "Skill")
            {
                // Don't reset _skillWasActive here.
                // Resetting causes stale announcements when root menu cursor
                // returns to "Skill" during normal navigation.
                return;
            }

            try
            {
                bool isActive = _skillSelector.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_skillWasActive)
                    {
                        _skillWasActive = false;
                        DebugLogger.LogState("CampSkill: selector hidden.");
                    }
                    return;
                }

                if (!_skillWasActive)
                {
                    _skillWasActive = true;

                    if (!_skillSuppressHeading)
                    {
                        ScreenReader.Say(Loc.Get("camp_skill_screen"));
                        DebugLogger.LogState("CampSkill: selector visible.");
                    }
                    else
                    {
                        _skillSuppressHeading = false;
                        DebugLogger.LogState("CampSkill: stale open — heading suppressed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateSkillSelector: {ex.Message}");
                _skillSelector = null;
                _skillWasActive = false;
                _skillSuppressHeading = false;
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
            if (!_formationWasActive) return;
            if (_formationSelector == null) return;
            if (_lastRootMenuItemName != "Formation") return;

            try
            {
                DebugLogger.LogGameValue("CampFormation.info",
                    $"name='{formationName}' effect='{effectDescription}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(formationName))
                    sb.Append(formationName).Append(". ");
                if (!string.IsNullOrEmpty(effectDescription))
                    sb.Append(effectDescription).Append(". ");

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
        /// in the skills list. Announces skill name, level, description, and position.
        /// </summary>
        private static void SkillInfoPresenter_Set_Postfix(UISkillInformationData data)
        {
            if (!_skillWasActive) return;
            if (_skillSelector == null) return;
            if (_lastRootMenuItemName != "Skill") return;
            if (data == null) return;

            try
            {
                string name = data.skillName ?? "";
                string description = data.skillDescription ?? "";
                int level = data.skillLevel;

                DebugLogger.LogGameValue("CampSkill.info",
                    $"name='{name}' lv={level} desc='{description}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))
                    sb.Append(name).Append(". ");
                if (level > 0)
                    sb.Append(Loc.Get("camp_skill_level", level)).Append(". ");
                if (!string.IsNullOrEmpty(description))
                    sb.Append(description).Append(". ");

                // Read position from the selector (cast to UIListSelectorBase).
                var baseSel = _skillSelector.TryCast<UIListSelectorBase>();
                if (baseSel != null)
                {
                    int idx = baseSel.currentIndex;
                    var list = baseSel.currentDataList;
                    int total = list?.Count ?? 0;
                    if (total > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_skill_position", idx + 1, total));
                }

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
