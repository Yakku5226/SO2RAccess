using HarmonyLib;
using Il2CppCommon;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces NPC dialogue text boxes to the screen reader.
    ///
    /// Patches applied:
    ///   UIConversationPresenter.SetMessage(string message, string talkerName,
    ///       string voiceID, bool isWait, ref Rect rect)
    ///     — fires whenever a new line of dialogue is displayed. All public
    ///       SetMessage overloads (field NPC, cutscene, etc.) delegate to this
    ///       method after resolving any messageID lookups, so one patch covers
    ///       all dialogue types.
    ///
    /// NPC NAME LEARNING
    ///   Each time dialogue fires with a speaker name the nearest NPC is located
    ///   and two maps are updated:
    ///     NpcDisplayNames      — runtime map: instance ID → display name.
    ///                            Used immediately within the same play session.
    ///     PersistentNpcNames   — persistent map: code name → display name.
    ///                            Saved to UserData\SO2RAccess_npc_names.txt and
    ///                            loaded on every startup so learned names survive
    ///                            across play sessions.
    ///   NavigationHandler reads both maps in ResolveNpcName.
    /// </summary>
    public partial class DialogueHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        // Deferred dialogue announcement — waits 1 frame then polls voice state.
        private static string _pendingMessage;
        private static string _pendingName;
        private static string _pendingVoiceID;
        private static int _pendingFrame = -1;

        // Cached reference to the conversation selector for polling voice state.
        private static UIConversationSelector _cachedSelector;

        /// <summary>
        /// Runtime map: FieldNpcCharacter instance ID → resolved dialogue display name.
        /// Built during the current play session as the player talks to NPCs.
        /// Instance IDs are reassigned each session so this map is session-only.
        /// Read by NavigationHandler as a fast first-pass lookup.
        /// </summary>
        internal static readonly Dictionary<int, string> NpcDisplayNames =
            new Dictionary<int, string>();

        /// <summary>
        /// Persistent map: ConstNpcParameter code name → resolved dialogue display name.
        /// e.g. "NPC_0003_01a_17_GRANDFATHER2" → "Elderly person".
        /// Loaded from disk on startup; updated and saved whenever a new name is learned.
        /// Code names are stable across sessions, so this map survives restarts.
        /// Read by NavigationHandler to show real names even before the player speaks
        /// to an NPC in the current session.
        /// </summary>
        internal static readonly Dictionary<string, string> PersistentNpcNames =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string _persistPath = null;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches for the dialogue text box announcements and
        /// loads the persistent NPC name map from disk.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            LoadPersistentNames();

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIConversationPresenter).TypeHandle);

                // Fires whenever a new line of dialogue text is set for display.
                // This is the shared implementation called by all SetMessage overloads.
                harmony.Patch(
                    AccessTools.Method(typeof(UIConversationPresenter), "SetMessage",
                        new Type[]
                        {
                            typeof(string),             // message  (resolved display text)
                            typeof(string),             // talkerName
                            typeof(string),             // voiceID
                            typeof(bool),               // isWait
                            typeof(Rect).MakeByRefType() // rect
                        }),
                    postfix: new HarmonyMethod(typeof(DialogueHandler),
                        nameof(ConversationPresenter_SetMessage_Postfix))
                );

                ApplyWindowMessagePatches(harmony);

                _patchesApplied = true;
                DebugLogger.LogState("DialogueHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"DialogueHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Postfix for UIConversationPresenter.SetMessage(...).
        /// Fires each time a new line of dialogue is sent to the presenter.
        /// Reads the display text and optional talker name, then announces them.
        /// </summary>
        private static void ConversationPresenter_SetMessage_Postfix(
            string message, string talkerName, string voiceID)
        {
            try
            {
                if (string.IsNullOrEmpty(message)) return;

                string cleanMessage = StripTags(message);
                string cleanName    = StripTags(talkerName ?? "");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Associate the speaker's display name with the nearest NPC so the
                // navigation list can show "Elderly person" instead of "Grandfather 2".
                if (!string.IsNullOrEmpty(cleanName))
                    TryRecordNpcName(cleanName);

                // If voice mode is AlwaysReadFull, announce immediately — no need to
                // wait for PlayVoice since we always read full text regardless.
                if (ModSettings.DialogueVoiceMode != DialogueVoiceMode.NameOnlyWhenVoiced)
                {
                    string announcement = string.IsNullOrEmpty(cleanName)
                        ? Loc.Get("dialogue_no_name", cleanMessage)
                        : Loc.Get("dialogue_with_name", cleanName, cleanMessage);
                    ScreenReader.Say(announcement);
                    DebugLogger.LogGameValue("Dialogue",
                        $"name='{cleanName}' voiced=N/A voiceID='{voiceID ?? ""}' mode={ModSettings.DialogueVoiceMode} msg='{cleanMessage}'");
                    return;
                }

                // NameOnlyWhenVoiced mode: defer announcement by 1 frame to give the
                // game's PlayVoice call a chance to fire (it runs after SetMessage in
                // the same native call chain). ProcessPendingDialogue() in OnLateUpdate
                // will check whether PlayVoice fired and announce accordingly.
                _pendingMessage = cleanMessage;
                _pendingName    = cleanName;
                _pendingVoiceID = voiceID;
                _pendingFrame   = Time.frameCount;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"ConversationPresenter_SetMessage_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Called from Main.OnLateUpdate. If a deferred dialogue announcement is
        /// pending and at least 1 frame has passed (giving PlayVoice a chance to
        /// fire), announces the dialogue — name-only if voice played, full text
        /// otherwise.
        /// </summary>
        public static void ProcessPendingDialogue()
        {
            if (_pendingMessage == null) return;
            if (Time.frameCount <= _pendingFrame) return; // wait 1 frame

            string msg  = _pendingMessage;
            string name = _pendingName;
            string vid  = _pendingVoiceID;

            // Clear pending state before announcing.
            _pendingMessage = null;
            _pendingName    = null;
            _pendingVoiceID = null;

            // Poll the game's voice state — by this frame PlayVoice has already
            // been called natively and the controller is actively playing audio.
            bool voiced = false;
            try
            {
                if (_cachedSelector == null)
                    _cachedSelector = UnityEngine.Object.FindObjectOfType<UIConversationSelector>();
                if (_cachedSelector != null)
                {
                    SeController vc = _cachedSelector.currentVoiceController;
                    voiced = vc != null && vc.IsPlaying();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler: voice poll error: {ex.Message}");
            }

            bool nameOnly = voiced && !string.IsNullOrEmpty(name);

            string announcement;
            if (nameOnly)
                announcement = Loc.Get("dialogue_speaker_only", name);
            else if (string.IsNullOrEmpty(name))
                announcement = Loc.Get("dialogue_no_name", msg);
            else
                announcement = Loc.Get("dialogue_with_name", name, msg);

            ScreenReader.Say(announcement);
            DebugLogger.LogGameValue("Dialogue",
                $"name='{name}' voiced={voiced} voiceID='{vid ?? ""}' mode={ModSettings.DialogueVoiceMode} msg='{msg}'");
        }

        private static string StripTags(string text) => TextUtil.StripTags(text);

        /// <summary>
        /// Finds the nearest field NPC (beyond party range) and records the
        /// dialogue speaker's display name in both the runtime and persistent maps.
        /// Called each time dialogue fires with a non-empty talker name.
        /// Silently does nothing if FieldManager is unavailable (e.g. cutscenes).
        /// </summary>
        private static void TryRecordNpcName(string displayName)
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return;

                var player = fm.GetControlPlayer();
                if (player == null) return;

                Vector3 playerPos = player.transform.position;
                var npcs = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (npcs == null) return;

                // The player character is itself a FieldNpcCharacter — skip it explicitly
                // or it will always "win" as the closest object at distance 0.
                int playerInstanceID = player.GetInstanceID();

                FieldNpcCharacter closest     = null;
                float             closestDist = float.MaxValue;

                foreach (var npc in npcs)
                {
                    if (npc == null) continue;
                    if (npc.GetInstanceID() == playerInstanceID) continue; // skip player
                    float dist = Vector3.Distance(playerPos, npc.transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest     = npc;
                    }
                }

                // 15-unit ceiling: generous enough for any normal interaction range,
                // tight enough to avoid associating cutscene narration with a random NPC.
                if (closest == null || closestDist >= 15.0f) return;

                // Runtime map — instance ID (session-only).
                int id = closest.GetInstanceID();
                NpcDisplayNames[id] = displayName;
                DebugLogger.LogState(
                    $"DialogueHandler: NPC id={id} dist={closestDist:F1} → '{displayName}'");

                // Persistent map — code name (survives restarts).
                // Look up the NPC's ConstNpcParameter by spawn position to get its code name.
                try
                {
                    var npcParams = ParameterManager.Instance?.GetNpcParameter(fm.currentFieldmapID);
                    if (npcParams == null) return;

                    Vector3 spawn = closest.InitialPosition;
                    for (int i = 0; i < npcParams.Count; i++)
                    {
                        var param = npcParams[i];
                        if (param == null) continue;
                        if (Vector3.Distance(spawn, param.Position) >= 2.0f) continue;

                        string codeName = param.Name;
                        if (string.IsNullOrEmpty(codeName)) break;

                        // Skip event objects (switches, triggers, cutscene actors) —
                        // they aren't real NPCs and shouldn't learn dialogue names.
                        if (codeName.StartsWith("ev_", StringComparison.OrdinalIgnoreCase))
                            break;

                        if (!PersistentNpcNames.ContainsKey(codeName))
                        {
                            PersistentNpcNames[codeName] = displayName;
                            AppendPersistentName(codeName, displayName);
                            DebugLogger.LogState(
                                $"DialogueHandler: saved '{codeName}' → '{displayName}'");
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"DialogueHandler.TryRecordNpcName (persist): {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler.TryRecordNpcName: {ex.Message}");
            }
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Loads previously learned NPC names from UserData\SO2RAccess_npc_names.txt.
        /// Each line has the format: codeName|displayName
        /// Called once at startup from ApplyPatches.
        /// </summary>
        private static void LoadPersistentNames()
        {
            try
            {
                // UserData is always {game root}\UserData — the game's working directory
                // is its install root when launched through Steam.
                string userDataDir = Path.Combine(
                    Directory.GetCurrentDirectory(), "UserData");
                Directory.CreateDirectory(userDataDir);   // no-op if already exists
                _persistPath = Path.Combine(userDataDir, "SO2RAccess_npc_names.txt");

                if (!File.Exists(_persistPath)) return;

                int loaded = 0;
                foreach (var line in File.ReadAllLines(_persistPath))
                {
                    int sep = line.IndexOf('|');
                    if (sep < 1) continue;
                    string code = line.Substring(0, sep).Trim();
                    string name = line.Substring(sep + 1).Trim();
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                    {
                        // Skip event objects that were incorrectly saved in older versions.
                        if (code.StartsWith("ev_", StringComparison.OrdinalIgnoreCase))
                            continue;
                        PersistentNpcNames[code] = name;
                        loaded++;
                    }
                }

                DebugLogger.LogState(
                    $"DialogueHandler: loaded {loaded} persistent NPC name(s) from disk.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler.LoadPersistentNames: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends a single new codeName|displayName entry to the persistent file.
        /// Only called for genuinely new entries — duplicates are never written.
        /// </summary>
        private static void AppendPersistentName(string codeName, string displayName)
        {
            if (_persistPath == null) return;
            try
            {
                File.AppendAllText(_persistPath, $"{codeName}|{displayName}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueHandler.AppendPersistentName: {ex.Message}");
            }
        }

        #endregion
    }
}
