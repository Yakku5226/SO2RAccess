# CampMenuHandler.Equip.cs (423 lines)

PARTIAL CLASS FILE — this is a `partial class CampMenuHandler` fragment containing
only the equipment sub-screen logic. The other parts of CampMenuHandler live in
separate files (e.g. CampMenuHandler.cs, CampMenuHandler.Status.cs, etc.).

Equipment sub-screen structure:
  UICampEquipSelector wraps:
    equipListSelector (UIEquipListSelector → UIListSelectorBase) — currently-equipped slots (polled)
    itemListSelector  (UICampEquipItemListSelector → UIListSelectorBase) — items to equip (driven by UIItemInformationPresenter.Set hook)
  Elemental resistance panel: shown on Triangle press; data cached from UIElementalGroupPresenter.Set hook.

namespace: SO2RAccess (line 11)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, System.Text, System.Text.RegularExpressions

## partial class CampMenuHandler (line 12)

fields/properties (declaration order):
- _equipSelector : static UICampEquipSelector (line 21)
- _equipState : static readonly SubScreenState (line 22)
- _equipSlotListBase : static UIListSelectorBase (line 25)
- _equipSlotLastIndex : static int (line 26)
- _equipSlotWasActive : static bool (line 27)
- _equipSlotCategoryNames : static string[] (line 30)  — friendly slot names by index; populated from EquipType enum order (Weapon=0..Accessory2=6)
- _equipItemListBase : static UIListSelectorBase (line 33)  — used by hook to read currentIndex and total count
- _equipItemListActive : static bool (line 34)
- _cachedElementalAnnouncement : static string (line 39)  — elemental data cached from UIElementalGroupPresenter.Set hook; announced on Triangle press
- _elementNameKeys : static readonly string[] (line 234)  — 9-element array mapping ElementID index (EARTH=0..DARK=8) to Loc keys

methods (declaration order):
- static void CacheEquipSlotCategories() (line 46)
  - note: Hard-codes _equipSlotCategoryNames = {"Weapon","Armor","Shield","Helmet","Greaves","Accessory 1","Accessory 2"} matching EquipType enum order.

- void UpdateEquipSelector() (line 63)
  - note: Called from the main camp poll loop. Gates on _lastRootMenuItemName == "Equip". Uses SubScreenState.CheckEntry for open/close detection. Caches _equipSlotListBase and _equipItemListBase on first entry. Detects _equipSelector.currentState == State.Item to set _equipItemListActive. Handles Triangle button press to announce cached elemental data.

- void UpdateEquipSlotList() (line 160)
  - note: Polls equipListSelector.currentIndex. On change, reads UIEquipListItemData (itemName, canDecision) and _equipSlotCategoryNames[idx]. Announces slot-empty / slot / slot-unavailable Loc strings. First-activation resets _equipSlotLastIndex=-1 to force re-announce.

- static List<string> FormatElementalResistances(Il2CppSystem.Collections.Generic.List<UIElementalData>) (line 247)
  - note: Shared by equip and status screen contexts. Maps dataList entries (non-INVALID only) to Loc strings using _elementNameKeys and ElementResistanceType switch (DOUBLE=weak, HALF=half, DISABLE=immune, ABSORB=absorb).

- static void ElementalGroupPresenter_Set_Postfix(Il2CppSystem.Collections.Generic.List<UIElementalData>) (line 285)
  - note: Postfix for UIElementalGroupPresenter.Set. Handles both equip screen (Triangle button) and status screen (page 0 init) contexts, gated by _lastRootMenuItemName. For equip: caches into _cachedElementalAnnouncement. For status: caches into _cachedStatusElementalLines and _cachedStatusElementalAnnouncement (fields defined in the Status partial).

- static void ItemInfoPresenter_Set_Postfix(UIItemInformationData data) (line 342)
  - note: Postfix for UIItemInformationPresenter.Set. Three-way dispatch: (1) if Item screen active, caches _itemCachedEffect/FactorName/FactorInfo for UpdateItemSelector polling; (2) if ShopHandler.IsShopOpen, forwards info to ShopHandler.CacheItemInfo; (3) if Equip screen open and State.Item active, announces name + description + effectInfo + 5 combat stats (attack/defence/magic/hit/dodge, non-zero only) + factorName + factorInfo + list position.
