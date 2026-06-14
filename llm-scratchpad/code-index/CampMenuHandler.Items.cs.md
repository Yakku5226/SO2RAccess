# CampMenuHandler.Items.cs (120 lines)

Partial class fragment of CampMenuHandler (see also CampMenuHandler.ItemCreation.cs).
No file-level comment block.
namespace: SO2RAccess (implicit, line 1 context)
usings: HarmonyLib, Il2CppGame, MelonLoader, System.Runtime.CompilerServices, System.Text

## partial class CampMenuHandler (line 10)
Camp Items sub-screen accessibility polling. Polls UICampItemSelector and announces item details.

fields/properties (declaration order):
- _itemSelector : UICampItemSelector (line 15)
- _itemListSelectorBase : UIListSelectorBase (line 16)  — Cast of UICampItemListSelector (inner field itemListSelector) to UIListSelectorBase for currentIndex/currentDataList access
- _itemState : SubScreenState (line 17)
- _itemCachedEffect : string (line 21)  — Cached from UIItemInformationPresenter.Set hook; effect text (e.g. "Restores 30% HP")
- _itemCachedFactorName : string (line 22)  — Cached from UIItemInformationPresenter.Set hook
- _itemCachedFactorInfo : string (line 23)  — Cached from UIItemInformationPresenter.Set hook

methods (declaration order):
- void UpdateItemSelector() (line 34)
  - note: Polls UICampItemSelector. Gated on _lastRootMenuItemName == "Item". Uses SubScreenState.CheckEntry for heading + stale-open suppression. On entry caches inner list selector. Announces: Name x[count]. Effect. Description. Factor. Position. Resets _itemSelector on exception.
