using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    // Partial class fragment of CampMenuHandler: the "use item on a character" target
    // screen (e.g. reading a skill book, using a healing item out of battle).
    public partial class CampMenuHandler
    {
        // When the player confirms an item to use, UICampItemSelector.currentState changes
        // SelectItem -> SelectCharacter and the item selector's own character picker
        // (selectCharacterSelector, a UICommonSelectCharacterListSelector) becomes active.
        // CONFIRMED by diagnostic (2026-06-15): the rich UICampItemCharacterStatusSelector
        // is NOT used here; this UICommonSelectCharacterListSelector is.
        //
        // That selector exposes the party as playerIDList plus the UISelectItemSelectorBase
        // presenter machinery (selectItemPresenterList + currentSelectPresenter). Navigation
        // is native-only and the cursor is purely visual, so the highlighted index is found
        // by matching currentSelectPresenter within selectItemPresenterList — there is no
        // currentIndex on this selector type. Name/HP/MP are resolved from the PlayerID.
        private static bool _itemCharWasActive = false;
        private static int _itemCharLastIndex = -1;

        /// <summary>
        /// Announces the highlighted party member while choosing who to use an item on.
        /// Active only while the item sub-screen is in its SelectCharacter state; announces
        /// the heading on entry, then each member (name, HP, MP, position) as the cursor moves.
        /// </summary>
        private void UpdateItemCharacterSelect()
        {
            if (!IsCampOpen || _itemSelector == null)
            {
                ResetItemCharacterSelect();
                return;
            }

            // The character-target phase is authoritative via the selector's own state.
            bool selectingChar;
            try
            {
                selectingChar =
                    _itemSelector.currentState == UICampItemSelector.State.SelectCharacter;
            }
            catch
            {
                selectingChar = false;
            }

            if (!selectingChar)
            {
                if (_itemCharWasActive)
                {
                    _itemCharWasActive = false;
                    _itemCharLastIndex = -1;
                    DebugLogger.LogState("CampItemTarget: left character select.");
                }
                return;
            }

            try
            {
                var sel = _itemSelector.selectCharacterSelector;
                if (sel == null || !sel.gameObject.activeInHierarchy) return;

                // Entry: announce the heading, then read the highlighted member next frame
                // (separate frames so the two announcements don't clip each other).
                if (!_itemCharWasActive)
                {
                    _itemCharWasActive = true;
                    _itemCharLastIndex = -1;
                    ScreenReader.Say(Loc.Get("camp_item_target_screen"));
                    DebugLogger.LogState(
                        $"CampItemTarget: entered character select, party={sel.playerIDList?.Count ?? 0}.");
                    return;
                }

                int idx = HighlightedIndex(sel);
                if (idx < 0 || idx == _itemCharLastIndex) return;
                _itemCharLastIndex = idx;

                var ids = sel.playerIDList;
                int total = ids?.Count ?? 0;
                if (total == 0 || idx >= total) return;

                PlayerID playerID = ids[idx];
                ScreenReader.Say(BuildCharacterAnnouncement(playerID, idx, total));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateItemCharacterSelect: {ex.Message}");
                ResetItemCharacterSelect();
            }
        }

        /// <summary>
        /// Returns the highlighted party index by matching the selector's
        /// currentSelectPresenter against its selectItemPresenterList (the cursor itself is
        /// visual-only, so there is no numeric index to read). Returns -1 if not found
        /// (e.g. the cursor is on a non-character "all allies" option).
        /// </summary>
        private static int HighlightedIndex(UICommonSelectCharacterListSelector sel)
        {
            try
            {
                var presenters = sel.selectItemPresenterList;
                var current = sel.currentSelectPresenter;
                if (presenters == null || current == null) return -1;

                System.IntPtr currentPtr = current.Pointer;
                int count = presenters.Count;
                for (int i = 0; i < count; i++)
                {
                    var p = presenters[i];
                    if (p != null && p.Pointer == currentPtr) return i;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItemTarget: index match error: {ex.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Builds "Name. HP x of y. MP x of y. n of total." for a party member, resolving
        /// the name and HP/MP from the PlayerID's CharacterParameter.
        /// </summary>
        private static string BuildCharacterAnnouncement(PlayerID playerID, int idx, int total)
        {
            string name = null;
            int hp = -1, hpMax = -1, mp = -1, mpMax = -1;
            try
            {
                var pm = ParameterManager.Instance;
                name = pm?.GetCharacterFirstName(playerID);

                var cp = pm?.UserParameter?.GetCharacterParameter(playerID);
                if (cp != null)
                {
                    hp = cp.HitPoint; hpMax = cp.HitPointMax;
                    mp = cp.MentalPoint; mpMax = cp.MentalPointMax;
                    if (string.IsNullOrEmpty(name)) name = cp.CharacterName;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampItemTarget: param lookup error: {ex.Message}");
            }

            if (string.IsNullOrEmpty(name)) name = TitleCasePlayerID(playerID);

            var sb = new StringBuilder();
            sb.Append(name).Append(". ");
            if (hpMax > 0) sb.Append(Loc.Get("camp_item_target_hp", hp, hpMax)).Append(". ");
            if (mpMax > 0) sb.Append(Loc.Get("camp_item_target_mp", mp, mpMax)).Append(". ");
            sb.Append(Loc.Get("camp_item_position", idx + 1, total));

            DebugLogger.LogGameValue("CampItemTarget.char",
                $"{name} HP {hp}/{hpMax} MP {mp}/{mpMax} ({idx + 1}/{total})");

            return sb.ToString().Trim();
        }

        /// <summary>Clears character-target tracking.</summary>
        private static void ResetItemCharacterSelect()
        {
            _itemCharWasActive = false;
            _itemCharLastIndex = -1;
        }

        /// <summary>Fallback name from a PlayerID enum (e.g. CLAUDE -> Claude) when the name is empty.</summary>
        private static string TitleCasePlayerID(PlayerID playerID)
        {
            string raw = playerID.ToString();
            if (string.IsNullOrEmpty(raw)) return raw;
            return char.ToUpper(raw[0]) + raw.Substring(1).ToLower();
        }
    }
}
