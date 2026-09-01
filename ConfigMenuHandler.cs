using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces config menu navigation to the screen reader.
    ///
    /// Patches applied:
    ///   UIConfigMenuSelector.Show            — announces first item when config menu opens
    ///   UIConfigMenuSelector.OnMoveCursor    — announces focused category on navigation
    ///   UIConfigGroupSelectorBase.MoveCursor — announces focused setting on navigation
    ///   UIConfigGroupSelectItemSelector.SetLabel    — caches voice-config row labels
    ///   UIConfigGroupSelectItemSelector.OnMoveCursor — announces new value on left/right
    /// </summary>
    public class ConfigMenuHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Cache of label strings set via UIConfigGroupSelectItemSelector.SetLabel().
        // Used for voice-config rows. Display/audio rows set labels via prefab/localization,
        // so we fall back to reading presenter.label.text for those.
        private static readonly Dictionary<IntPtr, string> _labelCache =
            new Dictionary<IntPtr, string>();

        // Lazily found config window — used only by the IsConfigOpen state query.
        private static UIConfigWindow _configWindow;
        private static float _nextConfigFindTime = 0f;

        // Time of this handler's last announcement. UIConfigWindow.IsOpened is still
        // false while the window plays its opening transition, so the very first row
        // would slip past the IsConfigOpen gate — this timestamp covers that gap.
        private static float _lastActivityTime = -999f;

        /// <summary>How long after an announcement config still counts as owning the screen.</summary>
        private const float ActivityWindow = 1.5f;

        #endregion

        #region Public State

        /// <summary>
        /// True while the config screen owns input — both the title-screen config
        /// and the in-game one opened from camp.
        ///
        /// The config category list is built from generic
        /// <c>UICommonListItemPresenter</c> rows, so the universal
        /// <see cref="ListSelectionHandler"/> safety net would announce every
        /// category a second time after this handler's own patch already spoke it
        /// (with position). That handler asks here to stay silent while config owns
        /// the screen.
        ///
        /// Two signals, because neither alone is enough: the window's own
        /// <c>IsOpened</c> covers steady state but is still false during the opening
        /// transition, and the recent-announcement stamp covers exactly that gap.
        /// </summary>
        public static bool IsConfigOpen
        {
            get
            {
                if (Time.unscaledTime - _lastActivityTime < ActivityWindow) return true;

                return UiFinder.TryGetActiveOverlay(
                    ref _configWindow, ref _nextConfigFindTime,
                    w => w != null && w.gameObject != null && w.IsOpened);
            }
        }

        /// <summary>Drops the cached window reference when the scene changes.</summary>
        public static void OnSceneChanged()
        {
            _configWindow = null;
            _nextConfigFindTime = 0f;
            _lastActivityTime = -999f;
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the config menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                // IL2CppInterop TryCast<T> silently returns null if T's class lookup
                // table has not been initialized. Force initialization before any postfix
                // tries to cast to these types.
                RuntimeHelpers.RunClassConstructor(typeof(UICommonListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIConfigGroupGaugeSelectItemSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIConfigGroupSelectItemPresenter).TypeHandle);

                // Top-level config category menu
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigMenuSelector), nameof(UIConfigMenuSelector.Show)),
                    postfix: new HarmonyMethod(typeof(ConfigMenuHandler), nameof(ConfigMenu_Show_Postfix))
                );
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigMenuSelector), nameof(UIConfigMenuSelector.OnMoveCursor)),
                    postfix: new HarmonyMethod(typeof(ConfigMenuHandler), nameof(ConfigMenu_OnMoveCursor_Postfix))
                );

                // Cursor movement within a settings submenu.
                // MoveCursor(int) is defined on the base class and not overridden by
                // any concrete subclass, so one patch covers all nine submenus.
                var moveCursorMethod = AccessTools.Method(typeof(UIConfigGroupSelectorBase), "MoveCursor");
                if (moveCursorMethod != null)
                {
                    harmony.Patch(
                        moveCursorMethod,
                        postfix: new HarmonyMethod(typeof(ConfigMenuHandler), nameof(ConfigGroup_MoveCursor_Postfix))
                    );
                }
                else
                {
                    MelonLogger.Warning("ConfigMenuHandler: UIConfigGroupSelectorBase.MoveCursor not found — submenu navigation will not be announced.");
                }

                // Cache label strings for rows that use SetLabel (voice config).
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigGroupSelectItemSelector), nameof(UIConfigGroupSelectItemSelector.SetLabel)),
                    postfix: new HarmonyMethod(typeof(ConfigMenuHandler), nameof(ConfigGroupItem_SetLabel_Postfix))
                );

                // Value adjustment (left/right within a setting row)
                harmony.Patch(
                    AccessTools.Method(typeof(UIConfigGroupSelectItemSelector), nameof(UIConfigGroupSelectItemSelector.OnMoveCursor)),
                    postfix: new HarmonyMethod(typeof(ConfigMenuHandler), nameof(ConfigItem_OnMoveCursor_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("ConfigMenuHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ConfigMenuHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods — Config Category Menu

        /// <summary>
        /// Records that the config screen is alive right now. Called from every
        /// config patch, including ones that end up announcing nothing — the point
        /// is screen ownership, not speech. See <see cref="IsConfigOpen"/>.
        /// </summary>
        private static void MarkActive()
        {
            _lastActivityTime = Time.unscaledTime;
        }

        // Postfix for UIConfigMenuSelector.Show()
        private static void ConfigMenu_Show_Postfix(UIConfigMenuSelector __instance)
        {
            MarkActive();
            try { AnnounceConfigMenuItem(__instance); }
            catch (Exception ex) { MelonLogger.Warning($"ConfigMenu_Show_Postfix: {ex.Message}"); }
        }

        // Postfix for UIConfigMenuSelector.OnMoveCursor()
        private static void ConfigMenu_OnMoveCursor_Postfix(UIConfigMenuSelector __instance)
        {
            MarkActive();
            try { AnnounceConfigMenuItem(__instance); }
            catch (Exception ex) { MelonLogger.Warning($"ConfigMenu_OnMoveCursor_Postfix: {ex.Message}"); }
        }

        // The config category menu stores items as UICommonListItemData (not UIConfigMenuItemData).
        // UICommonListItemData.text holds the already-localized display string.
        private static void AnnounceConfigMenuItem(UIConfigMenuSelector selector)
        {
            if (selector == null) return;

            int idx = selector.CurrentIndex;
            var list = selector.currentDataList;
            if (list == null) return;

            int count = list.Count;
            if (count == 0 || idx < 0 || idx >= count) return;

            var itemBase = list[idx];
            if (itemBase == null) return;

            var item = itemBase.TryCast<UICommonListItemData>();
            string text = item?.text ?? "";
            if (string.IsNullOrEmpty(text)) return;

            DebugLogger.LogGameValue("ConfigMenu.item", $"{text} ({idx + 1}/{count})");
            ScreenReader.Say(Loc.Get("config_menu_item", text, idx + 1, count));
        }

        #endregion

        #region Harmony Patch Methods — Settings Submenus

        // Postfix for UIConfigGroupSelectorBase.MoveCursor(int)
        private static void ConfigGroup_MoveCursor_Postfix(UIConfigGroupSelectorBase __instance)
        {
            MarkActive();
            try { AnnounceGroupItem(__instance); }
            catch (Exception ex) { MelonLogger.Warning($"ConfigGroup_MoveCursor_Postfix: {ex.Message}"); }
        }

        private static void AnnounceGroupItem(UIConfigGroupSelectorBase selector)
        {
            if (selector == null) return;

            var list = selector.groupSelectorList;
            if (list == null) return;

            int count = list.Count;
            if (count == 0) return;

            int idx = selector.currentIndex;
            if (idx < 0 || idx >= count) return;

            var itemSelector = list[idx];
            if (itemSelector == null) return;

            string label = GetItemLabel(itemSelector);
            if (string.IsNullOrEmpty(label)) return;

            string value = GetItemValue(itemSelector);

            DebugLogger.LogGameValue("ConfigGroup.item", $"{label}: {value} ({idx + 1}/{count})");

            if (string.IsNullOrEmpty(value))
                ScreenReader.Say(Loc.Get("config_setting_no_value", label, idx + 1, count));
            else
                ScreenReader.Say(Loc.Get("config_setting", label, value, idx + 1, count));
        }

        // Postfix for UIConfigGroupSelectItemSelector.SetLabel(string)
        // Only called for voice-config rows. Cached for fallback lookup.
        private static void ConfigGroupItem_SetLabel_Postfix(UIConfigGroupSelectItemSelector __instance, string label)
        {
            if (__instance == null || label == null) return;
            _labelCache[__instance.Pointer] = label;
        }

        #endregion

        #region Harmony Patch Methods — Value Adjustment

        // Postfix for UIConfigGroupSelectItemSelector.OnMoveCursor()
        // Fires when the user presses left/right to change a setting value.
        private static void ConfigItem_OnMoveCursor_Postfix(UIConfigGroupSelectItemSelector __instance)
        {
            MarkActive();
            try
            {
                if (__instance == null) return;

                // Option-list setting (dropdown/toggle): GetCurrentData() returns the selected item.
                var data = __instance.GetCurrentData();
                if (data != null)
                {
                    string optValue = data.text ?? "";
                    if (!string.IsNullOrEmpty(optValue))
                    {
                        DebugLogger.LogGameValue("ConfigItem.value", optValue);
                        ScreenReader.Say(Loc.Get("config_value", optValue));
                        return;
                    }
                }

                // Gauge/slider setting: read the value GameText instead.
                var gauge = __instance.TryCast<UIConfigGroupGaugeSelectItemSelector>();
                if (gauge != null)
                {
                    string gaugeValue = gauge.currentIndex.ToString();
                    DebugLogger.LogGameValue("ConfigItem.gauge", $"value='{gaugeValue}' idx={gauge.currentIndex}/{gauge.maxIndex}");
                    if (!string.IsNullOrEmpty(gaugeValue))
                    {
                        DebugLogger.LogGameValue("ConfigItem.gauge", gaugeValue);
                        ScreenReader.Say(Loc.Get("config_value", gaugeValue));
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Warning($"ConfigItem_OnMoveCursor_Postfix: {ex.Message}"); }
        }

        #endregion

        #region Helpers

        // Retrieves the label for a setting row.
        //
        // Strategy 1: voice-config rows — label cached from SetLabel intercept.
        // Strategy 2: search inside the selector's own hierarchy for a GameText
        //             that is not the gauge value text.
        // Strategy 3 (fallback): walk backward through sibling transforms for the
        //             nearest GameText — known to return section headers, not per-item
        //             labels, but better than nothing while we diagnose.
        private static string GetItemLabel(UIConfigGroupSelectItemSelector itemSelector)
        {
            if (itemSelector == null) return "";

            // Strategy 1: voice-config rows cached via SetLabel
            if (_labelCache.TryGetValue(itemSelector.Pointer, out string cached) && !string.IsNullOrEmpty(cached))
                return cached;

            // Strategy 2: any non-gauge-value GameText inside this selector is the label
            try
            {
                var gauge = itemSelector.TryCast<UIConfigGroupGaugeSelectItemSelector>();
                var gaugeValueGT = gauge?.value;

                var selfTexts = itemSelector.GetComponentsInChildren<GameText>(true);
                if (selfTexts != null)
                {
                    foreach (var gt in selfTexts)
                    {
                        if (gaugeValueGT != null && gt?.Pointer == gaugeValueGT.Pointer)
                            continue;
                        string t = gt?.text ?? "";
                        if (!string.IsNullOrEmpty(t))
                            return t;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ConfigMenuHandler.GetItemLabel Strategy2: {ex.Message}");
            }

            // Strategy 3 (fallback): walk backward through siblings
            try
            {
                var myTransform = itemSelector.transform;
                var parentTransform = myTransform?.parent;
                if (parentTransform == null) return "";

                int myIdx = myTransform.GetSiblingIndex();
                for (int i = myIdx - 1; i >= 0; i--)
                {
                    var sibling = parentTransform.GetChild(i);
                    var sibTexts = sibling.GetComponentsInChildren<GameText>(true);
                    if (sibTexts == null) continue;
                    foreach (var gt in sibTexts)
                    {
                        string text = gt?.text ?? "";
                        if (!string.IsNullOrEmpty(text))
                            return text;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ConfigMenuHandler.GetItemLabel Strategy3: {ex.Message}");
            }

            return "";
        }

        // Retrieves the current value text for a setting row.
        // For option-list settings: reads GetCurrentData().text.
        // For gauge/slider settings: reads the value GameText on the gauge selector.
        private static string GetItemValue(UIConfigGroupSelectItemSelector itemSelector)
        {
            if (itemSelector == null) return "";

            try
            {
                var data = itemSelector.GetCurrentData();
                if (data != null)
                {
                    string text = data.text ?? "";
                    if (!string.IsNullOrEmpty(text)) return text;
                }

                var gauge = itemSelector.TryCast<UIConfigGroupGaugeSelectItemSelector>();
                if (gauge != null)
                    return gauge.currentIndex.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ConfigMenuHandler.GetItemValue: {ex.Message}");
            }

            return "";
        }

        #endregion
    }
}
