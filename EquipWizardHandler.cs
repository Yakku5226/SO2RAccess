using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces equipment wizard navigation to the screen reader.
    ///
    /// The equipment wizard appears automatically when the player acquires new
    /// equipment that is better than what a party member currently has. It is
    /// hosted on UISystemWindow (not UICampWindow) and presents a Yes/No/Reject All
    /// menu for each character with suggested equipment changes.
    ///
    /// Detection: UISystemWindow found via FindObjectOfType (lazy init with throttle),
    /// IsShowingEquipWizard polled each frame to detect open/close transitions.
    ///
    /// All navigation in this menu is native C++ — no Harmony hooks fire for
    /// up/down. Polling is the correct approach (same pattern as game over menu).
    /// </summary>
    public class EquipWizardHandler
    {
        #region Fields

        private bool _patchesApplied;

        private static UISystemWindow _window;
        private static int _findCooldown;

        private static bool _isShowing;
        private static UIEquipWizardSelector _selector;
        private static UIListSelectorBase _selectorBase;
        private static int _lastMenuIndex = -1;
        private static int _lastDataIndex = -1;

        // Menu option names by index — matches UIEquipWizardSelector.Menu enum order.
        private static string[] _menuNames;

        #endregion

        #region Patches

        /// <summary>
        /// Initializes IL2CPP types for runtime access. No Harmony hooks needed —
        /// all announcements are polling-based.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UISystemWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipWizardSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipWizardPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(EquipWizardData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipWithFactorListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIEquipListItemData).TypeHandle);

                _patchesApplied = true;
                MelonLogger.Msg("[EQUIPWIZARD] Patches applied (polling-only).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EQUIPWIZARD] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Called on scene change to reset cached references.
        /// </summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            _isShowing = false;
            _selector = null;
            _selectorBase = null;
            _lastMenuIndex = -1;
            _lastDataIndex = -1;
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Detects equipment wizard open/close and polls navigation.
        /// </summary>
        public void Update()
        {
            DetectWindow();
            if (!_isShowing) return;

            UpdateMenu();
        }

        /// <summary>
        /// Finds UISystemWindow lazily (throttled) and polls IsShowingEquipWizard
        /// to detect open/close transitions.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    _window = UnityEngine.Object.FindObjectOfType<UISystemWindow>();
                    if (_window != null)
                        DebugLogger.LogState("EquipWizard: cached UISystemWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"EquipWizard detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                bool showing = _window.IsShowingEquipWizard;

                if (showing && !_isShowing)
                {
                    _isShowing = true;
                    _selector = _window.equipWizardSelector;
                    _selectorBase = _selector?.TryCast<UIListSelectorBase>();
                    _lastMenuIndex = -1;
                    _lastDataIndex = -1;

                    DebugLogger.LogState("EquipWizard: opened.");
                    AnnounceWizardEntry();
                }
                else if (!showing && _isShowing)
                {
                    _isShowing = false;
                    _selector = null;
                    _selectorBase = null;
                    _lastMenuIndex = -1;
                    _lastDataIndex = -1;
                    DebugLogger.LogState("EquipWizard: closed.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EquipWizard: detection error: {ex.Message}");
                _window = null;
                _isShowing = false;
                _findCooldown = 0;
            }
        }

        /// <summary>
        /// Polls the UIEquipWizardSelector and announces the focused menu item
        /// (Yes / No / Reject All) with position. Also detects character changes
        /// when the wizard advances to the next party member.
        /// </summary>
        private void UpdateMenu()
        {
            if (_selector == null || _selectorBase == null) return;

            try
            {
                // Detect character change (wizard advancing to next party member).
                int dataIdx = _selector.equipWizardDataIndex;
                if (dataIdx != _lastDataIndex && _lastDataIndex != -1)
                {
                    _lastDataIndex = dataIdx;
                    _lastMenuIndex = -1;
                    AnnounceWizardEntry();
                    return;
                }
                _lastDataIndex = dataIdx;

                // Poll menu option (Yes/No/Reject All).
                int idx = _selectorBase.currentIndex;
                if (idx == _lastMenuIndex) return;
                _lastMenuIndex = idx;

                if (_menuNames == null)
                {
                    _menuNames = new[]
                    {
                        Loc.Get("equip_wizard_menu_yes"),
                        Loc.Get("equip_wizard_menu_no"),
                        Loc.Get("equip_wizard_menu_reject_all")
                    };
                }

                int total = _menuNames.Length;
                if (idx < 0 || idx >= total) return;

                string name = _menuNames[idx];

                DebugLogger.LogGameValue("EquipWizard.menu",
                    $"{name} ({idx + 1}/{total})");

                ScreenReader.Say(Loc.Get("equip_wizard_position", name, idx + 1, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"EquipWizardHandler.UpdateMenu: {ex.Message}");
            }
        }

        #endregion

        #region Announcements

        /// <summary>
        /// Announces the wizard heading, description, equipment comparison,
        /// and current menu option when the wizard first opens or advances
        /// to a new character.
        /// </summary>
        private void AnnounceWizardEntry()
        {
            if (_selector == null) return;

            try
            {
                var sb = new StringBuilder();
                sb.Append(Loc.Get("equip_wizard_heading")).Append(". ");

                // Read description text from the presenter.
                var presenter = _selector.presenter;
                if (presenter != null)
                {
                    var descText = presenter.description;
                    if (descText != null)
                    {
                        string desc = descText.text;
                        if (!string.IsNullOrEmpty(desc))
                            sb.Append(desc).Append(". ");
                    }
                }

                // Read equipment comparison from the wizard data.
                AnnounceEquipmentChanges(sb);

                // Include the current menu option.
                if (_selectorBase != null)
                {
                    int idx = _selectorBase.currentIndex;
                    if (_menuNames == null)
                    {
                        _menuNames = new[]
                        {
                            Loc.Get("equip_wizard_menu_yes"),
                            Loc.Get("equip_wizard_menu_no"),
                            Loc.Get("equip_wizard_menu_reject_all")
                        };
                    }
                    int total = _menuNames.Length;
                    if (idx >= 0 && idx < total)
                    {
                        sb.Append(Loc.Get("equip_wizard_position", _menuNames[idx], idx + 1, total));
                    }
                    _lastMenuIndex = idx;
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);

                DebugLogger.LogState($"EquipWizard: announced entry. dataIndex={_lastDataIndex}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"EquipWizardHandler.AnnounceWizardEntry: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the current and proposed equipment lists from the presenter
        /// and appends changed slots to the string builder.
        /// Format: "[slot category]: [old item] to [new item]. "
        /// </summary>
        private void AnnounceEquipmentChanges(StringBuilder sb)
        {
            if (_selector == null) return;

            try
            {
                var presenter = _selector.presenter;
                if (presenter == null) return;

                // selectListPresenter = current equipment, postEquipListPresenter = proposed.
                var currentList = presenter.selectListPresenter;
                var proposedList = presenter.postEquipListPresenter;
                if (currentList == null || proposedList == null) return;

                // The slot category names, matching standard equip order.
                string[] slotNames = { "Weapon", "Armor", "Shield", "Helmet",
                                       "Greaves", "Accessory 1", "Accessory 2" };

                // Read items from both lists using the presenter's item data.
                // UICommonListPresenter doesn't expose a typed data list directly,
                // but we can get items from the wizard data itself.
                var dataList = _selector.equipWizardDataList;
                if (dataList == null || dataList.Count == 0) return;

                int dataIdx = _selector.equipWizardDataIndex;
                if (dataIdx < 0 || dataIdx >= dataList.Count) return;

                var wizardData = dataList[dataIdx];
                if (wizardData == null) return;

                var playerID = wizardData.playerID;

                // Get pre and post equip data lists from the selector's methods.
                var preList = _selector.GetPreEquipDataList(playerID);
                var postList = _selector.GetPostEquipDataList(playerID, wizardData);

                if (preList == null || postList == null) return;

                int count = Math.Min(preList.Count, postList.Count);
                count = Math.Min(count, slotNames.Length);

                for (int i = 0; i < count; i++)
                {
                    var postItem = postList[i];
                    if (postItem == null) continue;
                    if (!postItem.isChanged) continue;

                    string oldName = "";
                    if (i < preList.Count)
                    {
                        var preItem = preList[i];
                        oldName = preItem?.itemName ?? "";
                    }

                    string newName = postItem.itemName ?? "";
                    string slot = i < slotNames.Length ? slotNames[i] : $"Slot {i + 1}";

                    if (string.IsNullOrEmpty(oldName))
                        oldName = "None";

                    sb.Append(Loc.Get("equip_wizard_change", slot, oldName, newName)).Append(". ");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EquipWizard: equipment comparison error: {ex.Message}");
            }
        }

        #endregion
    }
}
