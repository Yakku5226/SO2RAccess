using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces the in-battle command menu (Triangle) via screen reader.
    ///
    /// Covers four sub-screens:
    ///   Phase A: Root menu (Item / Battle Skill / Tactics / Escape)
    ///   Phase B: Item sub-menu (Recovery / Combat tabs, item details)
    ///   Phase C: Spell/skill sub-menu (per-character tabs, skill details)
    ///   Phase D: Target selection (enemy or ally targeting after skill/item pick)
    ///
    /// All navigation is native C++ (CallerCount 0) — uses polling pattern.
    /// Data capture via Harmony postfixes on info panel presenters.
    /// </summary>
    public class BattleMenuHandler
    {
        #region Fields

        private bool _patchesApplied;

        // References (lazy-found, reset on scene change)
        private UIBattleWindow _battleWindow;
        private UIBattleMenuSelector _menuSelector;
        private UIBattleItemSelector _itemSelector;
        private UIBattleSpellSelector _spellSelector;
        private UIBattleSelectCharacterSelector _targetSelector;
        private UIBattleTacticsSelector _tacticsSelector;
        private int _findCooldown;

        // Phase detection via GetPeekSelector() — identifies active sub-screen
        private const int PHASE_NONE = 0;
        private const int PHASE_MENU = 1;
        private const int PHASE_ITEM = 2;
        private const int PHASE_SPELL = 3;
        private const int PHASE_TARGET = 4;
        private const int PHASE_TACTICS = 5;
        private const int PHASE_OTHER = 99;
        private int _lastPhase = -1;
        private bool _wasWindowOpen;

        // Phase A: Root menu polling
        private int _lastMenuIndex = -1;

        // Phase B: Item sub-menu polling
        private int _lastItemIndex = -1;
        private int _lastItemTab = -1;

        // Phase C: Spell sub-menu polling
        private int _lastSpellIndex = -1;
        private int _lastSpellTab = -1;

        // Phase D: Target selection polling
        private int _lastTargetIndex = -1;
        private bool _lastTargetIsEnemy;
        private bool _lastTargetAllAnnounced;

        // Phase E: Tactics polling
        private int _lastTacticsCharIndex = -1;
        private int _lastTacticsState = -1;
        private int _lastTacticsOpIndex = -1;
        private UIListSelectorBase _tacticsOpListBase;

        // Hook data caches (static — Harmony postfixes write here)
        private static string _cachedInfoLabel;
        private static string _cachedInfoEffect;
        private static string _cachedInfoRange;
        private static string _cachedInfoValueLabel;
        private static int _cachedInfoValue;
        private static string _cachedOpName;
        private static string _cachedOpDesc;
        private static string _cachedRangeDesc;
        private static string _cachedEffectDesc;
        private static string _cachedUseDescTitle;

        #endregion

        #region Patches

        /// <summary>
        /// Registers Harmony postfixes on battle info presenters to capture
        /// item/spell names, descriptions, and targeting info.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleMenuSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleMenuItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleItemSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleItemListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSpellSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSpellItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSpellInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSpellInformationData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillEffectRangePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleUseDescriptionPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSelectCharacterSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSelectEnemySelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIDefine).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ItemManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ParameterManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(TextManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleCharacter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ElementID).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleTacticsSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationCharacterListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationSelectListSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICampOperationInformationPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleOperationListItemPresenter).TypeHandle);

                // Hook UIBattleSpellInformationPresenter.Set(UIBattleSpellInformationData) — CallerCount(3)
                // Fires when info panel updates for both items and spells
                try
                {
                    var infoSetData = AccessTools.Method(
                        typeof(UIBattleSpellInformationPresenter), "Set",
                        new[] { typeof(UIBattleSpellInformationData) });
                    if (infoSetData != null)
                        harmony.Patch(infoSetData,
                            postfix: new HarmonyMethod(typeof(BattleMenuHandler),
                                nameof(SpellInfoData_Postfix)));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleMenuHandler: SpellInfoData hook failed: {ex.Message}");
                }

                // Hook UIBattleSkillEffectRangePresenter.Set(string, string, List<ElementID>) — CallerCount(2)
                // Fires with range/effect/element text for spells
                try
                {
                    var rangeSet = AccessTools.Method(
                        typeof(UIBattleSkillEffectRangePresenter), "Set",
                        new[] { typeof(string), typeof(string),
                                typeof(Il2CppSystem.Collections.Generic.List<ElementID>) });
                    if (rangeSet != null)
                        harmony.Patch(rangeSet,
                            postfix: new HarmonyMethod(typeof(BattleMenuHandler),
                                nameof(EffectRange_Postfix)));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleMenuHandler: EffectRange hook failed: {ex.Message}");
                }

                // Hook UIBattleUseDescriptionPresenter.Set(string, ElementID, List<string>) — CallerCount(1)
                // Fires when skill/item name shown during target selection
                try
                {
                    var useDescSet = AccessTools.Method(
                        typeof(UIBattleUseDescriptionPresenter), "Set",
                        new[] { typeof(string), typeof(ElementID),
                                typeof(Il2CppSystem.Collections.Generic.List<string>) });
                    if (useDescSet != null)
                        harmony.Patch(useDescSet,
                            postfix: new HarmonyMethod(typeof(BattleMenuHandler),
                                nameof(UseDescription_Postfix)));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleMenuHandler: UseDescription hook failed: {ex.Message}");
                }

                // Hook UICampOperationInformationPresenter.Set(string, string, string) — CallerCount(1)
                // Fires when tactics operation info panel updates with name + description
                try
                {
                    var opInfoSet = AccessTools.Method(
                        typeof(UICampOperationInformationPresenter), "Set",
                        new[] { typeof(string), typeof(string), typeof(string) });
                    if (opInfoSet != null)
                        harmony.Patch(opInfoSet,
                            postfix: new HarmonyMethod(typeof(BattleMenuHandler),
                                nameof(OperationInfo_Postfix)));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"BattleMenuHandler: OperationInfo hook failed: {ex.Message}");
                }

                _patchesApplied = true;
                MelonLogger.Msg("BattleMenuHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattleMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Hook Postfixes

        private static void SpellInfoData_Postfix(UIBattleSpellInformationData data)
        {
            try
            {
                if (data == null) return;
                _cachedInfoLabel = data.label ?? "";
                _cachedInfoEffect = data.effectDescription ?? "";
                _cachedInfoRange = data.rangeDescription ?? "";
                _cachedInfoValueLabel = data.valueLabel ?? "";
                _cachedInfoValue = data.value;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.SpellInfoData_Postfix error: {ex.Message}");
            }
        }

        private static void EffectRange_Postfix(string rangeDescription, string effectDescription)
        {
            try
            {
                _cachedRangeDesc = rangeDescription ?? "";
                _cachedEffectDesc = effectDescription ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.EffectRange_Postfix error: {ex.Message}");
            }
        }

        private static void UseDescription_Postfix(string title)
        {
            try
            {
                _cachedUseDescTitle = title ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.UseDescription_Postfix error: {ex.Message}");
            }
        }

        private static void OperationInfo_Postfix(string name, string description)
        {
            try
            {
                _cachedOpName = name ?? "";
                _cachedOpDesc = description ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.OperationInfo_Postfix error: {ex.Message}");
            }
        }

        #endregion

        #region Lifecycle

        /// <summary>Resets all state on scene change.</summary>
        public void OnSceneChanged()
        {
            _battleWindow = null;
            _menuSelector = null;
            _itemSelector = null;
            _spellSelector = null;
            _targetSelector = null;
            _tacticsSelector = null;
            _findCooldown = 0;
            ResetAllState();
        }

        /// <summary>Polls battle menu state each frame.</summary>
        public void Update()
        {
            // Lazy-find UIBattleWindow
            if (_battleWindow == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _battleWindow = UnityEngine.Object.FindObjectOfType<UIBattleWindow>();
                    if (_battleWindow == null) return;

                    _menuSelector = _battleWindow.menuSelector;
                    _itemSelector = _battleWindow.itemSelector;
                    _spellSelector = _battleWindow.spellSelector;
                    _targetSelector = _battleWindow.selectCharacterSelector;
                    _tacticsSelector = _battleWindow.tacticsSelector;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"BattleMenuHandler find error: {ex.Message}");
                    return;
                }
            }

            try
            {
                // Detect window open/close
                bool isOpen = _battleWindow.IsOpened;

                if (!isOpen)
                {
                    if (_wasWindowOpen)
                    {
                        ResetAllState();
                        _wasWindowOpen = false;
                    }
                    return;
                }
                _wasWindowOpen = true;

                // Detect active sub-screen via selector stack
                var peekSelector = _battleWindow.GetPeekSelector();
                int phase = IdentifyPhase(peekSelector);

                // Phase transition
                if (phase != _lastPhase)
                {
                    HandlePhaseTransition(phase, _lastPhase);
                    _lastPhase = phase;
                }

                // Dispatch to active poller
                switch (phase)
                {
                    case PHASE_MENU: PollRootMenu(); break;
                    case PHASE_ITEM: PollItemSelector(); break;
                    case PHASE_SPELL: PollSpellSelector(); break;
                    case PHASE_TARGET: PollTargetSelector(); break;
                    case PHASE_TACTICS: PollTacticsSelector(); break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.Update error: {ex.Message}");
            }
        }

        private void ResetAllState()
        {
            _lastPhase = -1;
            ResetPollingState();
            ClearHookCaches();
        }

        private void ResetPollingState()
        {
            _lastMenuIndex = -1;
            _lastItemIndex = -1;
            _lastItemTab = -1;
            _lastSpellIndex = -1;
            _lastSpellTab = -1;
            _lastTargetIndex = -1;
            _lastTargetAllAnnounced = false;
            _lastTacticsCharIndex = -1;
            _lastTacticsState = -1;
            _lastTacticsOpIndex = -1;
            _tacticsOpListBase = null;

        }

        private static void ClearHookCaches()
        {
            _cachedInfoLabel = null;
            _cachedInfoEffect = null;
            _cachedInfoRange = null;
            _cachedInfoValueLabel = null;
            _cachedInfoValue = 0;
            _cachedRangeDesc = null;
            _cachedEffectDesc = null;
            _cachedUseDescTitle = null;
            _cachedOpName = null;
            _cachedOpDesc = null;
        }

        #endregion

        #region Phase Detection

        /// <summary>
        /// Identifies the current phase by comparing the top-of-stack selector
        /// with our cached selector references.
        /// </summary>
        private int IdentifyPhase(UISelectorBase peekSelector)
        {
            if (peekSelector == null) return PHASE_NONE;
            if (peekSelector == (UISelectorBase)_menuSelector) return PHASE_MENU;
            if (peekSelector == (UISelectorBase)_itemSelector) return PHASE_ITEM;
            if (peekSelector == (UISelectorBase)_spellSelector) return PHASE_SPELL;
            if (peekSelector == (UISelectorBase)_targetSelector) return PHASE_TARGET;
            if (_tacticsSelector != null)
            {
                if (peekSelector == (UISelectorBase)_tacticsSelector)
                    return PHASE_TACTICS;
                // Operation sub-selector may be pushed onto the stack during operation selection
                var opSel = _tacticsSelector.operationListSelector;
                if (opSel != null && peekSelector == opSel.TryCast<UISelectorBase>())
                    return PHASE_TACTICS;
            }
            return PHASE_OTHER;
        }

        /// <summary>
        /// Handles transitions between phases — announces headings and resets state.
        /// </summary>
        private void HandlePhaseTransition(int newPhase, int oldPhase)
        {
            switch (newPhase)
            {
                case PHASE_MENU:
                    // Only announce heading when menu first opens (not when returning from sub-screen)
                    if (oldPhase < 0)
                        ScreenReader.Say(Loc.Get("battle_menu_heading"));
                    _lastMenuIndex = -1;
                    break;

                case PHASE_ITEM:
                    _lastItemIndex = -1;
                    _lastItemTab = -1;
                    ClearInfoCache();
                    break;

                case PHASE_SPELL:
                    _lastSpellIndex = -1;
                    _lastSpellTab = -1;
                    ClearInfoCache();
                    break;

                case PHASE_TARGET:
                    _lastTargetIndex = -1;
                    _lastTargetAllAnnounced = false;
                    break;

                case PHASE_TACTICS:
                    _lastTacticsCharIndex = -1;
                    _lastTacticsState = -1;
                    _lastTacticsOpIndex = -1;
                    _tacticsOpListBase = null;
        
                    _cachedOpName = null;
                    _cachedOpDesc = null;
                    ScreenReader.Say(Loc.Get("battle_menu_tactics_heading"));
                    break;
            }
        }

        private static void ClearInfoCache()
        {
            _cachedInfoLabel = null;
            _cachedInfoEffect = null;
            _cachedInfoRange = null;
            _cachedRangeDesc = null;
            _cachedEffectDesc = null;
        }

        #endregion

        #region Phase A: Root Menu

        private void PollRootMenu()
        {
            if (_menuSelector == null) return;

            int idx = _menuSelector.currentIndex;
            if (idx == _lastMenuIndex) return;
            _lastMenuIndex = idx;

            try
            {
                var dataList = _menuSelector.itemDataList;
                if (dataList == null || idx < 0 || idx >= dataList.Count) return;

                var item = dataList[idx];
                if (item == null) return;

                string name = item.menuName ?? "";
                int total = dataList.Count;

                if (item.canSelected)
                    ScreenReader.Say(Loc.Get("battle_menu_root_item", name, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("battle_menu_root_item_unavailable", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollRootMenu error: {ex.Message}");
            }
        }

        #endregion

        #region Phase B: Item Sub-menu

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

        #endregion

        #region Phase C: Spell Sub-menu

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
                        {
                            var tm = TextManager.Instance;
                            if (tm != null)
                            {
                                string resolved = tm.GetMessage(nameKey, TextManager.MessageType.System);
                                if (!string.IsNullOrEmpty(resolved))
                                    return resolved;
                            }
                            // Fallback: parse charaNameID
                            return TextUtil.ParseCharaNameID(nameKey);
                        }
                    }
                }

                return playerID.ToString();
            }
            catch { return "???"; }
        }

        #endregion

        #region Phase D: Target Selection

        private void PollTargetSelector()
        {
            if (_targetSelector == null) return;

            try
            {
                bool isAll = _targetSelector.isSelectedAll;
                bool isPlayer = _targetSelector.isSelectPlayer;

                // AoE: announce once
                if (isAll)
                {
                    if (!_lastTargetAllAnnounced)
                    {
                        _lastTargetAllAnnounced = true;
                        string skillName = ResolveUseDescTitle();

                        if (isPlayer)
                            ScreenReader.Say(Loc.Get("battle_menu_target_all_allies", skillName));
                        else
                            ScreenReader.Say(Loc.Get("battle_menu_target_all_enemies", skillName));
                    }
                    return;
                }

                if (isPlayer)
                    PollAllyTarget();
                else
                    PollEnemyTarget();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTargetSelector error: {ex.Message}");
            }
        }

        private void PollEnemyTarget()
        {
            var enemySel = _targetSelector.selectEnemySelector;
            if (enemySel == null) return;

            int idx = enemySel.currentIndex;
            if (idx == _lastTargetIndex && _lastTargetIsEnemy) return;
            _lastTargetIsEnemy = true;
            _lastTargetIndex = idx;

            try
            {
                var charList = enemySel.battleCharacterList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var bc = charList[idx];
                if (bc == null) return;

                string skillName = ResolveUseDescTitle();
                int total = charList.Count;

                // Resolve enemy name via BattleTargetHandler helpers
                var battleParam = bc.BattleCharacterParameter;
                var charParam = battleParam?.CharacterParameter;

                string enemyName = BattleTargetHandler.ResolveEnemyName(battleParam, charParam);
                enemyName = BattleTargetHandler.ResolveDuplicateName(bc, enemyName);

                // HP — exact if spectacled, percent otherwise
                if (charParam != null && charParam.HitPoint <= 0)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy",
                        skillName, enemyName, 0, idx + 1, total));
                }
                else if (BattleTargetHandler.IsEnemySpectacled(battleParam))
                {
                    int hp = charParam?.HitPoint ?? 0;
                    int hpMax = charParam?.HitPointMax ?? 1;
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy_exact",
                        skillName, enemyName, hp, hpMax, idx + 1, total));
                }
                else if (charParam != null)
                {
                    int hpMax = charParam.HitPointMax;
                    int pct = hpMax > 0 ? (charParam.HitPoint * 100 / hpMax) : 0;
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy",
                        skillName, enemyName, pct, idx + 1, total));
                }
                else
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_enemy_unknown",
                        skillName, enemyName, idx + 1, total));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollEnemyTarget error: {ex.Message}");
            }
        }

        private void PollAllyTarget()
        {
            int idx = _targetSelector.currentIndex;
            if (idx == _lastTargetIndex && !_lastTargetIsEnemy) return;
            _lastTargetIsEnemy = false;
            _lastTargetIndex = idx;

            try
            {
                var charList = _targetSelector.battleCharacterList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var bc = charList[idx];
                if (bc == null) return;

                string skillName = ResolveUseDescTitle();
                int total = charList.Count;

                var charParam = bc.BattleCharacterParameter?.CharacterParameter;
                string name = charParam?.CharacterName ?? "???";
                int hp = charParam?.HitPoint ?? 0;
                int hpMax = charParam?.HitPointMax ?? 0;
                int mp = charParam?.MentalPoint ?? 0;
                int mpMax = charParam?.MentalPointMax ?? 0;

                // Self-targeting (list has 1 entry)
                if (total == 1)
                {
                    ScreenReader.Say(Loc.Get("battle_menu_target_self", skillName));
                    return;
                }

                ScreenReader.Say(Loc.Get("battle_menu_target_ally",
                    skillName, name, hp, hpMax, mp, mpMax, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollAllyTarget error: {ex.Message}");
            }
        }

        /// <summary>Gets the skill/item name for target announcements.</summary>
        private string ResolveUseDescTitle()
        {
            // First try hook cache
            if (!string.IsNullOrEmpty(_cachedUseDescTitle))
                return _cachedUseDescTitle;

            // Fallback: read directly from presenter
            try
            {
                var presenter = _targetSelector?.useDescriptionPresenter;
                if (presenter != null)
                {
                    var titleText = presenter.title;
                    if (titleText != null)
                    {
                        string text = ((Il2CppTMPro.TMP_Text)titleText)?.text;
                        if (!string.IsNullOrEmpty(text))
                            return TextUtil.StripTags(text);
                    }
                }
            }
            catch { }

            // Last fallback: check the info cache label
            return _cachedInfoLabel ?? "";
        }

        #endregion

        #region Phase E: Tactics

        private void PollTacticsSelector()
        {
            if (_tacticsSelector == null) return;

            try
            {
                int state = (int)_tacticsSelector.currentState;

                // State transition
                if (state != _lastTacticsState)
                {
                    _lastTacticsState = state;
                    _lastTacticsCharIndex = -1;
                    _lastTacticsOpIndex = -1;
                    _tacticsOpListBase = null;
        
                }

                if (state == 0) // SelectCharacter
                    PollTacticsCharacter();
                else if (state == 1) // SelectOperation
                    PollTacticsOperation();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsSelector error: {ex.Message}");
            }
        }

        private void PollTacticsCharacter()
        {
            int idx = _tacticsSelector.currentIndex;
            if (idx == _lastTacticsCharIndex) return;
            _lastTacticsCharIndex = idx;

            try
            {
                var charList = _tacticsSelector.characterDataList;
                if (charList == null || idx < 0 || idx >= charList.Count) return;

                var charData = charList[idx];
                if (charData == null) return;

                string name = charData.characterName ?? "???";
                string operation = charData.operation ?? "";
                int total = charList.Count;

                ScreenReader.Say(Loc.Get("battle_menu_tactics_char",
                    name, operation, idx + 1, total));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsCharacter error: {ex.Message}");
            }
        }

        private void PollTacticsOperation()
        {
            // Lazy-find the operation list
            if (_tacticsOpListBase == null)
            {
                _tacticsOpListBase = _tacticsSelector.operationListSelector?.TryCast<UIListSelectorBase>();
                if (_tacticsOpListBase == null) return;
            }

            int idx = _tacticsOpListBase.currentIndex;
            if (idx == _lastTacticsOpIndex) return;
            _lastTacticsOpIndex = idx;

            try
            {
                // Read operation name from the list item presenter's displayed text.
                // Each UIBattleOperationListItemPresenter has an operationName GameText
                // that's already populated by the game for visible items.
                string opName = "";

                // Try hook cache first
                if (!string.IsNullOrEmpty(_cachedOpName))
                {
                    opName = _cachedOpName;
                }
                else
                {
                    // Read from the operation list item presenters on the UI.
                    // The battle tactics uses UICommonListItemPresenter (via UIOperationListItemPresenter)
                    // which has a textMesh GameText field containing the operation name.
                    try
                    {
                        var opListPresenter = _tacticsSelector.operationListPresenter;
                        if (opListPresenter != null)
                        {
                            var presenters = opListPresenter.gameObject
                                .GetComponentsInChildren<UICommonListItemPresenter>();
                            if (presenters != null && idx >= 0 && idx < presenters.Count)
                            {
                                var nameText = presenters[idx].textMesh;
                                if (nameText != null)
                                    opName = ((Il2CppTMPro.TMP_Text)nameText)?.text ?? "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState($"BattleMenuHandler: op presenter read error: {ex.Message}");
                    }
                }

                opName = TextUtil.StripTags(opName).Trim();

                var dataList = _tacticsOpListBase.currentDataList;
                int total = dataList?.Count ?? 0;

                // Check if this is the currently set operation
                bool isCurrent = false;
                if (dataList != null && idx >= 0 && idx < dataList.Count)
                {
                    var opData = dataList[idx]?.TryCast<UIOperationListItemData>();
                    if (opData != null)
                        isCurrent = opData.isSetting;
                }

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(opName))
                    sb.Append(opName).Append(". ");
                if (isCurrent)
                    sb.Append(Loc.Get("battle_menu_tactics_current")).Append(" ");
                if (total > 0 && idx >= 0)
                    sb.Append(Loc.Get("battle_menu_position", idx + 1, total));

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattleMenuHandler.PollTacticsOperation error: {ex.Message}");
            }
        }

        #endregion
    }
}
