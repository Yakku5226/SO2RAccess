using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler (Item Creation): material-slot + inventory pickers, factor rate, item-name resolution.
    public partial class CampMenuHandler
    {
        #region Screen 3b: Material Selection

        /// <summary>
        /// Polls the material selection screen (addMaterialSelector).
        /// This screen has two sub-states:
        ///   SelectMaterial — material slot list (choose which slot to fill).
        ///   ItemList — inventory picker (choose an item for the slot).
        ///
        /// Detection: polls sub-selectors' activeInHierarchy. The parent
        /// addMaterialSelector has stale activeInHierarchy, but the children
        /// (selectMaterialSelector, itemListSelector) should only be active
        /// when actually on the material screen. The Set hook is a bonus
        /// signal but not required.
        /// </summary>
        private void UpdateICMaterialSelection()
        {
            if (_icAddMaterialSelector == null) return;

            // Detection: hook-only. All sub-selectors have stale activeInHierarchy
            // (true from IC start), so we CANNOT use activeInHierarchy for detection.
            // The Set hook signals actual entry into the material selection flow.
            if (!_icMaterialSetHookFired) return;

            // Once hook fired, poll currentState for sub-state switching.
            int detectedState = -1;
            try
            {
                if (_icAddMaterialSelector.gameObject.activeInHierarchy)
                    detectedState = (int)_icAddMaterialSelector.currentState;
            }
            catch { return; }

            if (detectedState == -1)
            {
                if (_icMaterialLastState != -1)
                {
                    // Was active, now hidden — reset.
                    _icMaterialLastState = -1;
                    _icMaterialSelectState.Reset();
                    _icMaterialItemListState.Reset();
                    _icMaterialSelectListBase = null;
                    _icMaterialItemListBase = null;
                    _icMaterialSetHookFired = false;
                    DebugLogger.LogState("CampIC_Material: screen closed.");
                }
                return;
            }

            try
            {
                if (detectedState != _icMaterialLastState)
                {
                    int prevState = _icMaterialLastState;
                    _icMaterialLastState = detectedState;
                    DebugLogger.LogState($"CampIC_Material: state changed to {detectedState}");

                    if (detectedState == 0) // SelectMaterial
                    {
                        _icMaterialItemListState.Reset();
                        _icMaterialItemListBase = null;

                        if (prevState == -1)
                        {
                            // First entry — announce heading.
                            ScreenReader.Say(Loc.Get("ic_material_screen"));
                        }
                        else
                        {
                            // Returning from item list — announce current slot.
                            ScreenReader.Say(Loc.Get("ic_material_slots"));
                        }

                        // Announce factor rate on entry/return.
                        AnnounceMaterialFactorRate();
                    }
                    else if (detectedState == 1) // ItemList
                    {
                        _icMaterialSelectState.Reset();
                        _icMaterialSelectListBase = null;
                        ScreenReader.Say(Loc.Get("ic_material_itemlist"));
                    }
                }

                // Poll the active sub-selector.
                if (detectedState == 0)
                    PollMaterialSelectCursor();
                else if (detectedState == 1)
                    PollMaterialItemListCursor();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the material slot list (SelectMaterial state).
        /// Items are InvestFactorItemData — resolve names via ParameterManager.
        /// </summary>
        private void PollMaterialSelectCursor()
        {
            if (_icMaterialSelectListBase == null)
            {
                try
                {
                    var selMatSel = _icAddMaterialSelector.selectMaterialSelector;
                    _icMaterialSelectListBase = selMatSel?.TryCast<UIListSelectorBase>();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC_Material: selectMaterial cast error: {ex.Message}");
                    return;
                }
            }

            if (_icMaterialSelectListBase == null) return;

            try
            {
                int idx = _icMaterialSelectListBase.currentIndex;
                if (idx == _icMaterialSelectState.LastIndex) return;
                _icMaterialSelectState.LastIndex = idx;

                // Try to read material data from AddMaterialDataList.
                var selectMatSel = _icAddMaterialSelector.selectMaterialSelector;
                var materialList = selectMatSel?.AddMaterialDataList;
                int slotCount = materialList?.Count ?? 0;

                var sb = new StringBuilder();

                if (slotCount > 0 && idx >= 0 && idx < slotCount)
                {
                    var matData = materialList[idx];
                    if (matData != null && matData.itemID > 0)
                    {
                        // Slot has an item — resolve name.
                        string itemName = ResolveItemName(matData.itemID);
                        sb.Append(itemName);
                    }
                    else
                    {
                        // Empty slot.
                        sb.Append(Loc.Get("ic_material_empty"));
                    }
                }
                else
                {
                    // Might be the "Create" button at the end of the list.
                    // Try currentDataList for more entries.
                    var dataList = _icMaterialSelectListBase.currentDataList;
                    int totalCount = dataList?.Count ?? 0;

                    if (idx >= slotCount && idx < totalCount)
                    {
                        // Beyond material slots — likely Create button.
                        sb.Append(Loc.Get("ic_material_create"));
                    }
                    else
                    {
                        sb.Append(Loc.Get("ic_material_slot", idx + 1));
                    }
                }

                // Position.
                var totalList = _icMaterialSelectListBase.currentDataList;
                int total = totalList?.Count ?? 0;
                if (total > 0)
                    sb.Append(". ").Append(idx + 1).Append(" of ").Append(total).Append('.');

                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"CampIC_Material: selectMat [{idx}] {sb}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: selectMat poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the item list (ItemList state).
        /// Items are UIItemListItemData with itemName, itemCount, itemDescription.
        /// </summary>
        private void PollMaterialItemListCursor()
        {
            if (_icMaterialItemListBase == null)
            {
                try
                {
                    var itemListSel = _icAddMaterialSelector.itemListSelector;
                    _icMaterialItemListBase = itemListSel?.TryCast<UIListSelectorBase>();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"CampIC_Material: itemList cast error: {ex.Message}");
                    return;
                }
            }

            if (_icMaterialItemListBase == null) return;

            try
            {
                int idx = _icMaterialItemListBase.currentIndex;
                if (idx == _icMaterialItemListState.LastIndex) return;
                _icMaterialItemListState.LastIndex = idx;

                var list = _icMaterialItemListBase.currentDataList;
                int count = list?.Count ?? 0;
                if (count <= 0 || idx < 0 || idx >= count) return;

                var item = list[idx]?.TryCast<UIItemListItemData>();
                if (item != null)
                {
                    var sb = new StringBuilder();
                    string name = SanitizeItemName(item.itemName);
                    sb.Append(name ?? Loc.Get("ic_unknown_item"));

                    int qty = item.itemCount;
                    if (qty > 1)
                        sb.Append(", x").Append(qty);

                    sb.Append(". ").Append(idx + 1).Append(" of ").Append(count).Append('.');

                    ScreenReader.Say(sb.ToString());
                    DebugLogger.LogState($"CampIC_Material: itemList [{idx}] {name}");
                    return;
                }

                // Fallback: bare position.
                ScreenReader.Say(Loc.Get("ic_action_position", idx + 1, count));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: itemList poll error: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the current factor success rate from the factorInformationPresenter.
        /// </summary>
        private void AnnounceMaterialFactorRate()
        {
            try
            {
                var factorPresenter = _icAddMaterialSelector.factorInformationPresenter;
                if (factorPresenter == null) return;

                var factorRate = factorPresenter.factorRate;
                if (factorRate == null) return;

                int rate = factorRate.targetPercentage;
                ScreenReader.Say(Loc.Get("ic_material_rate", rate));
                DebugLogger.LogState($"CampIC_Material: factor rate = {rate}%");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: factor rate error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves an item name from its itemID via ParameterManager and TextManager.
        /// Falls back to parsing the raw key if TextManager can't resolve it.
        /// </summary>
        private static string ResolveItemName(int itemID)
        {
            try
            {
                var param = ParameterManager.Instance?.GetItemParameter(itemID);
                if (param == null) return Loc.Get("ic_unknown_item");

                string nameID = param.itemNameID;
                if (string.IsNullOrEmpty(nameID)) return Loc.Get("ic_unknown_item");

                // Try TextManager resolution.
                var tm = TextManager.Instance;
                if (tm != null)
                {
                    string resolved = tm.GetMessage(nameID, TextManager.MessageType.Item);
                    if (!string.IsNullOrEmpty(resolved))
                        return SanitizeItemName(resolved);
                }

                // Fallback: parse key (e.g. "ITEM_BLUEBERRY" → "Blueberry").
                string fallback = nameID;
                if (fallback.StartsWith("ITEM_"))
                    fallback = fallback.Substring(5);
                fallback = fallback.Replace('_', ' ');
                if (fallback.Length > 0)
                    fallback = char.ToUpper(fallback[0]) + fallback.Substring(1).ToLower();
                return fallback;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampIC_Material: resolveItemName({itemID}) error: {ex.Message}");
                return Loc.Get("ic_unknown_item");
            }
        }

        #endregion
    }
}
