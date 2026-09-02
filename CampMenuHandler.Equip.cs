using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        // Equip sub-screen
        // UICampEquipSelector wraps:
        //   equipListSelector (UIEquipListSelector → UIHelpListSelectorBase → UIListSelectorBase)
        //     — the list of equipment slots showing what is currently equipped
        //   itemListSelector (UICampEquipItemListSelector → UIListSelectorBase)
        //     — the list of items that can be equipped in the selected slot
        // Slot list: polled. Item list: driven by UIItemInformationPresenter.Set hook.
        private static UICampEquipSelector _equipSelector = null;
        private static readonly SubScreenState _equipState = new SubScreenState();

        // Equip slot list
        private static UIListSelectorBase _equipSlotListBase = null;
        private static int _equipSlotLastIndex = -1;
        private static bool _equipSlotWasActive = false;

        // Equip item list — used by the hook to read currentIndex and total count.
        private static UIListSelectorBase _equipItemListBase = null;
        private static bool _equipItemListActive = false;

        // Elemental resistance panel — shown by Triangle button.
        // Data cached from UIElementalGroupPresenter.Set hook; announced when panel
        // visibility transitions from hidden to shown (polled in UpdateEquipSelector).
        private static string _cachedElementalAnnouncement = null;

        /// <summary>
        /// Loc keys for the equipment slot categories, indexed by slot position.
        /// EquipType enum order: Weapon=0, Armor=1, Shield=2, Helmet=3, Greeve=4,
        /// Accessory1=5, Accessory2=6 — matches the slot list display order.
        /// The game renders only icons for these slots (no on-screen text to reuse),
        /// so the labels are the mod's own and come from the translation files.
        /// </summary>
        private static readonly string[] _equipSlotCategoryKeys =
        {
            "camp_equip_slot_weapon", "camp_equip_slot_armor", "camp_equip_slot_shield",
            "camp_equip_slot_helmet", "camp_equip_slot_greaves",
            "camp_equip_slot_accessory1", "camp_equip_slot_accessory2"
        };

        /// <summary>
        /// Localized name for an equipment slot. Resolved through Loc on every call
        /// so a live language switch is picked up immediately (no cached strings).
        /// </summary>
        private static string GetEquipSlotCategory(int idx)
        {
            if (idx >= 0 && idx < _equipSlotCategoryKeys.Length)
                return Loc.Get(_equipSlotCategoryKeys[idx]);
            return Loc.Get("camp_equip_slot_n", idx + 1);
        }

        /// <summary>
        /// Polls the UICampEquipSelector and its sub-selectors.
        /// Announces "Equipment." when the screen opens.
        /// The slot list (what is currently equipped) is polled here.
        /// The item list (items to choose from) is announced via the
        /// UIItemInformationPresenter.Set hook — this method only tracks its active state.
        /// </summary>
        private void UpdateEquipSelector()
        {
            if (_equipSelector == null) return;

            // Only poll when the root menu highlights "Equip".
            if (_lastRootMenuItemName != "Equip") return;

            try
            {
                bool isActive = _equipSelector.gameObject.activeInHierarchy;

                bool shouldPoll = _equipState.CheckEntry(
                    isActive,
                    () =>
                    {
                        ScreenReader.Say(Loc.Get("camp_equip_screen"));
                        _equipSlotLastIndex = -1;
                    },
                    "CampEquip",
                    onHidden: () =>
                    {
                        _equipSlotWasActive = false;
                        _equipItemListActive = false;
                        _equipSlotListBase = null;
                        _equipItemListBase = null;
                        _cachedElementalAnnouncement = null;
                        _equipCharTab.Reset();
                    });

                if (!shouldPoll)
                {
                    // On genuine first entry, cache sub-selectors.
                    if (_equipState.WasActive)
                    {
                        if (_equipSlotListBase == null)
                        {
                            var slotSel = _equipSelector.equipListSelector;
                            _equipSlotListBase = slotSel?.TryCast<UIListSelectorBase>();
                        }

                        if (_equipItemListBase == null)
                        {
                            var itemSel = _equipSelector.itemListSelector;
                            _equipItemListBase = itemSel?.TryCast<UIListSelectorBase>();
                        }
                    }
                    return;
                }

                // Use currentState to determine which sub-list is active.
                // State.EquipType = browsing slot list, State.Item = browsing item list.
                // (activeInHierarchy is always true for sub-selectors — unreliable.)
                _equipItemListActive = _equipSelector.currentState == UICampEquipSelector.State.Item;

                // When the item list is open, item announcements are handled by the hook.
                // Only poll the slot list while the item list is not shown.
                if (!_equipItemListActive)
                {
                    // L1/R1 switch which party member's equipment is on screen. Only the
                    // slot list carries the character tabs — the item picker does not —
                    // so the switch is detected here and the slot is forced to re-read,
                    // since it now shows the new character's gear.
                    if (TrackCharacterTab(_equipCharTab, _equipSelector.currentPlayerID))
                        _equipSlotLastIndex = -1;

                    UpdateEquipSlotList();
                }

                // Announce elemental resistances on Triangle press.
                // The panel's gameObject and CanvasGroup are NOT reliable visibility
                // indicators (activeSelf is always false, myCanvasGroup is null).
                // Instead, detect the Triangle button press directly and announce
                // the cached data from the UIElementalGroupPresenter.Set hook.
                var gim = GameInputManager.Instance;
                if (gim != null && gim.IsDown(GameInputManager.InputAction.Triangle))
                {
                    if (!string.IsNullOrEmpty(_cachedElementalAnnouncement))
                    {
                        ScreenReader.Say(_cachedElementalAnnouncement);
                        DebugLogger.LogState("CampEquip: Triangle pressed, announced elemental.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateEquipSelector: {ex.Message}");
                _equipSelector = null;
                _equipState.Reset();
                _equipSlotListBase = null;
                _equipSlotLastIndex = -1;
                _equipSlotWasActive = false;
                _equipItemListBase = null;
                _equipItemListActive = false;
                _cachedElementalAnnouncement = null;
            }
        }

        /// <summary>
        /// Polls the UIEquipListSelector (the equipment slot list showing what is
        /// currently equipped) and announces the highlighted slot on change.
        /// Each slot's data holds the name of the item currently equipped there.
        /// Called by UpdateEquipSelector only while the item sub-list is not open.
        /// </summary>
        private void UpdateEquipSlotList()
        {
            if (_equipSlotListBase == null) return;

            try
            {
                var slotSel = _equipSelector?.equipListSelector;
                if (slotSel == null) return;

                bool isActive = slotSel.gameObject.activeInHierarchy;

                if (!isActive)
                {
                    if (_equipSlotWasActive)
                    {
                        _equipSlotWasActive = false;
                        _equipSlotLastIndex = -1;
                        DebugLogger.LogState("CampEquip: slot list hidden.");
                    }
                    return;
                }

                // First-activation logic: resets index to force re-announcement.
                // WARNING: This resets _equipSlotLastIndex to -1, which overwrites any
                // stale-seed from CampWindow_Open_Postfix. The Open postfix MUST set
                // _equipSlotWasActive = true alongside the index seed to skip this block.
                // Without that, highlighting "Equip" on the root menu triggers a spurious
                // slot announcement. See SubScreenState.cs class docs for the full pattern.
                if (!_equipSlotWasActive)
                {
                    _equipSlotWasActive = true;
                    _equipSlotLastIndex = -1; // Force re-announce on entry.
                    DebugLogger.LogState("CampEquip: slot list visible.");
                }

                int idx = _equipSlotListBase.currentIndex;
                if (idx == _equipSlotLastIndex) return;
                _equipSlotLastIndex = idx;

                var list = _equipSlotListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIEquipListItemData>();
                if (item == null) return;

                string name = item.itemName ?? "";
                bool available = item.canDecision;

                // Look up the localized category name (e.g., "Weapon") for this slot index.
                string category = GetEquipSlotCategory(idx);

                DebugLogger.LogGameValue("CampEquip.slot",
                    $"category='{category}' name='{name}' available={available} ({idx + 1}/{total})");

                string line;
                if (string.IsNullOrEmpty(name))
                    line = Loc.Get("camp_equip_slot_empty", category, idx + 1, total);
                else if (available)
                    line = Loc.Get("camp_equip_slot", category, name, idx + 1, total);
                else
                    line = Loc.Get("camp_equip_slot_unavailable", category, name, idx + 1, total);

                // Prefixes the character's name when this re-read follows an L1/R1 switch.
                ScreenReader.Say(_equipCharTab.Decorate(line));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateEquipSlotList: {ex.Message}");
            }
        }

        // Element names by index position in the elemental data list.
        // Order matches ElementID enum (starting from EARTH=1): Earth, Water, Fire, Wind,
        // Lightning, Star, Negative, Light, Dark.
        private static readonly string[] _elementNameKeys =
        {
            "element_earth", "element_water", "element_fire", "element_wind",
            "element_lightning", "element_star", "element_negative",
            "element_light", "element_dark"
        };

        /// <summary>
        /// Formats elemental resistance entries from the game data list into localized
        /// strings. Shared by both equip and status screen contexts.
        /// Returns a list of formatted strings like "Fire: weak", "Water: absorb".
        /// Only non-INVALID resistances are included.
        /// </summary>
        private static List<string> FormatElementalResistances(
            Il2CppSystem.Collections.Generic.List<UIElementalData> dataList)
        {
            var parts = new List<string>();
            if (dataList == null) return parts;

            for (int i = 0; i < dataList.Count; i++)
            {
                var entry = dataList[i];
                if (entry == null) continue;
                if (entry.type == ElementResistanceType.INVALID) continue;

                string elemName = (i < _elementNameKeys.Length)
                    ? Loc.Get(_elementNameKeys[i])
                    : $"Element {i + 1}";

                string key = entry.type switch
                {
                    ElementResistanceType.DOUBLE  => "camp_equip_elemental_weak",
                    ElementResistanceType.HALF    => "camp_equip_elemental_half",
                    ElementResistanceType.DISABLE => "camp_equip_elemental_immune",
                    ElementResistanceType.ABSORB  => "camp_equip_elemental_absorb",
                    _ => null
                };

                if (key != null)
                    parts.Add(Loc.Get(key, elemName));
            }

            return parts;
        }

        /// <summary>
        /// Postfix for UIElementalGroupPresenter.Set(List&lt;UIElementalData&gt;).
        /// Fires when the elemental resistance panel updates. Handles both equip screen
        /// (Triangle button) and status screen (page 0 initialization) contexts.
        /// Only non-INVALID resistances are announced.
        /// </summary>
        private static void ElementalGroupPresenter_Set_Postfix(
            Il2CppSystem.Collections.Generic.List<UIElementalData> dataList)
        {
            bool isEquip = _lastRootMenuItemName == "Equip" && _equipSelector != null;
            bool isStatus = _lastRootMenuItemName == "Status";
            if (!isEquip && !isStatus) return;

            try
            {
                if (dataList == null || dataList.Count == 0)
                {
                    if (isEquip)
                        _cachedElementalAnnouncement = Loc.Get("camp_equip_elemental_none");
                    else
                        _cachedStatusElementalAnnouncement = Loc.Get("camp_status_elements_none");
                    return;
                }

                var parts = FormatElementalResistances(dataList);

                if (isEquip)
                {
                    _cachedElementalAnnouncement = parts.Count > 0
                        ? Loc.Get("camp_equip_elemental_heading") + " " + string.Join(". ", parts) + "."
                        : Loc.Get("camp_equip_elemental_none");
                    DebugLogger.LogGameValue("CampEquip.elemental",
                        $"count={dataList.Count} resistances={parts.Count} (cached)");
                }
                else
                {
                    _cachedStatusElementalLines = new System.Collections.Generic.List<string>(parts);
                    _cachedStatusElementalAnnouncement = parts.Count > 0
                        ? Loc.Get("camp_status_elements") + " " + string.Join(". ", parts) + "."
                        : Loc.Get("camp_status_elements_none");
                    DebugLogger.LogGameValue("CampStatus.elemental",
                        $"count={dataList.Count} resistances={parts.Count} (cached)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.ElementalGroupPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIItemInformationPresenter.Set(UIItemInformationData, ...).
        /// Fires whenever the item information panel is updated — which happens each time
        /// the cursor moves in the equip item list.
        ///
        /// Gated: only announces when the equip screen is open and its item list is active.
        /// Announces item name, description, battle effect, stat values with item equipped,
        /// factor name and description, and position in the list.
        ///
        /// Note on stats: statusParameterData contains absolute stats WITH the highlighted
        /// item equipped (not deltas). Only the five combat-relevant stats are announced
        /// (Attack, Defence, Magic/INT, Hit, Avoidance) and only those with non-zero values.
        /// </summary>
        private static void ItemInfoPresenter_Set_Postfix(UIItemInformationData data)
        {
            // Cache effect/factor info for the item sub-screen whenever
            // the info panel updates — used by UpdateItemSelector polling.
            if (_lastRootMenuItemName == "Item" && data != null)
            {
                _itemCachedEffect = data.itemEffectInformation ?? "";
                _itemCachedFactorName = data.itemFactorName ?? "";
                _itemCachedFactorInfo = data.itemFactorInformation ?? "";
            }

            // Forward description/effect to ShopHandler when shop is open.
            if (ShopHandler.IsShopOpen && data != null)
            {
                ShopHandler.CacheItemInfo(
                    data.itemInformation,
                    data.itemEffectInformation);
            }

            // Gate: only process equip announcements when the equip screen is open.
            if (_equipSelector == null) return;
            if (_lastRootMenuItemName != "Equip") return;

            try
            {
                if (!_equipSelector.gameObject.activeInHierarchy) return;
                if (_equipSelector.currentState != UICampEquipSelector.State.Item) return;
                if (data == null) return;

                string name        = data.itemName             ?? "";
                string description = data.itemInformation      ?? "";
                string effectInfo  = data.itemEffectInformation ?? "";
                string factorName  = data.itemFactorName        ?? "";
                string factorInfo  = data.itemFactorInformation ?? "";

                DebugLogger.LogGameValue("CampEquip.itemInfo",
                    $"name='{name}' desc='{description}' effect='{effectInfo}' " +
                    $"factor='{factorName}' factorInfo='{factorInfo}'");

                var sb = new StringBuilder();

                if (!string.IsNullOrEmpty(name))        AppendSentence(sb, name);
                if (!string.IsNullOrEmpty(description)) AppendSentence(sb, description);
                if (!string.IsNullOrEmpty(effectInfo))  AppendSentence(sb, effectInfo);

                // Announce the five combat stats shown in the stat comparison panel.
                // statusParameterData holds character stats with this item equipped.
                var stats = data.statusParameterData;
                if (stats != null)
                {
                    if (stats.attack  != 0) sb.Append(Loc.Get("camp_equip_stat_attack",    stats.attack)).Append(". ");
                    if (stats.defence != 0) sb.Append(Loc.Get("camp_equip_stat_defence",   stats.defence)).Append(". ");
                    if (stats.magic   != 0) sb.Append(Loc.Get("camp_equip_stat_magic",     stats.magic)).Append(". ");
                    if (stats.hit     != 0) sb.Append(Loc.Get("camp_equip_stat_hit",       stats.hit)).Append(". ");
                    if (stats.dodge   != 0) sb.Append(Loc.Get("camp_equip_stat_avoidance", stats.dodge)).Append(". ");
                }

                if (!string.IsNullOrEmpty(factorName))
                    sb.Append(Loc.Get("camp_equip_factor", factorName)).Append(". ");
                if (!string.IsNullOrEmpty(factorInfo))
                    AppendSentence(sb, factorInfo);

                // Append position in the list if the count is available.
                if (_equipItemListBase != null)
                {
                    int idx   = _equipItemListBase.currentIndex;
                    var lData = _equipItemListBase.currentDataList;
                    if (lData != null && lData.Count > 0 && idx >= 0)
                        sb.Append(Loc.Get("camp_equip_position", idx + 1, lData.Count));
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.ItemInfoPresenter_Set_Postfix: {ex.Message}");
            }
        }
    }
}
