# SO2RAccess — Known Issues

These are limitations we are already aware of. **You do not need to report them.**
They are listed here so you know what to expect. If you run into something that is
**not** on this list, please do report it.

---

## Guild menu — partially reads

In the Guild, the **mission list now reads**: each mission's name, status, and position
(e.g. "Customization Mission 1, Available. 1 of 7.") are announced as you move the cursor.

What is still **not** read (a game limitation we don't expect to fix):
- The guild master's first command menu (choosing "accept a mission" vs "report a mission")
  is drawn natively with no cursor the mod can follow — it stays silent. Operate it by
  position.
- A mission's full description and completion reward are not read at the guild itself.
- Workaround for both: open the **Quests** menu in the Camp menu to read mission descriptions
  and rewards there.

## World map auto-walk and towns

On the world map, auto-walk cannot tell which side of a town you are standing on, nor that
you may need to enter a town and walk out the other side to continue. When a town sits
between you and your destination, the auto-walk can run the player into the edge of the
town and report a false "stuck" / "cannot reach" message even though nothing is actually
broken.

- Workaround: manually walk into and through the town to the far side, then start
  auto-walk again from there to continue toward your destination.
- Note: the world map has a few other routing limitations in general; this is the most
  common one to be aware of.

## Assault Actions — skills have no description on this screen

In the Assist Formation (Assault Action) screen, hovering a character reads their skill's
name, type, and cooldown — which is everything the game shows there; the screen displays
no skill descriptions for anyone. For **party members'** skills you can read the full
description in the Battle Skills menu. For **assist-only characters** (guests like
Laeticia who cannot join the party) the game data contains no description text at all,
so there is nowhere to look one up.

## Item Creation — selected character not announced when opening a skill

When you open a specialty skill in Item Creation (for example Art or Cooking), the mod does
not announce which party member is selected by default.

- Workaround: press L1 or R1 to switch between party members — the character's name **is**
  announced each time you switch, so a quick tap of L1 then R1 (or vice versa) will tell you
  who is currently selected.
- Why: the game announces the recipe before the mod can tell which character is highlighted,
  so the name cannot be read at the moment the skill opens.

## Manual navigation sounds — what the beacons and wall tones cannot know

- **Jump point beacons only mark ledges you have already jumped down once.** The game has no
  list of jump-down ledges; the mod learns them from your own walks (the same breadcrumb data
  that powers "reachable" checks). A ledge you have never used stays silent until you find it
  with the game's own "press X to jump" prompt.
- **Stairs and ladder beacon has no sound yet.** The row is in the menu and the mod looks for
  `NavStairs.wav` in `UserData\SO2RAccess\Sounds`; until a file is there, stairs and ladders
  are silent in this mode (they are still in the navigation list).
- **Very narrow gaps can sound closed.** The wall probe samples the floor every 0.75 m, so an
  opening narrower than that may play a wall tone even though the character fits. Walk up to
  it; the tone does not stop you.
- **Wall tones are muted during auto-walk** on purpose — the mod is steering, and the tones
  would only be noise. Beacons keep playing.
- **Wall tones are off by default and can be wrong at very steep spots.** They only count the
  game's own wall layers and ignore climbable slopes, but a place where you climb a metre or
  more within a step or two (rough mountain paths, some cave stairs) can still play a tone.
  In the audit on Lasgus Mountains and Krosse Cave that happened on about one walked link in
  five hundred. Turn them on in Wall sounds if you want them; keep the start distance short
  (the slider stops at 8 m) to hear fewer distant guesses.
- **Wall bump sound is not available yet.** The mod already knows when you push against
  something and do not move — a bump that cannot be wrong — but no sound has been chosen, so
  it is hidden from the menu for now.
