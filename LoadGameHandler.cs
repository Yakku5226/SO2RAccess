using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Announces load game menu navigation to the screen reader.
    ///
    /// Patches applied:
    ///   UISaveLoadSelector.Show                — announces "Load game." when the screen opens.
    ///   UISaveLoadListItemPresenter.OnSelected — announces the focused save slot details
    ///                                            on navigation (slot label, hero name, level,
    ///                                            difficulty, location, playtime, position).
    /// </summary>
    public class LoadGameHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the load game menu.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UISaveLoadListItemData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UISaveLoadListItemPresenter).TypeHandle);

                // Fires when the load game screen opens.
                harmony.Patch(
                    AccessTools.Method(typeof(UISaveLoadSelector),
                        nameof(UISaveLoadSelector.Show)),
                    postfix: new HarmonyMethod(typeof(LoadGameHandler),
                        nameof(SaveLoadSelector_Show_Postfix))
                );

                // Fires whenever the cursor moves to a save slot in the list.
                harmony.Patch(
                    AccessTools.Method(typeof(UISaveLoadListItemPresenter),
                        nameof(UISaveLoadListItemPresenter.OnSelected)),
                    postfix: new HarmonyMethod(typeof(LoadGameHandler),
                        nameof(SaveLoadListItem_OnSelected_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("LoadGameHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"LoadGameHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UISaveLoadSelector.Show().
        /// Announces the screen heading — "Save game." or "Load game." depending on mode.
        /// </summary>
        private static void SaveLoadSelector_Show_Postfix(UISaveLoadSelector __instance)
        {
            try
            {
                var state = __instance.SaveLoadState;
                if (state == UIDefine.SaveLoadState.Save)
                    ScreenReader.Say(Loc.Get("save_game_screen"));
                else
                    ScreenReader.Say(Loc.Get("load_game_screen"));
                DebugLogger.LogState($"SaveLoad: screen shown, state={state}.");
            }
            catch
            {
                // Fallback if state read fails.
                ScreenReader.Say(Loc.Get("load_game_screen"));
                DebugLogger.LogState("SaveLoad: screen shown (state read failed).");
            }
        }

        /// <summary>
        /// Postfix for UISaveLoadListItemPresenter.OnSelected(ListItemDataBase).
        /// Fires whenever the cursor moves to a save slot. Reads the slot data and
        /// announces the hero name, level, difficulty, location, and playtime.
        /// </summary>
        private static void SaveLoadListItem_OnSelected_Postfix(
            UISaveLoadListItemPresenter __instance, ListItemDataBase itemData)
        {
            try
            {
                if (itemData == null) return;

                var data = itemData.TryCast<UISaveLoadListItemData>();
                if (data == null) return;

                // Get position within the list for "N of total" announcement.
                int position = 0;
                int total = 0;
                var selector = __instance.GetComponentInParent<UISaveLoadSelector>();
                if (selector != null)
                {
                    position = selector.currentIndex + 1;
                    total = selector.currentDataList?.Count ?? 0;
                }

                AnnounceSlot(data, position, total);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"SaveLoadListItem_OnSelected_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Builds and speaks a description of the focused save slot.
        /// For auto-saves uses "Auto save" as the label; for normal slots uses the
        /// slot number text from the data (e.g. "1", "2").
        /// Empty slots are announced as "[label]. Empty."
        /// Filled slots include hero name, level, difficulty, location, and playtime.
        /// </summary>
        private static void AnnounceSlot(UISaveLoadListItemData data, int position, int total)
        {
            // Determine the slot label for announcement.
            string slotLabel;
            if (data.isAutoSave)
            {
                slotLabel = Loc.Get("load_slot_label_auto");
            }
            else
            {
                string raw = data.slotText ?? "";
                slotLabel = string.IsNullOrEmpty(raw)
                    ? position.ToString()
                    : raw;
            }

            DebugLogger.LogGameValue("LoadGame.slot",
                $"exist={data.isExistData} auto={data.isAutoSave} label='{slotLabel}'" +
                $" hero='{data.heroName}' lv='{data.heroLevel}' diff='{data.difficultyLevel}'" +
                $" field='{data.fieldName}' time='{data.playTimeValue}' ({position}/{total})");

            if (!data.isExistData)
            {
                ScreenReader.Say(Loc.Get("load_slot_empty", slotLabel, position, total));
                return;
            }

            ScreenReader.Say(Loc.Get("load_slot_with_data",
                slotLabel,
                data.heroName ?? "",
                data.heroLevel ?? "",
                data.difficultyLevel ?? "",
                data.fieldName ?? "",
                data.playTimeValue ?? "",
                position,
                total));
        }

        #endregion
    }
}
