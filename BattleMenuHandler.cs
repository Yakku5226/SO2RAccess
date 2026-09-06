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
    /// Covers six sub-screens:
    ///   Phase A: Root menu (Item / Battle Skill / Tactics / Escape)
    ///   Phase B: Item sub-menu (Recovery / Combat tabs, item details)
    ///   Phase C: Spell/skill sub-menu (per-character tabs, skill details)
    ///   Phase D: Target selection (enemy or ally targeting after skill/item pick)
    ///   Phase E: Tactics (per-character strategy assignment)
    ///   Phase F: Operation quick list — the "change command" shortcut
    ///            (keyboard R / Square) opens this standalone strategy list;
    ///            one entry transitions into the full tactics screen (Phase E).
    ///
    /// All navigation is native C++ (CallerCount 0) — uses polling pattern.
    /// Data capture via Harmony postfixes on info panel presenters.
    /// </summary>
    public partial class BattleMenuHandler
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
        private UIBattleOperationSelector _operationSelector;
        private int _findCooldown;

        // Phase detection via GetPeekSelector() — identifies active sub-screen
        private const int PHASE_NONE = 0;
        private const int PHASE_MENU = 1;
        private const int PHASE_ITEM = 2;
        private const int PHASE_SPELL = 3;
        private const int PHASE_TARGET = 4;
        private const int PHASE_TACTICS = 5;
        private const int PHASE_OPERATION = 6;
        private const int PHASE_OTHER = 99;
        private int _lastPhase = -1;
        private bool _wasWindowOpen;

        // Debug-log latches: these two conditions hold for hundreds of frames in a
        // row (the whole battle result screen, for one), and MelonLogger writes to
        // the console and file synchronously — logging them every frame cost real
        // frame time while F12 was on (5,700 + 1,600 lines in one fight, 2026-09-06).
        private bool _loggedOperationSelectorWhileClosed;
        private string _loggedUnhandledSelector;

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

        // Phase F: Operation quick-list polling ("change command" shortcut)
        private int _lastOperationIndex = -1;

        // Hook data caches (static — Harmony postfixes write here)
        private static string _cachedInfoLabel;
        private static string _cachedInfoEffect;
        private static string _cachedInfoRange;
        private static string _cachedOpName;
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
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleOperationSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleOperationListItemData).TypeHandle);

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
            _operationSelector = null;
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
                    _operationSelector = _battleWindow.operationSelector;
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
                    // Diagnostic: the "change command" shortcut screen should
                    // open the battle window; if it ever shows while the window
                    // reports closed, this pinpoints why Phase F stays silent.
                    if (Main.DebugMode && _operationSelector != null &&
                        _operationSelector.gameObject.activeInHierarchy)
                    {
                        if (!_loggedOperationSelectorWhileClosed)
                        {
                            _loggedOperationSelectorWhileClosed = true;
                            DebugLogger.LogState(
                                "BattleMenu: operation selector active but window IsOpened=false.");
                        }
                    }
                    else
                    {
                        _loggedOperationSelectorWhileClosed = false;
                    }
                    return;
                }
                _wasWindowOpen = true;
                _loggedOperationSelectorWhileClosed = false;

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
                    case PHASE_OPERATION: PollOperationSelector(); break;
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
            _loggedUnhandledSelector = null;
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
            _lastOperationIndex = -1;
        }

        private static void ClearHookCaches()
        {
            _cachedInfoLabel = null;
            _cachedInfoEffect = null;
            _cachedInfoRange = null;
            _cachedRangeDesc = null;
            _cachedEffectDesc = null;
            _cachedUseDescTitle = null;
            _cachedOpName = null;
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
            // Standalone strategy quick list — the "change command" shortcut
            // (keyboard R / Square) opens this without going through the root menu.
            if (_operationSelector != null &&
                peekSelector == (UISelectorBase)_operationSelector)
                return PHASE_OPERATION;

            string unhandled = peekSelector.GetIl2CppType()?.Name;
            if (unhandled != _loggedUnhandledSelector)
            {
                _loggedUnhandledSelector = unhandled;
                DebugLogger.LogState($"BattleMenu: unhandled selector on stack: {unhandled}");
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
                    // No heading when arriving from the operation quick list —
                    // the user already heard "Strategy." there and the
                    // per-character announcement follows immediately.
                    if (oldPhase != PHASE_OPERATION)
                        ScreenReader.Say(Loc.Get("battle_menu_tactics_heading"));
                    break;

                case PHASE_OPERATION:
                    _lastOperationIndex = -1;
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

    }
}
