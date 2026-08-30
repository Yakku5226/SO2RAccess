using Il2CppGame;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces battle pause menu information via screen reader in a tiered system.
    ///
    /// When the player presses Start/Options during battle, a pause screen shows
    /// detailed status for all characters (party + enemies). This handler detects
    /// the pause menu, polls character selection, and announces info in tiers:
    ///   Tier 0: Basic info (name, HP, MP, position) — auto on character select
    ///   Tier 1+: Weaknesses, resistances, status, equipment, cooking, music, leader
    ///
    /// Tier cycling: L1/R1 (gamepad) or minus/equals (keyboard; no wrap).
    /// Character cycling: game's own D-pad left/right, or brackets (keyboard; wraps).
    ///
    /// Detection: polling GameUIManager.IsShowingBattlePause() + currentCharacterIndex.
    /// Data capture: Harmony postfixes on UIBattlePauseCharacterPresenter hooks.
    /// </summary>
    public class BattlePauseHandler
    {
        #region Fields

        private bool _patchesApplied;

        // References (lazy-found, reset on scene change)
        private UIBattleWindow _battleWindow;
        private UIBattlePauseSelector _pauseSelector;
        private int _findCooldown;

        // Pause state
        private bool _isPauseOpen;

        /// <summary>Whether the battle pause menu is currently open.</summary>
        public bool IsPauseOpen => _isPauseOpen;

        // Character polling
        private int _lastCharIndex = -1;
        private bool _characterChangePending;

        // Tier state
        private int _currentTier;
        private List<TierData> _tiers;

        // Icon category map (built once per pause open)
        private Dictionary<IntPtr, string> _iconCategoryMap;

        // Hook data caches (static, written by hook postfixes)
        private static int _cachedHp, _cachedHpMax;
        private static bool _cachedIsEnemy;
        private static int _cachedMp, _cachedMpMax;
        private static List<CachedElemental> _cachedElementals;
        private static List<CachedBuff> _cachedAllBuffs;
        private static string _cachedTargetName;

        private struct TierData
        {
            public string Label;
            public string Content;
        }

        private struct CachedElemental
        {
            public string Resistance;
            public ElementResistanceType Type;
        }

        private struct CachedBuff
        {
            public string Description;
            public IntPtr IconPtr;
        }

        #endregion

        #region Patches

        /// <summary>
        /// Registers Harmony postfixes on UIBattlePauseCharacterPresenter methods
        /// to capture HP, MP, elemental, and buff data when the game populates them.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIBattlePauseCharacterPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattlePauseSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(GameUIManager).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIElementalData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattlePauseAllBuffData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattlePauseBuffDebuffListListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIDefine).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleCharacter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CharacterParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleEnemyParameter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ElementResistanceType).TypeHandle);

                // Hook SetHp — CallerCount(3)
                var setHp = AccessTools.Method(typeof(UIBattlePauseCharacterPresenter),
                    nameof(UIBattlePauseCharacterPresenter.SetHp));
                if (setHp != null)
                {
                    harmony.Patch(setHp,
                        postfix: new HarmonyMethod(typeof(BattlePauseHandler),
                            nameof(SetHp_Postfix)));
                }

                // Hook SetMp — CallerCount(1)
                var setMp = AccessTools.Method(typeof(UIBattlePauseCharacterPresenter),
                    nameof(UIBattlePauseCharacterPresenter.SetMp));
                if (setMp != null)
                {
                    harmony.Patch(setMp,
                        postfix: new HarmonyMethod(typeof(BattlePauseHandler),
                            nameof(SetMp_Postfix)));
                }

                // Hook SetElemental — CallerCount(2)
                var setElemental = AccessTools.Method(typeof(UIBattlePauseCharacterPresenter),
                    nameof(UIBattlePauseCharacterPresenter.SetElemental));
                if (setElemental != null)
                {
                    harmony.Patch(setElemental,
                        postfix: new HarmonyMethod(typeof(BattlePauseHandler),
                            nameof(SetElemental_Postfix)));
                }

                // Hook SetAllBuffList — CallerCount(1)
                var setAllBuff = AccessTools.Method(typeof(UIBattlePauseCharacterPresenter),
                    nameof(UIBattlePauseCharacterPresenter.SetAllBuffList));
                if (setAllBuff != null)
                {
                    harmony.Patch(setAllBuff,
                        postfix: new HarmonyMethod(typeof(BattlePauseHandler),
                            nameof(SetAllBuffList_Postfix)));
                }

                // Hook SetTargetName — CallerCount(0), called from native code
                // to set the character's displayed name in the pause presenter.
                var setTargetName = AccessTools.Method(typeof(UIBattlePauseCharacterPresenter),
                    nameof(UIBattlePauseCharacterPresenter.SetTargetName));
                if (setTargetName != null)
                {
                    harmony.Patch(setTargetName,
                        postfix: new HarmonyMethod(typeof(BattlePauseHandler),
                            nameof(SetTargetName_Postfix)));
                }

                _patchesApplied = true;
                MelonLogger.Msg("BattlePauseHandler: initialized.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattlePauseHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Hook Postfixes

        private static void SetHp_Postfix(int hp, int hpMax, bool isEnemy)
        {
            try
            {
                _cachedHp = hp;
                _cachedHpMax = hpMax;
                _cachedIsEnemy = isEnemy;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause.SetHp_Postfix error: {ex.Message}");
            }
        }

        private static void SetMp_Postfix(int mp, int mpMax)
        {
            try
            {
                _cachedMp = mp;
                _cachedMpMax = mpMax;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause.SetMp_Postfix error: {ex.Message}");
            }
        }

        private static void SetElemental_Postfix(
            Il2CppSystem.Collections.Generic.List<UIElementalData> dataList)
        {
            try
            {
                var managed = new List<CachedElemental>();
                if (dataList != null)
                {
                    for (int i = 0; i < dataList.Count; i++)
                    {
                        var item = dataList[i];
                        if (item == null) continue;
                        managed.Add(new CachedElemental
                        {
                            Resistance = item.resistance ?? "",
                            Type = item.type
                        });
                    }
                }
                _cachedElementals = managed;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause.SetElemental_Postfix error: {ex.Message}");
            }
        }

        private static void SetAllBuffList_Postfix(
            Il2CppSystem.Collections.Generic.List<UIBattlePauseAllBuffData> dataList)
        {
            try
            {
                var managed = new List<CachedBuff>();
                if (dataList != null)
                {
                    for (int i = 0; i < dataList.Count; i++)
                    {
                        var item = dataList[i];
                        if (item == null) continue;
                        var icon = item.buffIcon;
                        managed.Add(new CachedBuff
                        {
                            Description = item.buffDescription ?? "",
                            IconPtr = icon != null ? icon.Pointer : IntPtr.Zero
                        });
                    }
                }
                _cachedAllBuffs = managed;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause.SetAllBuffList_Postfix error: {ex.Message}");
            }
        }

        private static void SetTargetName_Postfix(string name)
        {
            try
            {
                _cachedTargetName = name ?? "";
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause.SetTargetName_Postfix error: {ex.Message}");
            }
        }

        #endregion

        #region Scene Change

        /// <summary>
        /// Resets all state on scene change.
        /// </summary>
        public void OnSceneChanged()
        {
            _battleWindow = null;
            _pauseSelector = null;
            _isPauseOpen = false;
            _lastCharIndex = -1;
            _findCooldown = 0;
            _tiers = null;
            _iconCategoryMap = null;
            _characterChangePending = false;
            _currentTier = 0;
            ClearHookCaches();
        }

        private static void ClearHookCaches()
        {
            _cachedHp = 0;
            _cachedHpMax = 0;
            _cachedIsEnemy = false;
            _cachedMp = 0;
            _cachedMpMax = 0;
            _cachedElementals = null;
            _cachedAllBuffs = null;
            _cachedTargetName = null;
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Detects pause open/close and polls character selection.
        /// </summary>
        public void Update()
        {
            // Step 1: Find battle window (throttled)
            if (_battleWindow == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _battleWindow = UnityEngine.Object.FindObjectOfType<UIBattleWindow>();
                    if (_battleWindow != null)
                    {
                        _pauseSelector = _battleWindow.pauseSelector;
                        DebugLogger.LogState("BattlePause: cached UIBattleWindow reference.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"BattlePause: find error: {ex.Message}");
                }
                return;
            }

            // Step 2: Detect pause open/close
            try
            {
                var guiMgr = GameUIManager.Instance;
                if (guiMgr == null) return;

                bool showing = guiMgr.IsShowingBattlePause();

                if (showing && !_isPauseOpen)
                {
                    // Pause just opened
                    _isPauseOpen = true;
                    _currentTier = 0;
                    _tiers = null;

                    // Seed character index to current
                    if (_pauseSelector != null)
                        _lastCharIndex = _pauseSelector.currentCharacterIndex;

                    BuildIconCategoryMap();

                    ScreenReader.Say(Loc.Get("battle_pause_heading"));
                    DebugLogger.LogState("BattlePause: opened.");

                    // 1-frame delay to let game finish populating presenters
                    _characterChangePending = true;
                    return;
                }

                if (!showing && _isPauseOpen)
                {
                    // Pause just closed
                    _isPauseOpen = false;
                    _lastCharIndex = -1;
                    _tiers = null;
                    _currentTier = 0;
                    _iconCategoryMap = null;
                    ClearHookCaches();
                    DebugLogger.LogState("BattlePause: closed.");
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: detection error: {ex.Message}");
                _battleWindow = null;
                _pauseSelector = null;
                _isPauseOpen = false;
                _findCooldown = 0;
                return;
            }

            if (!_isPauseOpen) return;
            if (_pauseSelector == null) return;

            // Step 3: Handle pending character change (1-frame delay for game setup)
            if (_characterChangePending)
            {
                _characterChangePending = false;
                ClearHookCaches();
                RefreshPauseUI();
                BuildTiers();
                AnnounceTier();
                return;
            }

            // Step 4: Poll character index (game cycles characters natively via D-pad)
            try
            {
                int idx = _pauseSelector.currentCharacterIndex;
                if (idx != _lastCharIndex)
                {
                    _lastCharIndex = idx;
                    _currentTier = 0;
                    ClearHookCaches();
                    RefreshPauseUI();
                    BuildTiers();
                    AnnounceTier();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: poll error: {ex.Message}");
            }
        }

        #endregion

        #region Icon Category Map

        /// <summary>
        /// Builds a mapping of sprite pointer -> category name for buff categorization.
        /// Called once when pause opens.
        /// </summary>
        private void BuildIconCategoryMap()
        {
            _iconCategoryMap = new Dictionary<IntPtr, string>();
            if (_pauseSelector == null) return;

            try
            {
                // Equipment stat buff icons
                MapIcon(UIDefine.PauseBuffDebuffIcon.AtkUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.DefUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.IntUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.AvdUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.CrtUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.GutsUp, "equipment");
                MapIcon(UIDefine.PauseBuffDebuffIcon.SpdUp, "equipment");

                // Special category icons
                MapIcon(UIDefine.PauseBuffDebuffIcon.FoodBuff, "cooking");
                MapIcon(UIDefine.PauseBuffDebuffIcon.MusicBuff, "music");
                MapIcon(UIDefine.PauseBuffDebuffIcon.LeaderEnemy, "leader");

                // Status condition icons (skip in buff tiers — covered by conditions tier)
                MapIcon(UIDefine.PauseBuffDebuffIcon.Confusion, "conditions");
                MapIcon(UIDefine.PauseBuffDebuffIcon.SpdDown, "conditions");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: icon map error: {ex.Message}");
            }
        }

        private void MapIcon(UIDefine.PauseBuffDebuffIcon iconType, string category)
        {
            try
            {
                var sprite = _pauseSelector.GetIconSprite(iconType);
                if (sprite != null && sprite.Pointer != IntPtr.Zero)
                {
                    _iconCategoryMap[sprite.Pointer] = category;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: MapIcon({iconType}) error: {ex.Message}");
            }
        }

        #endregion

        #region Tier Building

        /// <summary>
        /// Builds the list of non-empty tiers for the current character.
        /// Tier 0 is always basic info. Subsequent tiers are added only if non-empty.
        /// </summary>
        private void BuildTiers()
        {
            _tiers = new List<TierData>();

            try
            {
                var charList = _pauseSelector.battleCharacterList;
                if (charList == null || _lastCharIndex < 0 || _lastCharIndex >= charList.Count)
                    return;

                var battleChar = charList[_lastCharIndex];
                if (battleChar == null) return;

                int total = charList.Count;
                int pos = _lastCharIndex + 1;
                bool isEnemy = !battleChar.IsPlayer();

                // Tier 0: Basic info (always present)
                string basicContent = BuildBasicTier(battleChar, isEnemy, pos, total);
                _tiers.Add(new TierData { Label = null, Content = basicContent });

                // Tier: Weaknesses (elements with DOUBLE damage)
                string weaknesses = BuildWeaknessesTier();
                if (!string.IsNullOrEmpty(weaknesses))
                {
                    _tiers.Add(new TierData
                    {
                        Label = Loc.Get("battle_pause_weaknesses"),
                        Content = weaknesses
                    });
                }

                // Tier: Resistances (HALF, DISABLE, ABSORB)
                string resistances = BuildResistancesTier();
                if (!string.IsNullOrEmpty(resistances))
                {
                    _tiers.Add(new TierData
                    {
                        Label = Loc.Get("battle_pause_resistances"),
                        Content = resistances
                    });
                }

                // Tier: Status conditions
                string conditions = BuildStatusConditionsTier(battleChar);
                if (!string.IsNullOrEmpty(conditions))
                {
                    _tiers.Add(new TierData
                    {
                        Label = Loc.Get("battle_pause_conditions"),
                        Content = conditions
                    });
                }

                // Tiers: Equipment, cooking, music, leader (from buff category map)
                BuildBuffCategoryTiers();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: BuildTiers error: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds tier 0: name, HP/MP, position.
        /// Reads HP/MP directly from CharacterParameter for reliability.
        /// </summary>
        private string BuildBasicTier(BattleCharacter battleChar, bool isEnemy,
            int pos, int total)
        {
            try
            {
                var charParam = battleChar.BattleCharacterParameter?.CharacterParameter;

                // Resolve name
                string name;
                if (isEnemy)
                {
                    var bp = battleChar.BattleCharacterParameter;
                    var cp = bp?.CharacterParameter;
                    if (bp != null && cp != null)
                    {
                        string baseName = BattleTargetHandler.ResolveEnemyName(bp, cp);
                        name = BattleTargetHandler.ResolveDuplicateName(battleChar, baseName);
                    }
                    else
                    {
                        name = "Enemy";
                    }
                }
                else
                {
                    name = ResolveAllyName(battleChar, charParam);
                }

                // Read HP/MP directly from CharacterParameter
                int hp = charParam?.HitPoint ?? 0;
                int hpMax = charParam?.HitPointMax ?? 0;

                DebugLogger.LogState($"BattlePause: char[{pos-1}] isEnemy={isEnemy} name='{name}' hp={hp}/{hpMax} charParam={charParam != null} battleParam={battleChar.BattleCharacterParameter != null}");

                // Check defeated
                if (hp <= 0 && hpMax > 0)
                {
                    return Loc.Get("battle_pause_defeated", name, pos, total);
                }

                // Ally: Name, HP X of Y, MP X of Y, position
                if (!isEnemy)
                {
                    int mp = charParam?.MentalPoint ?? 0;
                    int mpMax = charParam?.MentalPointMax ?? 0;
                    return Loc.Get("battle_pause_ally",
                        name, hp, hpMax, mp, mpMax, pos, total);
                }

                // Enemy: check spectacles
                var battleParam = battleChar.BattleCharacterParameter;
                bool spectacled = battleParam != null &&
                    BattleTargetHandler.IsEnemySpectacled(battleParam);

                if (spectacled && hpMax > 0)
                {
                    return Loc.Get("battle_pause_enemy",
                        name, hp, hpMax, pos, total);
                }

                return Loc.Get("battle_pause_enemy_unknown", name, pos, total);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: BuildBasicTier error: {ex.Message}");
                return $"Character {pos} of {total}.";
            }
        }

        /// <summary>
        /// Resolves ally name using multiple fallbacks:
        /// 1. SetTargetName hook cache (native caller sets displayed name)
        /// 2. CharacterParameter.CharacterName (direct property)
        /// 3. ParameterManager chain via BattlePlayerParameter → charaNameID
        /// 4. "Ally" fallback
        /// </summary>
        private string ResolveAllyName(BattleCharacter battleChar,
            CharacterParameter charParam)
        {
            // Try hooked name first (SetTargetName fires from native code)
            if (!string.IsNullOrEmpty(_cachedTargetName))
            {
                DebugLogger.LogState($"BattlePause: ally name from hook: '{_cachedTargetName}'");
                return TextUtil.StripTags(_cachedTargetName);
            }

            // Try direct CharacterParameter property
            string cpName = charParam?.CharacterName;
            if (!string.IsNullOrEmpty(cpName))
            {
                DebugLogger.LogState($"BattlePause: ally name from CharacterName: '{cpName}'");
                return TextUtil.StripTags(cpName);
            }

            // Try ParameterManager chain: BattlePlayerParameter → charaNameID → TextManager
            try
            {
                var bp = battleChar.BattleCharacterParameter;
                var playerParam = bp?.TryCast<BattlePlayerParameter>();
                if (playerParam != null)
                {
                    var constPlayer = playerParam.PlayerParameter;
                    if (constPlayer != null)
                    {
                        string nameKey = constPlayer.charaNameID;
                        DebugLogger.LogState($"BattlePause: ally charaNameID='{nameKey}'");
                        if (!string.IsNullOrEmpty(nameKey))
                            return TextUtil.ResolveCharaNameKey(nameKey);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: ally name ParameterManager error: {ex.Message}");
            }

            DebugLogger.LogState("BattlePause: ally name fallback to 'Ally'");
            return "Ally";
        }

        /// <summary>
        /// Builds weaknesses tier from cached elemental data.
        /// Filters for DOUBLE resistance type.
        /// </summary>
        private string BuildWeaknessesTier()
        {
            if (_cachedElementals == null || _cachedElementals.Count == 0)
                return null;

            var parts = new List<string>();
            foreach (var elem in _cachedElementals)
            {
                if (elem.Type == ElementResistanceType.DOUBLE)
                {
                    parts.Add(Loc.Get("battle_pause_elem_double", elem.Resistance));
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>
        /// Builds resistances tier from cached elemental data.
        /// Filters for HALF, DISABLE, ABSORB resistance types.
        /// </summary>
        private string BuildResistancesTier()
        {
            if (_cachedElementals == null || _cachedElementals.Count == 0)
                return null;

            var parts = new List<string>();
            foreach (var elem in _cachedElementals)
            {
                switch (elem.Type)
                {
                    case ElementResistanceType.HALF:
                        parts.Add(Loc.Get("battle_pause_elem_half", elem.Resistance));
                        break;
                    case ElementResistanceType.DISABLE:
                        parts.Add(Loc.Get("battle_pause_elem_immune", elem.Resistance));
                        break;
                    case ElementResistanceType.ABSORB:
                        parts.Add(Loc.Get("battle_pause_elem_absorb", elem.Resistance));
                        break;
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>
        /// Builds status conditions tier from the character's buff/debuff list.
        /// Reuses BattleTargetHandler's buff resolution logic.
        /// </summary>
        private string BuildStatusConditionsTier(BattleCharacter battleChar)
        {
            try
            {
                var charParam = battleChar.BattleCharacterParameter?.CharacterParameter;
                if (charParam == null) return null;

                string statusStr = BattleTargetHandler.ResolveBuffDebuffs(charParam);
                // ResolveBuffDebuffs wraps in Loc format — extract the inner text
                // by calling it and stripping the trailing period from the Loc wrapper.
                // Actually, ResolveBuffDebuffs returns null if empty, or "X." formatted.
                // For the tier system we want the raw comma-separated list.
                // Let's build our own from the raw buff list.

                var buffList = charParam.GetBuffDebuffList(BuffDebuffType.INVALID);
                if (buffList == null || buffList.Count == 0) return null;

                var tm = TextManager.Instance;
                var names = new List<string>();

                for (int i = 0; i < buffList.Count; i++)
                {
                    var id = buffList[i];
                    if (id == BuffDebuffID.BREAK || id == BuffDebuffID.INVALID) continue;

                    string msgId = id.ToMessageID();
                    if (string.IsNullOrEmpty(msgId)) continue;

                    string resolved = tm?.GetMessage(msgId, TextManager.MessageType.System);
                    if (!string.IsNullOrEmpty(resolved))
                        names.Add(TextUtil.StripTags(resolved));
                    else
                        names.Add(id.ToString());
                }

                return names.Count > 0 ? string.Join(", ", names) : null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: BuildStatusConditionsTier error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds buff category tiers (equipment, cooking, music, leader) from
        /// cached buff data by matching icon sprites to category map.
        /// </summary>
        private void BuildBuffCategoryTiers()
        {
            if (_cachedAllBuffs == null || _cachedAllBuffs.Count == 0) return;
            if (_iconCategoryMap == null || _iconCategoryMap.Count == 0) return;

            var equipment = new List<string>();
            var cooking = new List<string>();
            var music = new List<string>();
            var leader = new List<string>();

            foreach (var buff in _cachedAllBuffs)
            {
                if (string.IsNullOrEmpty(buff.Description)) continue;

                string category = "equipment"; // default if icon not mapped
                if (buff.IconPtr != IntPtr.Zero)
                {
                    _iconCategoryMap.TryGetValue(buff.IconPtr, out category);
                    if (category == null) category = "equipment";
                }

                // Skip conditions — already in status conditions tier
                if (category == "conditions") continue;

                string desc = TextUtil.StripTags(buff.Description);
                switch (category)
                {
                    case "equipment": equipment.Add(desc); break;
                    case "cooking": cooking.Add(desc); break;
                    case "music": music.Add(desc); break;
                    case "leader": leader.Add(desc); break;
                }
            }

            if (equipment.Count > 0)
            {
                _tiers.Add(new TierData
                {
                    Label = Loc.Get("battle_pause_equipment"),
                    Content = string.Join(", ", equipment)
                });
            }
            if (cooking.Count > 0)
            {
                _tiers.Add(new TierData
                {
                    Label = Loc.Get("battle_pause_cooking"),
                    Content = string.Join(", ", cooking)
                });
            }
            if (music.Count > 0)
            {
                _tiers.Add(new TierData
                {
                    Label = Loc.Get("battle_pause_music"),
                    Content = string.Join(", ", music)
                });
            }
            if (leader.Count > 0)
            {
                _tiers.Add(new TierData
                {
                    Label = Loc.Get("battle_pause_leader"),
                    Content = string.Join(", ", leader)
                });
            }
        }

        #endregion

        #region Tier Cycling

        /// <summary>
        /// Moves to the previous (higher priority) tier. No wrap — stays at tier 0.
        /// </summary>
        public void TierUp()
        {
            if (!_isPauseOpen || _tiers == null || _tiers.Count == 0) return;
            if (_currentTier <= 0) return;

            _currentTier--;
            AnnounceTier();
        }

        /// <summary>
        /// Moves to the next (lower priority) tier. No wrap — stays at last tier.
        /// </summary>
        public void TierDown()
        {
            if (!_isPauseOpen || _tiers == null || _tiers.Count == 0) return;
            if (_currentTier >= _tiers.Count - 1) return;

            _currentTier++;
            AnnounceTier();
        }

        /// <summary>
        /// Announces the current tier's information.
        /// Tier 0 uses its own format (no label/position).
        /// Other tiers use "Label: Content. X of Y." format.
        /// </summary>
        private void AnnounceTier()
        {
            if (_tiers == null || _currentTier < 0 || _currentTier >= _tiers.Count) return;

            var tier = _tiers[_currentTier];

            if (_currentTier == 0)
            {
                // Tier 0: basic info — content is the full announcement
                ScreenReader.Say(tier.Content);
                DebugLogger.LogState($"BattlePause: tier 0 — {tier.Content}");
            }
            else
            {
                // Other tiers: "Label: Content. X of Y."
                string msg = Loc.Get("battle_pause_tier",
                    tier.Label, tier.Content,
                    _currentTier + 1, _tiers.Count);
                ScreenReader.Say(msg);
                DebugLogger.LogState($"BattlePause: tier {_currentTier} — {msg}");
            }
        }

        #endregion

        #region Character Cycling

        /// <summary>
        /// Cycles to the previous character (wraps from first to last).
        /// Used for the keyboard character-cycle key and as a gamepad fallback.
        /// </summary>
        public void CycleCharacterLeft() => CycleCharacter(-1);

        /// <summary>
        /// Cycles to the next character (wraps from last to first).
        /// Used for the keyboard character-cycle key and as a gamepad fallback.
        /// </summary>
        public void CycleCharacterRight() => CycleCharacter(+1);

        /// <summary>
        /// Moves the pause-menu character selection by <paramref name="delta"/>
        /// (wrapping at both ends), then refreshes the UI and re-announces.
        /// </summary>
        private void CycleCharacter(int delta)
        {
            if (!_isPauseOpen || _pauseSelector == null) return;

            try
            {
                var charList = _pauseSelector.battleCharacterList;
                if (charList == null || charList.Count <= 1) return;

                int count = charList.Count;
                int newIdx = (_pauseSelector.currentCharacterIndex + delta + count) % count;

                _pauseSelector.currentCharacterIndex = newIdx;
                _lastCharIndex = newIdx;
                _currentTier = 0;
                ClearHookCaches();
                RefreshPauseUI();
                BuildTiers();
                AnnounceTier();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: CycleCharacter({delta}) error: {ex.Message}");
            }
        }

        /// <summary>
        /// Calls the game's update methods to refresh the UI display after
        /// programmatically changing the character index.
        /// </summary>
        private void RefreshPauseUI()
        {
            try { _pauseSelector.UpdateCharacterPresenter(); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: UpdateCharacterPresenter error: {ex.Message}");
            }

            try { _pauseSelector.UpdateBuffDebuffList(); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: UpdateBuffDebuffList error: {ex.Message}");
            }

            try { _pauseSelector.UpdateBonusEffect(); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BattlePause: UpdateBonusEffect error: {ex.Message}");
            }
        }

        #endregion
    }
}
