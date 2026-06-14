# CampMenuHandler.Formation.cs (278 lines)

PARTIAL CLASS FRAGMENT — this file is `partial class CampMenuHandler`. It covers the Formation
sub-screen and Skills sub-screen polling logic, plus Harmony postfix hook methods for
UICampFormationInformationPresenter.Set and UISkillInformationPresenter.Set.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 11)
Formation and Skills sub-screen fragment. See CampMenuHandler.cs for root class definition.

fields/properties (declaration order):
- _formationSelector : UICampFormationSelector (line 17)  — static; cached on camp open
- _formationState : SubScreenState (line 18)  — static readonly; tracks formation screen entry state
- _skillSelector : UICampSkillSelector (line 25)  — static; cached on camp open
- _skillState : SubScreenState (line 26)  — static readonly; tracks skill screen entry state

methods (declaration order):
- void UpdateFormationSelector() (line 34)
  - note: Polled each frame; guards on _lastRootMenuItemName=="Formation", checks activeInHierarchy, announces "Formation." on screen open via SubScreenState.CheckEntry.
- void UpdateSkillSelector() (line 63)
  - note: Polled each frame; guards on _lastRootMenuItemName=="Skill", checks activeInHierarchy, announces "Skills." on screen open via SubScreenState.CheckEntry.
- void FormationInfoPresenter_Set_Postfix(string, string, int, int, List<UIBonusBuffDescriptionData>) (line 92)
  - note: Harmony postfix for UICampFormationInformationPresenter.Set. Announces formation name, effect description, sphere count, bonus count, individual bonus descriptions (enabled/disabled), and position (index/total) from _formationSelector cast to UIListSelectorBase.
- void SkillInfoPresenter_Set_Postfix(UISkillInformationData) (line 158)
  - note: Harmony postfix for UISkillInformationPresenter.Set. Resolves SP cost and max-level freshly (itemDataList is stale for specialties after leveling): specialties use UICommon.CalcNeedSpecialSkillForLevelUp; knowledge skills use ParameterManager.GetSkillParameter. Announces skill name, level, max indicator, SP cost with balance, description, and position.
