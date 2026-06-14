# Star Ocean: The Second Story R — Conceptual Game Model

Audience: AI assistants building accessibility mods for blind players.
Purpose: Concise factual reference for game structure, screens, controls, and mechanics.
Game: Star Ocean: The Second Story R (2023 remake by Square Enix / tri-Ace / Gemdrops).
Original: Star Ocean: The Second Story (1998, PlayStation).

---

## 1. Overview

- Genre: Action JRPG with real-time battles
- Perspective: 2.5D — 2D pixel sprite characters move through full 3D environments (towns, dungeons, world map)
- Developer/Publisher: Gemdrops / Square Enix (2023 remake)
- Platforms: PS4, PS5, Nintendo Switch, PC (Steam)

### Protagonist Choice

At the start the player chooses one of two protagonists. Both stories cover the same main plot but differ in scenes, perspective, and which characters can join.

- **Claude C. Kenny** — Federation officer's son, accidentally transported to the medieval planet Expel. Carries a Phase Gun the locals mistake for a mystical "Sword of Light." His route gives broader narrative context about the universe and Federation.
- **Rena Lanford** — Native healer on Expel with mysterious healing powers she cannot explain. She believes Claude is the prophesied hero. Her route is more personal and character-focused.

### Story Branch Summary

- Both characters meet and join up within the first hour regardless of choice.
- Certain party members are exclusive to one route (Dias = Rena only; Leon = Claude only).
- Two pairs of characters are mutually exclusive (can't get both in one playthrough):
  - Ashton Anchors vs. Opera Vectra + Ernest Raviede
  - Precis F. Neumann vs. Bowman Jeane
- The plot spans two planets: Expel (medieval) and Energy Nede (advanced civilization).
- Multiple endings exist, determined by character affinity built through Private Actions.

---

## 2. Spatial Model

### Field Exploration

- Free-movement 3D space. Player controls the protagonist directly, moving in all directions.
- Camera is positioned behind/above the player; can be rotated.
- Towns and dungeons are distinct named maps, each loaded separately.
- A mini-map is always visible in the corner; shows NPCs, chests, exits, save points, Private Action markers, and story objectives.
- Enemies appear as visible symbols on the field (colored mist/aura):
  - Green = weaker than the party
  - Purple = roughly equal
  - Red = stronger than the party
- No random encounters. Battles are triggered by physical contact with enemy symbols.
- Approaching enemies from behind stuns them at battle start (player advantage).
- Enemies approaching from behind stun the player party (enemy advantage).
- Save points are scattered through towns and dungeons. Dungeons have Recovery Save Points that fully restore HP and MP.
- Interactable objects: treasure chests, NPCs, exits, save points, quest markers.

### World Map

- A separate traversal mode: overhead view of the planet surface.
- Player navigates using a movement stick (not free-3D; more like a top-down map).
- Early game: travel on foot or using a Bunny (a fast travel mount).
- Mid game: Psynard (a flying creature, equivalent to a Final Fantasy airship) is obtained, enabling flight and access to hidden/elevated locations.
- Bunny Call specialty: summons the Bunny for fast overland travel.
- The world map has two planets to traverse across the game's two halves (Expel then Energy Nede).

### Dungeon / Town Layout

- Multi-floor 3D spaces navigated by walking. Some have puzzle elements (switches, warp panels).
- Vertical movement via stairs, ledges, and some one-way jump-down spots.
- Shops, inns, save points, and NPCs appear inside towns.
- Dungeons contain enemies, chests, and boss rooms.

### Battles

- Arena-based: when contact with an enemy symbol occurs, a separate enclosed 3D arena opens.
- The battlefield is a flat rectangular arena. Party members and enemies move freely in it.
- After battle, the player returns to the field at the same position.
- Chain Battles: multiple enemy groups can be engaged in sequence (up to 5 waves), earning multiplied EXP, Fol, SP, and BP rewards.

---

## 3. Control Scheme

Note: The game supports both gamepad and keyboard. Keyboard uses WASD for movement. The game is primarily designed for gamepad; keyboard support exists but some actions may have awkward bindings.

### Gamepad (PlayStation layout / Xbox equivalent)

Field controls:
- Left Stick — move character
- Right Stick — rotate camera
- Circle / B — confirm / interact / talk
- Cross / A — cancel / back
- Triangle / Y — open camp menu
- Square / X — toggle AI targeting (unverified for field use)
- L1 / LB — activate Move Set 1 (Special Art slot 1) in battle; also used for mod nav hook
- R1 / RB — activate Move Set 2 (Special Art slot 2) in battle
- L2 / LT — switch targets in battle
- R2 / RT — switch controlled character in battle
- D-Pad — directional shortcuts to camp sub-menus; also trigger Assault Actions in battle
- Options / Start — show/hide mini-map or pause
- Trackpad / View — full map

Battle controls (same controller, additional context):
- Circle / B — normal attack (mash)
- L1 + direction / R1 + direction — activate assigned Special Arts or Symbology
- Double-tap L1 or R1 — Combo Link (fires a pre-set special art chain)
- L2 / LT — switch active target
- R2 / RT — switch which character the player controls
- D-Pad directions — trigger Assault Actions for reserve party members (when their Assault Gauge is full)

### Keyboard (PC)

- WASD — movement
- C — evasion/dodge (unverified as default; observed in community discussions)
- Other bindings are remappable in settings; exact defaults are not publicly documented in detail

---

## 4. Battle System

### Structure

- Real-time action; no turn menus.
- Up to 4 characters in the active combat party; up to 4 additional in reserve (8 party members max).
- Player directly controls one character at a time; can switch mid-battle with R2/RT.
- Other active party members are controlled by AI (tactics can be configured).

### Core Mechanics

**Normal attacks:**
- Mash the attack button (Circle/B) to chain normal hits.

**Special Arts / Symbology:**
- Special Arts are combat moves (melee characters). Symbology are spells (mage characters).
- Assigned to L1 and R1 button slots, with directional input for multiple assignments.
- Double-tapping L1 or R1 triggers a Combo Link (preset art chain).
- Spells have a cast time; getting hit interrupts and cancels casting.
- MP is consumed by Special Arts and Symbology.
- BP (Battle Points) are spent in the Camp menu to level up arts and spells.

**MP (Magic Points):**
- Consumed by Special Arts and Symbology spells.
- Maximum MP: 999.
- Successful Just Counter recovers 25% of a character's max MP (amount inversely proportional to current HP).
- MP also recovers partially after battles based on the STM (Stamina) stat.

**HP (Hit Points):**
- Maximum HP: 9,999.
- If a character's HP reaches 0 they are KO'd.
- Party wipe = game over.

**Break System:**
- Enemies have shield icons (shown as red segments on their health display).
- Continuous damage depletes shield segments.
- When all shield segments are destroyed: the enemy is Broken — briefly stunned, takes only critical hits, defense is bypassed.
- Shields regenerate after a few seconds.

**Just Counter System:**
- Perfectly timed dodge at the moment of an enemy attack.
- Success: character attacks from behind the enemy, recovers 25% MP.
- Failure or mistimed: no bonus; normal dodge or damage occurs.
- Failing a counter resets the Bonus Gauge.

**Bonus Gauge System:**
- A gauge (up to 3 levels) that builds during battle by: defeating enemies, landing critical hits, breaking enemy shields.
- Each level grants passive bonuses to the whole party determined by the equipped Formation.
- Gauge resets on: player character KO'd, failed Just Counter, enemy back attack.

**Assault Action System:**
- Reserve characters (not in active 4) have an Assault Gauge that fills over time.
- When a reserve character's gauge is full, pressing the corresponding D-Pad direction activates their Assault Action — they appear and use a Special Art or Spell.
- Reserve characters cannot be damaged and their MP is not consumed during Assault Actions.
- "Assault Formation" is a specific formation that enables summoning past Star Ocean protagonists (as NPCs) in a similar manner.

**Fury / Rage:**
- When a party member is KO'd, an ally with a high affection/relationship level to that character becomes enraged (fiery aura).
- Rage: doubles physical damage for approximately 30 seconds or until the enraged character is defeated.
- Righteous Fury: a character trait that doubles the duration of Rage.
- The Berserker Ring accessory grants permanent Rage status.

**Targeting:**
- L2/LT cycles through available enemy targets.
- Square/X can toggle AI-assisted targeting (unverified exact behavior).

**Formations:**
- Set in the Camp menu (Tactics/Formation sub-screen).
- Determines party positioning at battle start and which bonus the Bonus Gauge levels grant.
- Also sets AI behavior (Tactics) and which character is the Assault Formation summon.
- Examples include Square Shift (boosts Bonus Gauge bonuses), Escape Shift (at max stacks gives 50% chance not to consume items), and others.

**Item usage in battle:**
- Items can be used in battle.
- After using an item, a cooldown timer appears on the HUD (universal for all items — one item use at a time for the whole party).

---

## 5. Core Out-of-Battle Systems

### Camp Menu (Main Menu)

Accessed with Triangle/Y at any time outside of battle. Sub-screens:

- **Status** — view each character's stats, level, EXP, HP/MP, equipment, portrait, and Talents page
- **Item** — manage inventory (use items, see descriptions, discard)
- **Equip** — change weapons, armor, accessories for each character
- **Battle Skill** — spend BP to level up Special Arts, Symbology spells, and Combat Skills
- **Formation / Tactics** — set battle formation, AI tactics per character, Assault Formation assignment
- **Specialty (IC)** — access Item Creation and Specialty skills (see below)
- **Missions** — view and claim Guild Missions and Challenge Missions rewards
- **Database** — item encyclopedia, enemy encyclopedia, location discovery log, tutorials
- **System** — save/load, settings, quit

### Item Creation (IC)

A crafting system accessed through the Specialty sub-screen in the Camp menu. There are approximately 17 IC/Specialty types total, unlocked and leveled up by spending SP on prerequisite skills.

IC types that produce items:
- **Crafting** — converts minerals into accessories. Requires Mineralogy, Eye For Detail, Aesthetics skills.
- **Customization** — modifies weapons using minerals.
- **Compounding** — produces medicines and potions from herbs.
- **Cooking** — produces restorative food items.
- **Alchemy** — transmutes iron into precious metals (ores).
- **Writing (Authoring)** — produces skill-enhancing books and Leon's weapons. Requires Penmanship skill.
- **Machinist** — produces bombs, gadgets, and support items.
- **Art** — produces cards, paintings, sculptures for combat and exploration.
- **Appraising** — identifies mysterious/unidentified items.
- **Replication** — creates duplicates of existing items.
- **Music** — composes musical pieces (narrative/bonus use).

Specialty types (no physical item output):
- **Train** — increases EXP gain.
- **Scouting** — adjusts enemy encounter rates / behavior on field.
- **Survival** — auto-forages random materials during travel.
- **Familiar** — summons a bird that can shop on the party's behalf.
- **Oracle** — provides tips and occasional stat boosts.

Super Specialties (combine multiple characters' skills):
- **Orchestra** — boosts success rates for all IC.
- **Enlightenment** — boosts SP and BP earned per battle; reduces Fol earned.
- **Bunny Call** — summons the Bunny mount for fast world map travel.
- **Bodyguard** — prevents surprise/back attacks.
- **Publication** — produces books for income.
- **Group Appraising** — provides shop discounts.
- **Blacksmith** — crafts armor pieces.
- **Contraband** — creates items for profit (unverified exact function).
- **Remaking** — reworks existing items (unverified exact function).

IC rules:
- Most IC can be performed anywhere via the Camp menu; no workbench needed.
- Each IC attempt uses materials and may succeed or fail; higher skill level = better success rate.
- Material level affects output quality.
- Some IC types require multiple characters to contribute skills (Super Specialties).

### Skills and Skill Points (SP)

- Characters have a set of learnable skills (e.g., Mineralogy, Penmanship, Determination, Effort, Herbology, Eye For Detail, Aesthetics, Whistling, Animal Training, Pickpocket, etc.).
- Each skill has 10 levels, upgraded by spending SP.
- Skills unlock and raise the level of IC/Specialty abilities.
- Skills also grant direct stat bonuses (e.g., Mineralogy raises INT by 3 per level).
- SP is earned from: leveling up, Guild Missions, Challenge Missions.
- Determination skill reduces SP costs for other skills; Effort reduces XP needed to level up.

### Battle Points (BP)

- BP is a separate currency from SP.
- Used in the Battle Skill sub-screen of the Camp menu to: level up Special Arts, level up Symbology spells, unlock and upgrade Combat Skills.
- Combat Skills are passive abilities that trigger randomly in battle; trigger chance increases with level.
- BP is earned from: leveling up, battles, missions.

### Private Actions (PA)

- Short character-specific scenes triggered at marked locations in towns.
- Shown on the mini-map with PA icons (standard or time-limited variants).
- The player's protagonist interacts one-on-one with a party member or NPC.
- Dialogue choices affect character affection levels.
- Affection levels influence: story scenes, available endings (ending pairings), and whether certain characters leave the party permanently.
- PAs can also yield item rewards and open optional side events.
- Wrong choices can decrease affection.

### Shops

- Weapon shops, armor shops, item shops in towns.
- Standard buy/sell interface; currency is Fol.
- Some secret shops are only accessible via Psynard flight to hidden spots.
- Group Appraising super specialty provides shop discounts.

### Guild and Missions

- Guild buildings in towns offer Guild Missions (fetch quests, IC tasks, combat tasks).
- Completing Guild Missions earns SP, Fol, items.
- Challenge Missions are automatically tracked in-game achievements (hundreds of them), covering combat objectives and IC objectives.
- Both types are claimed from the Missions sub-screen in the Camp menu.

### Equipment

- Weapons, armor (body, head, legs, accessory slots).
- Managed via the Equip sub-screen.
- Customization IC can add or modify weapon properties (Factors).
- Blacksmith super specialty crafts armor.
- Best accessories are often only obtainable through Crafting IC.

### Pickpocketing

- A specialty (Pickpocket skill required).
- Player walks near NPCs and attempts to steal items.
- Success rate improved by the Nimble Fingers talent.
- Items obtained vary by NPC; some unique items only obtainable this way.

### Fishing

- Accessible at specific docks/ports (e.g., Harley).
- Mini-game to catch fish of different types.
- Collecting 15 different fish species earns the Masterwork Rod reward.
- Fish may be used as cooking ingredients.

### Save System

- Save points are fixed spots in towns and dungeons.
- Recovery Save Points in dungeons also restore full HP and MP.
- No autosave mid-dungeon (unverified whether autosave exists at other triggers).
- Save/Load is accessible from the Camp menu System sub-screen, but only at save points (unverified whether menu save works anywhere).

### Transportation / Fast Travel

- Early: walk on world map.
- Bunny Call specialty: summons a Bunny for faster world map travel.
- Mid-game: Psynard (flying mount) obtained in North City; enables flight to any location.
- Private Action / quest markers visible on map as icons.

---

## 6. Game Screens Enumeration

Listed roughly in order of first encounter. One-line descriptions.

- **Title Screen** — logo, New Game / Continue / Settings options.
- **Protagonist Select** — choose Claude or Rena before the game begins.
- **Cutscene / Dialogue Screen** — story scenes with character portraits, voiced dialogue, text boxes, and occasional dialogue choices.
- **Field HUD** — main play view: character sprite + 3D environment, mini-map (corner), party HP/MP bars, encounter symbols visible on map.
- **World Map** — top-down overworld traversal view; shows continent outlines, town icons, dungeon icons.
- **Battle Screen** — 3D arena with party and enemies; shows HP/MP bars per character, enemy HP/Break gauge, Bonus Gauge (3 pips), Assault Gauges for reserve characters (D-Pad icons), item cooldown timer, and active battle text.
- **Camp Menu Root** — main out-of-battle menu listing: Status, Item, Equip, Battle Skill, Formation, Specialty, Missions, Database, System.
- **Status Sub-screen** — character stats page with HP/MP/level/EXP/equipment; secondary Talents page.
- **Item Sub-screen** — inventory list; item name, count, description.
- **Equip Sub-screen** — character equipment slots; browsable gear list with stat comparisons.
- **Battle Skill Sub-screen** — Special Arts / Symbology list per character; BP spend interface.
- **Formation / Tactics Sub-screen** — formation grid picker, AI tactic assignments, Assault Formation selection.
- **Specialty / IC Sub-screen** — lists learned IC/Specialties; launches the IC crafting interface for each.
- **IC Crafting Interface** — select material, see possible outcomes, confirm attempt; shows skill level and success chance.
- **Missions Sub-screen** — two tabs: Guild Missions (fetched from guild NPC), Challenge Missions (auto-tracked achievements); claim rewards here.
- **Database Sub-screen** — item encyclopedia, enemy encyclopedia, discovered location list, tutorial review.
- **System Sub-screen** — save game, load game, settings (audio, display, controls), quit to title.
- **Shop Screen** — buy/sell interface with item list, prices, and party Fol displayed.
- **Private Action Scene** — triggered on field; dialogue-only screen with single NPC/party member, portrait, text, choice prompts.
- **Battle Result Screen** — post-battle summary: EXP gained, Fol gained, SP/BP gained, items obtained, level-up notifications.
- **Level-Up Notification** — brief overlay (may be part of battle result) showing new level and stat increases.
- **Game Over Screen** — appears on full party wipe; options: Retry (return to camp with option to adjust party/equipment) or Title Screen.
- **Save/Load Screen** — slot list with save file info (location, playtime, character level).
- **Settings Screen** — audio, display, button layout, controls.
- **Map Screen** — full-screen map toggled via Options/Trackpad; shows full dungeon or town layout.
- **Fishing Mini-Game** — triggered at dock; simple timing-based catch interface.
- **Pickpocket Interface** — proximity-based; no separate screen, outcome announced in field.

---

## 7. Key Mechanics with Numbers and State

### Character Stats

Each character has:
- **HP** — Hit Points. Max 9,999.
- **MP** — Magic Points. Max 999. Consumed by arts and spells.
- **ATK** — Attack power; affects damage of normal attacks and Special Arts.
- **DEF** — Defense; reduces physical damage received.
- **HIT** — Accuracy; affects hit rate of attacks and arts.
- **AVD** — Avoidance/Dodge; affects rate of dodging physical attacks.
- **INT** — Intelligence; affects spell damage and healing power.
- **LUC** — Luck; affects item drop rates.
- **STM** — Stamina; affects HP and MP recovery amount after battle.
- **Level** — Characters level up from 1 to a maximum of 255.
- **EXP** — Experience points; accumulated to reach next level.

### Points Currencies

- **SP (Skill Points)** — spent to level up IC/Specialty skills. Earned from leveling, missions, guild quests.
- **BP (Battle Points)** — spent to level up Special Arts, Symbology, and Combat Skills. Earned from leveling and battles.
- **Fol** — in-game currency. Earned from battles and selling items. Used at shops and for some IC.

### Party

- Max active party: 4 characters.
- Max party roster: 8 characters (4 active + 4 reserve for Assault Actions).
- Total recruitable characters in the game: 13 (including Claude and Rena).
  - Mandatory: Claude, Rena (whichever is not chosen becomes a party member shortly after).
  - Always available to recruit: Celine Jules, Noel Chandler, Chisato Madison, Welch Vineyard.
  - Route-exclusive: Dias Flac (Rena route only), Leon D.S. Gehste (Claude route only).
  - Mutually exclusive pair A: Ashton Anchors vs. Opera Vectra + Ernest Raviede.
  - Mutually exclusive pair B: Precis F. Neumann vs. Bowman Jeane.

### Difficulty Modes

All four difficulties are available from the start and can be changed at any time:
- **Earth Mode** — easiest; enemies deal minimum damage; for story-focused players.
- **Galaxy Mode** — default/normal; intended as the standard experience.
- **Universe Mode** — hard; major difficulty spike, enemies can one-shot unprepared characters.
- **Chaos Mode** — added in Version 1.1.0 update; hardest available; far above Universe.

### Battle Economy

- Chain Battles: up to 5 enemy waves fought in sequence; rewards multiplier increases with each wave (EXP, SP, BP, Fol all multiplied).
- Break bonus: broken enemies take only critical damage, defense bypassed.
- Just Counter MP recovery: 25% of character's max MP (inversely scaled with current HP).
- Bonus Gauge: 3 levels, reset on player KO, failed counter, or back attack.
- Fury duration: approximately 30 seconds active, doubles physical damage output.

### Item Creation Success

- IC success chance depends on character's skill level in prerequisite skills (e.g., Crafting = mean of Mineralogy + Eye For Detail + Aesthetics levels).
- Orchestra super specialty boosts success rates across all IC.
- Failed IC attempts still consume materials.

---

*End of game model. Compiled June 2026 from training knowledge plus web verification of specifics.*
*Sources verified: battle system details (fandom/dualshockers), SP/BP mechanics (dualshockers), difficulty modes (primagames/siliconera), party/characters (progameguides/thegamer), Private Actions (rpgsite), encounter system (fandom/rpgfan), IC types (steamcommunity guide), world map (multiple walkthroughs), controls (spottis.com), narrative/story (rpgsite/thegamer).*
