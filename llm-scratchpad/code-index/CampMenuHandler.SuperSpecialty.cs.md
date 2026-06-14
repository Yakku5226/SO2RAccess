# CampMenuHandler.SuperSpecialty.cs (345 lines)

Partial class fragment of CampMenuHandler — adds Super Specialty sub-screen accessibility.
Two contexts: Context A = IC skill selection Tab 2 (UICampSelectSpecialSkillSelector, polled from UpdateICSkillSelection);
Context B = Skill Learning selector (UICampSkillLearningSelector, Enhance → Skill → R2, polled from Update()).
namespace: SO2RAccess (line 7)
usings (non-System / notable only): Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 29)
Super Specialty fragment. Adds Context A (IC tab 2 polling) and Context B (skill learning polling).

fields/properties (declaration order):
- _slSelector : static UICampSkillLearningSelector (line 34)  — cached from _skillSelector.learningSelector
- _slState : static readonly SubScreenState (line 35)  — tracks visibility + last index for Context B

methods (declaration order):
- private static void CacheSuperSpecialtySelector() (line 45)
  - note: Called from CampWindow_Open_Postfix via CacheItemCreationSelectors. Resets _slState and _slSelector, then caches _skillSelector.learningSelector. If selector is already active on open, seeds _slState to suppress stale announcement.
- private void TryPollSuperSpecialtyTab() (line 92)
  - note: Context A. Polls currentIndex on _icSkillSelector (cast to UIListSelectorBase) when _icLastTab == 2. Reads skillName.text, skillDescription.text, and superSpecialSkillLearningPresenter conditions from the same informationPresenter used by tabs 0/1. Called from UpdateICSkillSelection.
- private void UpdateSkillLearning() (line 151)
  - note: Context B. Polls _slSelector.gameObject.activeInHierarchy via SubScreenState.CheckEntry; on index change reads UISkillLearningListItemData (skillName, level) plus infoPresenter.skillDescription and learning conditions. Called unconditionally from Update().
- private static void AppendLearningConditions(StringBuilder sb, UISuperSpecialSkillLearningPresenter learningPresenter) (line 250)
  - note: Shared by Context A and B. Reads condition1Skill/condition1Description and condition2Skill/condition2Description; appends "ss_requires" Loc string if any conditions present.
- private static void SuperSpecialSkillInfoPresenter_Set_Postfix(string skillName, string skillDescription, string learnSkill, Il2CppSystem.Collections.Generic.List<string> needSkillList) (line 294)
  - note: Postfix hook for UISuperSpecialSkillInformationPresenter.Set. CallerCount(1) but caller is native C++ — may never fire. If it does, announces immediately. Kept for potential future use.
