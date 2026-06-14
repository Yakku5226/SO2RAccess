using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of BattleMenuHandler: item and spell/skill sub-menu pollers + name/caster resolution.
    public partial class BattleMenuHandler
    {
        #region Phases B & C: Item + Spell sub-menus

        private void PollItemSelector()
        {
            if (_itemSelector == null) return;

            try
            {
                // Tab change detection
                int tab = _itemSelector.tabIndex;
                if (tab != _lastItemTab)
                {
                    _lastItemTab = tab;
                    _lastItemIndex = -1;
                    ClearInfoCache();

                    string tabName = tab == 0
                        ? Loc.Get("battle_menu_items_recovery")
                        : Loc.Get("battle_menu_items_combat");
                    ScreenReader.Say(tabName);
                    return;
                }

                // Index change
                int idx = _itemSelector.currentIndex;
                if (idx == _lastItemIndex) return;
                _lastItemIndex = idx;

                AnnounceItem(idx);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollItemSelector error: {ex.Message}");
            }
        }

        private void AnnounceItem(int idx)
        {
            try
            {
                var dataList = _itemSelector.currentDataList;
                if (dataList == null || idx < 0 || idx >= dataList.Count)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_items_empty"));
                    return;
                }

                int total = dataList.Count;

                // Get item name and effect from hook cache first
                string name = _cachedInfoLabel;
                string effect = _cachedInfoEffect;

                // Fallback: read directly from the info presenter's displayed text
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(effect))
                {
                    try
                    {
                        var presenter = _itemSelector.itemInformationPresenter;
                        if (presenter != null)
                        {
                            if (string.IsNullOrEmpty(name))
                            {
                                var labelText = presenter.label;
                                if (labelText != null)
                                    name = ((Il2CppTMPro.TMP_Text)labelText)?.text;
                            }
                            if (string.IsNullOrEmpty(effect))
                            {
                                var infoText = presenter.information;
                                if (infoText != null)
                                    effect = ((Il2CppTMPro.TMP_Text)infoText)?.text;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState($"BattleMenuHandler: info presenter read error: {ex.Message}");
                    }
                }

                // Last resort: resolve name from itemID via ParameterManager
                if (string.IsNullOrEmpty(name))
                {
                    var itemData = dataList[idx]?.TryCast<UIBattleItemListItemData>();
                    if (itemData != null)
                        name = ResolveItemName(itemData.itemID);
                }

                if (string.IsNullOrEmpty(name)) name = "???";

                // Get item count
                int count = 0;
                var rawData = dataList[idx]?.TryCast<UIBattleItemListItemData>();
                if (rawData != null)
                    count = ResolveItemCount(rawData.itemID);

                string effectStr = !string.IsNullOrEmpty(effect)
                    ? TextUtil.StripTags(effect).TrimEnd('.')
                    : "";

                if (!string.IsNullOrEmpty(effectStr))
                    ScreenReader.Say(Loc.Get("battle_menu_items_detail", name, count, effectStr, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("battle_menu_items_basic", name, count, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.AnnounceItem error: {ex.Message}");
            }
        }

        private static string ResolveItemName(int itemID)
        {
            try
            {
                var pm = ParameterManager.Instance;
                if (pm == null) return null;
                var param = pm.GetItemParameter(itemID);
                if (param == null) return null;
                string nameKey = param.ItemNameID;
                if (string.IsNullOrEmpty(nameKey)) return null;
                return pm.GetItemMessage(nameKey);
            }
            catch { return null; }
        }

        private static int ResolveItemCount(int itemID)
        {
            try
            {
                var im = ItemManager.Instance;
                if (im == null) return 0;
                return im.GetItemCount(itemID);
            }
            catch { return 0; }
        }



        private void PollSpellSelector()
        {
            if (_spellSelector == null) return;

            try
            {
                // Tab (character) change
                int tab = _spellSelector.tabIndex;
                if (tab != _lastSpellTab)
                {
                    _lastSpellTab = tab;
                    _lastSpellIndex = -1;
                    ClearInfoCache();

                    string charName = ResolveSpellCasterName(tab);
                    ScreenReader.Say(Loc.Get("battle_menu_spell_heading", charName));
                    return;
                }

                // Index change
                int idx = _spellSelector.currentIndex;
                if (idx == _lastSpellIndex) return;
                _lastSpellIndex = idx;

                AnnounceSpell(idx);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollSpellSelector error: {ex.Message}");
            }
        }

        private void AnnounceSpell(int idx)
        {
            try
            {
                var dataList = _spellSelector.currentDataList;
                if (dataList == null || idx < 0 || idx >= dataList.Count)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_spell_empty"));
                    return;
                }

                var spellData = dataList[idx]?.TryCast<UIBattleSpellItemData>();
                if (spellData == null) return;

                string name = spellData.spell ?? "???";
                int mp = spellData.consumeMp;
                bool usable = spellData.useInBattle;
                int total = dataList.Count;

                // Range and effect from hook caches
                string range = !string.IsNullOrEmpty(_cachedRangeDesc)
                    ? TextUtil.StripTags(_cachedRangeDesc).TrimEnd('.')
                    : (!string.IsNullOrEmpty(_cachedInfoRange)
                        ? TextUtil.StripTags(_cachedInfoRange).TrimEnd('.')
                        : "");

                string effect = !string.IsNullOrEmpty(_cachedEffectDesc)
                    ? TextUtil.StripTags(_cachedEffectDesc).TrimEnd('.')
                    : (!string.IsNullOrEmpty(_cachedInfoEffect)
                        ? TextUtil.StripTags(_cachedInfoEffect).TrimEnd('.')
                        : "");

                if (!usable)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_spell_unavailable",
                        name, mp, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("battle_menu_spell_detail",
                        name, mp, range, effect, idx + 1, total));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.AnnounceSpell error: {ex.Message}");
            }
        }

        private string ResolveSpellCasterName(int tabIndex)
        {
            try
            {
                var casterList = _spellSelector.spellcasterPlayerIDList;
                if (casterList == null || tabIndex < 0 || tabIndex >= casterList.Count)
                    return "???";

                var playerID = casterList[tabIndex];

                // Try ParameterManager → charaNameID → TextManager
                var pm = ParameterManager.Instance;
                if (pm != null)
                {
                    var param = pm.GetPlayerParameter(playerID);
                    if (param != null)
                    {
                        string nameKey = param.charaNameID;
                        if (!string.IsNullOrEmpty(nameKey))
                            return TextUtil.ResolveCharaNameKey(nameKey);
                    }
                }

                return playerID.ToString();
            }
            catch { return "???"; }
        }

        #endregion
    }
}
