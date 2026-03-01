using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;

namespace SO2RAccess
{
    /// <summary>
    /// Handles save-related accessibility announcements:
    /// 1. Reads the auto-save notification dialog shown at new game start.
    /// 2. Detects when the game saves (auto-save or manual) and announces
    ///    "Saving." plus an optional audio cue (toggleable via ModSettings).
    ///
    /// Detection uses three methods (debounced to prevent double-firing):
    ///   - Harmony prefix on GameSaveManager.Save (CallerCount 3, catches manual saves).
    ///   - Harmony postfix on GameSaveManager.OnSaveSuccess (CallerCount 1, catches completion).
    ///   - Polling GameSaveManager.IsSaving() each frame (backup for auto-saves).
    /// </summary>
    public class SaveNotificationHandler
    {
        #region Fields

        private bool _patchesApplied;
        private bool _wasSaving;

        // Static so both static hook methods and instance Update() can access it.
        private static float _lastSaveAnnouncedTime = -10f;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for save notification announcements.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIDialogWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(GameSaveManager).TypeHandle);

                // Hook 1: Auto-save announcement dialog (new game start).
                // CallerCount(2) — hookable.
                harmony.Patch(
                    AccessTools.Method(typeof(UIDialogWindow),
                        nameof(UIDialogWindow.SetupAutoSaveAnnounce)),
                    postfix: new HarmonyMethod(typeof(SaveNotificationHandler),
                        nameof(SetupAutoSaveAnnounce_Postfix))
                );

                // Hook 2: Save initiated — fires when any managed-code save starts.
                // CallerCount(3) — hookable.
                harmony.Patch(
                    AccessTools.Method(typeof(GameSaveManager),
                        nameof(GameSaveManager.Save)),
                    prefix: new HarmonyMethod(typeof(SaveNotificationHandler),
                        nameof(Save_Prefix))
                );

                // Hook 3: Save completed — fires after save data is written.
                // CallerCount(1) — hookable. Backup for saves not caught by prefix.
                harmony.Patch(
                    AccessTools.Method(typeof(GameSaveManager),
                        nameof(GameSaveManager.OnSaveSuccess)),
                    postfix: new HarmonyMethod(typeof(SaveNotificationHandler),
                        nameof(OnSaveSuccess_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("SaveNotificationHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SaveNotificationHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patches

        /// <summary>
        /// Postfix for UIDialogWindow.SetupAutoSaveAnnounce.
        /// Fires when the auto-save announcement dialog is set up (new game start).
        /// Reads the dialog text via the screen reader.
        /// </summary>
        private static void SetupAutoSaveAnnounce_Postfix(UIDialogWindow __instance)
        {
            try
            {
                string text = null;

                // Try to read the message text from the dialog presenter.
                var presenter = __instance.autoSaveAnnounceDialogPresenter;
                if (presenter != null)
                {
                    text = presenter.message?.text;
                    text = TextUtil.StripTags(text);
                }

                // Fallback if the game text couldn't be read.
                if (string.IsNullOrEmpty(text))
                    text = Loc.Get("save_autosave_announce_fallback");

                ScreenReader.Say(text);
                DebugLogger.LogGameValue("AutoSaveAnnounce", $"text='{text}'");
            }
            catch (Exception ex)
            {
                // Even if reading the text fails, announce the fallback.
                MelonLogger.Warning($"SetupAutoSaveAnnounce_Postfix: {ex.Message}");
                ScreenReader.Say(Loc.Get("save_autosave_announce_fallback"));
            }
        }

        /// <summary>
        /// Prefix for GameSaveManager.Save.
        /// Fires when any managed-code save operation begins.
        /// </summary>
        private static void Save_Prefix()
        {
            try
            {
                OnSaveDetected();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Save_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GameSaveManager.OnSaveSuccess.
        /// Fires after save data is written. Backup detection for saves
        /// not caught by the Save prefix.
        /// </summary>
        private static void OnSaveSuccess_Postfix()
        {
            try
            {
                OnSaveDetected();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"OnSaveSuccess_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Polling

        /// <summary>
        /// Called each frame from Main.UpdateHandlers().
        /// Polls GameSaveManager.IsSaving() as a backup detection method.
        /// </summary>
        public void Update()
        {
            try
            {
                var saveManager = GameSaveManager.Instance;
                if (saveManager != null)
                {
                    bool isSaving = saveManager.IsSaving();

                    if (isSaving && !_wasSaving)
                        OnSaveDetected();

                    _wasSaving = isSaving;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SaveNotificationHandler.Update: {ex.Message}");
            }
        }

        #endregion

        #region Detection

        /// <summary>
        /// Announces the save and plays the audio cue.
        /// Debounces to prevent double-firing when multiple detection
        /// methods trigger on the same save.
        /// </summary>
        private static void OnSaveDetected()
        {
            float now = UnityEngine.Time.time;
            if (now - _lastSaveAnnouncedTime < 2f) return;
            _lastSaveAnnouncedTime = now;

            if (ModSettings.SaveSoundEnabled)
                AudioCuePlayer.PlaySaveCue();
        }

        #endregion

        #region Scene Management

        /// <summary>
        /// Called on scene change to reset state.
        /// </summary>
        public void OnSceneChanged()
        {
            _wasSaving = false;
        }

        #endregion
    }
}
