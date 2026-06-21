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

## Item Creation — selected character not announced when opening a skill

When you open a specialty skill in Item Creation (for example Art or Cooking), the mod does
not announce which party member is selected by default.

- Workaround: press L1 or R1 to switch between party members — the character's name **is**
  announced each time you switch, so a quick tap of L1 then R1 (or vice versa) will tell you
  who is currently selected.
- Why: the game announces the recipe before the mod can tell which character is highlighted,
  so the name cannot be read at the moment the skill opens.
