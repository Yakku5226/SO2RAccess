using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler: Harmony patch registration + IL2CPP class-constructor priming (ApplyPatches).
    public partial class CampMenuHandler
    {
        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the camp menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEquipSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEquipItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusParameterData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICharacterStatusItemInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISelectBattleSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampCombatSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampCombatSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEnhanceBonusData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSettingSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampBattleSkillSettingEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusLevelData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusParameterPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusLevelPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstPlayerParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemTabPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemListSelectorBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommonSelectTextPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICharacterTabItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFormationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSkillListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICharacterTabListSelectorBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommon).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommon.SpecialSkillLevelUpData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UserParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstSkillParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSelectCharacterSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPartyMemberPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPartyMemberSelectItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CampCharacterStatusParameterData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampCharacterStatusPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingEquipListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingCharacterListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampAssistSettingCharacterListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationSelectListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationCharacterListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIOperationListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITalentPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITalentData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIElementalGroupPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIElementalData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusAgePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusAgeValuePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UILayoutElementTextPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampStatusFavorabilityRatingItemListData).TypeHandle);

                // Quest sub-screen (separate window opened from camp)
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestDescriptionPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestRewardElementPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIQuestRewardElementData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemNamePresenter).TypeHandle);

                // Mission sub-screen (separate window opened from camp)
                RuntimeHelpers.RunClassConstructor(typeof(UIMissionWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIMissionListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIMissionListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIMissionInformationSelector).TypeHandle);

                // Database sub-screens
                RuntimeHelpers.RunClassConstructor(typeof(UICampTutorialListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampTutorialListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommonBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampEnemyPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampItemPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFishPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampFishPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIFishPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampLocationPictureBookInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UITutorialInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampPlayerDataItemPresenter).TypeHandle);

                // Item Creation sub-screen types
                RuntimeHelpers.RunClassConstructor(typeof(UICampSelectSpecialSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillSelectorBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillActionSelectorBase).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISpecialSkillConsumeListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISpecialSkillCreationListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemCreationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIItemCreationInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISpecialSkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillResultSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillResultListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillAddMaterialSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillSelectMaterialSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillItemListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(InvestFactorItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISpecialSkillFactorInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIPercentagePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ConstItemParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillActionPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSpecialSkillActionSelectorData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIConditionGroupData).TypeHandle);

                // Super Specialty sub-screen types
                RuntimeHelpers.RunClassConstructor(typeof(UICampSuperSpecialSkillSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillSelectItemPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillSelectItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillNamePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillNeedLevelPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISuperSpecialSkillLearningPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillLearningSuperSpecialSkillInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampSkillLearningSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillLearningInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISkillLearningListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuItemPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampMenuItemData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UICampWindow),
                        nameof(UICampWindow.Open)),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CampWindow_Open_Postfix))
                );

                // UICampMenuItemPresenter.UpdateShow fires from managed code whenever a
                // root menu row (or System sub-menu row — same type) is populated with
                // its data. Captures the rendered localized label per CampMenuItem enum
                // value BEFORE any announcement. Cursor movement on this menu is native
                // and fires nothing (OnSelected included — verified in the 2026-08-31
                // log), so population time is the only reliable managed capture point.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampMenuItemPresenter),
                        nameof(UICampMenuItemPresenter.UpdateShow),
                        new Type[] { typeof(ListItemDataBase) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CampMenuItemPresenter_UpdateShow_Postfix))
                );

                // NO HOOK on UIItemTabPresenter — item category names are read from
                // itemTabDataList instead. SetTabName(string) looks hookable (CallerCount
                // 2) but never fired once in testing (log 26-9-2_20-17-0), and the
                // UpdateTabName that wraps it is CallerCount 0: both are inlined into
                // their callers, like the caption methods. See ResolveItemCategoryName.

                // UIItemInformationPresenter.Set has two overloads — patch the one that
                // takes UIItemInformationData (fires on every equip item navigation).
                harmony.Patch(
                    AccessTools.Method(typeof(UIItemInformationPresenter), "Set",
                        new Type[] {
                            typeof(UIItemInformationData),
                            typeof(UICharacterStatusItemInformationData),
                            typeof(bool)
                        }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(ItemInfoPresenter_Set_Postfix))
                );

                // UIElementalGroupPresenter.Set fires when the elemental resistance panel
                // updates (CallerCount 8 — hookable). Announces resistances on Triangle press.
                harmony.Patch(
                    AccessTools.Method(typeof(UIElementalGroupPresenter), "Set",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UIElementalData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(ElementalGroupPresenter_Set_Postfix))
                );

                // UIBattleSkillInformationPresenter.Set fires on every skill navigation
                // in the battle skill screen (CallerCount 4 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UIBattleSkillInformationPresenter), "Set",
                        new Type[] { typeof(UIBattleSkillInformationData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(BattleSkillInfoPresenter_Set_Postfix))
                );

                // UICampStatusParameterPresenter.Setup fires when the status screen
                // parameter panel updates (CallerCount 12 — hookable). Captures all stats.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusParameterPresenter), "Setup",
                        new Type[] { typeof(UICampStatusParameterData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(StatusParamPresenter_Setup_Postfix))
                );

                // --- STATUS SCREEN HOOKS (hook-driven detection) ---
                // Both activeInHierarchy and root-menu-hidden detection failed for the status
                // screen. These hooks fire when the status screen opens or character changes.
                // UpdatePresenter fires LAST and triggers the announcement.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdatePresenter",
                        new Type[] { typeof(int), typeof(int), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdatePresenter))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdateName",
                        new Type[] { typeof(PlayerID), typeof(ConstPlayerParameter) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdateName))
                );

                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusLevelPresenter), "Setup",
                        new Type[] { typeof(UICampStatusLevelData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusLevelPresenter_Setup))
                );

                // UpdateTalent(PlayerID) builds the talent list for a character. We hook
                // it (prefix) only to capture the playerID, so the talent readout can use
                // the authoritative HasTalent check on the correct character — the on-screen
                // list hides ownership in colour, which a screen reader cannot perceive.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusSelector), "UpdateTalent",
                        new Type[] { typeof(PlayerID) }),
                    prefix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(Diag_StatusSelector_UpdateTalent))
                );

                // UITalentPresenter.Set fires when the status screen initializes
                // (CallerCount 1 — hookable). Triggers building the talent announcement;
                // announced on page switch.
                harmony.Patch(
                    AccessTools.Method(typeof(UITalentPresenter), "Set",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UITalentData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(TalentPresenter_Set_Postfix))
                );

                // UICampStatusPresenter.SetEmotion fires when the friendship panel
                // updates (CallerCount 1 — hookable). Caches friendship data.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampStatusPresenter), "SetEmotion",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UICampStatusFavorabilityRatingItemListData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(StatusPresenter_SetEmotion_Postfix))
                );

                // UICampFormationInformationPresenter.Set fires when the formation info
                // panel updates (CallerCount 1 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UICampFormationInformationPresenter), "Set"),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(FormationInfoPresenter_Set_Postfix))
                );

                // UISkillInformationPresenter.Set fires when the skill info panel updates
                // in the skills sub-screen (CallerCount 1 — hookable from managed code).
                harmony.Patch(
                    AccessTools.Method(typeof(UISkillInformationPresenter), "Set",
                        new Type[] { typeof(UISkillInformationData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(SkillInfoPresenter_Set_Postfix))
                );

                // UICampCharacterStatusPresenter.SetStatus fires when the party formation
                // screen populates/updates the character data list (CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UICampCharacterStatusPresenter), "SetStatus",
                        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData>) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CharacterStatusPresenter_SetStatus_Postfix))
                );

                // UICampPartyMemberPresenter.SetData fires per-slot when a character is
                // assigned to a party member slot (CallerCount 2). Caches per-slot data.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampPartyMemberPresenter), "SetData",
                        new Type[] { typeof(int), typeof(UICampPartyMemberSelectItemData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(PartyMemberPresenter_SetData_Postfix))
                );

                // UICampOperationInformationPresenter.Set fires when the tactics operation
                // info panel updates (CallerCount 1). Announces operation name + description.
                harmony.Patch(
                    AccessTools.Method(typeof(UICampOperationInformationPresenter), "Set",
                        new Type[] { typeof(string), typeof(string), typeof(string) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(OperationInfoPresenter_Set_Postfix))
                );

                // GameUIManager.OpenQuestWindow fires when camp opens the quest sub-screen
                // (CallerCount 2 — hookable). Captures UIQuestWindow for polling.
                harmony.Patch(
                    AccessTools.Method(typeof(GameUIManager), "OpenQuestWindow"),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(OpenQuestWindow_Postfix))
                );

                // GameUIManager.OpenMissionWindow fires when camp opens the mission
                // sub-screen (CallerCount 2 — hookable). Captures UIMissionWindow.
                harmony.Patch(
                    AccessTools.Method(typeof(GameUIManager), "OpenMissionWindow"),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(OpenMissionWindow_Postfix))
                );

                // UISpecialSkillInformationPresenter.Set fires when the skill info panel
                // updates in the item creation skill selection screen (CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UISpecialSkillInformationPresenter), "Set",
                        new Type[] {
                            typeof(string), typeof(string),
                            typeof(Il2CppSystem.Collections.Generic.List<string>),
                            typeof(Il2CppSystem.Collections.Generic.List<UIConditionGroupData>),
                            typeof(string), typeof(bool), typeof(int)
                        }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(SkillInfoPresenter_Set_IC_Postfix))
                );

                // UIItemCreationInformationPresenter.Set fires when the creation info
                // panel updates (action screen, CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UIItemCreationInformationPresenter), "Set",
                        new Type[] { typeof(UIItemCreationInformationData) }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(CreationInfoPresenter_Set_IC_Postfix))
                );

                // UICampSpecialSkillAddMaterialSelector.Set fires when the material
                // selection screen is initialized (CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UICampSpecialSkillAddMaterialSelector), "Set",
                        new Type[] {
                            typeof(Il2CppSystem.Collections.Generic.List<int>),
                            typeof(Il2CppSystem.Collections.Generic.List<int>),
                            typeof(UIDefine.CampState),
                            typeof(int)
                        }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(AddMaterialSelector_Set_IC_Postfix))
                );

                // UISuperSpecialSkillInformationPresenter.Set fires when the super
                // specialty info panel updates (CallerCount 1).
                harmony.Patch(
                    AccessTools.Method(typeof(UISuperSpecialSkillInformationPresenter), "Set",
                        new Type[] {
                            typeof(string), typeof(string), typeof(string),
                            typeof(Il2CppSystem.Collections.Generic.List<string>)
                        }),
                    postfix: new HarmonyMethod(typeof(CampMenuHandler),
                        nameof(SuperSpecialSkillInfoPresenter_Set_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("CampMenuHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CampMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion
    }
}
