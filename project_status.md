# Project Status: SO2RAccess

## Project Info

- **Game:** Star Ocean: The Second Story R
- **Engine:** Unity (IL2CPP)
- **Architecture:** 64-bit
- **Mod Loader:** MelonLoader v0.7.2-ci.2398
- **Runtime:** net6
- **Unity Version:** 2021.3.22f1
- **Developer:** SquareEnix
- **Game directory:** E:\Program Files\Steam\steamapps\common\STAR OCEAN THE SECOND STORY R
- **User experience level:** Little/None
- **User game familiarity:** Somewhat
- **Languages:** English built in; community translations via JSON files in UserData\SO2RAccess\lang (since 0.2.0, see TRANSLATING.md)

## Setup Progress

- [x] Experience level determined
- [x] Game name and path confirmed
- [x] Game familiarity assessed
- [x] Game directory auto-check completed
- [x] Mod loader selected and installed (MelonLoader)
- [x] Tolk DLLs in place (Tolk.dll + nvdaControllerClient64.dll)
- [x] .NET SDK available (8.0.424 + 10.0.400 side by side; builds use SDK 10 since 2026-08-29)
- [x] Decompiler tool ready (ilspycmd 11.0.0.9375, updated 2026-08-29; needs the .NET 10 SDK)
- [x] Game launched once with MelonLoader (log + IL2CPP stubs generated)
- [x] Game code decompiled to `decompiled/Assembly-CSharp/`
- [ ] Tutorial texts extracted (if applicable)
- [x] Multilingual support decided (English built in + community translation files, v0.2.0)
- [x] Project directory set up (SO2RAccess.csproj, Main.cs, ScreenReader.cs, DebugLogger.cs, Loc.cs)
- [ ] CLAUDE.md updated with project-specific values
- [x] First build successful (SO2RAccess.dll copied to Mods folder)
- [x] "Mod loaded" announcement working in game

## Current Phase

**Phase:** Phase 3 — Feature Implementation

> 🚀 **v0.4.0 RELEASED (2026-09-06, end of session 13).** https://github.com/Yakku5226/SO2RAccess/releases/tag/v0.4.0
> Commit a79775e on master + tag v0.4.0, both pushed; asset SO2RAccess.zip (24.6 MB). Middle-number bump (user's call): the whole manual-navigation sound system ships — LoopMixer, wall tones
> (default OFF, slider 2–8 m), object beacons (NPC / chest / door / location / save / jump; stairs placeholder),
> two new F4 submenus — plus the day's fixes (R2 Party↔Assault swap, camp quick heal, battle log flood, dodge
> double-fire guard, save-point marimba). Release build 0 warnings. Zip = `SO2RAccess-0.4.0\` root, asset
> `SO2RAccess.zip`: SO2RAccess.dll (Release), Tolk.dll, nvdaControllerClient64.dll, README.md, LICENSE,
> KNOWN_ISSUES.md, SOUND_CREDITS.txt, TRANSLATING.md, lang\ (fr/de/sv/pt/zh-Hans). MelonInfo 0.4.0.
> **User decision on the wall audit:** "good enough" — wall tones stay opt-in at ~0.2 % false on short walked
> links (steep spots); KNOWN_ISSUES has the caveat. **Wall bump cue = FRAMEWORK ONLY:** `NavCueKind.Bump`
> (`NavBump.wav`, not bundled), detector `ManualNavHandler.UpdateBump` (push stick / WASD while position does
> not change ≥ 0.15 s → one-shot via LoopMixer autoStop 0.5 s, repeat 0.6 s, new direction at once), hidden from
> the menu because `NavCues.All` omits Bump, default disabled. Loc keys `mod_menu_label_wall_bump(_volume)`
> already in all six files. **TODO:** pick the bump sound → gzip `soundcues\NavBump.wav.gz` → add Bump to
> `NavCues.All` after WallLeft → credit it. Also still open: stairs sound (`NavStairs.wav`), rear-mode
> preference, spoken-directions quality opinion, guide reminder row test.

> 🔧 **2026-09-06 (session 13): FIRST SOUND TEST DONE — two fixes built, ⏳ UNTESTED, NOT committed.**
> User tested all the manual-navigation sounds: "still a bit overwhelming, will have to get used to it" (no
> tuning request yet; the range row 10/15/20/30 m and per-cue toggles are the levers if it stays too much).
> Two problems reported, both fixed in this build (DLL copied to Mods):
> 1. **Save points played the location shimmer.** By design the location cue covered markers + save points +
>    warps; the user hears the shimmer as "unique locations only". New cue `NavCueKind.Save` → `NavSave.wav`
>    (= the user's reserve pick "Marimba note struck", fs 185833, LloydEvans09, CC0; `soundcues\NavSave.wav.gz`,
>    credited in SOUND_CREDITS.txt + README). Own on/off + volume rows ("Save point beacon") right after the
>    Location rows; `NavCues.All` is now an explicit ordered array. Warps still share the location shimmer.
>    ⏳ ASK: is the marimba OK for save points, or should it be another sound?
> 2. **R2 on Party Formation opened a silent screen.** It is Assault Formation (game's label for the
>    `AssistFormation` root item). R2/L2 swap the two sibling selectors WITHOUT returning to the root menu, so
>    `_lastRootMenuItemName` stayed "PartyFormation" and `UpdateAssistSettingSelector` was gated out (log 11:07:
>    only "ListSelection ... suppressed (dedicated handler owns it)"). Fix: `SyncFormationSiblingScreen` polls
>    `UICampWindow.selectorStack.Peek()` each frame + Show() postfixes on both selectors; either flips the gate
>    and resets both SubScreenStates so heading + row are spoken, and swapping back speaks again. Logged as
>    `CampMenu: PartyFormation → AssistFormation (via stack|Show)`. Documented in game-api.md §17.
>    ⏳ TEST: Party Formation → R2 → expect "Assault formation." + the first slot row; L2 → "Party formation." +
>    character row. If still silent, the log line "selector stack top is now ..." says what the game did.
> 3. **Distance sliders (user request, same session).** New `Metres()` row builder (ModMenuHandler.cs, ±1 m per
>    press, stops at the ends, no wrap). Wall sounds menu gained "Wall sound start distance" 2–15 m (default 6,
>    `ModSettings.WallRangeMeters`; passed into `WallProbe.ProbeAround(range)` and the gain ramp — the probe's
>    `Range` const 6 m now only serves the audits). Beacon range became a 5–20 m slider (was 10/15/20/30
>    choices; old saved 30 clamps to 20). Persisted as `WallRangeMeters`. Loc key `mod_menu_label_wall_range`
>    in all six files (en 817 / others 800). README updated. ⏳ TEST both rows, and whether a long wall range
>    (say 15 m) causes false tones — it probes further over unaudited floor.
> ✅ User tested 1–3 (11:42–11:56 log): "all appears to be working well" — R2 swap announced via the Show
>    postfix (`PartyFormation → AssistFormation (via Show)`), save beacons started as `Save`, sliders fine.
> 4. **Dodge cue "harder to dodge" (user report).** Log audit: the cue is PlaySound async, fired inside the game's
>    own `DoAttackNotify` postfix, speech in that fight was sparse — NO mod-side delay path found. Two things fixed
>    anyway: (a) DEBUG LOG FLOOD in battle — `BattleMenu: operation selector active but window IsOpened=false`
>    every frame (5,700 lines in one fight) + `unhandled selector on stack: UIBattleResultSelector` (1,600) —
>    MelonLogger writes synchronously, so F12-on battles stuttered; both now latch (log once per state change).
>    (b) `DoAttackNotify` fires TWICE ~50 ms apart for some attacks; PlaySound restarted the cue → stutter; now
>    one warning per 150 ms window. ⏳ ASK the user to judge dodging with F12 OFF; if still late, the game's own
>    flash timing is the limit (we fire at the same instant as the flash).
> 5. **Camp quick heal silent (D-pad Right on the camp root menu).** A GAME feature (binding CampQuickRecovery),
>    separate class `UICampQuickRecoverySelector`; the camp window reports IsOpened=false while it shows, so the
>    mod thought the camp closed. `QuickRecoveryHandler` now handles both variants: Show/Hide/ForceHide postfixes
>    flag the camp dialog, `CurrentList()/CurrentChoice()` read whichever is open, heading/Yes-No/party status/
>    result all shared; new `FieldState` gate `IsCampRecoveryOpen`. game-api.md §17. ⏳ TEST: camp → D-pad Right →
>    "Quick Recovery. Recover party? ..." then Yes/No + result.
> 6. **F11 WALL AUDIT (12:49 Lasgus, 12:52 Krosse): FAIL on both → TUNING ROUND 1 applied (the one allowed).**
>    Floor grid audit PASSED on both (1548/1548 + 1499/1499, 0 drop violations). Wall probe: Lasgus 2,749 false
>    walls / 18,814 walked edges (Face 2,512, FloorStep 237), Krosse 469 / 16,436 (Face 426, FloorStep 27,
>    FloorGap 16). Top offenders were all OUTSIDE the game's foot wall mask: L24 Mesh_Col/Mesh_L0/Col_Height
>    (floor meshes), L25 Blend/In_House/Camera_Blend_Box/Bridge/Center (camera + area volumes), L27
>    FootstepCollision, L10 Global Volume, L20 Crossing; plus L22 MapCollision 60 / L15 collider 32 in Krosse at
>    0.5–0.7 m (knee ray vs climbable steps). Changes in `WallProbe`: face rays use `GameRenderManager.LayerMaskWall`
>    (live, fallback 0x04E28000) only; knee ray 0.35 → 0.6 m (above MaxStepY 0.5); floor rays skip volume layers
>    10/25/27. ALSO FIXED: `ProbeDirection` clamped range to 6 m, so the new wall slider had no effect above 6 —
>    now `MaxRange` 15. Wall tones STILL DEFAULT OFF. ⏳ RE-AUDIT: F12, F11 on both maps → need
>    `[WALLAUDIT] RESULT PASS` (0 false walls). If it fails again → REMOVE the four wall rows + WallProbe drive,
>    keep beacons (user rule, no second tuning round).
> ✅ Fights: user says better; log shows the battle log flood gone (2 + 1 lines instead of thousands) and the
>    double-fire guard triggered once in 11 warnings.
> 7. **RE-AUDIT after round 1 (13:01): Krosse 469 → 119, Lasgus 2,749 → 331 — still FAIL.** User asked for one
>    more attempt before dropping walls ("anything else you can do?") and to CAP the wall slider at 8 m.
>    Evidence: Lasgus 274 of 331 = FloorStep on L24 floor meshes (walked slopes failing the 0.5 m-per-0.75 m
>    rule, which is STRICTER than the breadcrumb-validated grid rule of 1.0 m per 1.5 m cell); Krosse 60 L22 +
>    32 L15 Face hits on 2.8–4.1 m breadcrumb links — TraversalGraph merges nodes within 1.6 m, so long straight
>    links clip corners the player walked round = audit artefact, not a probe fault. LIVE readouts this session
>    (66 `[WALLS]` lines, walls switched on by the user) showed ONLY 'MapCollision'/L22 faces — no volumes.
>    ROUND 2 applied: floor walk uses the validated rule over a two-sample window (`MaxWindowY` 1.0 m) + 0.75 m
>    per-sample cap; audit now logs short links (≤ 2 m) separately (`short links: N false on M judged`);
>    `WallProbe.MaxRange` + `ModSettings.WallRangeMax` = 8. Reference mod (E:\SO2r_SR_test.zip, decompiled in
>    reference-mods\) checked: its "sonar" is a single 3 m raycast per side on ALL layers at 0.8 m height with no
>    normal/layer filtering — cruder than ours, nothing to borrow for precision. Its one strong idea: a "wall
>    bump" cue that fires only when the player pushes the stick and the position does not change (true blockage,
>    zero false positives by construction). PROPOSED to the user as an addition, not built.
>    RE-AUDIT after round 2 (13:57): Lasgus 331 → 101 (short links 13 false / 7,076; long 88 / 11,844);
>    Krosse 119 → 112 (short 15 / 6,056; long 97 / 10,380). Floor grid audits PASS on both. Remaining short-link
>    false walls are all at steep walked spots (1.0–1.1 m rise over 1.5–2 m, ratio ≥ 0.55; stair faces on
>    L22 in Krosse; one 16-edge FloorGap cluster around Krosse (33–35, 8.7, 78) where the floor ray finds no
>    solid floor). ⏳ USER DECISION PENDING: drop the wall tones (the rule), or keep them opt-in (default OFF)
>    at ~0.2 % false on short links, and/or add the reference mod's "bump on real blockage" cue.
> ✅ Camp quick heal WORKED (13:02:50 party status, 13:02:54 result) — "Recovery complete.." double period fixed.
> ✅ Dodge: the 13:02 fight had ONE attack notify (13:02:10) → one cue; the cue mirrors the game's own incoming-
>    attack flash exactly, attacks without that flash never announce; Welch ended at full HP.
> Still pending from the plan below: F11 wall audit results on Lasgus + Krosse (wall tones still default OFF),
> rear-mode preference, stairs sound, older untested items. Lang files: en 816 keys, others 799 (same 17-key gap).

> ⏳ **NEXT SESSION (2026-09-06): TEST THE MANUAL NAVIGATION SOUNDS.** User said "I will test tomorrow."
> The DLL in the game's Mods folder is the current build (2026-09-05 evening, builds clean). Nothing committed.
> Start the session by asking for the results of the steps below, in order; each step is independent, so partial
> results are fine. Record every result here, then act on the "Claude then does" lines.
>
> **Before starting:** wall tones are switched OFF by default (rule: no geometry aid before its audit passes).
> Everything else (beacons, enemy cue) is on. No new keys were added — everything is in the F4 menu.
>
> **Step 1 — audio engine sanity (any field map, 2 minutes).**
> - Walk near a field enemy: the enemy proximity loop should still fade in, pan toward the enemy, and stop when
>   you move away or a dialogue/menu opens.
> - F4 → Sound and announcements → move to "Enemy proximity sound" and press Space (Square on pad): the loop
>   should play for about 1.5 seconds and stop by itself. Try two or three other rows too.
> - Report: does it sound the same as before? Any clicks, stutter, or a sound that keeps going after the menu
>   closes? (A stuck sound would be a mixer bug — nothing else can produce it now.)
>
> **Step 2 — F11 wall audit (Lasgus Mountains MF_0014_01A and Krosse Cave MF_0008_01A).**
> - Stand anywhere on the map, F12 (debug mode on), then F11. It speaks the floor-grid audit line first, then
>   "Wall probe audit: N false walls on M walked edges, wall density P percent." Do it on BOTH maps.
> - Report the two spoken lines (or just say "done" — Claude reads MelonLoader\Latest.log for the
>   `[WALLAUDIT]` lines, including the collider/layer evidence for every false wall).
> - Claude then does: PASS on both (0 false walls, density clearly above 0) → flip the wall default to ON in
>   `NavCues.Defaults` (`Enabled = !IsWall(kind)` → `Enabled = true`), rebuild. FAIL → ONE documented tuning
>   round from the evidence (exclude a named layer, or the step ratio), re-audit; still failing → remove the four
>   wall rows and the WallProbe drive, keep beacons (user rule).
>
> **Step 3 — wall tones live (after Step 2 PASS, or to preview them regardless).**
> - F4 → Wall sounds → turn on the four "… sound" rows (Space previews each: ahead and behind are centred and
>   differ in pitch, right is in the right ear, left in the left).
> - In a town or dungeon, face a wall and walk toward it: the ahead tone should rise from silent at about 6 m to
>   full at touching distance. Turn the camera a quarter turn: the same wall should move to the side tone.
> - Lasgus: stand at the foot of the slope you walked before (around x −35, y 9, z 31) facing up it and walk to
>   the top: NO wall tone ahead while the way is open. Stand in an open field: all four silent.
> - While auto-walking (nav list + `\` or L2 + stick up) the wall tones go quiet on purpose; beacons stay.
> - Report: any tone that played where you could walk (false wall), any wall that stayed silent, and whether
>   the four tones are easy to tell apart. Claude reads `[WALLS]` debug readouts if F12 was on.
>
> **Step 4 — object beacons (a town, e.g. Salva or Krosse).**
> - Default range 15 m, nearest 6 objects play. NPC = short "correct" hit every 1.2 s, chest = bolt-lock hit,
>   door/exit = gate latch every 3 s, location/save/warp = shimmer, jump ledge = jump hit (only ledges you have
>   jumped before). Stairs/ladders are silent (placeholder, see below).
> - Turn the camera with an NPC nearby: the beacon should swing between the ears; approach: louder; talk to
>   them: everything goes quiet, and resumes after. Open a chest: its beacon stops (within 10 s at most).
> - F4 → Object beacons → "Beacons behind you": Space previews the NPC beacon as if behind you; press Right to
>   switch Muffled ↔ Quieter only and preview again. Then walk past an NPC in town and listen to the change.
> - Report: does panning match where things are? Is six at once too many (there is a range row 10/15/20/30)?
>   Which rear mode do you prefer, or keep the toggle?
>
> **Still open afterwards:** pick a stairs/ladder sound (`NavStairs.wav`, drop it into
> `UserData\SO2RAccess\Sounds` to try it, then Claude bundles it); older untested items (climb points in the
> nav list, spoken-directions quality, guide reminder row). Then commit everything; version bump = user's call
> (Claude proposes the third number).

> 🔊 **MANUAL NAVIGATION SOUNDS (2026-09-05, session 12) — BUILT CLEAN, ⏳ UNTESTED, NOT committed.**
> Plan (user-approved): `C:\Users\Jaco\.claude\plans\enumerated-zooming-reddy.md`. User decisions: walls = volume
> only (no pitch); rear beacons = BOTH "Muffled" and "Quieter only" as a menu choice (Muffled default); stairs/ladder
> beacon = placeholder slot `NavStairs.wav` (user will pick a sound; Marimba stays unallocated); NO master key —
> every cue has its own on/off + volume in the mod menu.
> - **Audio:** `SpatialAudioPlayer.cs` DELETED → `LoopMixer.cs` (one waveOut device, 44.1 kHz, 4 × 25 ms buffers,
>   N voices summed) + `MixerVoice.cs` (50 ms gain ramps, constant-power pan, one-pole low-pass "muffle", auto-stop
>   for previews, STALE RULE: a voice not `Set()` for 0.5 s fades to silence — covers menu-open/handler-stall) +
>   `SoundBank.cs` (load-once cue cache, resamples to 44.1 k). Enemy proximity cue and menu previews migrated
>   (`EnemyProximityHandler` holds a voice; `ModMenuHandler.Sound.cs` `PreviewLoop(cue, vol, pan, muffle)`).
>   `LoopMixer.Update(dt)` runs from `Main.OnUpdate` every frame (closes the device after 10 s idle).
> - **Walls:** `WallProbe.cs` — per camera direction: (A) floor walk, downward RaycastAll every 0.75 m out to 6 m,
>   obstacle when |Δy| > 0.5 m (= FloorProbeGrid.MaxStepRatio 0.67 × step) or no floor; (B) knee (0.35 m) + waist
>   (0.9 m) horizontal rays, ONLY hits with |normal.y| < 0.4 count (slope faces ignored). Mask = everything but
>   player layer 6; `FloorProbeGrid.IsSolidFloorCollider` (now internal) shared. Never trusts the game's wall mask.
> - **Audit gate (F11, debug):** `NavigationHandler.WallProbeAudit.cs` `RunWallProbeAudit` — every breadcrumb edge
>   (minus one-way drops / ratio ≥ 1.2 ledges) probed along its direction; FALSE WALL = obstacle nearer than the
>   edge end − 0.3 m; logs `[WALLAUDIT]` per false wall with test + collider + layer, totals by test/collider, wall
>   density %, `RESULT PASS/FAIL`; spoken (`debug_wallaudit_result`). Runs right after `RunFloorGridAudit` on F11.
>   **WALL TONES DEFAULT OFF until PASS on Lasgus MF_0014_01A + Krosse MF_0008_01A** (NavCues.Defaults) — flip
>   `Enabled = !IsWall(kind)` → `true` after the gate passes. FAIL → one documented tuning round (layer exclusion
>   from the collider evidence, or the step ratio), then drop walls, keep beacons.
> - **Handler:** `ManualNavHandler.cs` (+ `.Beacons.cs`), ticks every 0.1 s, gates: `FieldState.IsFieldFree`, not
>   world map, any cue enabled; walls muted while `IsAutoWalking`; wall gain linear 6 m → 0.6 m, left/right hard
>   panned, front/behind centre; wall voice released after 2 s silence. Beacons: `NavigationHandler.Beacons.cs`
>   `TryGetBeaconTargets` reads `_categories` via `EnsureListReady(allowRefresh: userIdle, fromUser: false)`
>   (new `_lastNavKeyTime` stamp: never refreshes within 10 s of a modeless nav key, so item order stays stable);
>   map: NPC→NavNpc, unopened chest (`NavItem.Consumed` flag)→NavChest, exits+stone doors→NavDoor,
>   markers+save points+warps→NavLocation, stairs+climb→NavStairs (placeholder), `_traversal.OneWayDrops` high
>   ends (dedupe 2 m)→NavJump. Nearest 6 within `BeaconRangeMeters` (10/15/20/30, default 15); gain linear to
>   1.5 m; pan = camera-right component; rear = −forward component → Muffled (muffle=rear, ×0.6) or QuieterOnly
>   (×0.5). Debug: `[WALLS]` readout every 0.5 s, `[BEACON] start …`.
> - **Settings/menu:** `NavCues.cs` (`NavCueKind`, `BeaconRearMode`, `CueSetting`, file names, label keys);
>   `ModSettings.NavCue(kind)` dictionary persisted as `NavCues` {kind: {Enabled, Volume}} + `BeaconRear` +
>   `BeaconRangeMeters`. Two new root submenus (`ModMenuHandler.NavSounds.cs`): "Wall sounds", "Object beacons"
>   (rear row previews the NPC beacon as if behind you). Loc: 30 new keys, all six files (en 814; others 797 —
>   pre-existing 17-key gap from the en-only guide keys remains). `soundcues\` gained 9 gz files; SOUND_CREDITS.txt
>   updated; `NavStairs.wav` NOT shipped (user override folder or soundcues later).
> - **TEST ORDER:** (1) enemy cue + Sound menu previews still work, no clicks; (2) F11 on Lasgus + Krosse →
>   `[WALLAUDIT] RESULT PASS`; (3) enable wall tones → approach a wall, turn camera, climb the Lasgus slope
>   (must stay silent ahead), open field silent; (4) beacons in town (NPC pans/louder/quiet in dialogue; opened
>   chest stops; entrance latches every 3 s); rear-mode comparison. Then commit; version bump = user's call.

> ✅ **COMMITTED + PUSHED 2026-09-05 (end of session 11): spoken directions
> released to all players, floor-grid routes, climb points, sound bundling.**
> The L2 + stick-DOWN gesture's debug gate is REMOVED (Main.ProcessGamepad) —
> user-validated on the Lasgus summit climb. README: gamepad list, feature
> bullet, "Directions reminder" row; F1 help text in all six languages.
> Project `traversals\` refreshed from UserData for MF_0014_01A (Lasgus,
> 920 nodes) and MF_0006_01A. One commit carries the whole day (the
> sound-bundling and navigation changes overlap in Main.cs).
> Still ⏳ UNTESTED from today's last two rounds: reminder row (default Off),
> resume after battle, short "Unverified route.", queued item notifications,
> heading-change-only directions, removal of "Went upstairs/downstairs".
> NO version bump (user's call) — MelonInfo still 0.3.1.

> 🔊 **SOUND SET FOR THE MANUAL NAVIGATION SYSTEM — FINAL, USER-APPROVED (2026-09-05).** Allocation table: `sound-samples\CHOSEN.txt`; files: `sound-samples\final\` (mod-ready 16-bit 44.1 kHz mono, NOT yet in soundcues\). ALL cues loop as constant beacons:
> - NavNpc / NavChest / NavJump: short hit + silence, period 1.2 s. NavDoor: gate-latch phrase + silence, period 3.0 s. NavLocation: 1.5 s seamless shimmer loop. Wall_behind/left/right/front: 4 s seamless "breath" loops at 294/392/494/587 Hz (D-major spread). Marimba_reserve: 1.2 s beacon, unallocated.
> - Sources all CC0 (Freesound originals downloaded by the user; wall loops generated). Credit lines ready in `final\README.txt` → copy into SOUND_CREDITS.txt when shipping; gzip WAVs into `soundcues\`.
> - **NEXT SESSION: design + build the manual navigation system** (raycasts around the player → wall loops with volume/pitch by distance; spatial beacons for NPCs, chests, jump points, doors, locations). Discuss design before coding. SpatialAudioPlayer currently handles ONE loop with volume+pan — needs a multi-voice mixer for 4 walls + N beacons.

> 🔊 **SOUND SAMPLES FOR THE MANUAL NAVIGATION SYSTEM (2026-09-05) — GATHERED, ⏳ USER TO LISTEN AND PICK.**
> Folder `sound-samples\` (git-ignored via .gitignore, nothing ships until moved into `soundcues\` + credited in SOUND_CREDITS.txt). 153 WAVs, all 16-bit 44100 Hz mono, loudness-normalised. `sound-samples\README.txt` explains folders + how to listen; `sound-samples\CREDITS_DRAFT.txt` has origin/licence for every file.
> - `wall-proximity\`: four synthesised loop styles (softpad / pulse / shimmer / hum), 4 pitches each: behind 147 Hz, left 196, right 247, front 294 (D-major spread so overlapping walls sound like a chord). Loops are mathematically seamless (whole cycles per 2 s file, verified sample-continuous). Plus a 5-step pitch ladder of style A (+2 semitones per step). Plus Amethyst's Digimon wall_north/south/east/west as fallback (LICENCE UNKNOWN — reference only, do not ship without asking).
> - `chest\`, `door\`, `jump-point\`, `npc\`, `location\`, `stairs-climb\`: Kenney CC0 packs (RPG Audio, Interface, Impact, Sci-fi, UI, Music Jingles, Digital) + Freesound CC0 picks (fetched from public HQ previews — re-download originals before shipping) + Digimon item/person/rail fallbacks.
> - Runtime note: SpatialAudioPlayer loops one buffer with live volume+pan; continuous "pitch rises as you approach" = variable-rate read pointer in the refill, or swap ladder files per distance band.
> - 2026-09-05 later: user picked cues for every category EXCEPT walls ("all a bit jarring, want more natural / less harsh synth"). Second attempt: `wall-proximity\natural-1-breath` (pink-noise breath), `natural-2-puresine`, `natural-3-ocarina`, `natural-4-bell`, `natural-5-bowl` (last three = real CC0 Freesound recordings, rubberband pitch-shifted, cut to whole cycles via zero-crossing pitch measurement so the crossfade never phase-cancels, RMS -21 dB). Pitches one octave up: 294/392/494/587 Hz. ⏳ USER TO LISTEN.
> - 2026-09-05 later still: user rejected the chest, door and NPC picks too → `chest\batch2`, `door\batch2`, `npc\batch2` (Freesound CC0, ~50 files: game-style item-get/coins/fanfares, heavy doors/gates/latches/knocks, human "hmm/ahem/whistle/hello/huh" + marimba/wood block/kalimba). Freesound originals must be fetched MANUALLY by the user (no browser automation: Firefox fork, privacy) — Claude gives page URLs. ⏳ USER TO LISTEN.
> - 2026-09-05 PICKS MADE (see `sound-samples\CHOSEN.txt`): NPC = fs243701 "correct"; chest = fs448416 "Bolt Lock 5"; door = fs126041 "Gate Latch" (needs slow-loop edit); jump = fs350906 "Jump_C_04"; location = fs351408 "GLEAM-GLOW-SFX-CHIME" (needs loop cut); walls = natural-1-breath; reserve = fs185833 "Marimba note struck". ⏳ User downloads originals manually → `sound-samples\originals\` → Claude converts/loops → gzip into soundcues\ + SOUND_CREDITS.txt when the nav feature lands.
> - 2026-09-05 ORIGINALS RECEIVED + CONVERTED → `sound-samples\final\` (NavNpc, NavChest, NavJump one-shots; NavDoor = latch phrase + silence, 3 s slow loop; NavLocation = gleam sustain flattened + crossfade, 1.5 s seamless loop; Wall_behind/left/right/front = natural-1-breath; Marimba_reserve unallocated). User heard all loops 2026-09-05 and is HAPPY; all cues are beacon loops now (one-shots padded to a 1.2 s period, raw hits in `finaloneshot-raw`). See `final\README.txt`. User dropped the originals in `Sounds\` (git-ignored) — copies in `sound-samples\originals\`. Ready to gzip into soundcues\ when the manual-nav code needs them.
> - Next: design + build the manual navigation system (raycasts around the player → 4 wall loops; spatial beacons for POIs).

> 🔬 **FLOOR-GRID RAMP FINDER — PHASE 1 AUDIT ONLY (2026-09-05) — BUILT, ⏳ AWAITING F11 TEST.**
> Plan (user-approved): `C:\Users\Jaco\.claude\plans\compressed-chasing-feigenbaum.md`.
> Built clean, DLL copied. Not committed. **Changes NOTHING in live navigation.**
>
> **The user's rule (now a feedback memory):** any automatic ramp/bridge method
> must reproduce the recorded breadcrumb routes 100 % (every node present,
> every walked edge a short grid route, zero one-way-drop violations) and must
> not produce wall-leading candidates — otherwise it is DISCARDED, like
> DungeonNavGraph in June. No nav-list integration until the gate passes.
>
> **What was built.**
> - `FloorProbeGrid.cs` — discovery-only floor map: 1.5 m cells, one
>   `Physics.RaycastAll` down per cell from above the map (all layers except the
>   player's, triggers ignored, capsule/character/sphere colliders skipped so
>   NPC heads are not floors), keep hits with normal.y ≥ 0.4, multi-level
>   (one node per distinct floor in a cell, 0.5 m dedupe). Neighbours connect
>   when |Δy| ≤ 0.67 × horizontal distance (1.0 m per cell — NOT the old 5 m).
>   Flood-fill components; sloped nodes (steepest step ≥ 0.15) grouped into
>   runs; runs rising ≥ 2 m = ramp candidates (foot, top, rise, component,
>   collider under the foot), nearest 12 kept. Bounds = union of solid
>   collider bounds, capped 400 m. Queries: FindNode, ComponentOf,
>   TryLocalRoute (bounded Dijkstra). Reuses `TraversalGraph.MinHeap` (now internal).
> - `TraversalGraph.cs` — read-only views for audits: `Nodes`, `Edges`
>   (directed), `OneWayDrops`, `HasNodeWithin`. `DropSummary` now uses `OneWayDrops` (DRY).
> - `NavigationHandler.FloorGridAudit.cs` — `RunFloorGridAudit` wired to
>   **F11 on field maps** (after LogTraversalDiagnostic). Logs `[FLOORGRID]`
>   build stats and `[GRIDAUDIT]` checks: node coverage (XZ 1.1 m / Y 0.8 m),
>   edge connectivity (grid route ≤ max(3×len, 6 m)), drop respect (grid must
>   not climb a one-way ledge within max(1.5×straight, 3 m)), candidate list
>   flagged WALKED (breadcrumb within 1.6 m of foot/top) or unverified, with
>   distance + compass from the player. Up to 30 misses listed per check;
>   `RESULT PASS/FAIL` line. Spoken summary via `debug_gridaudit_result`
>   (+ `debug_gridaudit_nogrid`), both keys in all six lang files (763 keys;
>   en 777 = +14 English-only guidance keys, deliberate).
>
> **ROUND 1 RESULT (2026-09-05 17:40, user ran F11 on both maps):**
> - **Lasgus:** structural checks PASS — 458/458 nodes, 4088/4088 edges, 0 drop
>   violations; 19 330 nodes / 498 components / 37 ramp runs in 167 ms. BUT all
>   12 candidates "unverified" and the walked slope (foot ≈ −35,9,31, grade
>   0.3–0.55 per the traversal JSON) is NOT among them. Hypothesis: the slope is
>   part of one huge sloped run (the mountain surface) whose foot is far away,
>   so it falls outside the nearest-12 cut and the foot/top-only WALKED test.
> - **Krosse Cave:** 9 node misses + 86 edge misses, ALL at the two jump-down
>   ledges (breadcrumbs at (33,9.7,79)→(31.8,6.5,79) and (21.8,6.4,73)→(21.8,3.4,72)
>   with nearest floor 1–4 m BELOW) = breadcrumbs laid mid-air during the jump,
>   not floors the grid missed. 6 drops, 0 violations. 30 563 nodes / 200
>   components / 7 runs; 2 of 7 candidates WALKED (real ramps), all in the
>   player's component.
>
> **Round-2 audit changes (built, DLL copied):** no threshold changed.
> - Node misses that touch a recorded one-way drop AND sit above the nearest
>   floor are classed AIRBORNE and reported separately (edges touching them
>   too); PASS/FAIL now counts floor misses only — the log states both numbers.
> - New check 4 "walked slopes": every walked edge at ramp grade (0.15–1.2) →
>   does the grid see slope at both ends, and which RUN holds it (`WALKED RUN`
>   lines with edge/breadcrumb counts, foot/top/rise/nodes); plus the LARGEST
>   RUN list. This answers the Lasgus mystery directly.
> - New check 5 "targets": each save point / chest → grid component vs player,
>   grid route length, traversal verdict side by side.
> - RAMP candidates now flagged WALKED when ANY breadcrumb lies inside the run
>   (`RunOf` per node; `AllRuns` kept; `RunId` on candidates).
>
> **ROUND 2 RESULT (2026-09-05 17:47) — GATE PASSED ON BOTH MAPS.**
> - **Lasgus:** PASS 458/458 nodes, 4088/4088 edges, 0/0 drops. Giant-run
>   hypothesis CONFIRMED: run 0 = 7 973 nodes, rise 145.9 m (foot y −41 → top
>   y 104.7), holds 351 of 458 breadcrumbs and 1 292 of 1 298 walked slope
>   edges — the whole mountainside is one sloped surface, so "runs" cannot
>   name a ramp here. TARGET check: unopened chest = same component, grid
>   route 115 m (user walked ~120 m ✔); summit save (−164.5, 92.4, 72.8) =
>   same component, grid route 316 m, traversal=False (unexplored) ← the
>   natural Phase-2 acceptance test.
> - **Krosse Cave:** PASS 1499/1499 floor nodes (+9 airborne at the two
>   ledges), 16362/16362 floor edges (+86 airborne), 0/6 drop violations.
>   6 of 7 candidates WALKED; 1 unverified (RAMP 2, 19 nodes, rise 3 m, foot
>   (4.3, 3.4, 77.2), 57 m north of the entrance save). Chests 8/9/10 = grid
>   same component, traversal=False (opened before recording existed).
> - 20 % of walked edges at 0.15–0.19 grade read flat at one end — threshold
>   edge cases, not misses. No threshold was changed in either round.
>
> **PHASE 2 BUILT (user: "build both") — ⏳ AWAITING IN-GAME TEST.** Built
> clean, DLL copied. Not committed. Route-based, never drives the character:
> - `FloorProbeGrid.TryRoute(from, to, out corners, out length)`: snaps ends
>   (XZ 2.2 m; Y 1.6 m player / 3.0 m target), A* (binary heap, Euclidean),
>   then greedy string-pulling (`SimplifyRoute`/`LineWalkable`: a straight cut
>   is kept only if every 0.75 m sample finds same-component floor within
>   1.0 m of the line's height) so 8-direction zig-zags become long legs.
>   corners[0] = node under the player, last = exact target (NavMesh shape).
> - New `NavigationHandler.FloorGridRoute.cs`: per-map cached lazy grid
>   (`GetFloorGrid`, invalidated in CheckFieldmapChange), `TryFloorGridRoute`
>   (honours the map-exit barrier via PathCrossesMapExit, sets
>   `_lastPathBlockedByExit`), `FloorGridRouteHint`.
> - (A) `NavigationHandler.cs` cannot-reach site: message + " " + hint. Hint =
>   first corner-to-corner stretch that changes height ≥ 1 m in the target's
>   direction at ramp grade → `nav_autowalk_route_hint_up/_down` ("A possible
>   route of {0} meters exists. It starts climbing {1} meters {2}.") else
>   `nav_autowalk_route_hint`. Keys in ALL six lang files (766 each).
> - (B) `NavigationHandler.Guidance.cs`: `GuideTo` and `RefreshGuideRoute`
>   fall back to the grid route when CalculateAndStorePath fails (and the
>   failure was not the exit barrier). `_guideUnverified`; start message
>   `nav_guide_unverified_start`, mid-walk switch `nav_guide_unverified_switch`
>   (English-only like the other nav_guide_* keys, en.json 782); when a
>   walked/NavMesh route appears again it silently takes over (logged).
>
> **TEST 1 (2026-09-05 18:56–19:00) — FAILED, three defects found and fixed (build 2):**
> 1. **"Lost path" within 1 s of every walk to a chest/marker on another level**
>    — PRE-EXISTING BUG in the moving-target recalc (`NavigationHandler.cs`
>    ~1121): it compared the target to the path's LAST CORNER; a partial path
>    always ends far from such a target, so it recalculated WITHOUT
>    allowPartial, failed, and spoke "Lost path". Also explains the 16:16
>    "Lost path to Unopened chest 1" from earlier today. FIX: new
>    `_pathTargetAtCalc` (set in CalculateAndStorePathCore) — recalc only when
>    the target itself moved, and with allowPartial: true.
> 2. **Guidance never used the grid** (`unverified=False` in the log): the
>    partial NavMesh path counted as "routed", so legs were the old behaviour
>    — partial path to the cliff foot, then the straight bearing "North, 60 m"
>    into the wall. The dead end the user hit was NOT the grid route. FIX:
>    `_lastPathWasPartial` (set in CalculateAndStorePathCore) +
>    `GuideRouteFallsShort` (partial end ≥ 2 m vertical or > 5 m flat from the
>    target) → GuideTo/RefreshGuideRoute prefer the grid route; a partial route
>    with no grid alternative is still followed (better than a bearing).
> 3. **The hint WAS spoken (twice) but inaudible**: guidance was still active
>    while auto-walk ran and its "60 meters." reminder interrupted the message
>    70 ms later. FIX: AutoWalkTo calls `StopGuidance("auto-walk started")`.
> Open question for test 2: the grid route from the cliff foot was 426 m
> ("starts climbing 14 m South") — the mountain mesh extends beyond the
> playable walls (run foot at y −41), so the route may leave the play area.
> The walk decides.
>
> **✅ TEST 2 (2026-09-05 19:10–19:16) — SUCCESS, user: "not seamless, but I
> got there… I would say it is a success."** Log: hint spoken in full
> ("A possible route of 423 meters exists. It starts climbing 19 meters
> South."), directions started `unverified=True` with 15 legs over the grid
> route, player climbed from y 0 to y 87 following it (breadcrumbs 539 →
> 845), a battle at 19:14:55 stopped the directions, user restarted them at
> the top (now `unverified=False` — the breadcrumbs had learned the way),
> arrived at the marker 19:16:08, "Discovered The Monarch's Roost". The user's
> gate is met: no leg into a wall on an unverified route. The grid mesh's
> beyond-the-walls worry did not materialise (route was 423 m because the
> real path winds).
>
> **User tweaks (2026-09-05, BUILT, ⏳ untested):**
> 1. **Reminder interval setting** — `ModSettings.GuideReminderSeconds`
>    (0/3/5/8/12/20, default 5, persisted) as "Directions reminder" row in the
>    Language & speech submenu (`mod_menu_label_guide_reminder`,
>    `mod_menu_seconds`, all six langs → 768 keys; en 785). The reminder now
>    repeats the WHOLE leg (direction + distance), not just the distance.
> 2. **Directions resume after battle** — `OnSceneChangeGuidance` (Main.cs
>    scene hook) saves the destination; `UpdateGuideResume` (Update) mirrors
>    the auto-walk field resume: battle seen + field free + same map →
>    `ResumeGuidance` re-routes (walked/NavMesh else grid) and says
>    `nav_guide_resuming` "Resuming directions to {0}."; non-battle
>    interruptions dropped after 3 s. GuideTo/ResumeGuidance share
>    `TryRouteForGuidance` + `StartGuidance` (DRY). AutoWalkTo/GuideTo clear a
>    pending resume.
> 3. **Shorter unverified messages** — "Directions to {0}. Unverified route."
>    / "Unverified route."
> 4. **Field notifications no longer interrupt** — NotificationHandler's
>    item/skill flush is now queued (`interrupt: false`); "Went upstairs/
>    downstairs" is queued while directions are active.
> Guidance.cs is ~870 lines now — split the battle-resume region into
> `NavigationHandler.Guidance.Resume.cs` at the next touch.
>
> **User tweaks round 2 (2026-09-05, BUILT, ⏳ untested):**
> 5. **Directions only on a heading change** — the "new leg, same word"
>    trigger is gone (`GuideAimChangeMeters` removed); a leg is spoken at
>    start and when the compass word changes with ≥30° swing. Reminder
>    setting now defaults to Off (0); turning it on repeats the leg.
> 6. **"Went upstairs / downstairs" REMOVED** — user diagnosis confirmed: it
>    was `CheckFloorChange`, a 2 m Y-delta heuristic with a 1.5 s cooldown,
>    never tied to FieldStairs objects / minimap stair icons. Deleted method,
>    call, `_lastPlayerY`, `_floorChangeCooldownTimer`, `FloorChangeCooldown`,
>    `nav_floor_up/down` (all six langs → 766 keys; en 783).
>    `FloorChangeThreshold` stays (above/below labels, partial-path honesty,
>    floor-grid). If real stair announcements are ever wanted, build them on
>    `FieldManager.FieldStairsList` proximity, not on Y.
>
> **TEST 2 procedure (for reference):**
> 1. Auto-walk to the summit save point / undiscovered marker → expect the
>    cannot-reach message to end with "A possible route of ~316 meters exists.
>    It starts climbing N meters <direction>."
> 2. Hold L2, pick the summit save point, push the stick DOWN → expect
>    "Directions to Save point, unverified route…" then legs. Walk it. The
>    chest (already breadcrumb-reachable) should give a normal verified start
>    message — a good control.
> 3. Acceptance = the legs never point into a wall and you reach the summit.
>    One wall = feature fails the user's rule (remove B, keep the audit).
> Log lines: `NAV floor grid: built …`, `NAV floor grid: UNVERIFIED route
> length=…`, `NAV guidance: leg N/M …`.
>
> **Phase 2 (only after PASS):** candidates into the Stairs category as
> "Possible ramp up/down" / "Ramp up (walked)", and a "Possible ramp N meters
> <direction>" tail on the cannot-reach message. Phase 3 (separate approval):
> slope compass for the manual nav system; probe walk.

> 🧗 **CLIMB POINTS IN THE NAV LIST (2026-09-05) — BUILT, ⏳ NOT YET TESTED.**
> Built clean, DLL copied to Mods. Not committed.
>
> **Why.** User stuck in the Lasgus Mountains (`MF_0014_01A`, user-confirmed; NOT the Sacred Grounds): the
> chest sits 34m above ground level, auto-walk ends at the nearest ground
> point below it, and a side event said "We could probably climb up here if
> we tried." No ladder/climb item existed in the nav list.
>
> **Finding.** The game's climb is `FieldGimmick03`, a contact gimmick with
> StartPosition/EndPosition; `FieldCharacterLadderBaseTask` references it and
> the character state machine has Ladder states. The mod already scanned
> Gimmick03 but called it "Platform" under Warp Points — and found none in this
> map's live gimmick list (no `NAV:WARP` lines all session). Documented in
> game-api.md §16.
>
> **What was built.** New `NavigationHandler.Build.Climb.cs`: reads BOTH the
> live gimmick list and the static placement table
> `ParameterManager.GetGimmick03ParameterList(map)`, dedupes within 2m, and
> appends "Climb up" / "Climb down" / "Climb point" items to the **Stairs**
> category (direction from which end is on the player's floor). The
> "Platform" branch and its two Loc keys were removed from Warp Points; six
> `nav_climb*` keys added to ALL six lang files (761 keys each; en.json 775 —
> the 14 extra are the deliberately-English-only guidance keys).
>
> **Diagnostics.** Every gimmick in the scene is logged by type name
> (`NAV:CLIMB gimmick[i] <Type> at (...)`), the table's raw lists are logged
> per entry, and each item logs its source (scene/table). **If this dungeon's
> climb is not a Gimmick03, the type-name dump is the next clue.**
>
> **❌ TESTED 2026-09-05 (same day): NO climb item in the Lasgus Mountains.**
> Log: `NAV:CLIMB gimmick list has 0 entries` AND `table for MF_0014_01A: 0
> entries` — this map has no Gimmick03 at all, so its climb is NOT the ladder
> gimmick. The code stays (it is correct for maps that do use ladders, and
> the type-name dump is useful), but it did not solve this dungeon.
>
> **Current theory.** The trigger `ev_sub_040600` is a thin 2×3×8 m box at
> the foot of a 39 m-tall cliff collider ('Center (1)'), whose top (~y 31)
> matches the chest height (34.5). The far overworld exit is also unreachable
> from ground level → the whole upper region is a separate NavMesh island
> reached by a SCRIPTED event. The sub event re-enables after a reload
> (events=1 again). "…if we tried" reads like a HINT STAGE; the real climb is
> probably a later stage of the same placement, gated on sub-scenario /
> main-scenario progress, a party member (`enablePlayerID`) or a flag.
>
> **Added: debug-only sub-event table dump** (`NavigationHandler.EventTableDiag.cs`,
> `LogSubEventTable`, called from ScanAndOpenList after BuildEvents). Prints
> every `ConstSubEventParameter` for the map — placement, eventFunction,
> SubScenarioID + progress ranges, main-scenario range, mapjump/nextMap,
> enable/disable player, party count, scenario flags — plus the player's
> current main and sub progress. Lines are `NAV:SUBEVT …`. Built clean.
>
> **✅ RESOLVED 2026-09-05 (later): the "scripted event" theory was WRONG too.**
> The user simply walked up: a long slope, "Went upstairs" every 2 m from y 8
> to y 34.5, and opened the chest (Flare Ring+). The `NAV:SUBEVT` dump shows
> `ev_sub_040600` is pure flavour (no sub-scenario range, disabled by
> `FLAG_LASGAS_FIRST` after the first visit); `ev_sub_040700/040800` are the
> Ashton sub-scenario (needs Ashton, enemy party 199); `ev_sub_042000/042100`
> the SUB_SCENARIO_029 variant; `ev_sub_320000` needs `FLAG_PUT_METOKUS`.
> **Real cause:** the baked NavMesh has no connection up the slope — the
> textbook dungeon case from [dungeon-traversal-navigation]: first visit is
> manual, the breadcrumb recorder learns the route. Verified in the saved
> `UserData\SO2RAccess\traversals\MF_0014_01A.json`: 458 nodes, ONE connected
> component, entrance → chest reachable, no steep edges. Auto-walk to the
> chest will work from the entrance next time. The summit marker (y 101) is
> still 86 m from the nearest breadcrumb → needs one more manual walk.
> The "look for stairs or a ramp" arrival message was correct advice.
>
> Kept: Gimmick03 climb listing (right for ladder maps) and the `NAV:SUBEVT`
> dump (debug-only, useful for any event gate question). Not committed.
> TODO before next release: copy the UserData traversal for MF_0014_01A into
> project `traversals\` (project copy has only 3 nodes).

> 🧭 **SPOKEN TURN-BY-TURN DIRECTIONS (2026-09-03) — BUILT, NOT YET TESTED.**
> Built clean (Debug, 0 warnings). DLL copied to Mods. Not committed.
>
> **What it is.** The hands-on alternative to auto-walk: the player walks
> themselves and is told, leg by leg, which way to push the stick and how far.
> "North East, 14 meters." A new instruction is spoken whenever the direction
> to push changes — because the route turns a corner, or because the camera
> rotated far enough that the same corner now needs a different word.
> Modelled on the user's own Eiyuden Chronicle mod
> (`NavigationHandler.Guide.cs` there, Directions mode).
>
> **Trigger — DEBUG-GATED for now (user's request).** Hold L2 to open the
> navigation list, highlight a target, push the **left stick DOWN**. Mirrors
> the existing L2 + stick UP = auto-walk. The gate is `Main.DebugMode` (F12)
> at the input site in `Main.ProcessGamepad`; in normal play the gesture does
> nothing. Note L2+L3 was NOT available — it is already the mod menu toggle.
>
> **Decisions the user made before coding:**
> - Compass frame: **camera-relative** ("North" = stick straight up), matching
>   the mod's existing arrival hints. World-relative was rejected as
>   unusable without sight (you would have to know where the camera points).
> - Scope: **field and dungeon maps only.** The world map refuses with
>   `nav_guide_not_worldmap`; its grid A* route can feed the same leg splitter
>   later.
> - Re-press: same target **stops**, different target **switches**. The walk
>   key (backslash) also cancels — one "stop guiding me" key for both aids.
>
> **New file `NavigationHandler.Guidance.cs` (~470 lines).** Reuses
> `CalculateAndStorePath` — the same NavMesh/recorded-traversal pathfinder
> auto-walk uses — so anything auto-walk can reach can also be described.
> The corner list is reduced to spoken **legs**: corners bending the route
> less than 22° are merged into the straight stretch they belong to, stretches
> under 1.2m are folded into the next, and the real destination always
> terminates the list (a partial route stops short of it). Repath every 1s.
>
> **Three deliberate anti-chatter measures** — the failure mode here is a
> voice that talks constantly:
> 1. **Sector hysteresis.** A bearing resting exactly on a sector boundary
>    would flip "North"/"North East" forever while the player walked straight.
>    A re-word needs the bearing to have swung 30° from where it was spoken.
> 2. **Reminder needs movement.** The 5s distance repeat only fires if the
>    player covered ≥1m since the last announcement, so standing still to
>    think is never interrupted on a timer.
> 3. **Silent while the L2 overlay is held.** The list is reading items out
>    loud; directions would cut it mid-word. Memory is kept across that pause
>    (the instruction is still valid), so nothing is echoed on release.
>
> **Pauses, never dies.** Guidance moves nothing, so dialogue/battle/menu only
> pause it — and it forgets what it said, so the player is re-oriented instead
> of dropped back mid-leg. It stops on arrival, map change, scene change, the
> walk key, or an auto-walk taking over.
>
> **Localization fix carried along.** `GetCompassDirection` returned hardcoded
> English ("North", "South East") — a pre-existing gap that this feature would
> have made constant. Split into `CompassBearing` (degrees) →
> `BearingToSector` (0-7) → `CompassName` (Loc), so the eight directions now
> come from `nav_dir_n` … `nav_dir_nw`. The existing arrival hints get this
> for free.
>
> **14 new en.json keys** (`nav_dir_*` ×8, `nav_guide_start`, `_stopped`,
> `_leg`, `_distance`, `_no_route`, `_not_worldmap`). Deliberately NOT copied
> into fr/de/sv/pt/zh: English fallback is automatic and logged, and seeding
> them with English would hide the gap from translators.
>
> **README not updated** — the feature is debug-only, so there is nothing for a
> normal player to read yet. Update it when the gate comes off.
>
> **⏳ NEEDS TESTING — DEFERRED BY THE USER (2026-09-03): "will test another
> day."** Nothing was committed this session; the working tree carries the
> guidance feature on top of the still-uncommitted sound-bundling work.
>
> Suggested run when you get to it: F12 for debug mode, hold L2 in a town,
> pick an NPC across the square, push the stick down, and walk it. Watch for:
> too much or too little speech, legs that describe a wall rather than the
> route, whether the stop/switch gesture behaves, and whether the distances
> match what walking them feels like.
>
> **The most likely thing to be wrong is the leg splitter** — the 22° merge
> and the 1.2m fold are judgement calls made without play data. If a leg ever
> points at a wall, that is where to look first; the debug log prints every
> announcement as `NAV guidance: leg N/M <direction> <meters>` with the reason
> (start / new leg / direction changed), which should make it readable.

> 🔊 **SOUNDS BUNDLED INTO THE DLL (2026-09-02) — ✅ TESTED AND CONFIRMED.**
> Built clean (Debug, 0 warnings), DLL copied to Mods. Not committed yet.
>
> **Why.** User's request: "make it so that the next release will bundle the
> sounds with the .dll… it will make installation and updating the mod easier."
> Installing is now one DLL — no Sounds folder to copy, and updating a sound is
> just replacing the DLL.
>
> **How.** All seven cues are gzipped into the project's `soundcues\` folder and
> compiled in as embedded resources, the same way `grids\*.grid.gz` already were.
> New `EmbeddedSounds.cs` resolves a cue by file name and hands back raw WAV
> bytes, decompressed straight into memory — nothing is written to disk.
>
> **The user's own sounds still win.** A WAV of the same name in
> `UserData\SO2RAccess\Sounds` overrides the bundled copy, so anyone who prefers
> their own cue keeps it across updates. The folder is now optional rather than
> required. Which source answered is logged.
>
> **Refactor.** Both players read `File.ReadAllBytes` then worked purely on the
> array, so the change was small: `AudioCuePlayer.TryParseWav(string path, …)` →
> `TryParseWav(byte[] raw, string name, …)` behind a new shared
> `TryLoadCue(fileName, …)` that all six `Load*Sound` methods now call (their
> duplicated File.Exists checks are gone). `SpatialAudioPlayer.LoadWav(string)` →
> `LoadWav(byte[])`. `Main.cs` passes names, not paths — 23 lines down to 7.
>
> **Verified before handing over:** all 7 resources extract and gunzip
> byte-for-byte identical to the originals in the game's Sounds folder
> (`cmp` clean on every one). Resource names confirmed in the built assembly.
>
> **Licensing handled.** GaugeFill and bubble_big are CC-BY (Attribution 4.0), so
> the attribution has to keep travelling with the audio. New `SOUND_CREDITS.txt`
> at the release root carries the full listing plus instructions for substituting
> your own cues; the old `Sounds\Game sound license directory.txt` would have
> vanished with the folder. README credits now point at it.
>
> **Sizes.** DLL 13 MB → 25 MB (12 MB of cues; gzip saves 2.6 MB over raw). The
> release zip should not grow — it already carried these WAVs, just outside the
> DLL. `Enemynearby.wav` alone is 12.3 MB of that: 48 kHz **stereo** 16-bit, and
> `SpatialAudioPlayer` downmixes it to mono at load. A mono re-encode would halve
> it with no audible change. NOT done — user's audio, user's call.
>
> **README.** Installation no longer says to copy `Sounds`; Updating explains that
> sounds now ride along in the DLL and that a leftover pre-0.3.2 `Sounds` folder
> can be deleted (it still takes priority, so a stale one would shadow a future
> sound change).
>
> **✅ TESTED AND CONFIRMED — log 26-9-2_20-56-50, 0 warnings, 0 exceptions.**
> All seven cues loaded straight from the DLL, every byte count matching the
> original file exactly: dodge 13274, save 1186954, private action 879636,
> gauge fill 115808, jump 26504, fishing 44142, and the proximity loop
> "3066300 samples, 48000Hz, 2ch 16bit -> mono". No "loaded from the user's
> Sounds folder" line anywhere, so the rename held and these really are the
> embedded copies. The proximity loop started and stopped cleanly in play.
> User drove it through F4 → Sound and announcements → Space, previewing save,
> dodge, proximity, gauge, jump and both fishing rows by ear; announcement rows
> correctly answered "No sound to preview."
>
> **Test setup to undo when convenient:** the game's `UserData\SO2RAccess\Sounds`
> folder is still renamed to `Sounds_renamed_for_test`. Nothing depends on it —
> rename it back only to restore the user-override path, or delete it.
>
> **🟡 SURFACED BY THIS WORK, NOT CAUSED BY IT — 24-bit cues ignore their volume
> slider.** The log now prints each cue's bit depth, which shows `Save_sound.wav`
> and `PrivateAction.wav` are **24-bit**, while `AudioCuePlayer.ScalePcmSamples`
> handles only 16-bit and 8-bit and falls through silently for anything else.
> So the save and private action volume settings do nothing — those two always
> play at full volume. Pre-existing (both files predate this change), untouched
> tonight. Fix is either a 24-bit branch in ScalePcmSamples or re-encoding the
> two cues to 16-bit; the second is simpler and matches every other cue.

> 🔁 **ROUND 2 (2026-09-02) — FOUR BUGS FOUND IN THE LOG AND FIXED. ✅ TESTED
> AND CONFIRMED.** Log 26-9-2_20-17-0 was decisive on every one.
> User's verdict on round 1: "Looks good but for one or two oddities."
>
> **1. Item categories were named WRONG — every single one.** Not just the stray
> "Category changed." on the first switch: `itemTabLabel.currentText` is a
> `UICommonSelectTextPresenter` mid-animation and holds the PREVIOUS category.
> Proven by pairing each announced name with the item list that arrived with it —
> "Weapons" came with Tuna Sashimi, "Armor" with a Longsword, "Accessories" with
> Holey Armor+, "Food" with Primavera. Shift the names one step and all ten
> switches line up perfectly. The first switch of a visit had no previous name to
> give, which is exactly where the "Category changed." the user heard came from.
> ➡ Now read from `itemTabPresenter.itemTabDataList[(int)currentCategory].tabName`
> — a static table, one entry per Category enum value, so the name always belongs
> to the category the poll just detected. The table is dumped to the log once per
> visit to the Items screen (`CampItem.tabTable`) so the mapping stays checkable.
>
> **2. `UIItemTabPresenter.SetTabName` never fires — patch removed.** CallerCount
> 2 said it was hookable; it applied and did not fire once. Inlined into its
> callers like the caption methods. `UpdateTabName` is CallerCount 0. No hook on
> that presenter at all now.
>
> **3. Root Special Arts/Spells had TWO speakers for one cursor move.** The slot
> list and the skill information panel both describe the same row.
> `UIBattleSkillInformationPresenter.Set` (the rich readout: MP, type, element,
> range, description, level) fires ~14 ms BEFORE the slot poll — so the rich line
> was started and then cut off by the shorter "L1Button: Knuckle Burst. 1 of 3."
> The user heard the full details only on the entries where the slot index
> happened not to change, which is exactly the inconsistency they described.
> Round 1 made it worse by forcing the slot to re-read on a character switch.
> ➡ On that screen the hook now CACHES its text and the slot poll speaks both as
> one sentence: "Claude. L1Button, 1 of 3: Knuckle Burst. MP: 22. Type: Shield
> break. …" A cached readout the poll never claims (re-entering the screen on the
> row you left) is spoken alone a quarter-second later rather than dropped.
>
> **4. Phantom character name on entry.** Both Equipment and the assignment
> screen greeted the user with a name. A camp sub-screen reports
> `PlayerID.INVALID` (enum value 0) until the game populates it — up to a second
> after the mod starts polling — and seeding the tracker on that turned the first
> real character into a switch. New shared `TrackCharacterTab` ignores INVALID.
>
> **Confirmed working from the same log, unchanged:** Improve → Combat Skill
> ("Rena. Guardbreak. Level: 0 of 10. BP: 1071 / 30. …" — name merged into the
> full readout, exactly as designed) and Equipment's slot readouts.
>
> **Localization.** All six lang files at parity: **757 keys** (756 before). New:
> `camp_battleskill_setting_slot_detail` = "{0}, {1} of {2}: {3}".
> `docs/game-api.md` section rewritten with the two traps (stale animated label,
> inlined tab presenter) plus the "two speakers, one cursor move" rule.
>
> **✅ CONFIRMED WORKING (user: "Seems to work fine now"), log 26-9-2_20-33-27
> reconciles on every point — 0 warnings, 0 exceptions:**
> 1. Items categories now name the tab you land on: 'Food' → Tuna Sashimi,
>    'Weapons' → Longsword, 'Armor' → Holey Armor+, 'Accessories' → Amulet of
>    Antivenin, 'Healing Items' → Primavera. The off-by-one is gone, and the
>    first switch of a visit names a real category.
> 2. Root Special Arts/Spells reads as one sentence, every time:
>    "Rena. L1Button, 1 of 7: Heal. MP: 10. Type: Support. Range: Single target…"
>    on a character switch, "R1Button, 2 of 3: Helmetbreak. MP: 22…" on up/down.
>    Every slot line logged `info=merged` — the rich readout is never cut off now.
>    Entering the screen takes the flush path ("slot poll did not claim the skill
>    info") and speaks the full detail with no character name, as designed.
> 3. No phantom name on entry anywhere: Equipment and the assignment screen both
>    seeded at 1 (CLAUDE) and the first thing spoken was a genuine switch to Rena.
> 4. Improve → Combat Skill unchanged and still correct.
>
> **One diagnostic gap closed afterwards:** the `CampItem.tabTable` dump printed
> nothing in that log — the table is still empty on the first poll after the
> screen opens, and the dump spent its one shot on it. It now waits for a
> populated table. Diagnostic only; no behaviour change.
>
>
> **What was tested (debug F12 on):**
> 1. Camp → Items, L1/R1: the category name must match the items that follow it.
>    Weapons should bring a weapon, Food should bring food. The first switch of a
>    visit must name a real category, not "Category changed."
> 2. Camp → Special Arts/Spells: entering should read the skill with all its
>    detail and no character name. Up/down through the slots should keep reading
>    the detail. L1/R1 should read "Claude. L1Button, 1 of 3: Knuckle Burst. MP…"
>    — name, slot, position, then the full detail, in one breath.
> 3. Camp → Equipment: entering should NOT say a character name; L1/R1 still
>    should.
> 4. Improve → Combat Skill and Improve → Special Arts/Spells: unchanged.

> 🔁 **L1/R1 TAB SWITCHES ANNOUNCED EVERYWHERE IN CAMP (2026-09-02) — BUILT,
> ✅ TESTED, see ROUND 2 above for the four bugs it turned up.** Built clean.
> Committed 2026-09-02 (see ROUND 2). No version bump yet — user's call.
>
> **The problem.** Some camp screens read the new character when you cycle with
> L1/R1 and some said nothing at all, and the Items screen never named the
> category you cycled into. Log 26-9-2_19-55-44 shows it: the Skills screen
> announced "Rena. Determination…", the Status screen read the new character's
> whole block — but Equipment, Special Arts/Spells and the Improve screens
> announced nothing, and seven seconds of category cycling in Items produced not
> one line.
>
> **Why it was silent.** A tab switch replaces the list under the cursor without
> moving the cursor, so every index the mod polls stays exactly the same and no
> announcement path fires. It has to be detected on its own.
>
> **Now consistent — four screens gained it:**
> - **Equipment** — `UICampEquipSelector.currentPlayerID`, slot list only.
> - **Special Arts/Spells** and **Improve → Special Arts/Spells** —
>   `UISelectBattleSkillSelector.currentPlayerID`.
> - **Improve → Combat Skill** — `UICampCombatSkillSelector.currentPlayerID`.
> - **Special Arts/Spells → button assignment** —
>   `UICampBattleSkillSettingSelector.currentPlayerID`, Equip state only.
>
> **Items category** — triggered off `UIItemListSelectorBase.currentCategory`
> (an enum, so language-safe), named from the game's own localized tab label.
> Primary source is a capture-only postfix on `UIItemTabPresenter.SetTabName`
> (CallerCount 2; the `UpdateTabName` that wraps it is CallerCount 0, i.e.
> inlined — hooking it would be another silent no-op). Live fallback reads
> `itemTabPresenter.itemTabLabel.currentText.text`. Last resort says "Category
> changed." and logs which source failed.
>
> **One utterance, not two.** `ScreenReader.Say` interrupts, so saying the name
> and then the new row would cut the name off. New `CampMenuHandler.TabSwitch.cs`
> holds `TabSwitchAnnouncer`: it parks the label, the screen's own row
> announcement prefixes it ("Rena. Weapon: Sword, 1 of 7."), and if nothing
> claims the label within 0.3 s it is spoken alone — a switch is never silent.
> The hook-driven screens detect the switch inside
> `UIBattleSkillInformationPresenter.Set` (which runs before the mod's frame
> poll) so the name merges into the skill row; the frame poll is the safety net.
> The Skills screen keeps its own deferred-flush machinery but now builds its
> prefix from the same `camp_character_prefix` key.
>
> **Left alone:** Status (already reads the whole block, name first) and Item
> Creation (already speaks the name; its action-list tracking is validated and
> uses `currentTabIndex`, not a PlayerID).
>
> **Localization.** All six lang files re-verified at parity: **756 keys** each
> (752 before). New: `camp_character_prefix`, `camp_item_category_prefix`,
> `camp_item_category_empty`, `camp_item_category_unknown`. Translations written
> for fr/de/sv/pt/zh-Hans. New reference section in `docs/game-api.md`:
> "L1/R1 tab strips in camp — every screen that has one", listing every camp
> class that carries a tab strip and which field to read.
>
> **What was tested (debug mode F12 on, so the log shows
> the `tab tracking seeded` / `tab switched to` lines):**
> 1. Camp → Equipment, then L1/R1: each press should say the new character's
>    name followed by the highlighted slot, e.g. "Rena. Weapon: Sword, 1 of 7."
> 2. Camp → Special Arts/Spells, L1/R1 on the skill list: "Celine. Wind Blade.
>    MP: 12…" — name first, then the skill, in one go.
> 3. Same screen → button assignment (the slot list), L1/R1 there too.
> 4. Camp → Improve → Combat Skill, and Improve → Special Arts/Spells: L1/R1.
> 5. Camp → Items, L1/R1 to cycle categories: the category name should come
>    first, then the item under the cursor. Check an empty category too — it
>    should say the name and "Empty."
> 6. Nothing should be announced merely on ENTERING any of these screens (the
>    first read is the row as before, with no name or category glued on front).
> 7. Camp → Improve → IC/Specialty Skills (the Skills screen) and Status should
>    behave exactly as they did before.

> 🔊 **MOD MENU RESTRUCTURE + SOUND PREVIEW (2026-09-02) — ✅ TESTED AND
> CONFIRMED by the user. Built clean (Debug, 0 warnings).
> Committed 2026-09-02. No version bump yet — user's call.
>
> **What changed, and why.** The mod menu was one flat list of 24 settings.
> It is now a root list of three submenus, so nothing is more than one Enter
> away and the root is three rows long:
> - **Sound and announcements** (19 rows) — every audio cue as an on/off row
>   followed by its volume, then the spoken announcements (ally health, ally
>   ailments, player damage, gauge break, gauge percentage, jump prompt speech,
>   enter prompt speech). User's wording: "instead of sounds, make it sounds
>   and announcements, then you have most of them covered."
> - **Language and speech** (3 rows) — Speech language, Dialogue mode, Cutscene
>   subtitles.
> - **Key bindings** — the existing rebind screen, unchanged.
>
> **Sound preview.** Space (keyboard) or **Square / buttonWest** (gamepad) plays
> the sound the highlighted row controls, at its current volume. Works on both
> the on/off row and the volume row of every cue, and deliberately plays even
> when that cue is switched off — hearing what you just muted is the point.
> - One-shot cues (save, dodge, private action, gauge fill, jump, fishing) reuse
>   the existing `AudioCuePlayer.Play*Cue` methods as-is.
> - The enemy-proximity cue is a **loop** (`SpatialAudioPlayer`), not a one-shot,
>   so the preview starts it centred at full distance volume and a 1.5 s timer
>   stops it. That timer is driven by the new `ModMenuHandler.Tick()`, called
>   from `Main.OnUpdate()` **before** `ProcessHotkeys()` — and unconditionally,
>   open or closed, so a preview can never be left looping after the menu shuts.
>   Safe against `EnemyProximityHandler` because `ProcessHotkeys()` returns true
>   while the menu is open, so `UpdateHandlers()` (and therefore the proximity
>   handler) does not run.
> - Never silently silent: `ModMenuItem.Preview` is a `Func<bool>`. A row with no
>   sound says "No sound to preview."; a sound whose WAV failed to load says
>   "That sound file is not loaded." New `Is*Loaded` properties on
>   `AudioCuePlayer` (dodge/jump/save/private action — gauge and fish already had
>   them) and `SpatialAudioPlayer.IsInitialized` back this.
>
> **Removed from the menu** (settings kept in `settings.json` at their defaults,
> still editable by hand): **NPC-aware pathfinding**, **Walk assist**, **Event
> NPCs in nav list**. User's call — "leave [them] on the current setting and
> remove [them] from the menu completely."
>
> **Added:** a **Jump prompt sound volume** row. `ModSettings.JumpPromptSoundVolume`
> has existed since the jump cue landed but had no menu row at all — the volume
> was unreachable in game.
>
> **Escape now means back, not close.** Escape leaves a submenu (saving on the
> way out) and only closes at the root; F4 closes the whole menu from any depth.
> Circle/B mirrors Escape.
>
> **Files.** `ModMenuHandler.cs` slimmed to menu machinery only (~400 lines);
> new `ModMenuHandler.Sound.cs` and `ModMenuHandler.Language.cs` hold the two new
> submenus; `ModMenuHandler.Rebinding.cs` untouched (it already returned to the
> root list by index, which still works). Also fixed while in there: the fishing
> prompt WAV was never freed in `AudioCuePlayer.Shutdown()`.
>
> **Localization.** All six lang files re-verified at parity: **752 keys** each
> (750 before; +8 new, −6 for the three removed settings). New keys:
> `mod_menu_label_sound_group`, `mod_menu_label_language_group`,
> `mod_menu_sound_group_open`, `mod_menu_language_group_open`, `mod_menu_back`,
> `mod_menu_no_preview`, `mod_menu_preview_unavailable`,
> `mod_menu_label_jump_volume`. Translations written for fr/de/sv/pt/zh-Hans.
>
> **What was tested and confirmed:**
> 1. F4 reads "Mod settings menu." then "Sound and announcements: press Enter to
>    open. Item 1 of 3."
> 2. Enter on Sound and announcements; Space on each sound row plays it; Left/Right
>    changes a volume and Space confirms the new level by ear.
> 3. Square on the gamepad does the same as Space.
> 4. Enemy proximity preview plays for about 1.5 s and stops on its own; leaving
>    the submenu or pressing F4 mid-preview cuts it off cleanly.
> 5. Space on an announcement row says "No sound to preview."
> 6. Escape goes back one level; F4 closes from inside a submenu.
> 7. Key bindings submenu still behaves exactly as before.

> 🚀 **v0.3.1 RELEASED (2026-09-01):**
> https://github.com/Yakku5226/SO2RAccess/releases/tag/v0.3.1
> Commit 4ea8b22 on master, tag v0.3.1, both pushed. Release build (0 warnings).
> Zip = `SO2RAccess-0.3.1\` root folder, asset named `SO2RAccess.zip` (23 MB).
> Contents: SO2RAccess.dll (Release), Tolk.dll, nvdaControllerClient64.dll,
> README.md, LICENSE, KNOWN_ISSUES.md, Sounds\ (7 wavs + license listing,
> unchanged), lang\ (fr/de/sv/pt/zh-Hans — en.json stays embedded).
> Cutscene subtitles + config/title double-read fixes. **Point release, 0.3.1 not
> 0.4.0 — user's call: "you are too quick with those version numbers."** Middle
> number is for major updates; a contained feature plus fixes is a third-number
> bump. See the three round entries below for the full story.

> 🎬 **ROUND 3 (2026-09-01) — SUBTITLE SOURCE FOUND, POLLING BUILT. ✅ CONFIRMED
> WORKING (user: "seems to work as intended"), SHIPPED IN v0.3.1.**
> Log 26-9-1_20-5-12 reconciles exactly: 17 caption lines polled during the opening
> movie, 4 suppressed while the user had the toggle off, 13 spoken, 0 duplicate
> drops, 0 errors. Every announcement tagged `[poll]` — NONE `[hook]`, confirming
> the inlining diagnosis is real and not a one-off. Multi-line captions arrive whole
> (embedded newlines, not truncated). Toggle verified in both directions mid-movie.
> Tracer cleanup landed too: 17 MovieText lines instead of 1315, log 47 KB vs 389 KB.
> Config double-read CONFIRMED FIXED by user ("works correctly again") — rounds
> 2a/2b done, no further work there.
> - ⚠️ NOT YET SEEN FIRING: in-engine event caption balloons (`captionPresenter` /
>   `captionPresenterList`). The opening movie only exercises `movieCaptionPresenter`.
>   Same polling path, so it should work — but it is untested. Watch for it.
>
> **The diagnostics paid off.** Log 26-9-1_19-56-24:
> - `SubtitleHandler: 4/4 caption patches applied` — every hook attached.
> - A movie really played: `Movie tracer: movie started (file='ev_0100000_c'
>   state=Playing voice=NEW lang=Japanese onUI=True)`.
> - **ZERO `Caption.*` hook lines** — not one of the four fired.
> - But the tracer caught the subtitles on screen, ~6 s apart:
>   `uiRoot/Endroll/UICaptionController/UICaptionSelector/ui_movie_caption_presenter/Caption`
>   = 'Space:' / 'the realm of infinite possibility.' / 'It has long inspired the
>   dreams of billions.'
>
> **Conclusion: the caption methods are inlined natively.** The patches attach to
> standalone copies the game never calls — same situation as camp/shop menus. Hooking
> harder cannot work; anything upstream is inlined too.
>
> **Fix — poll the presenter's own GameText** (`SubtitleHandler.PollCaptions`, every
> frame):
> - `FindObjectsOfType<UICaptionSelector>(true)` — **includeInactive is required**:
>   the caption root sits inactive between movies, and an active-only scan would only
>   find it up to a second AFTER the first subtitle had gone.
> - Per selector: `movieCaptionPresenter`, `captionPresenter`, and every entry of
>   `captionPresenterList` (event captions are appended there on demand).
> - Per presenter: read `caption` (GameText → TMP_Text) and speak on CHANGE. Gated on
>   `gameObject.activeInHierarchy`, which for captions really is the show/hide signal
>   (proven by the trace), so stale hidden text is never read.
> - Dead presenters are pruned each frame so a reused native pointer cannot silence a
>   new line.
> - Hooks KEPT (they cost nothing, still log, and would take over if a future game
>   build stops inlining). Both paths funnel through `AnnounceCaption`, whose repeat
>   filter makes double-speech impossible. New log lines: `Caption.poll`,
>   `Caption.spoken [poll|hook]`.
> - Automatic movie tracer narrowed to caption paths only — unfiltered it logged
>   ~1300 hidden-menu lines. The full sweep stays on the Semicolon key, on demand.
>
> **TEST:** watch the opening movie (or any cutscene) with subtitles on. Expect each
> line spoken as it appears. Debug mode optional now; if anything is off, the
> `Caption.poll` / `Caption.spoken` lines tell the story. Also worth checking: an
> in-engine event with caption balloons, and that the F4 "Cutscene subtitles" toggle
> still silences it.

> 🔁 **ROUND 2 (2026-09-01, after first test) — config fixes CONFIRMED by user.**
> User report: config still doubled ONCE when opening; "Config" spoken twice when
> backing out; subtitles silent. Log 26-9-1_19-44-22 checked — three distinct causes.
>
> **2a. Config first row still doubled — FIXED.** The suppression worked for every
> row EXCEPT the first (log: 19:45:07.656 "pending generic read", then both voices).
> Cause: `UIConfigWindow.IsOpened` is still FALSE while the window plays its opening
> transition, so the gate said "not config" for the first row only. Two fixes, both
> needed: (a) `ConfigMenuHandler.MarkActive()` stamps a timestamp in every config
> postfix and `IsConfigOpen` now returns true for 1.5 s after any config activity,
> covering the transition gap; (b) `ListSelectionHandler` re-checks screen ownership
> in `Update()` right before speaking, not only when the event fires — the gate is
> now the shared `GetScreenOwnerReason()` helper (camp/shop/config, one place).
>
> **2b. "Config" twice when backing out — FIXED (TitleMenuHandler).** Unrelated to
> the config work; a latent bug the config trip exposed. `TitleMenu_Show_Postfix`
> set `_lastAnnouncedIndex = -1` then announced, but `AnnounceCurrentItem` never
> recorded the index it spoke. Closing a sub-screen re-shows the title menu with the
> cursor on the row you left from, OnInput fires for that unchanged row, -1 != 3, so
> it spoke again 313 ms later. `AnnounceCurrentItem` now records the index it
> announced. Affects every title sub-screen exit (config, load, license), not just
> config.
>
> **2c. Subtitles — NO EVIDENCE EITHER WAY, diagnostics added.** The test log
> contains NO cutscene at all (session ended on the field ~30 s after loading), so
> nothing proves the hook does or does not fire. Also: `SubtitleHandler` logged its
> patch result through DebugLogger, which is silent at scene-load time — a failed
> hook looked exactly like a working one. Now:
> - Patch attachment logs via `MelonLogger.Msg` → "SubtitleHandler: N/4 caption
>   patches applied", with a named warning per hook that fails. CHECK THIS LINE
>   FIRST in the next log.
> - Each hook attaches independently (`TryPatch`), so one bad signature no longer
>   costs the rest.
> - Trace hooks added upstream: `UICaptionController.ShowCaption`,
>   `UICaptionSelector.ShowCaption`, `UICaptionSelector.ShowMovieCaption` — logs
>   `Caption.controller` / `Caption.selector` / `Caption.movie`, so we see which
>   layer a caption reaches even if it never gets to `SetCaption`. The `Vector2`
>   anchors are simply not declared on the postfixes (Harmony passes only what a
>   patch asks for), so no struct is marshalled.
> - **Movie text tracer** (debug mode only): while `GameMovieManager.IsPlaying`,
>   every visible TMP text is logged once with its full GameObject path
>   (`MovieText` lines), plus a movie-start line naming the message file, state,
>   voice type and language.
> - **NEW DEBUG HOTKEY — Semicolon** (`ModAction.DebugTextDump`, DebugOnly context,
>   so it is dead unless F12 debug mode is on). Dumps EVERY visible on-screen text
>   with its object path, anywhere. This is the fallback if the cutscene turns out
>   not to be a GameMovieManager movie. Verified free against the binding dump;
>   auto-excluded from the rebinding menu (DebugOnly actions are skipped) and it
>   inherits the existing "reserved for debugging" clash warning.
> - Loc key `debug_text_dump` added to all six lang files (now 750 keys each,
>   parity verified; fr/de/sv/pt/zh-Hans re-copied to the game's UserData lang).
>
> **NEXT TEST — please do all three:**
> 1. Config: open from title AND from camp, arrow through categories, back out.
>    Every row once; "Config" once on the way back.
> 2. Turn debug mode ON (F12) BEFORE a cutscene, then watch one.
> 3. During the cutscene, press Semicolon a few times while text is on screen.
> Then send the log. The `SubtitleHandler: N/4` line, any `Caption.*` lines, and the
> `TextDump` / `MovieText` lines together will say exactly where subtitles live.

> 🗣️ **CUTSCENE SUBTITLES + CONFIG DOUBLE-READ FIX (2026-09-01) — round 1.**
> Two changes, both built (0 warnings / 0 errors, DLL deployed).
>
> **1. Config menu double output — FIXED.** User report: every config category
> was spoken twice. Log proof (26-9-1_19-32-9.log): ConfigMenuHandler speaks
> "Audio Settings, 1 of 7", then ~150 ms later the universal OnSelected net
> speaks "Audio Settings" again — the config category list is built from
> generic `UICommonListItemPresenter` rows, which were not in
> ListSelectionHandler's covered-types set. Fix follows the existing camp/shop
> pattern: new `ConfigMenuHandler.IsConfigOpen` (UiFinder-cached
> `UIConfigWindow` + `IsOpened`), checked in `OnSelected_Postfix`; suppression
> is logged. Reset on scene change from Main.
> - TEST: open Config from the title screen AND from camp, arrow through the
>   categories — each should be spoken exactly ONCE, with its position.
>
> **2. Cutscene subtitles — NEW (`SubtitleHandler.cs`).** Captions are a
> separate text layer from the dialogue box (`UIConversationPresenter`) and
> nothing read them, so cutscene subtitles were silent. Hook:
> `UICaptionPresenter.SetCaption(string caption, bool isSubtitles)` — the
> funnel both `UICaptionController.ShowCaption` and
> `UICaptionSelector.ShowMovieCaption` reach, with the text already resolved
> from its messageID and localized. Empty text = the game clearing a caption
> (ignored, not a fault). Identical text within 1 s is treated as a repeat.
> New lines interrupt, matching how a subtitle replaces the previous one on
> screen. `UICaptionController.ShowCaption(string, string)` is patched for
> DEBUG LOGGING ONLY, so a caption that never reaches SetCaption shows up in
> the log instead of going silently missing. The `Vector2` anchor overloads
> are deliberately NOT patched (their text lands in SetCaption anyway).
> - New mod-menu toggle (F4): "Cutscene subtitles", default ON, persisted as
>   `SubtitlesEnabled` in settings.json. Label added to all six lang files
>   (now 749 keys each, parity verified); fr/de/sv/pt/zh-Hans re-copied to
>   the game's UserData\SO2RAccess\lang.
> - TEST (debug mode F12 on, so the log records what fires): watch a movie
>   cutscene — subtitle lines should be read as they appear. Then check the
>   log for `Caption.set` / `Caption.show` lines. Also confirm the F4 menu
>   row toggles on/off and that turning it off silences the readout.
> - WATCH FOR: captions used somewhere unexpected (battle shouts, narration)
>   causing spam, or a caption line doubling with the dialogue box. The log
>   lines will show it.

> 🚀 **v0.3.0 RELEASED (2026-08-31):**
> https://github.com/Yakku5226/SO2RAccess/releases/tag/v0.3.0
> Commit 7c7a402 (localization overhaul + zh-Hans + README update section),
> tag v0.3.0, pushed. Zip = SO2RAccess-0.3.0\ layout, asset SO2RAccess.zip
> (same contents as 0.2.0 plus lang\zh-Hans.json; Release build, 0 warnings).
> Camp/config localization fixes were log-verified in French before release
> ("It looks much better now"); zh-Hans itself is a fresh draft — first
> in-game Chinese report will come from users. lang\XX.json remains
> untracked on purpose (local test file, not shipped).
>
> 🇨🇳 **SIMPLIFIED CHINESE TRANSLATION ADDED (2026-08-31) — shipped in
> v0.3.0, first draft untested in game.**
> `lang\zh-Hans.json` (special user request for next release): full AI first
> draft of all 748 keys, validated (key parity + every placeholder intact,
> same script as the other four). File name matches the auto-detect mapping
> (game text language 4 → zh-Hans), so picking 简体中文 in the game config
> switches mod speech automatically — the log's earlier "No translation file
> for 'zh-Hans'" fallback is now covered. language_name = 简体中文 (spoken by
> the F4 speech-language row). Copied to the game's UserData lang folder.
> No rebuild needed (loose file; only en.json is embedded).
> - 📦 RELEASE NOTE: next zip's lang\ folder = fr/de/sv/pt/zh-Hans.json.
> - TEST: set game text language to 简体中文 → mod should announce the
>   switch and speak Chinese; F4 speech-language row should say 简体中文.

> 🔧 **CONFIG MENU PREFIX FIX (2026-08-31) — PENDING TEST.** User report:
> every config category was announced with a leading "Config" word. Cause:
> the `config_menu_item` string itself was "Config, {1} of {2}: {0}". Fixed
> to "{0}, {1} of {2}" in all six lang files (en/fr/de/sv/pt/XX), rebuilt
> (embedded en.json refreshed), and re-copied fr/de/sv/pt to the game's
> UserData\SO2RAccess\lang. Test: open config, arrow through categories —
> should hear only the option name plus position.
>
> 🐞 **CAMP/NAV LOCALIZATION — FIX ROUND 2 (2026-08-31) — PENDING TEST.**
> Round 1's camp-root fix FAILED in test: the log proved
> `UICampMenuItemPresenter` NEVER fires the universal OnSelected hook (camp
> root cursor movement is fully native), so the label cache stayed empty and
> every announcement fell back to the English enum name (`spoken='Equip'` in
> the log). Round 2 replaces the capture point:
> - Camp root labels now captured by a postfix on
>   `UICampMenuItemPresenter.UpdateShow(ListItemDataBase)` — fires from
>   managed code when the game POPULATES each row (menu build), before any
>   announcement. Also covers the second-level menus that flow through the
>   same selector/data type (System: Save/Load/Config/ReturnToTitle; Database
>   children; Enhance children; Operation children) — these were the "many
>   sub menus" also reading English. Debug log line: "CampMenu.UpdateShow:
>   cached 'X' = 'label'".
> - SWEEP DONE (whole repo, two patterns: announced enum-ToString/hardcoded
>   literals, and gates comparing localized game text):
>   1. IC Train/Scout gates compared `data.categoryName` (LOCALIZED!) to
>      "Train"/"Scouting" — those sub-screens were silently DEAD in French.
>      Now `_icActiveSkillId` (SpecialSkillID enum) read via
>      `selectSpecialSkillSelector.GetCurrentSelectedSpecialSkillID()`.
>   2. Party formation roles Leader/Battle/Reserve/Assist → Loc keys
>      `camp_party_role_*`.
>   3. Nav NPC categories (Innkeeper/Equipment shop/...) → Loc keys
>      `nav_npc_cat_*` + `nav_npc_qualified`; generic-"NPC" comparisons now
>      compare against Loc.Get("nav_npc_cat_generic").
>   Clean per sweep: elemental resistances, Train ON/OFF speech, Database
>   locked entries (all already Loc'd); `_lastRootMenuItemName` gates use
>   enum names (language-safe); remaining literals are debug-only.
> - Lang files now 748 keys, all six in parity, build clean, DLL + fr/de/sv/pt
>   deployed to the game.
> - TEST (French): camp root + System/Database/Enhance/Operation sub-menus
>   French; equip slots "Arme:"; party formation roles French; IC Train and
>   Scout screens SPEAK AT ALL (they were silent); field nav NPC categories
>   French. Regression check in English: camp root, IC Train/Scout, nav NPCs.
> - Equip slot categories + camp equip Loc keys from round 1 unchanged
>   (that part worked).
>
> 🌐 **FOUR STARTER TRANSLATIONS CREATED (2026-08-30, session 6, after L1
> passed): fr.json, de.json, sv.json, pt.json in the project's lang\ folder**
> — full machine-assisted first drafts of all 724 keys each (AI-translated;
> community will refine). Verified programmatically: valid UTF-8 JSON, same
> key set and order as en.json, every {n} placeholder intact in every string
> (incl. the 16-placeholder help text). Portuguese is Brazilian-flavored
> (largest player base), Swedish/Portuguese can't be auto-selected (game has
> no such text language) — manual pick via F4 only; fr/de auto-select works.
> - csproj changed: `lang\*.json` EmbeddedResource narrowed to `lang\en.json`
>   only, so bundled translations stay LOOSE files (players/community can
>   edit without a rebuild) and the DLL doesn't bloat. Build clean after.
> - All four copied to the game's UserData\SO2RAccess\lang for testing
>   (old test fr.json overwritten — it was a renamed English copy).
> - README Translations section now mentions the bundled starter files.
> - 📦 RELEASE NOTE: next release zip must include a `lang\` folder with
>   fr/de/sv/pt.json (en.json not needed — embedded + auto-extracted).
> - ✅ TESTED 2026-08-30 (user: "Tested and works") → committed, pushed,
>   and released as v0.2.0.
>
> 🌍 **LOCALIZATION / COMMUNITY TRANSLATION SUPPORT BUILT (2026-08-30,
> session 6) — PENDING IN-GAME TESTS (L1 below). Version bumped to 0.2.0.**
> - All 628 spoken strings moved OUT of Loc.cs into `lang\en.json` (flat
>   key→text JSON; the ~90 `//` section comments preserved as `_section_*`
>   marker keys the loader skips). Export was done by a throwaway tool that
>   compiled Loc.cs itself — round-trip verified byte-exact, then deleted.
> - `lang\en.json` is an EmbeddedResource in the DLL (the permanent English
>   fallback) and is re-extracted to `UserData\SO2RAccess\lang\en.json` on
>   EVERY launch as the translators' reference template (en.json is always
>   overwritten by design; other files in lang\ are never touched).
> - Loc.cs rewritten (910 → ~150 lines): per-key lookup = active translation
>   → embedded English → key itself. New API: SetLanguage(code), ActiveCode,
>   AvailableCodes(). New LocLoader.cs owns all file/resource IO (parse,
>   template extraction, `language_name` peeking, game-enum→code map:
>   ja en ko zh-Hant zh-Hans fr it de es).
> - New Language setting (settings.json `"Language"`, default `"auto"`).
>   New LanguageHandler.cs: one-shot startup auto-detect reads
>   `ParameterManager.Instance.SystemConfigParameter.TextLanguage` after
>   game-ready (game singletons unsafe earlier); Harmony postfix on
>   `TextManager.OnChangeLanguage(Language)` (by-value enum, hook-safe)
>   follows mid-session language changes; in auto mode a missing language
>   file falls back to English. All load failures logged with reasons.
> - F4 menu: new "Speech language" row (before Key bindings) cycling
>   Automatic + English + every `*.json` in lang\; applies instantly.
> - Docs: new TRANSLATING.md (translator guide: placeholder rules incl. the
>   16-placeholder help string, UTF-8, missing-key fallback, naming/auto
>   codes); README Translations section; docs/localization-guide.md marked
>   SUPERSEDED (described the old hardcoded-dictionaries design).
> - ✅ **L1 TESTS PASSED 2026-08-30 (user: "it works properly now", tested
>   main menu + camp menu):** startup unchanged, en.json extracted, test
>   language loads/persists via F4 row, auto-detect works, live switching
>   works, missing-language fallback to English works (ko/zh-Hant refusals
>   logged cleanly). KEY FINDING: the OnChangeLanguage Harmony hook NEVER
>   fires (game switches language via native ChangeLanguageTask) — the 2s
>   polling backup (commit f2a1399) is the working mechanism; hook kept as
>   a harmless proof-logger. Tests 4 (partial file missing-key fallback),
>   5 (malformed JSON), 8 (regression sweep) not explicitly run — same
>   code paths largely exercised, optional. Test files fr.json in the
>   game's lang folder can be deleted or kept for future testing.
> - Original checklist (for reference):
>   1. Startup speaks "Welcome to SO2R Access." unchanged; F1 help reads
>      with all key names filled in (proves export fidelity).
>   2. `UserData\SO2RAccess\lang\en.json` exists after launch; Latest.log
>      has `[LOC]` lines (embedded loaded + template refreshed).
>   3. Test language: `copy en.json xx.json` in the lang folder, edit
>      `language_name` to "Test" and `mod_loaded` to "Test language
>      loaded."; F4 → Speech language → Test → menu re-announces from the
>      file; close menu (saves); restart → startup speaks the test string.
>   4. Delete a few keys from xx.json → those lines speak English; log
>      shows the missing-key count.
>   5. Break xx.json (delete a closing brace) → mod stays English, no
>      crash, parse reason in log.
>   6. Auto-detect: set Speech language to Automatic; with game language
>      French and no fr.json → stays English (log explains); rename
>      xx.json → fr.json, restart → auto-loads it.
>   7. Live switch: on Automatic, change the game's text language in the
>      game config mid-session → mod announces "Speech language: …"
>      without restart (tests the OnChangeLanguage hook; if it never
>      fires, fallback plan = slow polling in LanguageHandler.Update).
>   8. Regression sweep: F12 debug, F3 Fol, F2 dialogue mode, nav keys,
>      keybind submenu (composed keybind_action_* keys) all still speak.
>
> 🚀 **FIRST PUBLIC RELEASE PUBLISHED (2026-08-30, session 5): v0.1.0 on
> GitHub** — https://github.com/Yakku5226/SO2RAccess/releases/tag/v0.1.0
> - Release build (`dotnet build -c Release`, 0 warnings) tagged v0.1.0 on
>   master d7e170c; tag + commit pushed.
> - Zip asset `SO2RAccess-0.1.0.zip` contains: SO2RAccess.dll (Release),
>   Tolk.dll, nvdaControllerClient64.dll, README.md, LICENSE,
>   KNOWN_ISSUES.md, and Sounds\ (the 7 TESTED wavs from the game's
>   UserData\SO2RAccess\Sounds — incl. Jump.wav and the user's own dodge.wav,
>   shipped as Dodge.wav — plus the sound license listing).
> - Sound credits corrected first (commit d7e170c): dodge + jump sounds are
>   Yakku's own work (user confirmed); andersmmg triple-beep credit removed
>   (that sound no longer ships); typo fix in the license listing.
> - Versioning convention (user-set): 0.x until everything tested at least
>   once; middle number = major updates, third = tiny updates. MelonInfo
>   version stays "0.1.0" (already matched).
> - NEXT SESSION (user-set): localization/community translation support —
>   move Loc.cs strings to an external en.json template + language file
>   loader + fallback to embedded English + TRANSLATING.md for the
>   community. (Guild menu + reference-mod items: user says already done.)
>
> ✅ **REBINDING MENU: TEST R1 PASSED (2026-08-30, session 3 — user: "The
> keybindings menu works"). COMMITTED + PUSHED 2026-08-30 (see below).**
>
> ✅ **FINALISATION TWEAKS (same day, DLL deployed, user: "Looks good"):**
> - Debug hotkeys F5–F11 REMOVED from the rebind list (BuildRebindRows skips
>   ModKeyContext.DebugOnly; their 7 keybind_action_* Loc strings deleted;
>   list is now 16 actions + 2 commands = 18 rows). Capturing F5–F11 for any
>   other action gives a new passive warning instead (keybind_clash_debug):
>   "Warning: <key> is reserved for debugging and will only work if debug
>   mode is not active." Key still accepted; F12 (debug toggle) and F1–F4
>   stay rebindable. Reset-all still covers debug actions internally (scrubs
>   any stale settings.json override). README + docs/mod-bindings.md updated.
> - Startup announcement (mod_loaded) changed to "Welcome to SO2R Access."
>   (was "SO2RAccess loaded. Press F1 for help." — F1 hint dropped on purpose,
>   still in README/help).
>
> ✅ **DEMO COMPATIBILITY (2026-08-30, session 4):** `MelonGame` attribute in
> Main.cs changed from `("SquareEnix", "SO2R")` to universal (no-argument
> `[assembly: MelonGame]`), so MelonLoader loads the mod on both the full game
> and the free demo regardless of their internal product names (game code is
> identical; the reference BepInEx mod works on both for the same reason —
> BepInEx has no game-name gate at all). README Requirements updated. Built and
> deployed.
>
> ✅ **ENTIRE PILE COMMITTED + PUSHED 2026-08-30 (origin/master 6c18e90,
> tree clean).** Four commits: 6a6884e battle strategy quick list
> (PHASE_OPERATION), 900b32d input scheme rework + ModKeys + rebinding
> menu + universal MelonGame (demo support), b4c3ee0 nav data (grid
> growth + 68 new traversals), 6c18e90 docs (README, LICENSE, sound
> license listing, CLAUDE.md rule, status).
>
> ➡️ **NEXT:** open items — G4 missed-dialogue hooks still inconclusive
> (watch during story scenes); bonus gauge deferred until the user has
> experimented with it.
>
> What was built:
> - New "Key bindings" item at the BOTTOM of the mod settings menu (F4). Enter
>   opens it (Cross/A on pad). New file `ModMenuHandler.Rebinding.cs` (partial
>   class); `ModMenuHandler.cs` gained a MenuScreen state machine (Root /
>   Rebind / Capture), Enter/NumpadEnter + Cross/A activation, and a null-Change
>   guard for submenu rows ("Press Enter to open." on left/right).
> - Submenu lists ALL 23 ModActions in enum order (debug F5–F11 ones at the
>   end), then "Reset all keys to defaults", then "Save and go back". Rows read
>   "Story hint: apostrophe. Item 15 of 25." Arrows/D-pad navigate, wraps.
> - Enter on an action = capture: "Press the new key for X. Escape cancels."
>   Next key pressed (polled via kb.allKeys wasPressedThisFrame — RUNTIME
>   UNVERIFIED under IL2CPP interop, see test R1c) becomes the PENDING binding.
>   Escape / Circle-B cancels the capture only. The Enter that started the
>   capture can't be captured (next-frame polling).
> - Passive clash warnings on capture (accepted anyway): (a) game keys via new
>   `InputBindingDump.GameActionsForKey(key)` (live map, best-effort — empty if
>   game data not loaded); (b) other PENDING mod bindings whose context
>   overlaps (same context, or either Global). Nav vs battle-pause sharing
>   stays non-clash.
> - Nothing applies until "Save and go back": ModKeys.Apply(pending) +
>   ModSettings.Save(), announce "Key bindings saved." Escape in the list =
>   "Key binding changes discarded." Both return to the root settings list.
>   Force-close (L2+L3) while in the submenu also discards — safe.
> - Menu nav keys (arrows/Enter/Escape) are FIXED, never rebindable; Escape
>   can't be captured by design.
> - Persistence: settings.json `KeyBindings` dict (action name → key name),
>   ONLY non-default keys saved (so future default changes reach non-rebinders).
>   ModSettings.Load → ApplyKeyBindings → ModKeys.Apply at startup; unknown
>   names logged + skipped. ModKeys gained `_defaultKeys` snapshot +
>   GetDefault().
> - F1 help text now builds key names LIVE from ModKeys (16 placeholders) —
>   stays truthful after rebinding. README + docs/mod-bindings.md updated.
> - Loc: ~40 new strings (keybind_*, mod_menu_label_keybinds, mod_menu_submenu,
>   mod_menu_use_enter, keybind_action_<EnumName> per action).
>
> TEST SCRIPT R1 (F12 debug ON):
> - R1a: F4 → arrow to last item "Key bindings: press Enter to open" → Enter.
>   Hear intro + first row "Help: F1. Item 1 of 25"? Arrows wrap both ways?
> - R1b: Enter on an action (e.g. Quick recovery) → prompt → press a free key
>   (e.g. O) → "… is now O."? Escape during a capture cancels cleanly?
> - R1c: CRITICAL — does capture register keys at all (kb.allKeys under
>   IL2CPP)? If every key press is silent in capture mode, report — fallback
>   is polling the Key enum via kb[key].
> - R1d: capture a game key (e.g. F = attack) → hear the game-clash warning;
>   capture a key used by another mod action in same context → mod-clash
>   warning; both still accepted.
> - R1e: change a key, ESCAPE out → old key still works, new key does nothing.
>   Change a key, "Save and go back" → new key works immediately (try Quick
>   recovery or story hint), F1 help speaks the NEW name, settings.json gains
>   KeyBindings entry. Restart game → rebind persists.
> - R1f: "Reset all keys to defaults" → rows read defaults; still needs Save.
>   After saving defaults, settings.json KeyBindings is empty.
> - R1g: gamepad — D-pad navigates submenu, Cross/A activates, Circle/B
>   discards; L2+L3 while submenu open closes whole menu, changes discarded.
>
> Foundation (previous sessions): ModKeys.Apply() swaps the dictionary with
> zero call-site churn, InputBindingDump.FindKeyboardClashes() is the checker,
> game's own Set/SwapKeyboardKey APIs documented in game-api.md §4.
> User-approved plan (QOL round 1): NumPad removed entirely (numpadless
> keyboards), gamepad modifier L1 → L2 (L1 = game pickpocket clash, user
> report), all bindings centralized in NEW `ModKeys.cs` (ModAction/ModKeyContext
> enums, dictionary-backed — future rebinding menu swaps contents, zero call
> sites; DisplayName() feeds all logs/speech). NEW `InputBindingDump.cs`: on
> F12 debug-enable, logs the game's LIVE keyboard map (per-action via
> `ParameterManager.Instance.SystemConfigParameter.GetKeyboardKey`, verified
> decompiled SystemConfigParameter.cs:1572) + pad map (`GetBindInputKey`) +
> reverse pad summaries (which actions sit on L1/L2/R1/R2/L3/R3) + per-mod-key
> FREE/CLASHES verdict, spoken one-liner (Loc binddump_*). This is the
> foundation of the future rebind clash checker (FindKeyboardClashes()).
>
> New keyboard scheme: F-keys unchanged (F1 help, F2 voice, F3 Fol, F4 menu,
> F12 debug, F5–F11 debug). NAV IS NOW MODELESS — no open/close: `-`/`=` prev/
> next category (auto-refresh >10s), `[`/`]` prev/next item, `\` walk/cancel
> toggle; list lives in the background, silently rebuilt when absent/stale/
> other map (EnsureListReady in NavigationHandler.List.cs; BuildList split from
> ScanAndOpenList — gamepad hold-overlay path unchanged; ToggleNavList/CloseList
> deleted; nav_close Loc removed; nav_autowalk_cancelled_menu renamed
> nav_autowalk_cancelled). Announced distances now LIVE-recomputed from player
> position (LiveDistanceUnits) — build-time distances mislead on a background
> list. Battle pause reuses the family: `-`/`=` tier, `[`/`]` character.
> Story hint NumPad7→H (camp), party status NumPad0→P (quick recovery);
> fallbacks Quote/Semicolon if dump shows clash (one line in ModKeys).
> Gamepad: hold L2 = nav overlay, L2+L3 menu, L2+R3 Fol, L3-alone guards now
> "L2 not held"; pause keeps L1/R1. Suppression while overlay held is now a
> DYNAMIC set: RebuildModifierSuppressSet (Patches.cs) collects every action
> whose live pad binding == InputKey.L2 on each overlay open (fallback set
> TriggerLeft2/FieldWalk/FieldCameraUp/Down if API not ready); hardcoded
> FieldCameraLeft dropped (L1 no longer ours). NavCategoryNext/Prev now
> re-announce when only one category (feedback on every press). Docs updated:
> game-api.md §3 rewritten + SystemConfigParameter API in §4, NEW
> docs/mod-bindings.md (user-facing reference), project_status key table,
> README.md Controls section filled in. Builds clean, DLL deployed.
>
> ✅ K1 BINDING DUMP PASSED (2026-08-30, log 11:19): dump + spoken verdict
>   worked. Results: `-` `=` `[` `]` `\` P all FREE; **H CLASHED** (game maps
>   keyboard H → its R3 action: BackLog/BattleTargetLock) → **story hint moved
>   to APOSTROPHE (Key.Quote)**, docs/help updated, rebuilt + deployed.
>   PickPocket confirmed on L1 (pad). L2 pad actions = TriggerLeft2,
>   FieldPsynardFallBack, PhotoMode_CameraZoomout, BattleTargetChangeMode —
>   dynamic suppress set logged exactly that at runtime (working). Game
>   keyboard defaults recorded in game-api.md §3 + full battle-key list in
>   docs/mod-bindings.md (F confirm, C cancel/dodge, Q/E skills, Tab battle
>   menu, R change command, T battle pause, LeftCtrl switch character,
>   LeftShift target mode, LeftAlt lock, 1-4 assaults, Space skip).
>   User session results: everything works EXCEPT battle "R" menu (below).
>   🐛 FOUND VIA LOG: game's R (BattleChangeCommand) opens
>   UIBattleOperationSelector (quick strategy/operations list; also has
>   isTacticsTransition into the full tactics screen) — SILENT because
>   UIBattleOperationListItemPresenter is on ListSelectionHandler's
>   suppression list ("dedicated handler owns it") but NO handler reads that
>   standalone screen (BattleMenuHandler only handles tacticsSelector reached
>   via Tab menu root; operationSelector lands in PHASE_OTHER). ✅ FIX BUILT +
>   DEPLOYED + K4 PASSED (2026-08-30, "all looks good"): PHASE_OPERATION added to
>   BattleMenuHandler (peek == _battleWindow.operationSelector, a
>   UIListSelectorBase; announces "Strategy." heading + polls currentIndex,
>   reads itemDataList[idx].operationName via battle_menu_root_item Loc;
>   Phase F in class docs). Entering the full tactics screen from it hits the
>   existing PHASE_TACTICS (entry-path-agnostic peek check) — its duplicate
>   "Strategy." heading is suppressed when oldPhase==PHASE_OPERATION. Row
>   type deliberately KEPT on the ListSelectionHandler suppression list (same
>   presenter used inside the menu tactics op list — unsuppressing would
>   double-speak). Two debug diagnostics added: unhandled-peek-selector type
>   logging in IdentifyPhase (names any future silent battle screen), and
>   "operation selector active but window IsOpened=false" probe in case the R
>   shortcut doesn't open the battle window (would explain a silent retest).
>   ✅ K4 PASSED — R strategy menu reads (heading + orders), no double-speak.
> ✅ K2 KEYBOARD + K3 GAMEPAD PASSED (2026-08-30, user: "Everything seems to
>   work fine but for the in battle controls" → the one battle finding became
>   the K4 fix below; user then confirmed "all looks good"). Modeless nav,
>   walk/cancel toggle, battle-pause family, apostrophe hint, P status, L2
>   overlay + chords, L1 pickpocket regression — all working. No L2 flutter
>   or release-blip issues reported (hysteresis remedy stays on the shelf).
>   International-layout caveat stands (Key values are physical positions,
>   QWERTY assumed) — the rebinder is the real fix.
>
> 📘 **2026-08-30 DOCS SESSION: README + LICENSE drafted (NOT committed — new
> uncommitted pile).** New root `README.md`: intro, feature list, installation
> guide (MelonLoader links, Tolk DLLs step, Sounds → UserData\SO2RAccess\Sounds),
> Controls section = PLACEHOLDER until the control scheme is final, credits
> (user as Yakku, MelonLoader, Tolk, Square Enix disclaimer, all six Freesound
> sounds from `Sounds\Game sound license directory.txt`), license section. New
> `LICENSE` = MIT, "Copyright (c) 2026 Yakku" (free use/modify/redistribute as
> long as the credit notice is kept). `.gitignore`: `Sounds/` → `Sounds/*` +
> negation so the sound-license txt is now tracked (WAVs stay ignored).
> **EMBEDDED NAV DATA REFRESHED:** the DLL ships breadcrumbs + world map grid as
> embedded resources (csproj `traversals\*.json` + `grids\*.grid.gz`; auto-
> extract at runtime, local UserData copies always win) — but the embeds were
> STALE: 4 breadcrumb files from June 12 and the July-5 grid (pre-D1-fix bake).
> Fixed: all 61 live breadcrumb JSONs copied from game UserData into
> `traversals\`; live July-10 rebaked grid re-gzipped into
> `grids\worldmap_expel.grid.gz` (11.2MB). Rebuilt; verified 62 embedded
> resources in the DLL; deployed to Mods (same code, fresh data). README has a
> "Navigation data (no action needed)" note. OPEN: dodge sound (andersmmg) has
> NO license line in the credits txt — user to check its Freesound page and add
> it (README carries it license-less for now). RELEASE CHECKLIST seed: before
> any release, re-copy live traversals + re-gzip the current grid into the
> project so the shipped DLL stays current.
> **NEW STANDING RULE (added to CLAUDE.md):** major new features or other
> user-facing changes must be reflected in README.md too (features,
> installation, credits), not just project_status.md.

> ✅ **FISHING M10 PASSED (2026-08-29, session end): world-map fishing feature-complete
> and user-accepted ("Seems good") — but NOT COMMITTED: user has more changes planned
> first.** M10 log (20:13–20:16) confirmed everything green: comfort-first stand
> routing working ("kept as last resort" lines), comfort-skip firing (refusals 7s →
> ~2s), repeated clean bubble-confirmed arrivals ("Arrived at Fishing spot X. You can
> fish here." — incl. after battles), menu-open cancel speech working. One user
> question ANSWERED (no code change): near-vs-far verdict flip at the Krosse river
> pinch — standing IN the narrow area puts its wedges inside the sweep's deliberate
> 16m start-side exemption (WmSweepEndpointExemptDist, the Marze gate fix / accepted
> caveat 2026-07-11) → optimistic walk + honest stuck give-up; from ~99m away the
> same wedges are mid-route → honest pre-walk refusal. Both verdicts are the same
> truth; do NOT shrink the exemption (re-breaks town gates).
>
> 📋 **REFERENCE-MOD MENU PLAN (user-approved 2026-08-29, in steps):**
> 1. ✅ TEST G1 PASSED (user 2026-08-29: "Seems to work perfectly"): Universal
>    list-selection safety net (`ListSelectionHandler.cs`) — postfix on
>    `UICanSelectedListItemPresenterBase.OnSelected` (game-api.md §19). Covered
>    screens suppressed by concrete type name (44-type list) + camp/shop open
>    gates; unknown rows spoken generically (GameText children joined) after a
>    0.15s settle delay, deduped. Every fire logs type + decision in debug mode.
> 2. ✅ GUILD FIRST COMMAND MENU WORKS via the generic fallback — log shows
>    clean "Accept"/"Report"; row class = `UIShopMenuListItemPresenter` (same
>    type as the shop's Buy/Sell root menu, which stays suppressed via the
>    IsShopOpen gate). Quest list did NOT double-speak (suppressed, GuildHandler
>    spoke). 🟡 STALE-ANNOUNCE FIX BUILT + DEPLOYED (PENDING TEST G2): opening
>    the command menu woke the quest selector with stale data → GuildHandler
>    spoke "Guild missions." + a stale mission (log 21:02:23, "Alchemy Mission 1
>    Completed (5)" while on Accept/Report). Fix: real focus always fires
>    UIQuestListItemPresenter.OnSelected; new
>    `ListSelectionHandler.WasRecentlySelected(type, 1.0s)` gate in
>    PollGuildQuests — no announcements without a recent selection event.
>    ✅ TEST G2 PASSED (user 2026-08-30: "Works perfectly").
> 3. ✅ STORY HINT BUILT + DEPLOYED (PENDING TEST G3) — user changed binding to
>    L3 IN THE CAMP MENU (safe: QuickRecoveryHandler's L3 party status only
>    listens while its field overlay is open; L1+L3 mod menu unaffected —
>    L1-held check). Keyboard twin NumPad 7. New
>    `CampMenuHandler.StoryHint.cs`: reads the balloon LIVE on press (no
>    caching, no SetSpeechBalloon hook needed) via
>    UICampDotCharacterPresenter.speechBalloonPresenter.speechBalloonText;
>    prefers a balloon with IsShowingSpeechBalloon, falls back to any real
>    text (balloon animates in/out); placeholders (0000/目的/-) filtered;
>    always answers ("Story hint: ..." or "No story hint available."). Loc:
>    camp_story_hint / camp_story_hint_none.
>    ✅ TEST G3 PASSED (user 2026-08-30: "Works perfectly").
> 4. ✅ MISSED DIALOGUE BUILT + DEPLOYED (PENDING TEST G4). New
>    `DialogueHandler.WindowMessages.cs` (DialogueHandler now partial):
>    postfixes on UIConversationWindow SetConversationAutoMessage
>    (string,string,float,bool), SetConversationMessageFollowObject
>    (string,string), ShowCenterMessage, ShowEntireMessage (ambient bubbles +
>    center/full-screen text; messageID → TextManager System→Skill→Item,
>    UNRESOLVED IDs SUPPRESSED + logged) and ShowEventInformation
>    (title+description, spoken verbatim if not IDs). 1.0s same-text dedupe.
>    ⚠️ ref-Vector3 overloads of these methods exist and were deliberately NOT
>    hooked (native-crash rule) — only the plain overloads (reference-mod
>    proven). TEST G4 STILL OPEN (2026-08-30 session: hooks fired ZERO times —
>    no bubbles triggered, INCONCLUSIVE, nothing regressed): keep playing;
>    ambient NPC bubbles, center-screen messages, and event info panels
>    should speak; normal dialogue must NOT double-speak. Log lines:
>    "DialogueHandler.windowMessage".
> 5. 🟡 WALKING-NPC AUTO-WALK FIXES BUILT + DEPLOYED (PENDING TEST G5),
>    user-approved 2026-08-30 (snappier-follow tweak DECLINED for now — recalc
>    stays 1.5s/3m). Root cause of both user reports = wandering NPCs:
>    (a) "Arrived at Cunning Fighter..." spoke 6× in 4s — proximity-lock
>    re-announced on every re-catch of a walker; FIX: _autoWalkArrivalAnnounced
>    latch, full message + diagnostics dump only on FIRST catch per walk,
>    re-locks log "(silent)".
>    (b) FALSE "Could not reach Youth. Stopped 4 meters" (Lacuer) — path was
>    computed to the STALE list-build position, NPC had walked 4m, and
>    path-exhaustion gave up without recomputing; FIX 1: AutoWalkTo paths to
>    item.LiveTransform.position when present (worldmap location/fishing
>    overrides unaffected — they run after); FIX 2: path-exhaustion recalcs to
>    the live position for live-transform targets (MaxPathExhaustRecalcs=2 per
>    walk, then honest refusal).
>    TEST G5 RESULT (2026-08-30): fixes a+b VISIBLY WORKING in log (one
>    announcement + "(silent)" re-lock; exhaust-rescue armed) BUT follow at
>    1.8m = OUT OF TALK RANGE: 10s of following the Cunning Fighter with zero
>    dialogue despite action presses; the ONE successful talk logged fired at
>    dist=1.0m. → FIX ROUND 2 BUILT + DEPLOYED (PENDING TEST G6):
>    (c) NpcApproachRadius = 1.2m — the walk now TRIES to get within 1.2m of
>    NPCs (atTarget check, CAT_NPC only; physical minimum ~1.0m from
>    colliders); ACCEPTANCE in IsAtRealTarget stays 1.8m so a path that
>    exhausts between 1.2–1.8m (carve hole, collider) still honestly reports
>    arrival — no new false refusals.
>    (d) Follow-phase exhaust-rescue UNCAPPED (initial approach keeps the
>    2-recalc cap → honest refusal): short 2-corner paths to a walker exhaust
>    every few steps; stuck detector still guards livelock. Log says "follow
>    phase (uncapped)".
>    ✅ TEST G6 PASSED (user 2026-08-30: "Looks good").
> 6. 🟡 FISHING PROMPT SOUND BUILT + DEPLOYED (PENDING TEST G7), user request
>    2026-08-30: the fishing bubble now plays `bubble_big.wav` (user-provided,
>    copied to game UserData\SO2RAccess\Sounds) INSTEAD of speaking "You can
>    fish here." — speech remains ONLY as fallback when the WAV is
>    missing/unparseable (never silent by accident). New AudioCuePlayer
>    Load/Play/IsLoaded trio (jump-cue pattern); ModSettings
>    FishPromptSoundEnabled (default on) + FishPromptSoundVolume (0.8),
>    persisted; mod menu entries "Fishing prompt sound" toggle + volume (after
>    Enter prompt speech). Auto-walk arrival message ("Arrived at Fishing
>    spot X...") UNCHANGED — only the bubble prompt readout became a sound.
>    Sounds/ added to .gitignore (bubble_big.wav must never be committed;
>    older tracked WAVs from the initial commit left as-is).
>    G7 ATTEMPT 1 (2026-08-30): sound did NOT play — the user's WAV was
>    32-bit IEEE FLOAT; AudioCuePlayer needs integer PCM. Speech fallback
>    worked as designed. Fixed by converting the ASSET to 16-bit PCM in both
>    locations. ✅ RETEST G7 PASSED (user: "Works perfectly").
>
> ✅ **ENTIRE PILE COMMITTED + PUSHED 2026-08-30** (origin/master a1408e9):
> three commits — c9b2e5b menus & speech (list hook, guild, story hint,
> missed dialogue, fish-prompt sound), e2bb9f7 navigation (fishing complete +
> walking-NPC fixes), a1408e9 docs & build. Working tree clean.
> STILL OPEN: TEST G4 (missed-dialogue hooks armed but no bubble has
> triggered yet in play — watch for newly spoken ambient/center-screen text).
> ⚠️ UNCOMMITTED WORK PILE (commit together when user says go): entire fishing
> build (M4–M10 fixes: perf, bubble detection, creep, resume bounds, routing,
> distances, cancel speech), Welsh specialty SP fix (validated), csproj
> reference-mods exclusion. Do not commit until the user calls it ready.
>
> (M9 record follows.)
>
> 🟡 **FISHING M9 (2026-08-29): creep+resume fixes WORKED (multiple clean
> bubble-confirmed arrivals incl. "Arrived at Fishing spot X. You can fish here."),
> but 3 findings → 2 FIXED (PENDING M10), 1 = known floor-tier limit (discuss).**
> (1) STUCK SAGA (user: "stuck in a narrow area near something else"): the big
> river spot near Krosse lists at ~19–27m but its only VERIFIED stands are 58–148m
> away, and the only route threads a corridor that FAILS the 0.6m comfort clearance
> (route exists only on the raw 0.50m floor tier). The walk inched along a ledge
> (obstacle wall 0.51m away) with RENA/CELINE/PRECIS crowding within ~1m in every
> stuck snapshot (party followers in the pinch), advanced ~2.5m per stuck-recalc,
> and after 5 recalcs refused honestly ("Cannot reach"). NOT a creep/detection bug —
> this is the known floor-tier pinch limit, survivable-but-miserable variant.
> ✅ USER APPROVED + IMPLEMENTED same day ("Fix the routing please"): stand
> selection is now two-pass — PASS 1 accepts only stands whose ACCEPTED route
> stays on the comfort tier (new _wmLastRouteFloorTier field; the pathfinder's
> LastPathUsedFloorTier static alone can reflect a dropped safe-exit leg or
> re-plan, so the accepted tier is latched in WorldmapCalculateAndStorePath);
> PASS 2 falls back to the FIRST floor-tier stand, recomputed with
> skipComfortTier (monotone-safe; threaded through CalculateAndStorePath →
> Core → WorldmapCalculateAndStorePath → FindPath). Nothing reachable becomes
> unreachable — only the order changes. COST NOTE for M10: a spot whose stands
> are ALL floor-tier (the Krosse river spot) now tries every stand before
> walking — a few extra seconds at walk start for exactly those spots.
> (2) "39–40m at the spot" = the inside-box
> fallback's own lie (distance to the center snap across the lake). FIXED: list
> target + distance = nearest walkable shore cell on the PLAYER's side
> (TryFindShoreOnPlayerSide now runs for every spot, subsuming the far-bank rescue;
> no same-side shore → old edge metric + filter). Standing at the stand should now
> read ~1–2m; Krosse exit ~50–70m. BUILD log: shore=True/False dist=. (3) FREEZES
> on walk-start/refusal (~7s measured): stand verification (game water probes,
> 1.5–3.5s, up to 336 perimeter samples) + per-stand comfort A* (1.25M cells
> ≈1.2s) + floor A* + capsule sweeps ×3 stands ×2 re-plan rounds. Provable waste
> REMOVED: re-plan rounds no longer repeat a comfort-tier search that already
> failed (wedge stamps only REMOVE cells — monotone; FindPath gained
> skipComfortTier, passed only within one walk computation). Refusals should be
> ~half as long; the 2–3.5s stand verification remains (= the accuracy the user
> asked about; cacheable later if still annoying). TEST M10: distances at
> stand/Krosse exit; refusal wait shorter; everything from M9 still green.
>
> (M8 record follows.) M8 log findings:
> (a) the ONE false positive = a battle-interrupted fishing walk: the world-map resume
> path didn't carry _autoWalkTriggerBounds/_autoWalkFacePosition (CancelAutoWalk nulls
> them), so the resumed walk lost its fishing identity, skipped the creep, and spoke
> the old false early "Arrived" (bubble 7s later after manual shuffle). FIXED:
> _wmResumeTriggerBounds/_wmResumeFacePosition saved + restored. (b) The creep is NOT
> slow — the world map ignores stick magnitude, so 3.5m took 0.55s and the honest
> give-up message BEAT the bubble by 82ms/57ms (user heard apology + "You can fish
> here" back to back, twice). FIXED: when the distance budget is spent, HOLD (zero
> stick, face water) until the 5s deadline instead of giving up immediately — the
> bubble typically fires ~0.1s after the last step. TEST M9: (1) world-map fishing
> walk WITH a battle on the way: after "Resuming walk", arrival must still be
> bubble-confirmed ("Arrived" + "You can fish here" together); (2) plain world-map
> fishing walk: no more "prompt not showing" apology racing the bubble; (3) the
> honest no-prompt message should now appear ONLY when truly not fishable after 5s.
>
> (M7 record follows.)
> M7 result ("Much better"): distances honest, blink latch works (log: repeated
> "re-shown (blink), announce suppressed"), cancel announcement in. Remaining issue
> (user): world-map "Arrived" spoke BEFORE "You can fish here". Log truth: both walks
> stopped 1.29/1.27m short of the verified stand (InteractableArrivalRadius=1.3) and
> the bubble did NOT come on its own — it appeared 5–6s later only after the user
> shuffled ~2m TOWARD THE WATER by hand (game probes water ~5m in FRONT of player;
> stopping short puts the probe out of range). FIX (deployed, NOT committed): on
> reaching a world-map fishing stand without the bubble, do NOT stop/announce —
> enter a CREEP phase (UpdateFishCreep: slow stick toward nearest water-box point,
> WmFishCreepSpeedScale 0.4, stuck detection bypassed) until FishPromptShowing ends
> the walk with the real "Arrived" + "You can fish here", or after 5s / 3.5m give an
> honest "Arrived near X, but the fishing prompt is not showing..." (Loc
> nav_autowalk_arrived_no_fish_prompt). Creep state reset in StopAutoWalk /
> CancelAutoWalk / walk start. TEST M8: world-map fishing walk end-to-end — expect
> "Arrived" and "You can fish here" together at a truly fishable stand, cross starts
> fishing; also re-verify town flow unchanged.
>
> (M6 record follows.) M6 log (19:19–19:22, full debug)
> proved the bubble poll works: town flow perfect (auto-walk arrived at pond stand →
> bubble 28ms later → "You can fish here"; FISHDIAG icons=[field_fishing_icon],
> fishingSpriteId resolved), and NO false announce at the Krosse exit (contactID=25,
> bubble=False, silent ✅ — the volume-contact lie is fully demoted). Three issues
> found + FIXED this session: (1) "Fishing spot 1, 0 meters" while 83m from any
> fishable stand — the water-box AABB contains the player (spans land), so
> edge-distance collapsed to 0; now falls back to walk-target distance when the
> player is INSIDE the box (stands sit ~1.5m outside → keep edge metric). (2) The
> game BLINKS the bubble (hide/re-show cycles while standing still — M6 log:
> shows of 0.2–2s, hides of 6–10s) and every re-show re-announced; now an announce
> latch holds until the player moves >3m from the announce position (re-approach
> re-announces, blinks don't). This was the user's town "false positive". (3) The
> world-map fishing walk NEVER FAILED — it was 23m from the verified stand when the
> user opened the nav menu, which cancels the walk SILENTLY; menu-open cancel now
> speaks "Walk to {label} cancelled." (explicit cancels — NumPad 1, stick — stay
> silent by design). TEST M7 checklist in the "World map fishing spots" entry.
> Welsh specialty SP fix (validated) still NOT committed — do NOT commit the fishing
> build until M7 passes; the Welsh fix can be committed separately.
>
> 🔧 **2026-08-29 TOOLING SESSION: ilspycmd updated 9.1.0 → 11.0.0.9375.** Newer
> ilspycmd (10.x+) ships only for .NET 10, so the .NET 10 SDK (10.0.400) was installed
> alongside 8.0.424 (side by side, nothing removed; `dotnet build` now uses SDK 10 —
> verified: mod builds clean, 0 warnings). While verifying, found the build broken by
> the decompiled reference mod: `reference-mods\` was never excluded from compilation
> (unlike `decompiled\`/`tools\`) → 90 compile errors. FIXED: added
> `<Compile Remove="reference-mods\**" />` to SO2RAccess.csproj. ⚠️ This csproj fix is
> NOT committed — user decision: commit it after/with the fishing-freeze fix. Verified
> clean: reference-mods is gitignored + untracked, never compiled into any DLL (builds
> containing it always failed), no copied code in our sources (docs mentions only).
>
> 📚 **2026-08-29: THIRD-PARTY MOD ANALYSED — big new API knowledge in
> `docs/game-api.md` Section 19.** "ScreenReaderMOD" (Galaxy Laboratory MM, BepInEx) was
> temporarily installed in the game dir; decompiled to `reference-mods\` (gitignored —
> not ours, never copy verbatim, API facts only; BepInEx files in the game dir conflict
> with MelonLoader and can be deleted once user confirms). Verified-in-our-decompile
> findings, each a candidate feature/refactor (user picks priorities after fishing):
> - UNIVERSAL LIST HOOK: `UICanSelectedListItemPresenterBase.OnSelected(ListItemDataBase)`
>   Harmony postfix fires for EVERY list-item selection game-wide (camp/shop/guild/
>   picture-book/config/battle). Refines our "no hooks" rule (command-menu cursor moves
>   still silent). Pattern: TryCast → pending flag → read 0.05–0.4s later; generic
>   TMP-text fallback for unknown screens.
> - CAMP STORY HINT: hook `UICampWindow.SetSpeechBalloon`; text at
>   `UICampDotCharacterPresenter` child `ui_camp_speech_balloon_presenter/SpeechBalloon/Text`.
> - MISSED DIALOGUE: `UIConversationWindow.SetConversationAutoMessage /
>   SetConversationMessageFollowObject / ShowCenterMessage / ShowEntireMessage /
>   ShowEventInformation` — messageID → `TextManager.GetMessage` (types 0/100/200).
> - GUILD QUEST RECIPE: full field map (name/state/description/rewards/achievable
>   members) in game-api.md §19 — enables redoing the quest screen properly.

> 📌 **BUNNY PHASE IN PROGRESS (plan approved 2026-07-05 session 3, full plan file:
> C:\Users\Jaco\.claude\plans\jazzy-marinating-acorn.md).** Per-travel-mode reachability
> (foot / bunny / psynard). User HAS the bunny and can ride now. Cadence: Build 1 = Phase A
> investigation (below, deployed) → analyze log → Build 2 = Phases B+C+D (grid v2 "WMGI" with
> per-mode blocked-bit flags lane + pure height lane, flood-fill d<4→8 fix, WMGG/WMGH magic
> drift cleanup, bake with the game's LIVE LayerMaskWall/LayerMaskBunnyWall, per-mode region
> maps [bunny lazy-built], FindPath(mode), annotate-first list "unreachable on foot/by bunny"
> + [WMReach] reason logs, chest/enemy line-check → region check, psynard = everything
> reachable) → user F9 regen + tests → re-gzip grid into grids\ + rebuild → Build 3 = Phase E
> hide-toggle ONLY after zero false-unreachables validated. Key game facts: bunny =
> FieldBunny : FieldPlayer (control player swaps), psynard = FieldPsynard : Field3DObject
> (separate object), detect via FieldManager.IsFieldFlag(FieldBitFlag.Bunny=2/Psynard=1);
> no ship travel form exists in the game.
>
> 🔴 **D1 RETRY FAILED AGAIN (2026-07-10 log 11:17–11:21) → TRUE ROOT CAUSE FOUND AND
> FIXED: ComputeSafeExitPoint picked a rock-plateau top. Build 0/0 deployed. TEST F1
> below is the current pending test.**
> - E-SCRIPT RESULTS: E1 census ran near Krosse (not the wedge spot): only 3 small
>   mismatches (−0.7..−0.9m) — grid essentially honest there. E2 rebake ran; the
>   pool-prefab fallback rescued ZERO pools ("no colliders on layoutItem or pool prefab"
>   for every previously skipped pool) → the missing-rocks hypothesis is REFUTED (those
>   pools are genuinely visual-only; the ResolveBakeSource fallback stays as cheap
>   insurance). E3 failed identically to D1.
> - MY EARLIER "missing rocks" READING WAS A DIRECTION ERROR: the stuck-terrain "3m ahead"
>   probe (NavigationHandler.Worldmap.cs ~:327) points at the TARGET (Salva), not along
>   the path — it measured a rock beside the route. Offline checks (scratchpad wedgeprobe):
>   grid vs live heights agree; the NEW grid is honest. C2's audit conclusions STAND
>   (incl. the open Arlia-ridge D2 question).
> - REAL CHAIN (from log 11:20:13–15 + offline clearance BFS): player stood 21m from
>   Krosse City's trigger → ComputeSafeExitPoint engaged and returned (-134.9,-20.1) — a
>   point 25m away on TOP of the rock plateau (Y≈35 vs player Y≈28) because its checks
>   were only "has ground + no L22/L23 within 2m" (plateau tops pass both). The exit leg
>   to it failed the 0.60m comfort pass (plateau only floor-reachable via body-width
>   gaps up the rocks: legacy grid called the belt noGround so this pick used to fail
>   harmlessly into "going direct") and the player wedged at wp31 between two
>   Col_Obstacle walls 0.51m from center. All recalcs then re-planned from inside the
>   rock pocket → floor-tier belt routes → 5 wedges → give-up. MEANWHILE the MAIN leg
>   (exit→ring) found a 600-wp 0.60m COMFORT ROAD ROUTE in 60ms — the road to Salva is
>   fine; offline BFS with the real baked clearance table confirms plains→ring connects
>   at 0.60m (strict goal-cell closure, 318k cells).
> - FIX (deployed): ComputeSafeExitPoint candidates now additionally require
>   (a) |candidateY − playerY| ≤ 2m (exit = flat field next to town, never a ledge/pit),
>   (b) WorldmapPathfinder.IsWalkableWorld, (c) same GetRegionId as the player (fail-open
>   when either region is 0). Rejection counts logged per reason (log-failure-reasons
>   rule); all-rejected → existing "going direct" fallback (can never reduce
>   reachability).
> 🟡 F1 PARTIAL (2026-07-10 log 11:40–11:41): safe-exit fix WORKED (plateau rejected via
> "2 height", exit at player level, walk started on a 551-wp comfort road route and ran
> clean for 30s) — but a mid-walk re-plan at (-122.5,-133) (battle resume suspected)
> failed the 0.60m comfort pass (1.25M cells) where the walk-start plan had SUCCEEDED
> toward the same ring point, took a floor route into rocks, and wedged at (-135.9,-163.7)
> (Col_Obstacle 0.51m from center — squeeze signature, terrain flat). Contradiction
> (comfort route exists from A but not from B, same goal, same grid) is unexplained —
> user called for large-scale diagnostics instead of further spot fixes. CORRECT.
>
> ✅ **ROUTE AUDIT RAN 3× (2026-07-10 log 11:54–11:57) — FULL DIAGNOSIS + FIX SET
> DEPLOYED (build 0/0). Supersedes script G.**
> AUDIT FINDINGS (audit 3, from (-97,-156) near the Arlia wedge; audits 1–2 were lost to
> an auditor bug, fixed below):
> - Harley (1258wp) and Kurik (1433wp): comfort roads, ZERO wedges — long plains routes
>   are perfect. Ocean towns: honest refusals. The grid+pathfinder core is healthy.
> - Salva / Krosse Cave / Marze / Lasgus: comfort roads with 1–2 wedge segments each,
>   all AT THE DESTINATION GATE (e.g. Salva wp369/380 at (-166.5,-303.5), Col_Obstacle
>   0.02m) — end-of-route pinches inside the arrival/prompt zone, tolerable.
> - Arlia: FLOOR-tier route (comfort pass exhausted 1.25M cells; goal cell clearance
>   0.94m = fine, the ROUTE doesn't exist at 0.60m) with 27 impassable segments through
>   the rock belt at (-100,-160..-176) — exactly where the user wedged. Floor-tier
>   routes thread body-width gaps and are physically unwalkable. THIS answers D2
>   partially: no comfortable overland route Krosse→Arlia exists; the honest answer is
>   the refusal message ("may require passing through another location" — Salva pass).
> - Mountain Palace: comfort route with 188 "wedges" + mismatches up to 30.8m — believed
>   AUDIT ARTIFACT (probe from +25m lands on overhangs above passes; probe fixed to
>   short-ray from wp.y+2). Re-audit will tell.
> - 💥 SMOKING GUN (new clearance logging): mid-walk STUCK-RECALCS and BATTLE RESUMES
>   re-planned to goal cell (3522,2564) = (-159,-318) = Salva's TOWN-CENTRE symbol —
>   INSIDE the walls — while fresh walks aim at the ring point (-161.9,-302.8). A
>   centre-aimed re-plan can never pass the comfort tier → every post-stuck/post-battle
>   route collapsed to wall-hugging floor quality. Explains ALL "worked at start, died
>   after battle/stuck" behavior across three days.
> FIXES (deployed, build 0/0):
> 1. _wmPathGoal: WorldmapCalculateAndStorePath stores its true goal; stuck-recalcs
>    (NavigationHandler.Worldmap.cs) and battle resumes (NavigationHandler.cs) re-plan
>    CAT_LOCATION walks to the stored RING POINT, never the centre symbol.
> 2. Floor-tier pre-walk physics validation (WorldmapCalculateAndStorePath): FOOT routes
>    that fell back to the 0.50m floor are body-capsule-swept before walking (goal-10m
>    exempt); impassable segments become blocked zones + up to 2 re-plans; if still
>    impassable → honest refusal via the no-land-route message (LastNoPathWasDisconnected
>    now internal-settable). Floor-tier SAFE-EXIT legs with wedges are dropped (direct).
> 3. Auditor fixes: null-collider overlap hits no longer abort a route (guarded +
>    per-segment try/catch, counted as "unresolvable overlap hits"); ground probe is a
>    short ray from wp.y+2 (overhang-safe) — audits 1–2 failure mode gone.
> TEST SCRIPT H RESULTS (2026-07-10 log 12:13–12:14, user: "performance much better"):
> [✅] H1. Krosse plains → Salva ARRIVED (~50s): comfort road, ONE brief stick at the
>        known gate pinch (-166.4,-303.4) then immediate "Arrived at Salva" (enter-prompt
>        branch). NOT EXERCISED: battle resume during the walk (no battle spawned) —
>        verify opportunistically; if a post-battle walk ever collapses again, check the
>        resume goal-cell in the "no route at 0.60m" line (must be ≈ring, not centre).
> [✅] H2. Krosse plains → Arlia: floor-route sweep found 2 impassable segments →
>        re-planned → 4 more → re-planned → still impassable → HONEST REFUSAL in 3.4s
>        with the no-land-route message. Exactly as designed; refusal is correct (real
>        route to Arlia goes through Salva).
> [ ] H3. F7 re-audit (auditor bug fixes + Mountain Palace overhang check) — not yet run,
>        do opportunistically.
> KNOWN COSMETIC GAP (discuss later, do not "fix" blindly): Arlia reads as a PLAIN name
> in the list (region-connected) but the walk refuses honestly (physically impassable) —
> list annotation and walk verdict can disagree for floor-tier-only targets. Options
> later: sweep-based annotation (costly per list-open) or a "route may be blocked"
> phrasing.
>
> ✅ **D3–D5 PASSED (2026-07-10 log 12:20–12:22) — PER-MODE REACHABILITY VALIDATED.**
> - D3 foot list: all ocean towns "unreachable on foot"; continent towns plain. Arlia/
>   Mountain Palace plain = the known cosmetic gap above.
> - D4 bunny: mounted list kept ocean towns "unreachable by bunny" (bunny regions built
>   in 576ms); the (-370,-179) chest walk ARRIVED mounted (the old foot-impossible
>   target!); mounted Lacuer City attempt → honest "unreachable by bunny" refusal
>   message; mounted Krosse City walk arrived. Battle resume ALSO exercised with a
>   foot→bunny mode switch mid-walk (user mounted after the battle) — re-planned on the
>   bunny lane cleanly.
> - D5: no false "unreachable" observed.
> - EXTRA (user's own test, NOT in script): Mountain Palace ON FOOT — comfort-tier
>   overland route up Lasgus, physically wedged twice at rock passages (first at
>   (-261,-92.5), exactly the audit-3 flagged spot), then battle → user mounted →
>   abandoned. CONFIRMS the old known issue: MP's overland route is fiction-free but
>   physically unwalkable in places, and the game intends entry via the Lasgus
>   Mountains dungeon. NOT caught today because the pre-walk sweep only runs on
>   FLOOR-tier routes.
> NEXT SESSION CANDIDATES (in order):
> 1. ✅ DONE 2026-07-11 (entry below): Extend the pre-walk body sweep to COMFORT-tier
>    routes too.
> 2. ✅ CLOSED 2026-07-11 (entry below): list stays untouched by user decision; only the
>    refusal message wording improved.
> 3. H3 leftover: one F7 re-audit to confirm auditor fixes (opportunistic).
> 4. Then back to the regular queue (item creation material screen etc.).
>
> 🚧 **2026-07-11: PRE-WALK SWEEP EXTENDED TO COMFORT ROUTES (candidates 1+2) —
> BUILT + DLL DEPLOYED (branch worktree-comfort-route-sweep) — PENDING TEST SCRIPT I.**
> Decision record: Option A ("remember refusals" list tag) REJECTED by user — a
> remembered verdict is position-stale (refused-from-Krosse stops being true after
> crossing Lasgus; success there stops being true after teleporting back). Agreed
> principle: the mod only speaks verdicts computed LIVE from the current position.
> The list's plain name claims only "same landmass" (position-independent, always true);
> walkability is judged at walk start. Long-term accurate-list idea (NOT now, grid frozen
> until zero false-unreachables): bake-time capsule sweep would disconnect MP/Arlia in
> the grid and the existing region annotation would become honest for free.
> CHANGES:
> - NavigationHandler.Worldmap.Pathfinding.cs: pre-walk body-capsule sweep now runs on
>   EVERY foot route, comfort tier included (gate no longer requires bestPathFloorTier).
>   Re-planned routes are re-swept every round — the old loop trusted a comfort re-plan
>   blindly, which is exactly the Mountain Palace hole. Goal-10m exemption, up-to-2
>   re-plans, honest refusal via LastNoPathWasDisconnected all unchanged. Sweep logs
>   now name the tier of the offending route. Safe-exit leg gate left floor-tier-only
>   (its fallback "go direct" is benign; not part of this change).
> - Loc.cs nav_autowalk_no_land_route: "No walkable route to {0} from here. It may lie
>   beyond mountains or water, or the way may lead through another location or a dungeon."
> TEST SCRIPT I RESULTS (2026-07-11, user: "everything worked as you said it should
> except Marze → Krosse Cave"):
> [✅] I1. Salva walks/arrives. [✅] I3. Mountain Palace overland refused fast (log
>        12:30:09: 179 impassable comfort-tier segments). [✅] I4. Arlia refusal
>        unchanged (12:30:21). [🔴→fixed] I2/I5: Marze → Krosse Cave FALSE REFUSAL.
> FALSE-REFUSAL DIAGNOSIS (log 12:32 + user's F7 audit from outside Marze 12:33):
> the route is a clean 873-wp COMFORT road whose ONLY wedge is the known Krosse Cave
> canyon-mouth pinch at (89.8,-97.2) — 14.0m from the ring point (99.5,-87.1), just
> OUTSIDE the 10m arrival exemption (re-plans shifted it to 12.6m/12.0m — still out).
> The same pinch class passed from the Krosse City side because it fell inside 10m
> there. Audit cross-check: Mountain Palace/Arlia wedges sit 50m+ from their goals
> (spread over foothills/rock belt), so widening the exemption cannot un-refuse them.
> Audit also showed 7–8 shared "wedges" within metres of the player standing at
> Marze's gate (start-side pinch, sweep-conservative; live walks not affected).
> FIX (2026-07-11, built + deployed): goal exemption 10m → 16m, named constant
> WmSweepGoalExemptDist with the data rationale in its doc comment.
> RETEST J RESULT (2026-07-11 log 12:42+, game restarted with the 16m build): STILL
> REFUSED — but differently: "Cannot reach" (not no-land-route). Log 12:42:59 shows the
> GOAL-side fix worked (no cave-mouth flag) — the new blocker is the START side: user
> stood IN Marze's gate rocks; sweep flagged ONE segment at (208.5,110.0) = 6m from the
> player (Marze gate pinch); marking it + 5 rounds of L22 pre-validate markings SEALED
> the gate corridor → re-plan found no path at all → "Cannot reach". Rest of the 887-wp
> road swept clean. (Also: 12:44:09 the user started a walk to Krosse Cave from near
> Krosse City — cancelled 1s later by opening the nav menu, which stops a walk.)
> FIX 2 (2026-07-11, built + deployed): sweep exemption now applies at BOTH route
> endpoints — constant renamed WmSweepEndpointExemptDist (16m), CountRouteWedges gained
> startExemptDist (measured from path[0]). Start-side gate pinches are never marked
> (marking them seals the player in and refuses routes they physically just walked).
> MP/Arlia unaffected: their wedges sit 44m+ from any endpoint.
> RETEST K RESULTS (2026-07-11 log 12:52+):
> [✅] K1. Marze → Krosse Cave WORKS — from BOTH Marze exits (Marze has 2 world-map
>        triggers, user confirmed; sweep passed cleanly, no false refusal). Caveat:
>        first walk wedged in the cave-mouth canyon ~20m from the entrance (party
>        members crowding + 0.51m walls; 5 stuck-recalcs accumulated 28 blocked zones
>        that poisoned re-plans in the narrow canyon) → "Cannot reach" give-up; the
>        user re-triggered the walk and ARRIVED in 4s (fresh walk clears blocked
>        zones → clean plan straight in).
> [—] K2/K3 not re-run this round; MP/Arlia refused correctly at 12:43/12:44 under the
>        16m build, and the endpoint change cannot affect them (wedges 44m+ from
>        endpoints).
> DECISION (user, 2026-07-11): stuck-give-up behavior stays AS IS — proposed one-shot
> "clear blocked zones + fresh re-plan before cancelling" auto-retry was DECLINED;
> manual walk re-trigger is the accepted recovery. (If this annoys later, the proposal
> is in the session record: once per walk command, on 5th-recalc give-up, wipe blocked
> zones, re-plan fresh, reset counter; cancel for real on second exhaustion.)
> H3 NOTE: the two Marze F7 audits double as the pending auditor-fix re-audit — the
> null-collider guard visibly worked ("N segments had unresolvable overlap hits
> (ignored)") and Mountain Palace audited at 10 wedges instead of 188 overhang
> artifacts. H3 considered DONE.
>
> 🗂️ (superseded by the entry above — instrument built, results in) **ROUTE AUDITOR
> (2026-07-10, build 0/0) — the definitive instrument.
> F7 on the world map (F12 debug ON), no walking needed:**
> - Replays the EXACT auto-walk planning pipeline for EVERY nav-list location from the
>   current position (ring point → safe exit → exit/main legs via the real FindPath) and
>   logs per leg whether it used the comfort tier or the 0.50m FLOOR tier (new
>   WorldmapPathfinder.LastPathUsedFloorTier).
> - PHYSICS-VALIDATES each planned route: sweeps the player's body capsule (r=0.45m,
>   step allowance 0.45m, live ground-truthed per segment) along every waypoint segment
>   against the live wall mask + L24 rock bodies; logs every segment the body cannot pass
>   ("[RouteAudit] ... WEDGE ... hit <collider> L<layer>") plus grid-vs-live height
>   mismatches >1m per route. Summary line + spoken totals (clean / wedgy / no-route).
> - Pathfinder tier-1 failures now log start+goal cell clearance ("no route at 0.60m ...
>   start cell (...) clearance=..., goal cell (...) clearance=...") — this answers the
>   A-vs-B contradiction next time it fires.
> TEST SCRIPT G (F12 ON, world map, from the Krosse plains repro spot):
> [ ] G1. Press F7. Hear "Route audit started..." then (up to ~1 min freeze) "Route audit
>        complete. N routes clean, M with wedge points...". Send Latest.log — the
>        [RouteAudit] table is the whole diagnosis: which routes are physically walkable,
>        which legs run at FLOOR tier, where every wedge segment sits, and endpoint
>        clearances for every comfort-pass failure.
> [ ] G2. OPTIONAL but valuable: after a battle on the road to Salva (the resume-repro),
>        press F7 AGAIN from wherever the battle ended — captures the planning state that
>        produced today's mid-walk floor route.
> [ ] F3 (unchanged, after the audit verdict): D2 (Arlia), D3 (list honesty), D4 (bunny +
>        chest), D5 (stop rule).
>
> 🗂️ (superseded — resolution above) **D1 FAILED (2026-07-10, log 10:49–10:51) — root
> cause was NOT missing rocks (that hypothesis tested and refuted same day); kept for
> the investigation record. Fix + census diagnostic deployed (build 0/0).**
> - SYMPTOM: Krosse plains → Salva on foot wedged 5× in the rock belt at
>   (-171..-183, -236..-255), then "Cannot reach Salva". Reachability verdicts and initial
>   608-wp route were fine; battle interrupt + resume fine; failure was pure movement wedging.
> - OFFLINE WEDGE ANALYSIS (scratchpad wedgeprobe, reads the F9 WMGI grid + today's log):
>   grid steps ahead of EVERY wedge point are gentle (max +0.26m/cell), but the game's live
>   CalcHeight 3m ahead is 1.3–2.1m HIGHER than the grid at all 5 wedges. Player position Y
>   always matches the grid; the rock in front of him does NOT exist in the grid. So NOT a
>   climb-cap problem (the earlier hypothesis) — the bake is missing rocks here.
> - ROOT CAUSE (design gap confirmed in code + game data): the bake instantiates
>   unit.layoutItem, but the game's culler instantiates PoolInfo.poolObject (linked to units
>   by NAME, not reference — CullingUnit has only unitBounds/layoutItem/position/rotation/
>   scale/layer). The bake's collider check ran on layoutItem ONLY: 45+ pools were skipped as
>   "no colliders — visual-only" (bake log 26-7-7_20-16-7, e.g. whole ma_1521_*_LOD and
>   ma_6411_* families). Any such pool whose poolObject DOES carry the collision baked as
>   missing rocks map-wide.
> - CONSEQUENCE FOR C2: the "Krosse↔Arlia↔Lasgus one foot region / gentle ridge east of
>   Salva" audit finding is now SUSPECT (could be missing-rock fiction). Re-audit after the
>   E2 rebake; do not interpret D2 until then.
> - BUILD (2026-07-10, 0/0, deployed):
>   1. F10 now also runs WorldmapGridDiagnostics.LogHeightTruthCensus ([WMCensus] lines):
>      16-direction ring at 2/4/6m comparing live CalcHeight vs grid height (logs any
>      mismatch >0.5m) + the nearest 25 L24 colliders within 20m with tag/topY/ancestor
>      chains — names the prefab that owns any missing rock.
>   2. WorldmapChunkLoader: new ResolveBakeSource — when a layoutItem has no colliders, fall
>      back to the same-named pool prefab (poolByName built from cullingData.poolInfoList)
>      and bake with THAT if it has colliders; logs "baking with the pool prefab (was
>      missing from earlier bakes)" once per rescued pool; ready-line now logs poolPrefabs=N;
>      genuinely collider-free pools still skipped (logged with "layoutItem or pool prefab").
> TEST SCRIPT E (F12 ON, world map):
> [ ] E1. CENSUS — stand in/near the wedge rocks between Krosse and Salva's north entrance
>        (last known player pos (-183.3,-255.2) is perfect) and press F10. Log gets
>        [WMCensus]: expect MISMATCH lines with diff around +1 to +2 and rock prefab names
>        in the census. This confirms the diagnosis with live data BEFORE rebaking.
> [ ] E2. REBAKE — press F9 (same chunk-loading bake as C1, ~2 min, screen freezes). The
>        log MUST show "baking with the pool prefab" lines — that's the fix engaging. If
>        E1 showed mismatches but E2 shows NO such lines: STOP, send Latest.log (hypothesis
>        wrong — different cause).
> [ ] E3. D1 RETRY — after the bake completes, auto-walk Krosse plains → Salva on foot.
>        Must arrive at the north entrance.
> [ ] E4. If E3 passes: tell Claude FIRST (offline re-audit of the new grid, incl. the
>        Arlia-ridge question), then continue D2–D5 from the script below.
>
> ⏳ **PENDING USER TEST (2026-07-07c): CHUNK-LOADING BAKE — deployed, build 0/0.
> F9 now streams the map itself while baking (user approved the design).** New
> WorldmapChunkLoader.cs + WorldmapGridGenerator.cs restructured: the per-cell probe
> body is unchanged but extracted into a local ProbeCell(ax,az); the bake iterates
> 64m TILES — per tile: instantiate every CullingData unit overlapping the tile
> (bounds+2m margin), Physics.SyncTransforms, probe the tile's cells, DestroyImmediate
> the clones (try/finally at both tile and bake level — no clone leaks even on
> exception). Chunk clones are side-effect-free: instantiated under an INACTIVE
> staging root (Awake deferred), ALL MonoBehaviours (CullingSettings/DistanceLOD/...)
> destroyed while asleep, then reparented to an active root with every child
> activated (disabled LOD children carry real colliders; double LOD surfaces are
> near-coincident — fine at 0.5m cells). Collider-free visual pools are skipped
> (checked once per prefab, logged). Unit root gets unit.layer; collider children
> keep prefab layers (the real ones). ABORT rule (honesty): culling data unreadable →
> spoken abort, same as the mask-read rule — never bake far-field fiction. Start
> speech now says chunk loading + several minutes + game freeze. Progress logs every
> 200 tiles incl. chunk-load count + elapsed; final "[GridGen] Chunk stats:" line
> (unitsIndexed/instantiations/skippedNoColliders/activationFixes).
> KNOWN LIMITATION (accepted): ClearEntranceTriggers runs after chunk disposal — its
> CalcHeight probes see resident-only terrain; entrances sit on resident base-road
> geometry (legacy grid always got them right), revisit only if the audit flags one.
> TEST SCRIPT (F12 ON, world map, anywhere):
> [ ] C1. Press F9. Expect the NEW start speech ("...loading distant terrain chunks...
>        several minutes... game will freeze"). Wait it out (est. 3–15 min, screen
>        frozen is NORMAL). Expect the "Grid saved..." speech with per-mode counts.
>        Log must show "[GridGen] Chunk loader ready: ~32633/32633 units indexed" +
>        tile progress lines + Chunk stats line. If it aborts with a spoken error,
>        STOP and send the log.
> ✅ C1 PASSED (2026-07-07, log 20:17–20:18): bake ran in ~119s total (17546 chunk
>        loads over 2340 tiles, 32633/32633 units indexed, 4253 activation fixes, no
>        instantiate failures). Saved WMGI 118MB. ~18 pools logged collider-free
>        (visual-only) and were skipped — expected.
> 🟡 C2 AUDIT RESULT (offline, scratchpad gridaudit): **HEIGHTS ARE HONEST — but the
>        foot region result overturns an assumption, needs ONE in-game experiment.**
>        - Truth anchors PASS: belt samples A/C/D + B6 spot match legacy exactly where
>          legacy was locally correct (23.2/32.3/54.3/51m); failed chest (-370,-179)
>          now has ground 10.3m footBlocked but BUNNY-PASSABLE (was noGround);
>          global diff direction exactly as predicted for honesty (1.02M cells HIGHER
>          vs 0.12M lower — legacy pierced unloaded rocks; +350k real ground cells).
>        - Counts sane: footBlocked=412k (incl 112k sealed), bunnyBlocked=160k,
>          bunnyWalkable > footWalkable. Salva buried ring point blocked+sealed same
>          as legacy (ring picker tiers handle it).
>        - SURPRISE: Krosse↔Arlia↔Lasgus remain ONE foot region even at a 50cm/cell
>          climb cap (sweep 500→50cm never separates them; legacy only separated them
>          via its FICTIONAL obstacle belts). BFS path trace shows a specific GENTLE
>          ridge route east of Salva: (-42,-400) y5 → x≈-36..-45 climbing to y≈39 →
>          (-93,-82) Krosse plains, ~322m, max step ≤50cm — real geometry, no wall
>          colliders there (walls were fully resident during bake, proven by trace).
>          EITHER the ridge is genuinely walkable in-game (old "Arlia unreachable" was
>          an artifact of legacy fiction!) OR the game blocks steep slopes without
>          walls (then per-mode climb caps are the fix). Only a real walk decides.
> TEST SCRIPT NEXT SESSION (F12 ON; the new grid loads AUTOMATICALLY — F9 already
> saved it to UserData; first world-map path builds foot regions ~1-2s, first mounted
> query builds bunny regions):
> [ ] D1. FOOT REGRESSION — auto-walk Krosse plains → Salva. Must still route and
>        arrive (north entrance).
> [ ] D2. THE ARLIA EXPERIMENT (decides the climb-rule question) — on FOOT, from
>        Krosse plains, auto-walk to Arlia. The pathfinder WILL now offer a ~322m+
>        route over the hills east of Salva. Outcomes: (a) you physically ARRIVE in
>        Arlia → geography assumption was wrong, grid+game agree, no fix needed;
>        (b) auto-walk wedges/gives up on a slope around x≈-40 (east of Salva) →
>        the game DOES block slopes for foot → send log, Claude adds per-mode climb
>        caps (data: the wedge Y/position tells the real foot slope limit).
>        Either outcome is a WIN — it's the deciding measurement.
> [ ] D3. LIST HONESTY — nav list on foot: Arlia/Mountain Palace will likely show
>        PLAIN names now (annotations gone) — expected pending D2's verdict; ocean
>        towns must still read "unreachable on foot".
> [ ] D4. BUNNY — mount, reopen list (one-time region build pause OK), ocean towns
>        keep "unreachable by bunny"; a mounted walk to a foot-blocked spot (the
>        (-370,-179) chest!) should route and arrive.
> [ ] D5. Anything reading "unreachable" that you can genuinely reach = STOP, send
>        log ([WMReach] lines are the diagnosis).
>
> ✅ **STREAMING CONTROLLER FOUND (2026-07-07, F6 trace log 19:51–19:53, user rode
> Krosse→south coast): it is the game's `CullingManager` (SingletonMonoBehaviour,
> Il2CppGame) — a pooled prefab-instancing culler, NOT scene loading.** Trace facts:
> 41000 baseline watched colliders; L15/L17/L21/L22/L23 counts CONSTANT the whole ride
> (walls do NOT stream — consistent with B7: the far-field wall flips came from wrong
> HEIGHTS, not missing walls); ONLY L24 Height detail churns (1440→1930, IN=707 OUT=219,
> ACT/DEACT=0 → colliders are created/destroyed, i.e. pool instantiate/repool). Streamed
> parents are `ma_XXXX_XXXa(Clone)` prefab clones in scene 'DontDestroyOnLoad' under
> hierarchy `System(SystemManager) → CullingManager(CullingManager) → New Game Object →
> <clone>(CullingSettings)`; some via `DistanceLOD`. IN distances 4–1187m → culling is
> frustum+distance (FrustumTestJob with frustumPlaneArray/boundsArray/cameraFarClip +
> static HeightThreshold/DistanceThresholdSqr; landingTestJob exists for psynard landing;
> SetLandingState(bool)). Decompiled API (interop stubs, no bodies): CullingManager fields
> `cullingData` (CullingData ScriptableObject: `unitList` = EVERY unit's bounds/position/
> rotation/scale/layer + `poolInfoList` = poolObject prefab, cullingDistanceType, pool
> count), `cullingObjPoolDict`, `drawUnitList`, distances arrays; static
> GetCullingDistance(CullingDistanceType{Near,Middle,Far,Farthest}).
> **BAKE FIX DIRECTION (to discuss with user before building): don't fight the culler —
> at F9 time iterate `cullingData.unitList` ourselves: for each unit, briefly instantiate
> its collision prefab at position/rotation/scale (or move one pooled clone), probe the
> grid cells inside its bounds, then remove it. Full-map honest heights, bounded memory,
> no dependency on camera/frustum. Pool-size limits make "inflate culling distances"
> unreliable (pools sized for the working set).**
>
> ✅ **CULLING DATA DUMP DONE (2026-07-07b, log 20:03) — BAKE-FIX DESIGN VALIDATED.**
> Facts: culling distances Near=100m / Middle=330m / Far=535m / Farthest=775m — the
> Near tier IS the ~100–150m bake-fidelity radius from B7 (ground detail pools like
> ma_6411_* are Near-tier); Far/Farthest explain trace INs at 700–1100m. 129 pools
> (prefab name + tier + poolSize 1–32 — pool caps confirm "inflate distances" is a
> dead end). unitList = 32,633 units covering X=[-1092,1125] Z=[-689,826] (whole
> populated Expel map), layoutItem null for 0 units, and layoutItem prefab names match
> pool names 1:1 (e.g. 'ma_6706_452a', 'ma_1403_011c_LOD') → unit→prefab mapping is
> direct via unit.layoutItem. Unit.layer is mostly L0 (28041) + L24 (4592) — the root
> layer is NOT the collider layer; collider children (Mesh_Col/Col_Height) carry their
> own prefab layers, so the bake must NOT filter units by unit.layer.
> NEXT: present the unit-iterating bake rewrite plan to the user (discuss-first rule)
> — per unit: Instantiate(layoutItem) at position/rotation/scale, Physics.SyncTransforms,
> re-probe grid cells inside unit bounds (+margin), destroy; then normal resident-terrain
> pass covers everything else. LOD prefabs: ensure highest-detail child (Mesh_L0) active.
> Expect a longer F9 (maybe 5–15 min) — one-time cost, honest map-wide.
>
> 🗂️ (superseded — results above) **STREAMING INVESTIGATION BUILD (F6) — deployed,
> build 0/0.** New WorldmapStreamingDiagnostics.cs + F6 hotkey in DebugHotkeys.cs (debug
> only, world map only). Purpose: identify the game's collision streaming mechanism so a
> future F9 bake can force-load everything (item A of the approved plan). What F6 does:
> press ONCE = baseline census of ALL colliders on the live wall+height masks (union of
> LayerMaskWall/Bunny/Psynard/WallHeight read live, fallback to L15/17/21/22/23/24/26 if
> unreadable — logged), including INACTIVE ones (FindObjectsOfType(includeInactive)),
> grouped by parent GameObject with XZ extents + ancestor-component dumps of the 8 biggest
> groups; then a full re-sweep every 2.5s diffs the scene: [WMStream] IN (new collider,
> with parent/scene/player-distance), OUT (destroyed/unloaded), ACT/DEACT (active or
> enabled flips) — first event per parent also dumps its ancestor chain (the streaming
> controller should appear there, or as an additive scene name). Press F6 AGAIN = summary:
> event counts, stream-IN distance range (= the real streaming radius), parents/scenes
> involved, and a VERDICT line (created/destroyed vs merely toggled — decides whether
> force-load must drive the loader or can just activate objects). Trace self-stops when
> leaving the world map or after 5 sweep errors. Event log capped 40 lines/sweep
> (overflow counted), ancestor dumps capped 30 parents, census line only when the
> collider count changes.
> TEST SCRIPT (F12 debug ON, on the world map):
> [ ] S1. Press F6 → hear "Streaming trace started. Ride around, then press F6 again to
>        stop." Log gets [WMStream] TRACE START + baseline groups + ancestor chains.
> [ ] S2. Ride the bunny a LONG stretch (300m+, e.g. Krosse plains → past Salva, or along
>        the coast). Every ~2.5s the trace diffs; expect IN/OUT (or ACT/DEACT) events as
>        rock detail loads/unloads around you.
> [ ] S3. Press F6 again → "Streaming trace stopped." Log gets event totals, the distance
>        band, and the VERDICT line.
> [ ] S4. Send Latest.log. Analysis gate: the VERDICT + ancestor dumps decide the
>        force-load approach (drive the loader vs activate objects vs progressive bake).
> NOTE: do NOT press F9 until the streaming fix lands — any bake made now is only
> trustworthy ~125m around the bake spot (proven 2026-07-06).
>
> ✅ **RECOVERY CHECK PASSED (2026-07-07, log 19:40): after the retired-grid deletion, the
> embedded legacy grid re-extracted on launch** ("[GridGen] Extracted embedded expel grid"
> + WMGH legacy load + bunny fail-open hint all present); user auto-walked to Krosse Cave
> and arrived normally (known-good foot behavior confirmed). User intended an F9 regen this
> session but no bake ran (no [GridGen] bake lines in the log) — deliberately NOT redone,
> see the NOTE above.
>
> 🔴 **B7 AUDIT GATE FAILED (2026-07-06) — ROOT CAUSE FOUND AND PROVEN: the world map
> STREAMS detail ground collision (rock Mesh_Col/Col_Height, layer 24) only within
> ~100–150m of the player. A single-spot F9 bake is only correct near where the player
> stands.** Test results (log 18:49–18:57): B1–B5 PASS on the legacy grid (foot annotations
> correct, Krosse→Salva walk OK, Arlia honest refusal in 0ms, chests/enemies kept, bunny
> fail-open with regenerate hint). B6 bake ran clean (live masks foot=0x04E28000
> bunny=0x00620000, ~100s, saved WMGI 117MB). B9 partially exercised: bunny regions built
> lazily (517ms), Arlia correctly lost its suffix mounted, ocean towns kept "unreachable by
> bunny", mounted walks arrived. BUT offline audit vs the legacy grid proved the new grid
> is FICTION beyond ~125m of the bake spot (player stood near the first chest at
> (-266,-105) during F9):
> - Height agreement ~99% within 125m of the bake spot; 10–30% of cells diverge beyond,
>   with the new height LOWER in ~100% of divergent cells (CalcHeight pierced unloaded
>   rock colliders and hit base terrain: rock belt west of Salva real standing Y≈15–17,
>   baked 4.8–5.6). Massive ground-status flips both directions vs legacy (the LEGACY grid
>   has the same disease far from ITS OWN bake spot — it was only ever locally correct).
> - Because the obstacle probe runs at groundY+0.5, pierced heights put it BELOW the real
>   L22/L23 walls' Y-span → 7082 obstacle→walkable flips (0 reverse) in the Salva box alone
>   → foot regions FALSELY MERGED: Krosse plains ↔ Arlia valley ↔ Lasgus are ONE region in
>   the new grid (two fictional smooth descents east and west of Salva). "Arlia reachable
>   on foot" / "Mountain Palace reachable" in the 18:54 list were honest reads of a wrong
>   grid. B8's expectation (Mountain Palace unreachable) can NOT be validated on this grid.
> - USER-REPORTED BUG EXPLAINED (foot walk to Unopened chest at (-370,-179) kept failing):
>   A* routed a fictional low corridor (grid Y≈5) while the player physically walked the
>   rock tops 12m above; real Col_Obstacle walls up there wedged him (3 stuck-recalcs,
>   each recalc re-planned the same fiction). The chest cell itself baked noGround. The
>   bunny succeeded because it physically climbs everything regardless of grid fiction.
> - USER-REPORTED BUG 2 (bunny chest arrival "not close enough"): both mounted arrivals
>   stopped at distXZ≈1.25m; the game's "Open Treasure Chest" prompt is side/facing-
>   sensitive — first approach (NE side) prompt didn't persist, second (NW side) worked
>   and the chest opened. Candidate fix (NOT implemented): creep-closer-until-prompt for
>   object targets, like locations gate arrival on the enter prompt. Discuss first.
> DECISIONS TAKEN: new grid RETIRED (do NOT re-embed, do NOT trust annotations from it,
> Phase E stays blocked). Offline evidence tooling in scratchpad (gridprobe) built on
> permanent tools\GridAnalysis.cs. Game stores map collision as ScriptableMapCollisionData
> → MapCollisionDataGroup (CollisionPointerData/CollisionTransformData); the consumer is
> native/scene-side (not in managed stubs) — finding the streaming controller needs
> in-game scene inspection.
> ✅ DONE (2026-07-06, user approved): retired grid DELETED from UserData — the embedded
> legacy grid re-extracts on next game launch (watch for "[GridGen] Extracted embedded
> expel grid" + WMGH legacy load line in the log; foot behavior back to known-good,
> bunny fail-open, no annotations while mounted).
> USER OBSERVATION CONFIRMED BY MECHANISM (2026-07-06): past navigation trouble around
> Lacuer and Linga (vs. fine behavior around Krosse/Arlia) matches the streaming bug —
> no bake was ever done standing on the Lacuer continent, so its grid data (old AND new)
> is far-field fiction; the offline diff map shows heavy old-vs-new disagreement across
> that whole landmass. AFTER the streaming fix lands: rebake standing on the Lacuer
> continent and RE-TEST navigation around Lacuer City, Linga, Hilton — remaining issues
> there are then real, separate bugs.
> NEXT WORK ITEM (user approved): (A) STREAMING INVESTIGATION — in-game diagnostic to
> find the collision streaming controller: dump which GameObjects own the rock
> Mesh_Col/Col_Height (layer 24) colliders near a distant coordinate, log what
> enables/disables them as the player moves (component names up the hierarchy, active
> state transitions), then try force-loading during the bake. Fallback if forcing is
> impossible: (B) progressive bake — F9 bakes/merges only the trustworthy ~100m patch
> around the player with a per-cell confidence flag; grid fills in as the user travels.
>
> ⏳ (superseded by the 2026-07-06 entry above) **BUILD 2 DEPLOYED (2026-07-05 session 4,
> build 0/0): Phases B+C+D implemented.** Session 4 ended with NO tests run yet.
> The bake+audit gate (B6/B7) must pass BEFORE trusting any bunny annotations.
> What changed (all world-map only):
> - **Grid format v2 "WMGI"** (new WorldmapGridFormat.cs, save/load/CachedGrid moved out of the
>   generator): PURE height lane (0 = no ground) + per-cell FLAGS byte (bit0 footBlocked,
>   bit1 bunnyBlocked, bit2 sealedInterior); header records the baked masks/floors. The current
>   embedded grid is legacy WMGH: it loads foot-IDENTICAL to before, bunny data "unavailable" →
>   every bunny question fails open (no annotations) + one log hint to regenerate.
> - **Generator** (F9): reads GameRenderManager.LayerMaskWall/LayerMaskBunnyWall LIVE at bake
>   (aborts with spoken error if unreadable — never falls back to hardcoded bits); probes each
>   cell per mode (foot = full 6-layer mask incl. the 4 layers the old grid missed; bunny =
>   L17|L21|L22, no CharaWall, no sub-cell tables); flood-fill seal runs PER MODE and the
>   d<4→8 diagonal bug is FIXED; entrance punch clears both mode bits (+ repairs roof-height
>   cells to road level). Bake now ~2 min (two probe passes), speech says per-mode counts.
> - **Pathfinder**: FindPath(start, end, MODE, blocked); per-mode connected regions (foot eager
>   at load, bunny LAZY on first mounted query, one-time ~1-2s + 75MB); region fast-reject per
>   mode (skipped when that mode's map is missing — fail open); 0.60m comfort tier + clearance
>   penalty/offsets are FOOT-ONLY (bunny ignores CharaWall gaps); runtime stamps/start-clears
>   journal the FLAGS lane (height lane read-only; legacy grid keeps its old height-lane clear).
>   Bunny on legacy grid searches with the foot predicate (safe, logged). Psynard never searches.
> - **Nav list honesty (annotate-first)**: BuildWorldmapLocations resolves per-location
>   reachability for the CURRENT mode (new NavigationHandler.Worldmap.Reachability.cs: one
>   mapjump scan per list-open; entrance candidates from the SAME sampler auto-walk uses, so
>   list and walk can never disagree). PROVEN-disconnected items read "{name}, unreachable on
>   foot" / "by bunny" (Loc keys nav_wm_unreachable_foot/bunny). NOTHING is hidden; walking an
>   annotated item still gives the honest refusal. EVERY verdict logged at Msg level:
>   "[WMReach] <name>: <verdict> (<mode>, player region N) — <reason>". Unknown ALWAYS =
>   reachable (all failure paths fail open).
> - **Chest/enemy check**: the 10-sample CalcHeight ocean line-check is DELETED (it false-hid
>   chests behind lake fingers); replaced by per-mode region compare (region 0 either side →
>   keep; psynard → keep). Keep-all safety net in SortAndFilterUnreachable unchanged.
> - **Mode plumbing**: travel mode re-queried at every path computation (walk start, battle
>   resume, mid-walk recalc) — dismounting mid-walk re-plans on the foot lane automatically.
> - Offline audit tool GridAnalysis.cs updated for WMGI (both lanes, per-mode regions, WMGH
>   branch kept) — now PERMANENT at E:\StarOcean\tools\GridAnalysis.cs (tools\ is excluded
>   from the mod build in the csproj; run it from a scratch script, no game needed).
> - Files new: WorldmapGridFormat.cs, WorldmapPathfinder.Regions.cs,
>   NavigationHandler.Worldmap.Reachability.cs. Modified: WorldmapGridGenerator.cs (rewritten),
>   WorldmapPathfinder.cs (rewritten, now partial), WorldmapGridDiagnostics.cs,
>   WorldmapDiagnostics.cs (type refs), NavigationHandler.Worldmap.Pathfinding.cs,
>   NavigationHandler.Worldmap.cs, NavigationHandler.AutoWalk.cs,
>   NavigationHandler.Build.Worldmap.cs, Loc.cs.
>
> SELF-REVIEW PASS (same session, 8-angle multi-agent review of the diff, fixes applied +
> rebuilt 0/0): (1) all reachability verdicts (list annotation, chest/enemy filter, ring-point
> tier 1) now compare against the player's START-REGION SET (new
> WorldmapPathfinder.GetStartRegionIds = snapped cell + 3m start-clear disc + rim, exactly
> FindPath's own StartTouchesRegion semantics) instead of the single player cell — the
> single-cell compare could falsely report everything unreachable while the player stands in
> a small rocky pocket the A* bridges out of; (2) stuck-position stamps now WIN over the
> start-clearing (old code's implicit precedence, restored via a stamped-cell exemption set)
> — without it a recalc near the wedge spot could re-route into it forever; (3) grid load now
> bulk-reads both lanes (was ~75M per-element stream reads = seconds of freeze at first
> world-map path); (4) F11 grid diagnostics made flags-aware (v2 blocked cells read as
> obstacles, not walkable); (5) failed bunny region build latches instead of retrying its
> 75MB allocation every query; (6) reachability ring sampling stops at first proven-connected
> candidate (saves physics calls per list-open). Known accepted deviations (reported, not
> changed): dev-only F9/F10 speech strings remain raw (pre-existing file style, CLAUDE.md
> Loc rule violation to clean up later); WorldmapPathfinder.cs 840 lines (over the ~500 aim);
> location→entrance attribution uses nearest-mapjump-within-100m (same rule as the walk
> picker, so list and walk stay consistent even if a location has no own trigger).
>
> TEST SCRIPT (F12 debug ON; steps B1–B5 work on the CURRENT embedded legacy grid, before F9):
> [ ] B1. FOOT ANNOTATIONS (legacy grid) — on the world map near Krosse, open the nav list,
>        category Locations. Same-region towns (Krosse City, Salva...) read PLAIN names; Arlia
>        (valley region) should read "Arlia, unreachable on foot". Log has one [WMReach] line
>        per location with a reason. Bunny NOT summoned yet.
> [ ] B2. FOOT REGRESSION — auto-walk Krosse → Salva still completes (connected ring point at
>        the north entrance, as before).
> [ ] B3. HONEST REFUSAL UNCHANGED — walk an annotated town (Arlia): still the "No walkable
>        route..." message, near-instant, no walking.
> [ ] B4. CHESTS/ENEMIES REGRESSION — world-map chests + enemies: nothing that used to be
>        listed vanishes; walks still arrive. (If something vanishes: send log — the [WMReach]
>        chest line names both region ids.)
> [ ] B5. BUNNY ON LEGACY GRID (before F9) — mount the bunny, reopen the list: NO bunny
>        annotations (fail open), log has the bunny-data-unavailable / regenerate hint.
>        Auto-walk while mounted still works (uses foot routes — safe).
> [ ] B6. F9 REGEN — on the world map press F9. Expect start speech mentioning foot AND bunny,
>        ~2 minutes, then "Grid saved..." speech with per-mode obstacle counts. Log must show
>        "[GridGen] Bake masks (read live): foot=0x04E28000 ... bunny=0x00620000 ..." — if it
>        aborts with a spoken mask error, STOP and send the log.
> [ ] B7. OFFLINE AUDIT GATE — after F9, tell me; I diff the new grid offline with GridAnalysis
>        BEFORE we trust it in game: known Krosse/Salva/Arlia probe points keep their foot
>        regions, per-mode counts sane, Mountain Palace/Lasgus split off the mainland foot
>        region. Any known-walkable road flipping foot-blocked = stop and investigate.
> [ ] B8. FOOT + NEW GRID — reopen list on foot: Mountain Palace should NOW read "unreachable
>        on foot" (the 4 missing wall layers are baked). Krosse→Salva walk still completes.
> [ ] B9. BUNNY + NEW GRID — mount, reopen list: first open builds bunny regions (one-time
>        ~1-2s pause is OK, logged). Towns beyond CharaWall region borders (Arlia!) should
>        LOSE the suffix (bunny crosses region walls); anything beyond OCEAN keeps
>        "unreachable by bunny". Auto-walk mounted to a previously foot-unreachable town →
>        should route and arrive. Dismount mid-walk → next recalc re-plans on foot or refuses
>        honestly.
> [ ] B10. ANY false "unreachable" in ANY mode (a town you can genuinely walk/ride to reads
>        the suffix) = STOP, send Latest.log — the [WMReach] reason line is the diagnosis.
> AFTER VALIDATION: I gzip the new grid into grids\worldmap_expel.grid.gz + rebuild so the
> shipped DLL carries the v2 grid. Phase E (hide toggle) comes ONLY after zero
> false-unreachables confirmed.
>
> ✅ **PHASE A COMPLETE — ANALYSIS GATE PASSED (2026-07-05 session 3, log 14:07–14:11).**
> All Build-2 inputs confirmed from the user's ride log:
> - **Foot mask = 0x04E28000** = L15 ObjectWall + L17 PsynardWall + L21 GimmickWall + L22 Wall
>   + L23 CharacterWall + L26 CameraDitherWall. Our grid bakes ONLY L22|L23 → **4 layers
>   missing (L15/L17/L21/L26)** — this is the confirmed suspect for the Lasgus/Mountain-Palace
>   false-connection. Build 2 bakes the game's masks verbatim.
> - **Bunny mask = 0x00620000** = L17 + L21 + L22 ONLY. So the bunny IGNORES L23 CharacterWall
>   (region boundary walls — matches it crossing Salva/Arlia-type boundaries in the trace) and
>   L15 ObjectWall, but IS still blocked by L22 Wall (plain rock walls). **Psynard mask =
>   L17 PsynardWall only.**
> - **Virtual GetLayerMaskWall() confirmed**: FieldPlayer returns foot mask, FieldBunny returns
>   bunny mask. **Detection validated**: IsFieldFlag(Bunny) and TryCast<FieldBunny> agreed on
>   every sample; "mode Foot→Bunny" transition logged correctly at mount.
> - **Bunny body = IDENTICAL to foot** (capsule radius 0.5000m, height 1.70m,
>   MoveCollisionRadius 0.50m) → same 0.50m clearance floor, NO re-measure/re-bake cycle needed
>   (user's instinct was right).
> - **Ride trace**: bunny repeatedly crossed foot-BLOCKED cells and climbed slopes up to
>   ~15.8m ΔY over ~9.3m horizontal (~0.85m per 0.5m cell — under the existing MaxClimbCm=500,
>   so the SAME climb rule works for both modes; colliders, not slope, are the differentiator).
>   Bunny NEVER entered an ocean (no-ground) cell → needs ground confirmed.
> BUILD 2 RULES LOCKED: foot lane blocked by game foot mask {15,17,21,22,23,26}; bunny lane
> blocked by game bunny mask {17,21,22}; both 0.50m floor; same climb rule; ocean = no ground
> blocks both; psynard = everything reachable (no grid).
>
> 📝 **FEATURE REQUEST (user, 2026-07-11 — for a FUTURE session, not now): world map
> nav list is missing FISHING SPOTS.** The list currently does not offer them as
> targets. Task when picked up: (1) find how fishing spots exist on the world map
> (decompiled/ — likely related to the fishing system, see memory fishing-system for
> the field-map integration) and add them to the nav list build; (2) survey what OTHER
> world-map point-of-interest types exist that the list doesn't cover yet (user asked
> for a general audit, not just fishing).
>
> ✅ **2026-07-11: WELSH SP BUG FIXED + VALIDATED (retest M, user: "works perfectly,
> calculations are perfect") — CLOSED.** Log 18:55 confirms: specialty rows match in
> specialSkillItemDataList (specialID set, e.g. SCOUT), fresh-compute branch runs
> (Scouting = Danger Radar 55; Compounding = Biology 235 + Herbology 70 + Mental
> Science 226). Temp SpDiag diagnostics REMOVED, final build 0/0 deployed.
> FindSkillRowOnScreen (name-verified list lookup) is the permanent fix.
> Committed to master as 6135530 (local; not yet pushed to origin). Test L logs (18:02) were definitive: hover diagnostic showed
> screen='Scouting' idx=8 but row name='Resilience' consumeSP=1 — the Enhance→Skill
> screen has TWO Square-toggled tabs backed by SEPARATE lists (skills: itemDataList,
> specialties: specialSkillItemDataList) and the mod always indexed itemDataList, so
> every specialty row announced the SAME-POSITION row of the SKILLS tab (Oracle→
> Biology's 4, Train→Faeriology's 60, Familiar→Eye for Detail's 18, Scouting→
> Resilience's 1 — "believable" costs on other characters were coincidental garbage
> too). Triangle narrow-down worked (narrowDownItemDataList path, Danger Radar 130 ✓).
> The specialty fresh-compute branch NEVER ran (specialSkillID always INVALID on
> skills-tab rows). Hypotheses 1–3 all wrong: it was list mis-selection, one level up.
> FIX: FindSkillRowOnScreen(name, idx) — name-verified lookup across all three lists
> (priority: narrowDown if active → specialSkill → skill); currentState NOT trusted
> (IC action selector precedent: native state fields go stale); no match → announce
> WITHOUT SP cost + log reason (never a wrong number). SpDiag hover log kept to
> confirm specialty rows now hit the fresh-compute branch; remove all SpDiag after M.
> RETEST M (F12 ON, restart game):
> [ ] M1. Welch → specialties tab: Scouting must NOT read "1 SP" (expect Danger
>        Radar-driven cost, ~130); Oracle/Train/Familiar read sane costs.
> [ ] M2. Skills tab unchanged (Danger Radar 130, Eye for Detail 18).
> [ ] M3. Triangle on Scouting: components still read correctly.
> [ ] M4. Another character's specialties for comparison (e.g. Claude).
> [ ] M5. Send Latest.log (SpDiag lines must show specialID set + "matched in
>        specialSkill list" on the specialties tab).
>
> 🔬 **2026-07-11: WELSH SP BUG PICKED UP — DIAGNOSTIC BUILT + DEPLOYED (build 0/0),
> PENDING TEST SCRIPT L.** Debug plan below executed: LogSpecialtySpDiagnostic in
> CampMenuHandler.Formation.cs (called from the specialty branch, F12-gated, marked
> TEMPORARY — remove after diagnosis). Per specialty hover it logs: specialSkillID,
> playerID the cost was computed for, stale itemData.consumeSP/isLevelMax, AND both
> level-up lists side by side — the fresh CalcNeedSpecialSkillForLevelUp result and
> the game's own precomputed itemData.levelUpDataList — each entry as
> skillID('name') levelUps=N sp=M. Decides hypothesis 1 (garbage entries) vs
> 2 (lagging-component semantics: fresh list legitimately short/cheap) vs 3 (wrong
> character: playerID mismatch).
> TEST SCRIPT L (F12 debug ON):
> [ ] L1. Camp → Enhance → Skill, switch to WELSH, hover 2–3 specialties that read
>        "1 SP" (include Scouting), plus one that reads a believable cost.
> [ ] L2. On Scouting press Triangle and note the component skills + SP costs the
>        narrowed list reads out (tell me what you heard).
> [ ] L3. Also hover Scouting on a NORMAL character (e.g. Claude) for a baseline.
> [ ] L4. Send Latest.log ([GAME] [SpDiag] lines are the evidence).
>
> 🐛 **BUG NOTED (2026-07-05, user report — fix in a FUTURE session, not now): Welsh specialty
> SP cost reads "1 SP" wrongly.** Camp → Enhance → Skill on WELSH: most SPECIALTY rows announce
> "1 SP to increase" (e.g. Scouting lv7 → 1 SP), but Triangle (component-skill view) shows the
> real component costs are far higher (Danger Radar). Code path: CampMenuHandler.Formation.cs
> SkillInfoPresenter_Set_Postfix, specialty branch (~:233-258) — cost = SUM of
> levelUpList[i].consumeSP over UICommon.CalcNeedSpecialSkillForLevelUp(charaParam,
> specialSkillID) (the "fresh compute" added for the stale-itemDataList fix). Hypotheses, in
> order: (1) IL2CPP field read on the returned list entries is wrong for some entries (constant
> "1" smells like reading a count/level/bool, not SP); (2) semantics: the game function may
> return ONLY the lagging component(s) needed for +1 specialty level — if one cheap component
> lags, "1 SP" could even be CORRECT and the real bug is unclear wording (needs verification
> against the game's own displayed number); (3) charaParam from tabBase.currentPlayerID stale
> for Welsh (recently recruited, unique skill set). DEBUG PLAN when picked up: F12-log each
> levelUpList entry (skillID + name + consumeSP) plus the stale itemData.consumeSP for a Welsh
> specialty, compare with the Triangle per-component costs the user hears. See memory
> welsh-specialty-sp-bug.
>
> ✅ (tested, superseded by the analysis above) **PHASE A INVESTIGATION BUILD (build 0/0,
> deployed — zero behavior change, all debug-gated).** New WorldmapTravelMode.cs
> (WorldmapTravel.CurrentMode()), WorldmapGridDiagnostics.cs (LogTravelMasks + moved
> LogPlayerCollider/MeasureGapWidth out of the generator, which shrank 1126→893 lines),
> WorldmapPathfinder.TryGetCellRaw (diagnostic cell reader), DebugHotkeys F10 now also dumps
> travel masks + per-frame WorldmapGridDiagnostics.Tick (mode-transition log + bunny ride
> trace vs the loaded FOOT grid, 0.5s throttle, [WMInvest] prefix).
> TEST SCRIPT (F12 debug ON, world map):
> [ ] A1. On FOOT press F10 → "Collision diagnostics logged." Log gets the [WMInvest] mask
>        dump (LayerMaskWall/BunnyWall/PsynardWall decoded per layer) + foot capsule.
> [ ] A2. Summon + MOUNT the bunny → log should show "[WMInvest] mode Foot→Bunny". Press F10
>        again (captures the BUNNY capsule + the virtual GetLayerMaskWall for the bunny).
> [ ] A3. RIDE across things that block you on foot: rocky obstacle belts, a region boundary
>        (e.g. toward Arlia past Salva), up a mountain slope (Lasgus), along a shoreline/
>        shallows. The trace logs "[WMInvest] Bunny entered foot-BLOCKED / OCEAN / walkable
>        cell at (x,z)" transitions + climb lines — this is the DATA that decides the bunny
>        grid rules.
> [ ] A4. DISMOUNT → log "mode Bunny→Foot". Press F10 once more on foot.
> [ ] A5. Send Latest.log. (Allocating skill points first is fine — no mod impact.)
> ANALYSIS GATE for Build 2: exact per-mode mask bits; does footMask == L22|L23 (if NOT, the
> extra layers likely explain the Lasgus/Mountain-Palace false-connection); bunny capsule
> radius; what the bunny actually crossed (walls? slopes? shallows?) → bunny region rule.

> ✅ **CONFIRMED WORKING (2026-07-05 session 3, user tested): "Unreachable Salva from Krosse" —
> FIXED via region-aware entrance picking.** User walked the Salva route successfully.
> (P1–P6 perf tests below: re-run opportunistically; speed itself was already confirmed.) User tested the perf pass: SPEED CONFIRMED (paths 0–436ms,
> rejects 0ms in log), but Salva reported unreachable from Krosse City — a real bug, diagnosed
> OFFLINE from the actual grid file (scratchpad GridAnalysis.cs, no game needed):
> - The grid is FINE: Krosse plains, Krosse ring, and Salva's NORTH mapjump entrance
>   (-162.9,-307.1) are all in the same connected region (mainland, 1.58M cells). Salva IS
>   grid-reachable. The corridor is not sealed.
> - ROOT CAUSE: Salva is a BOUNDARY town (Krosse plains on the north, Arlia valley on the
>   south — its story role is the pass between them). PickReachableRingPoint chose a walkable
>   entrance point on the VALLEY side (region 213 in-game) because it checked only walkability,
>   never connectivity, and sampled only one trigger. FindPath then honestly (and correctly,
>   for that wrong point) said "different regions". This was the KNOWN RESIDUAL from 2026-06-30.
> - Old code would have failed here too (full-sweep no-path → straight-line grind → cancel);
>   the new region check just exposed it fast and honestly.
> FIX (build 0/0, deployed): ComputeEnterTriggerTarget now collects ALL ground-level entrance
> triggers for the destination fieldmap (boundary towns have several); PickReachableRingPoint
> samples each trigger's CENTER + player-closest point + 16 perimeter points and picks in tiers:
> (1) walkable AND same region as player (via new WorldmapPathfinder.GetRegionId), nearest wins;
> (2) walkable only (old behavior, pathfinder rejects honestly if wrong); (3) location centre.
> Logs which tier fired. NOTE: "Cannot reach Arlia from Krosse" remains CORRECT — Arlia's
> entrances are all valley-side; the real route passes THROUGH Salva town (multi-leg routing
> through pass-through towns = possible future feature; the no-land-route message already hints it).
> TEST: from Krosse plains auto-walk to Salva → expect log "connected ring point at (~-163,-307)"
> (the NORTH entrance), a real route, full walk, arrival on Salva's enter prompt. Arlia should
> still refuse with the no-land-route message. Re-run P1–P6 below as convenient.
> LATENT BUG NOTED (fix at next F9 regen, not now): WorldmapGridGenerator flood-fill loop
> iterates d<4 (cardinal only) though its comment + arrays are 8-dir — diagonal-only threads
> can be wrongly sealed. Harmless for this bug; fold into the bunny-phase regeneration.

> ⏳ **PENDING USER TEST (2026-07-05 session 2): WORLD MAP PERFORMANCE + HONESTY PASS.**
> User confirmed a 2–3s freeze on EVERY world-map auto-walk start. Full review of the world map
> nav method done this session (user-requested). Implemented (build 0/0, DLL deployed):
> 1. **WorldmapPathfinder.cs internals rewritten (public API unchanged):**
>    - Persistent generation-stamped A* buffers (gCost/state/parentDir, ~260MB allocated once,
>      shared by both maps) replace ~300MB of fresh arrays + a 37M-cell init loop PER CALL —
>      this was the main freeze cause (auto-walk start runs FindPath 2–4+ times).
>    - Grid mutated in place with an undo journal (finally-restored) instead of a 75MB clone/call.
>    - **Connected-region map** built ONCE at grid load (BFS, same neighbor rule as the
>      authoritative 0.50m-floor A* pass, ~1–2s one-time, logged "NAV WM regions: N ... in Xms").
>      FindPath fast-rejects when start and target are in different regions → unreachable answers
>      in microseconds instead of a full-landmass double sweep. Fail-open (region 0 = unknown);
>      stamps only remove connectivity, so the reject can NEVER hide a reachable target.
>    - Tier-1 (0.60m comfort) pass expansion-capped at 1.5M cells — it's a preference, not the
>      authority; tier-2 floor pass stays uncapped. Fixes the long sweep at cave mouths where
>      "no route at 0.60m" previously searched the whole continent before falling back.
>    - Waypoint Y now read from baked grid heights (was: one CalcHeight raycast per waypoint —
>      hundreds per path for a Y the stick follower never uses).
>    - All path logs now include Stopwatch ms + cells searched (verify-with-data).
> 2. **Straight-line fallback GATED** (Worldmap.Pathfinding.cs): no grid path + target farther
>    than 15m → WorldmapCalculateAndStorePath returns false (honest "unreachable") instead of
>    marching the player into walls (the old grind). ≤15m keeps the fallback (grid-snap edge
>    cases). Battle-resume call site (NavigationHandler.cs) now handles false: announces
>    unreachable, abandons resume, does NOT start walking.
> 3. **New message** `nav_autowalk_no_land_route` (Loc.cs): when the region map proves
>    disconnection, says "No walkable route to X from here. It may lie beyond mountains or
>    water, or require passing through another location." (AutoWalk.cs picks it via
>    WorldmapPathfinder.LastNoPathWasDisconnected.)
> 4. **Grid now SHIPS WITH THE MOD:** grids\worldmap_expel.grid.gz (9.3MB gzip of the 80MB grid)
>    embedded in the DLL (csproj EmbeddedResource, DLL now ~10MB); auto-extracted to
>    UserData\SO2RAccess on first load if missing. A locally F9-regenerated grid is NEVER
>    overwritten. Nede: generate with F9 when the story gets there, gzip into grids\, rebuild.
> RAM note: ~410MB added while on the world map (grid 75 + regions 75 + buffers 260). If the
> user reports memory pressure, quantize later — do not pre-optimize.
>
> TESTS NEXT SESSION (F12 debug ON):
> [ ] P1. SPEED — auto-walk from open plains to a normal town (e.g. Krosse City). Player should
>        start moving almost immediately (well under 1s, after the one-time region build).
>        FIRST walk of the session logs "NAV WM regions: N connected regions labeled in Xms"
>        (one-time ~1–2s is OK). Log should show "found path with N waypoints in Xms" — expect
>        low tens of ms, not seconds. Compare against the old constant 2–3s freeze.
> [ ] P2. INSTANT HONEST UNREACHABLE — auto-walk to Arlia from the Krosse region. Expect the NEW
>        message ("No walkable route to Arlia...") near-instantly, NO walking, no 20s flail.
>        Log: "different connected regions ... Rejected in Xms".
> [ ] P3. CAVE-MOUTH REGRESSION — Krosse Cave mouth → Krosse City still completes (tight-terrain
>        follow unchanged). Log may show "preferred pass hit expansion cap" — that is the new
>        fast give-up of the comfort pass, not an error.
> [ ] P4. CHEST + ENEMY REGRESSION — world-map chest and enemy walks still arrive.
> [ ] P5. BATTLE RESUME — get interrupted by a battle mid-walk; resume should still work.
> [ ] P6. SHIPPING (optional) — rename UserData\SO2RAccess\worldmap_expel.grid to .bak, restart,
>        auto-walk on the world map: log "[GridGen] Extracted embedded expel grid"; walk works.
>        (Restore/delete the .bak afterwards — the extracted file replaces it.)
> KNOWN NOT FIXED (expected): Mountain Palace may still flail — the grid genuinely believes the
> Lasgus mountain terrain is connected (false-reachable, the safe direction). Real fix lands with
> the bunny/per-mode grid phase (see below).
>
> 🗺️ **AGREED DIRECTION (2026-07-05, user's plan, NOT yet implemented): bunny-aware dynamic
> reachability.** User wants the nav list to reflect TRUE current reachability: on foot vs riding
> the giant bunny (which crosses most land obstacles). Game provides per-mode truth:
> GameRenderManager.LayerMaskWall / LayerMaskBunnyWall / LayerMaskPsynardWall (static masks) and
> FieldPlayer.GetLayerMaskWall() is virtual (returns current form's mask). Plan sketch:
> (1) investigate ride-state detection + what layers each mask holds; (2) extend grid format with
> per-mode blocked bits (foot/bunny) using the game's own masks — also expected to fix the Lasgus
> false-connection for foot mode; (3) per-mode region maps → list items annotated, then filtered,
> by CURRENT mode; (4) VALIDATION FIRST: rollout is annotate-("unreachable" suffix)-and-log
> before any hiding, per the never-exclude-reachable rule. Chest ocean line-check
> (WorldmapIsReachableViaCalcHeight) to be replaced by region check in the same phase — the
> line-check can hide reachable chests (false negative) and is now the weakest link.

> ✅ **CONFIRMED WORKING (2026-07-05): World-map tight-terrain slow-follow fix (the 2026-06-30b
> plan below).** Tested against the exact repro. Log 26-7-05 10:32–10:34:
>  - TEST 1 (PRIMARY) **PASS** — Krosse Cave → Krosse City: `tight-terrain ENTER` fired at the
>    cave mouth, player threaded out slowly at `tight=True speed=0.50`, `LEAVE` + `speed=1.00`
>    once clear, and it COMPLETED: "Arrived at Krosse City. Press Cross to Enter." No
>    `stuck after 5 recalcs`. The old wedge-and-give-up is gone.
>  - TEST 2 (OPEN-TERRAIN REGRESSION) **PASS** — long open stretches all logged
>    `tight=False speed=1.00` (full speed, straight line); `tight=True` appeared ONLY within
>    2.5m of real rocks/walls, then cleared. No false slowdowns in the open.
>  - The tight-terrain change is accepted and kept. (Chest/enemy world-map regression, TEST 3,
>    not exercised in this log — low risk, re-check opportunistically.)
>
> 🔵 **DEFERRED to next session (user has a more robust solution in mind): geography-gated
> world-map locations are still OFFERED as auto-walk targets and flail before giving up.**
> Same log: auto-walking to **Mountain Palace** traveled ~180m then physically wedged at
> ~(-272,54,-88) — which is the **Lasgus Mountains entrance**. Mountain Palace lies BEYOND the
> mountains (enter Lasgus Mountains, cross, exit the far side), so it is geography-gated like
> Arlia and "Cannot reach" is the CORRECT answer. But it cost ~20s of `tight=True` wedge + 5
> recalcs first (the F9 grid bakes terrain-walkability only — it has no notion of story gates or
> "this region is a dungeon you walk through," so A* happily routes over the continuous
> mountain terrain toward the fixed Mountain-Palace marker). Not caused by the tight-terrain
> fix — pre-existing. Options discussed (user deferred, has own plan): (1) scenario/unlock-gate
> the nav LIST so gated locations aren't offered [need to confirm an unlock flag exists for
> map-jump destinations]; (2) fail-fast on a no-net-approach `tight=True` wedge instead of 5
> recalcs. USER WILL PROPOSE A MORE ROBUST APPROACH NEXT SESSION — do not implement yet.
>
> ============================================================================
> ### ✅ (DONE 2026-07-05) NEXT SESSION TEST PLAN (prepared 2026-06-30) — results above
> ============================================================================
> Build is 0/0 and the DLL is already in the Mods folder. **Enable F12 debug mode first**
> (so the diagnostic log is written). These changes are ALWAYS ON — there is no F4 toggle to
> flip. The fix targets ONE thing: auto-walk getting physically wedged in tight rocky areas
> on the WORLD MAP (the Krosse Cave mouth), even though the route exists.
>
> WHAT WAS DONE (one-line): world-map auto-walk now SLOWS DOWN to half speed and stops
> corner-cutting when it is near obstacle walls, so the player threads out of tight rock gaps
> instead of overshooting and clipping. Full detail in the 30b entry just below this plan.
>
> ----------------------------------------------------------------------------
> TEST 1 — PRIMARY: walk back OUT of Krosse Cave to Krosse City (the exact repro)
> ----------------------------------------------------------------------------
> STEPS:
>  1. Be on the EXPEL world map, walk to Krosse Cave and ENTER it (press X at the prompt).
>  2. Inside, auto-walk to the exit ("Town gate to Overworld") and leave — you are now back on
>     the world map standing at the rocky cave mouth.
>  3. Open nav (hold L1 / NumPad 5), category Locations, select Krosse City, auto-walk
>     (LStick up / NumPad 1).
> EXPECT (success):
>  - Screen reader: "Walking to Krosse City."
>  - Log near the start: `NAV WM tight-terrain ENTER (walls within 2.5m=N) — slowing + no skip-ahead`.
>  - Log shows `tight=True speed=0.50` while among the rocks; the player keeps MOVING (slowly)
>    and works north out of the cave mouth.
>  - Once clear of the rocks: `NAV WM tight-terrain LEAVE ... full speed` and `tight=False speed=1.00`.
>  - The walk COMPLETES: "Arrived at Krosse City" with the "Press Cross to Enter" prompt.
> MUST NOT GET (failure):
>  - `NAV worldmap: stuck after 5 recalcs. Cancelling.` followed by "Cannot reach Krosse City."
>  - The player pacing / frozen at the cave mouth for ~30s then giving up (the old behavior).
>
> ----------------------------------------------------------------------------
> TEST 2 — OPEN-TERRAIN REGRESSION: a normal long walk must NOT be slowed
> ----------------------------------------------------------------------------
> STEPS: From open overworld (e.g. the Krosse plains, away from rocks), auto-walk to any
>  distant location (e.g. Marze) across open ground.
> EXPECT: `tight=False speed=1.00` for the whole open stretch; the player runs at FULL speed in
>  a straight line as before; `tight ENTER` only ever appears when actually passing close to
>  rocks/walls, then `LEAVE` again.
> MUST NOT GET:
>  - `tight=True` / `speed=0.50` while out in the OPEN with no walls nearby (a false positive —
>    would mean the player crawls everywhere). If you hear/see the walk feel sluggish on open
>    ground, that is this failure.
>
> ----------------------------------------------------------------------------
> TEST 3 — REGRESSION: world-map chest + enemy still reached
> ----------------------------------------------------------------------------
> STEPS: On the world map, auto-walk to a Chest and to an Enemy as normal.
> EXPECT: both still arrive and announce arrival exactly as before.
> MUST NOT GET: a new failure to reach a chest/enemy that used to work.
>
> ----------------------------------------------------------------------------
> CONTROL (not a bug) — Arlia is SUPPOSED to be unreachable
> ----------------------------------------------------------------------------
> Auto-walking to Arlia will still say "Cannot reach Arlia." This is EXPECTED — Arlia is
> geography-gated (must pass through Salva first). Do NOT treat this as a regression. It is
> only a problem if a location that has a CLEAR overland route (like Krosse City) says it.
>
> ----------------------------------------------------------------------------
> IF IT FAILS — what to capture for me
> ----------------------------------------------------------------------------
>  - Send Latest.log. The key lines are the `NAV WM:` ones around the cave mouth — I need to see
>    `tight=` and `speed=` values, any `tight-terrain ENTER/LEAVE`, the skip-ahead lines, and the
>    `stuck`/`recalc` lines.
>  - The CRITICAL question the log answers: when the player was wedged, did it say `tight=True`?
>     • `tight=True` but still wedged = the player is perfectly on the thread but the gap is
>       genuinely too narrow for the body → next step is the gentle wall-nudge we deliberately
>       held back (already planned, not yet built).
>     • `tight=False` while wedged among rocks = the wall probe is not detecting the rocks →
>       I widen the probe radius / fix the layer mask.
> ============================================================================

> ✅ **CONFIRMED WORKING 2026-07-05 (was: PENDING USER TEST 2026-06-30b): World-map auto-walk
> WEDGES in tight obstacle areas (Krosse Cave mouth) — precise + slow following fix.** See the
> CONFIRMED entry at the top of this Phase section for test results.**
> SYMPTOM (user): from Krosse Cave could not auto-walk back to Krosse City ("Cannot reach"),
> a place just visited. (Arlia "Cannot reach" is EXPECTED — geography-gated, must pass Salva
> first; NOT a bug, out of scope.)
> DIAGNOSIS CORRECTED (log 26-6-30 19:49–19:50): the grid pathfinder is NOT broken and the
> ring-endpoint fix (30b-prev below) WORKS — log shows `reachable ring point at (-94.0,-54.7)`
> and a CLEAR 509-waypoint route to Krosse City, re-found 5+ times. The failure is in
> MOVEMENT EXECUTION: exiting the cave drops the player into a tight rock field (Col_Obstacle
> L22/L23, 0.50m body-width gaps; only route is `no route at 0.60m → 0.50m floor`). World-map
> auto-walk steered straight at each waypoint at FULL speed with no slow-down, so it overshot
> the 0.5m gap-centered waypoints and clipped rocks → wedge → 5 recalcs (same path) → give up.
> FIX (build 0/0, deployed; user chose "precise + slow only", NO repulsion — repulsion failed
> on corners before, and here the grid path is already correct so we just follow it faithfully):
> all in NavigationHandler.Worldmap.cs —
>  - `UpdateTightTerrain(playerPos)`: throttled (6-frame) Physics.OverlapSphere on L22|L23
>    (`WmTightProbeMask`, radius 2.5m) → `_wmTightTerrain`. Logs ENTER/LEAVE transitions.
>    (Waypoint spacing is uniformly ~0.5m everywhere, so the old gap-detector can't tell tight
>    from open — a wall-proximity probe is the real signal.)
>  - While tight: (A) `ApplyWorldmapMovement` scales the injected stick to `WmTightSpeedScale`
>    (0.5) so the player tracks the gap-centered waypoints instead of overshooting; (B) skip-
>    ahead stuck-recovery clamps its max jump to `WmTightSkipAheadMaxDist` (1.0m) so it can't
>    hop metres ahead through a rock. Open terrain = unchanged (full speed, normal skip-ahead).
>  - Diagnostics: `NAV WM:` line now logs `tight=` and `speed=`.
> Reset `_wmTightTerrain`/counter on world-map auto-walk start (AutoWalk.cs).
> TEST (F12 ON): from inside Krosse Cave, EXIT to overworld and auto-walk to Krosse City — expect
> `NAV WM tight-terrain ENTER` at the cave mouth, the player to thread out slowly and COMPLETE
> the walk (no `stuck after 5 recalcs. Cancelling.`). Watch `tight=True speed=0.50` near rocks,
> `tight=False speed=1.00` in the open. Regression: open-terrain walks still full speed/straight;
> world-map chest/enemy still arrive. If it STILL wedges while `tight=True` (perfectly on-thread),
> report — next step is the demoted gentle wall-nudge. NOTE: Arlia is expected-unreachable.

> ✅ **CONFIRMED WORKING (2026-06-30): World-map enter-ring point buried in wall → reachable-ring
> snap.** Log 19:50 shows `reachable ring point at (-94.0,-54.7)` + a found 509-wp route — the
> `PickReachableRingPoint` fix below now returns a walkable entrance and the grid routes to it.
> (The remaining cave-mouth issue was a separate EXECUTION bug — see 30b above.)

> ⏳ **PENDING USER TEST (2026-06-30): World-map enter-ring point buried in wall → pathfinder
> "no path found" everywhere → straight-line grind / "Cannot reach" a place just visited.**
> SYMPTOM (user, log 26-6-30 19:11–19:15): left Krosse City and got wedged against the model
> (failsafes freed him); reached Krosse Cave; from the cave could NOT route back to Krosse City
> (or Arlia) — "Cannot reach", despite having just walked from there.
> DIAGNOSIS (data, not guess): EVERY fresh CAT_LOCATION walk logged `enter-trigger: routing to
> ring point` immediately followed by `no path found` at BOTH 0.60m and the 0.50m floor. The grid
> is FINE — stuck-recalc and post-battle RESUME (which aim at the location CENTRE) found 422–468-wp
> routes over the SAME ground seconds later. The asymmetry = the bug: fresh walks aim at the
> entrance RING (`ComputeEnterTriggerTarget` returned `ring.ClosestPoint(player)`), which hugs the
> model wall and lands on a baked-OBSTACLE cell (Height==1). `SnapToTerrain` only escapes Height<2
> by distance (ignores which side of the wall), so A* gets an endpoint it can't stand on → no path
> → straight-line fallback (the grind), or "Cannot reach" when far. Centre cells are open → succeed.
> FIX (build 0/0, deployed; user-directed approach):
>  - `WorldmapPathfinder.IsWalkableWorld(Vector3)` — public; true iff the cell is Height>=2 (mirrors
>    the A* fallback floor: real road/terrain, not ocean/obstacle).
>  - `ComputeEnterTriggerTarget` now calls new `PickReachableRingPoint(ring, player, locationPos,
>    out usedCenter)`: tries plain ClosestPoint, then samples the ring from 16 directions
>    (project external ref pts onto the collider → covers every side/road angle), keeps the NEAREST
>    candidate that IsWalkableWorld accepts. If none walkable → returns the location CENTRE
>    (usedCenter=true) per user ("there must be a reachable ring square; if not, route to centre").
>  - Arrival UNCHANGED: still gated solely on `EnterPromptMatchesTarget()` (this location's prompt),
>    so the centre fallback still stops at the door, never enters/teleports.
> Files: WorldmapPathfinder.cs, NavigationHandler.Worldmap.Pathfinding.cs.
> TEST (F12 ON): (1) leave Krosse City, auto-walk straight back to it — expect a real grid route
> (log `reachable ring point at (x,z), Nm from player`), NO `no path found`, NO wall grind, arrives
> on the prompt. (2) From Krosse Cave, walk back to Krosse City AND to Arlia — both must now route
> (the exact repro that said "Cannot reach"). (3) Watch for `routing to the location centre instead`
> — should be RARE; if it fires for a normal town, the ring sampling missed and we tune Steps/reach.
> (4) Regression: chests/enemies on the world map still reachable. KNOWN RESIDUAL: if a walkable
> ring cell is found but it's a disconnected island, FindPath still fails (no centre retry at the
> caller yet) — report if seen and I'll add a caller-level centre retry.

> ⏳ **PENDING USER TEST (2026-06-28): World-map "Press X to enter" prompt + auto-walk arrival.**
>
> ===================================================================================
> TESTS TO RUN NEXT SESSION (all build OK, DLL in Mods folder; enable F12 debug first):
> ===================================================================================
> [ ] T1. PROMPT READ — Walk (manually or auto) up to a TOWN until "Press X to enter" pops
>         above the player. Screen reader should speak it once (e.g. "Press Cross to Enter.
>         Krosse City"). Should NOT repeat every frame. (Confirmed once on 2026-06-28; re-verify.)
> [ ] T2. PROMPT READ — DUNGEON. Same as T1 at a dungeon entrance. KEY UNKNOWN: confirm dungeons
>         actually raise the "Press X to enter" prompt at all (only towns seen in logs so far).
>         If a dungeon shows NO prompt, tell Claude — arrival logic depends on it.
> [ ] T3. F4 TOGGLE — Open mod menu (F4), find "Enter prompt speech", toggle OFF → prompt no
>         longer spoken; toggle ON → spoken again. Verify setting persists after game restart.
> [ ] T4. ARRIVAL TIMING — Auto-walk to a TOWN. It must announce "arrived" EXACTLY when the
>         enter prompt appears (player in the ring), NOT at a fixed distance. Must STOP and must
>         NOT enter/teleport into the location. Player presses X themselves to enter.
> [ ] T5. ARRIVAL TIMING — DUNGEON. Same as T4.
> [ ] T6. NO TELEPORT / NO FALSE ARRIVAL — Auto-walk to a FAR location whose route PASSES CLOSE
>         to another town (repro: walk to Mountain Palace passing near Krosse City). The passed
>         town's prompt must NOT end the walk or teleport; auto-walk continues to the real target.
> [ ] T7. ROUTES TO THE RING, NOT THROUGH THE MODEL — Auto-walk to a town/dungeon and confirm the
>         player approaches the ENTRANCE ring and does NOT grind into the side of the model.
>         In the log watch for: "NAV WM enter-trigger: routing to ring point (x,z)".
> [ ] T8. AWKWARD-ANGLE APPROACH (user's open question) — Auto-walk to a location so the route
>         approaches from an unusual side. Expectation: ring surrounds the model, so the player
>         still reaches the ring and the prompt fires. If instead it ends up "unreachable" or
>         stuck on the wrong side, report — Claude will add a "nearest REACHABLE ring cell" snap.
> [ ] T9. UNREACHABLE HONESTY — If a location genuinely cannot be reached, it should say
>         "unreachable" (not a false "arrived"). Sanity-check this still behaves.
> [ ] T10. REGRESSION — Auto-walk to a CHEST and an ENEMY on the world map still works (the
>         destination-hole removal in the grid pathfinder affects all targets, not just locations).
> NOTE: if routing looks off after these changes, the grid may need regenerating — press F9 on
>       the world map in debug mode to rebake worldmap_expel.grid / worldmap_nede.grid.
> ===================================================================================
>
> GOAL: read the world-map location-entry prompt (shown above the player near a town/dungeon)
> via screen reader, AND use it to fix auto-walk grinding into the city model forever without
> arriving. Jump prompt uses UIFieldOperationPresenter.Set (List<string>); the labelled
> "enter" guide is the SIBLING presenter UIFieldLabelOperationPresenter.Set(string label,
> string operation, ...) [CallerCount 2, hookable]. NOTE: cpp2il dump types its `.label`
> property as GameText, but the real Il2CppInterop assembly types `.label` as
> UIFieldSymbolNamePresenter (no `.text`) — `.operation` IS GameText. So the hide-poll reads
> only `.operation.text` + activeInHierarchy.
> IMPLEMENTED (build OK):
>  - FieldPromptHandler.cs: 2nd Harmony postfix on UIFieldLabelOperationPresenter.Set. Speaks
>    once per appearance (toggle ModSettings.EnterPromptSpeechEnabled, default on); raises
>    static FieldPromptHandler.EnterPromptShowing + EnterPromptLabel; hide-poll in Update().
>    DEBUG (F12) logs every label prompt: "[GAME] FieldPrompt LABEL ... worldmap=? label=[..]
>    operation=[..]" — first walk to a city CONFIRMS exact text so speech wording can be refined.
>  - NavigationHandler.Worldmap.cs: WorldmapLocationArrivalRadius 10→18m; CAT_LOCATION arrival
>    now fires on EnterPromptShowing OR within 18m (was 10m, never reached → stuck-recalc loop).
>    Shared helper ArriveAtWorldmapLocation(reason) (DRY: main check + stuck fallback).
>  - Loc.cs enter_prompt / enter_prompt_no_button / enter_prompt_echo (pass-through, echoes the
>    game's already-localized text); mod_menu_label_enter_speech. ModMenuHandler F4 toggle.
> CONFIRMED (15:47 log): prompt reads correctly — "[SR] Press Cross to Enter. Krosse City";
> LABEL operation=[<sprite name=Cross>Enter] label=[Krosse City] anchor='cp_0001_01(Clone)'
> worldmap=True. So the enter prompt IS UIFieldLabelOperationPresenter. Good.
>
> TELEPORT BUG (15:48:48 log) — FIXED. Walking to Mountain Palace (targetDist=210m), the enter
> prompt that popped was for KROSSE CITY (player passed near it). EnterPromptShowing is GLOBAL,
> so it tripped arrival for the distant Mountain Palace target → old ArriveAtWorldmapLocation
> called TryEnterWorldmapLocation → nearest mapjump to the TARGET (MF_0013_02A) → ChangeFieldmap()
> = teleport 200m across the map into the dungeon.
> FIX (user: NEVER auto-enter, only announce + read prompt):
>  1. ArriveAtWorldmapLocation now ONLY StopAutoWalk + announce "arrived" — no ChangeFieldmap.
>  2. DELETED TryEnterWorldmapLocation entirely (+ _wmOriginalTarget field/assignment) so no
>     teleport path can be reintroduced.
>  3. New EnterPromptMatchesTarget() gates the prompt-arrival on EnterPromptLabel matching
>     _autoWalkLabel (case-insensitive containment, tolerates " (Dungeon)" suffix) — a passed
>     town's prompt no longer counts as arrival at a distant target. Used in both the main
>     arrival check and the stuck fallback. 18m distance arrival unchanged.
> Player now presses X themselves to enter; mod just reads the prompt + announces arrival.
>
> PREMATURE-ARRIVAL FIX (16:12 log) — the fixed 18m radius announced "arrived (within 18m)"
> at targetDist=18.6 BEFORE the enter prompt fired (line 299), i.e. before the player was
> actually in the enterable ring. Each location's enter-trigger ring has its OWN size, so a
> fixed distance is wrong. The ring = FieldMapjumpCollision (: EventCollision, a navigable Unity
> TRIGGER collider w/ OnTriggerEnter/Stay on the event layer) — DISTINCT from the model's
> non-trigger wall collider on a wall layer. The "Press X to enter" prompt IS the runtime signal
> that the player entered that ring. So arrival is now PROMPT-ONLY for CAT_LOCATION:
> EnterPromptMatchesTarget() is the sole trigger (main check + stuck fallback); the fixed-distance
> arrival and WorldmapLocationArrivalRadius const are DELETED. A genuine stuck short of the ring
> now falls through to recalc → "unreachable" (honest) instead of a false arrival. Player keeps
> walking toward centre; ring is outside the wall so they enter it (prompt fires) before ramming.
> RE-TEST: auto-walk to (a) a town and (b) a DUNGEON; confirm it announces "arrived" exactly when
> the "Press X to enter" prompt appears (not at a fixed 18m), stops without entering, and that
> dungeons DO raise the prompt (user says each location has its own notification ring — verify).
>
> WALL-ROUTING + TARGET-THE-RING REDESIGN (user: "keep impassable terrain impassable; snap the
> pathfinder to the event trigger ring, don't guess holes in the model"). Findings: the worldmap
> pathfinder reads a PRE-BAKED grid (F9-generated). The grid generator DOES detect physical walls
> — L22 (Wall) + L23 (CharacterWall) non-trigger colliders baked as obstacle; town interiors
> flood-fill SEALED; small FieldMapjumpCollision TRIGGER colliders punch entrance holes. BUT two
> things routed players through walls: (1) ComputeSafeApproachPoint aimed 20m OUTSIDE the ring (so
> the player never reached it), and (2) WorldmapPathfinder.FindPath cleared a 10m passable hole
> around the DESTINATION centre (leftover from deleted TryEnterWorldmapLocation/10m arrival),
> punching straight through the model wall. FIXES (build OK):
>  - WorldmapPathfinder.FindPath: END clearance hole REMOVED (start 3m clearance kept). Walls now
>    fully impassable; SnapToTerrain pulls endpoint to nearest passable cell.
>  - ComputeSafeApproachPoint DELETED → replaced by ComputeEnterTriggerTarget(locationPos,playerPos):
>    finds the target location's nearest ground-level (Y<20m) FieldMapjumpCollision TRIGGER collider
>    (the enter ring) and returns ring.ClosestPoint(player) = nearest navigable point ON the ring.
>  - AutoWalk.cs: for worldmap CAT_LOCATION, walkTarget = ComputeEnterTriggerTarget(...) used for
>    BOTH the path AND _autoWalkTarget (so initial path, per-frame recalc, resume, and straight-line
>    fallback all aim at the ring, never the centre). ComputeSafeExitPoint (leave-town-on-start) kept.
> RE-TEST: auto-walk to town + dungeon; confirm the player routes to the entrance ring (no grinding
> into the model side), prompt fires, "arrived" announced, no entry. Watch log "NAV WM enter-trigger:
> routing to ring point (...)". If a location's centre is sealed and the ring point snaps to the wrong
> side, may need a reachable-cell snap; report if seen.

> ⏳ **PENDING USER TEST (2026-06-27): Auto-walk carve-oscillation (livelock) fix.**
> BUG: walking to a target walled in by NPCs (repro: Crosse Castle throne room MF_0007_01A,
> a side-event ~2m behind a row of soldiers you must talk to) made the player PACE BACK AND
> FORTH FOREVER. Root cause (log Latest.log/26-6-27_13-56-42): the near-only NPC carvers
> (CarveBand=7) toggle as the player moves → NavMesh flip-flops between the short direct route
> and a ~32-wp loop around the room (far soldiers ringing the event are un-carved, so the
> planner thinks that floor is open). Existing stuck give-up never fired because it resets on
> RAW movement and the player IS moving (back and forth).
> FIX (no length heuristics — user rejected those twice; near-only carving KEPT): detect the
> flip-flop by counting first-leg HEADING REVERSALS of successive stored paths
> (TrackPathStability, dot < PathReversalDot=-0.25) combined with NO improvement to best-ever
> XZ approach to the target (LivelockApproachEps=1.5). A baked-wall detour keeps one stable
> heading + keeps netting closer, so it's structurally immune (no false positive on legit long
> routes). On confirm (>=3 reversals, HandleCarveLivelock): SUPPRESS carving for the rest of the
> walk (_carverPool.Suppress + _carveSuppressedForBlock), recompute un-carved (= pre-carving
> behavior), then give up on hard-wedge or a 3s timeout. Give-up message is CAUSAL via the game's
> own truth FieldNpcCharacter.isPlayerObstacle / obstacleEventFunction (IsBlockedByPersonAhead,
> forward cone 1.8m): new Loc nav_autowalk_blocked_people "{0} is blocked by people. Auto-walk
> stopped." (neutral wording per user — no "talk to them", no event claim). All existing stuck
> give-ups now route through AnnounceBlockedGiveUp so they're people-aware too. Carve recalc sites
> frozen while committed; livelock state reset on start/resume/cancel/detour (ResetLivelockState).
> Files: NavigationHandler.cs, NavigationHandler.AutoWalk.cs, Loc.cs. Build 0/0, deployed.
> **TEST RESULT (CONFIRMED WORKING, log 26-6-27 18:56 / 19:02 / 19:05):** oscillation is FIXED —
> `NAV livelock: first-leg reversed, count=1..3` → `NAV livelock CONFIRMED: reversals=3,
> bestApproach=2.9m (no improvement). Suppressing carvers, committing to the direct route` → the
> pacing stops and auto-walk gives up cleanly (~3s timeout) on the side-event AND the King. User
> confirms it works as intended. Committed.
> **PEOPLE-AWARE MESSAGE — CLOSED (generic message kept, by user choice).** The straight-line
> commit fix WORKED (log 26-6-27 19:55): the player now presses straight into the soldiers (give-up
> at player (0.1,-56.7), SOLDIER1b 1.0m ahead, in cone). BUT `blockedByPeople` is still False — a
> diagnostic build confirmed these throne-room soldiers expose NEITHER `IsPlayerObstacle`/
> `isPlayerObstacle` NOR an `obstacleEventFunction`; they block via ordinary character collision, so
> the people-aware branch never fires. User decided the generic "Path blocked to {0}. Auto-walk
> stopped." is fine and NOT worth chasing the "blocked by people" wording. Diagnostics reverted; the
> IsBlockedByPersonAhead helper + nav_autowalk_blocked_people Loc key are KEPT (harmless, and they
> WILL fire for any NPC that does expose isPlayerObstacle/obstacleEventFunction elsewhere). The
> committed oscillation fix (4bfe481) + straight-line/property follow-up (14b414a) is the final state.
> REGRESSION CHECKS still to do: a legit long detour (around a wall / upstairs) must still complete
> (reversals stay <3, no CONFIRMED line); a normal threadable crowd (arena) must still carve around.
> Tunables: LivelockMinReversals, LivelockApproachEps, PathReversalDot, BlockCommitTimeout, BlockProbeRange.

> ⏳ **PENDING USER TEST (2026-06-27): Event NPCs in Events category + toggle.**
> Event-carrying NPCs (active "!", detected via NpcEnableScenario/NpcEnableSub) can now also
> appear in the Events nav category, so they're findable in crowded maps (castle/arena). New
> setting `ModSettings.EventNpcDisplay` (EventNpcDisplayMode NpcList/EventsList/Both, default
> Both) + F4 menu item "Event NPCs in nav list" (cycles NPC list / Events list / Both). NavItem
> is a struct so the Events copy is independent. Files: ModSettings.cs, NavigationHandler.Build.Npcs.cs,
> ModMenuHandler.cs, Loc.cs. Build 0/0, deployed. (Celine `cp_` resolved: split party = FieldPlayer
> control=False, surfaced via their trigger NPC's event flag — confirmed in log 26-6-27 11:20.)
>
> ⏳ **PENDING USER TEST (2026-06-27): NavMesh-carving Phase B — LIVE auto-walk.** POC (F7)
> PASSED (log 12:27): AddComponent<NavMeshObstacle> works, carved path stayed PathComplete,
> changed=True (rerouted, no sealing). Phase B now wires carving into field auto-walk:
> `NavigationHandler` parks the carver pool on the nearest ≤12 NPCs within 7m of the player
> (`UpdateFieldCarvers`/`GatherCarveTargets`, throttled 0.25s, excludes player/followers/enemies/goal),
> recomputes the path 0.4s after walk-start (`_carveForceRecalcAt`) so it routes around the crowd,
> and the existing stuck-recalc is now ALSO carve-aware (no longer NPC-blind). Disconnection safety
> net: `CalculateAndStorePath` retries on the un-carved mesh if carving yields no path (wrapper around
> new `CalculateAndStorePathCore`). Cleanup: `CancelAutoWalk` → `DeactivateAll` (covers scene change
> via Main.OnSceneWasLoaded). Carvers are mod-owned DontDestroyOnLoad GameObjects (radius 0.35m,
> carveOnlyStationary=false). New F4 toggle "NPC-aware pathfinding" (ModSettings.NpcAwarePathfindingEnabled,
> default ON). SpatialSensor/detour UNTOUCHED (still the fallback). Build 0/0, deployed.
> Files: NavMeshCarverPool.cs, NavigationHandler.cs, NavigationHandler.AutoWalk.cs, ModSettings.cs,
> ModMenuHandler.cs, Loc.cs.
> WATCH IN TEST (F12 ON): auto-walk to a party member across the arena — expect the route to bend
> around the stands with far fewer/no 8s wedges; log shows `NAV carve: re-routed around N NPCs`. Toggle
> F4 "NPC-aware pathfinding" OFF to A/B vs old behavior. Re-check a town + a dungeon for no "cannot reach"
> regressions (watch for `recomputed on the un-carved mesh` = disconnection fallback firing). F7 POC
> hotkey kept for radius tuning.
>
> **TEST 1 RESULT (log 12:40) — "much better, not perfect" + v2 FIX (2026-06-27):** Attempt 2 (same-floor)
> fired `NAV carve: re-routed around 5 NPCs` → ARRIVED in ~4.6s (smooth, the win). Attempt 1 (different-floor,
> longer) still hard-wedged on GRANDFATHER1 and cancelled — because the carve reroute never ran: the forced
> recalc fires ONCE at +0.4s (player still far from the crowd → ActiveCount 0 → skipped), and the hard-wedge
> fast-track SKIPPED the recalc (that skip was from when recalc was NPC-blind). **v2 (build 0/0, deployed):**
> (1) PERIODIC carve-aware recalc every 1.0s while carving (`_carvePeriodicTimer`/CarvePeriodicInterval) so
> the path re-bends around the crowd the player is approaching BEFORE wedging — fixes the root cause (the
> moving-NPC recalc never fires for a stationary target). (2) Hard-wedge now tries a carve-aware recalc FIRST
> (up to MaxCarveWedgeRecalcs=3) and only falls to the physical detour if carving is off/exhausted; logs
> `NAV carve: hard wedge — re-routed …`. Counters reset in AutoWalkTo/CancelAutoWalk. NEXT TEST: re-run the
> different-floor arena walk — expect periodic re-routes and far fewer detours; A/B with F4.
>
> --- (POC, now passed) NavMesh-carving Phase A (F7). Plan approved
> (`happy-knitting-flamingo.md`): make the FIELD pathfinder NPC-aware by carving small
> NavMeshObstacle holes on standing NPCs so NavMesh.CalculatePath routes around them (game
> already carves via FieldGimmick16). New `NavMeshCarverPool.cs` = pool of mod-owned invisible
> GameObjects each with a carving NavMeshObstacle (capsule, radius 0.35m, height 2m). POC hotkey
> **F7** (DebugHotkeys.cs, debug-only, FIELD maps): computes an un-carved path to the farthest
> NPC, parks carvers on the nearest ~10 NPCs (carveOnlyStationary=false for immediate carve),
> waits 0.4s, recomputes the carved path, logs `[F7]` status/corners/length for both + a "changed"
> flag + a WARNING if carving turned a Complete path into Partial (crowd sealed). Carvers destroyed
> after each test. Build 0/0, deployed.
> **DECISION GATE before Phase B:** F12 debug ON, stand in the arena near the crowd, press F7, send
> the `[F7]` log lines. Proceed only if carving (a) CHANGES the path (routes around NPCs) and (b)
> keeps it PathComplete (no sealing). If it seals or doesn't carve → fall back to gap-seeking steering.
> NOTE: AddComponent<NavMeshObstacle> in IL2CPP is the key unproven bit — F7 confirms it works.
>
> 🔵 **NEXT (planning): crowd navigation.** Auto-walk struggles in dense NPC crowds (arena/castle) —
> 8s wedges on stationary spectators, ~3 min to cross the colosseum. NOT walls (every blocker logged
> is an NPC). User wants the spatial sensor to thread toward open gaps while STAYING on the plotted
> path — no exaggerated veers, don't rewrite the model. Idea: inject NPC positions into pathfinder/steer.
> User's "X" presses in the log were MANUAL stuck-testing, not ambient dialogue (so the dialogue-cancel
> theory is DROPPED).

> ⏳ **PENDING USER TEST (ask first thing next session):**
> **Quick Recovery stale announcement during cutscenes (2026-06-24).** Verify the stray
> "Quick Recovery. Recover party? Yes. Press NumPad 0 or L3 for party status." no longer
> intrudes during conversations/cutscenes, AND that a real D-pad-Right quick heal still
> opens, reads Yes/No + party status (L3) + result. Fix = QuickRecoveryHandler now gates
> detection on `FieldState.IsFieldFree()` (cutscenes set EventManager.IsRunning, which the
> menu's own isPause=False did NOT reflect) plus a `UIConversationWindow.IsShowingConversation`
> belt-and-suspenders check. Build 0/0, deployed. Details below.
>
> ⏳ **PENDING USER TEST + 1 REPRO (2026-06-27):**
> **Story-trigger NPCs in nav — TWO DISTINCT bugs found (log 26-6-27).** The "event NPC
> doesn't show" turned out to be two different problems:
>   1. **`ev_` event actors = present but UNLABELLED.** The arena gatekeeper that triggers
>      the tournament scene is `ev_1902500_r_Soldier` (codeName prefix `ev_`, contactDistance
>      1.0 → kept as a counter NPC). It WAS in the nav list, just as a generic "Soldier" among
>      ~15 identical soldiers → unidentifiable to a blind user (no visual "!"). **FIX v1
>      CONFIRMED WORKING (log 26-6-27 10:15):** codeName-prefix `ev_` → "(event)" tag, e.g.
>      "Soldier (event)", kept in NPCs category. Loc `nav_npc_event_tag`. **v3 — DYNAMIC SIGNAL,
>      PENDING USER TEST (2026-06-27):** the static prefix tag went STALE (persisted after the event
>      fired). NAV:NPCEVT log (10:56–10:57) PROVED the fix: gatekeeper soldier read `scenario=True`
>      BEFORE triggering, `scenario=False` AFTER — while its `ev_` name and `HasEvent()` stayed true
>      the whole time (HasEvent is static junk, true for ~every NPC). Also a `Spectator` +
>      `EV_Colosseum_Woman` read `scenario=True` at the next story beat — ordinary names the prefix
>      would MISS. SWITCHED the tag to `GetEnableScenarioEvent()!=null || GetEnableSubEvent()!=null`
>      (dynamic, IL2CPP-guarded helpers NpcEnableScenario/NpcEnableSub); DROPPED the `ev_` prefix and
>      `HasEvent()` entirely. Tag now clears after the event AND catches non-`ev_` triggers. Lean
>      `NAV:NPCEVT tagged …` diag kept. PA NPCs still handled separately (PA excluded from this tag).
>      Files: NavigationHandler.Build.Npcs.cs, Loc.cs. Build 0/0, deployed.
>      WATCH IN TEST: a CURRENT story-trigger NPC reads "X (event)"; after you trigger it (or once
>      its "!" is gone) it reverts to plain "X"; no false "(event)" on background NPCs.
>   2b. **Celine `cp_0003_01` = NOT a FieldNpcCharacter (narrowed 2026-06-27).** Repro CONFIRMED
>      she's present at scan time (prompt 10:59:22, scan 10:59:29, convo 10:59:34) yet appears in
>      NEITHER the NPC list NOR any NAV:NPCSKIP line. Since every Field*Character inherits
>      FieldNpcCharacter, FindObjectsOfType<FieldNpcCharacter> would catch her unless she's hit by
>      a SILENT skip (FieldEnemy or FieldPlayer — the only two without logging). HYPOTHESIS:
>      split-off party members spawn as FieldPlayer instances. Added NAV:NPCSKIP logging to the
>      FieldEnemy and FieldPlayer branches (+ control-player flag via GetControlPlayer instance-id).
>      NEXT REPRO: stand at Celine's Talk prompt, open nav; expect e.g.
>      `NAV:NPCSKIP 'cp_0003_01(Clone)' — FieldPlayer (control=False)`. Then fix = include
>      non-control FieldPlayer characters that have an active event (NpcEnableScenario) as nav
>      targets, tagged "(event)". Build 0/0, deployed. Files: NavigationHandler.Build.Npcs.cs.
>
>   2. **`cp_` party characters = EXCLUDED entirely (DIFFERENT bug).** The Celine trigger
>      (anchor `cp_0003_01(Clone)`, near log end ~09:45:24) never appeared in the NPC list at
>      all — the full scrolled readout had no party members. These are party members placed in
>      the world after they split off ("Celine left / Precis left" at 09:43:16). They're being
>      dropped by one of BuildNpcs's silent skips (`FieldFollowCharacter` OR `NpcType.INVALID`).
>      **DIAGNOSTIC ADDED:** both skips now log `NAV:NPCSKIP '<gameobject name>' — <reason>`
>      (SafeNpcName). REPRO NEEDED (F12 ON): at the Celine-conversation story point, walk near
>      her and open nav; the log will show e.g. `NAV:NPCSKIP 'cp_0003_01(Clone)' — NpcType.INVALID`.
>      Then the fix = include `cp_`/`ev_` event characters even when INVALID-typed, tagged
>      "(event)". Build 0/0, deployed. Files: NavigationHandler.Build.Npcs.cs, Loc.cs.
>
> ⏳ **(SUPERSEDED — see above) PENDING USER REPRO (2nd diagnostic build):**
> **Arena story-event marker missing from nav (2026-06-24, round 2).** EVENTDIAG repro
> (log 26-6-24 19:33) was decisive: arena room **MF_0017_51A has 0 FieldEventCollision**,
> the party members are NOT present as NPCs, and the only exit is the GATE back to the hall
> (MF_0017_01A). The neighbour map DID have 2 FieldEventCollision (`ev_block_lacuer_*`,
> sub isDisableIcon) — so the scanner WORKS, the arena just has no event collision. =>
> the red "!" is **NOT a FieldEventCollision**. It is a `MapIconType.SCENARIO_EVENT` minimap
> icon. `FieldMapjumpCollision` (exits) carries BOTH `iconType` AND `subIconType` — the arena
> exit's primary iconType=GATE, but the code never read `subIconType`, where a "!" overlay
> would live. NEW HYPOTHESIS: the story marker is a SCENARIO_EVENT/SUB_EVENT **subIconType on
> an exit** (the gate to the staging area), shown by nav as a plain "Town gate to …".
> Enriched NAV:EXIT diag now logs `iconType=… subIconType=…` for every exit on every map +
> NAV:EXITDIAG count. REPRO NEEDED (F12 ON): at the story point where the "!" is active, walk
> the arena AND the hall, OPEN nav on each, send the log — look for any exit/object with
> subIconType (or iconType) = SCENARIO_EVENT/SUB_EVENT/PA_EVENT. ALSO awaiting user answer:
> last time, did sighted help guide them to a DOOR/GATE, to a spot in the open room, or to
> specific characters? That disambiguates exit-vs-NPC-vs-position. Build 0/0, deployed.
> NOTE: BuildEvents NAV:EVENTDIAG diagnostics from round 1 are KEPT.
>
> ⏳ **PENDING USER TEST (ask first thing next session):**
> **World-map wide-route preference (2026-06-21).** Verify auto-walk to **Lacuer City**
> (and other world-map destinations) now takes the wider road instead of wedging in the
> body-width pinch at ~(789.8, -316.3). Fix = tiered A* in WorldmapPathfinder.cs: first
> pass requires 0.60m clearance, falls back to the old 0.50m floor only if no wider route
> exists (so nothing reachable today becomes unreachable). Details below.
>
> ⏳ **PENDING USER TEST (ask first thing next session):**
> **Pickpocket stale-announcement fix (2026-06-21).** Verify the stray "Pickpocket /
> Strength Bottle" no longer intrudes during cutscenes/dialogue/shops, AND that a real
> pickpocket (L1 on an NPC) still opens and reads items/rates. Fix = PickpocketHandler.cs
> now detects via `selectChoicePresenter.gameObject.activeInHierarchy`. Details below.

### Assault Action skill readout — FINAL (2026-07-11): screen parity, no descriptions

USER DECISION (option 2 of 2): the screen shows NO skill descriptions for anyone (user
confirmed visually via sighted info) — readout now matches the screen exactly for all
characters: "[skill name], [type], cooldown [N] seconds." Description resolution code
REMOVED from BuildAssistSkillSummary (incl. the UICommon fallback — doc comment warns it
returns stale cache for non-party assists; the name mismatch safety net is gone with it
since the displayed name is now always preferred, const-message name only as first-hover
fallback). KNOWN_ISSUES "Assault Actions" section reworded: descriptions aren't shown on
this screen for anyone; party skills readable in Battle Skills menu; assist-only guests
have no description text in the game data at all.
Build 0/0, deployed. ✅ USER CONFIRMED WORKING (2026-07-11) — feature CLOSED.

### Assault Action skill readout — v4 probe record (2026-07-11): no description exists for assist-only skills

v4 probe results (log 15:03) — DEFINITIVE:
- Celine / M_FIREVOLT: constMsg name=9 desc=37 effect=0 area=14 → full data, reads fine.
- Laeticia / LAETICIA_ASSIST_01: constMsg name=12 desc=0 effect=0 area=0, and
  'ASSIST_0019' → '' in ALL THREE TextManager tables (System/Skill/Item).
CONCLUSION: the game data has NO description text for assist-only characters' skills —
the name is the only string that exists. Not a mod bug; sighted players also see only
name/type/cooldown on this screen. Probe code removed (kept areaDescription as a desc
fallback — real skills like Fire Bolt do populate it); a debug line notes "no description
in game data" when it happens. KNOWN_ISSUES.md updated (new "Assault Actions" section).
Final behavior: party members read name/type/cooldown/description (live-tracked when
reassigned); assist-only characters read name/type/cooldown.
Remaining: user confirmation of final build, then feature CLOSED.

### Assault Action skill readout v4 (2026-07-11) — description last-resorts (probes done, see above)

v3 RETEST (log 14:57): Laeticia name now resolves CLEAN from the const message table
('Ultime Camui', no mismatch, no fallback) — but desc=none: the row's description AND
effectDescription are both empty, and the UICommon fallback returned nothing either.
The devs left assist-skill descriptions blank in the battle-skill message table (this
camp screen never displays one). Celine (M_FIREVOLT) keeps working perfectly.
v4 adds two cheap last-resorts + logging, one test decides:
(a) ConstBattleSkillMessage.areaDescription (third text field, previously unread) +
    a constMsg length log for all four fields;
(b) assistEffectMessageID ('ASSIST_0019') probed against ALL THREE TextManager tables
    (System was empty before; Skill/Item untested) — each probe logged.
If both come back empty, the game data simply has no reachable description for
assist-only skills; readout stays name/type/cooldown (matches what sighted players see
on this screen) and KNOWN_ISSUES gets a note.

### Assault Action skill readout v3 (2026-07-11) — const-message description (superseded by v4)

v2 RETEST (log 14:40–14:41): Celine PERFECT (skillID=M_ERUPTION fromConst=False, live
tracking works, desc reads). Laeticia: her const skillID IS valid (LAETICIA_ASSIST_01) but
CreateBattleSkillInformationData STILL returned the stale cache for it (resolved 'Fire
Bolt'/'Eruption' vs displayed 'Ultime Camui') — the builder can't handle non-party assists.
Safety net worked as designed: read "Ultime Camui", dropped the wrong description. So the
user heard her real skill name but still no description.
v3: NEW PRIMARY SOURCE `pm.GetConstBattleSkillMessage(skillID)` → ConstBattleSkillMessage
(.name/.description/.effectDescription — resolved strings, const table, no UI cache; keyed
directly by BattleSkillID so LAETICIA_ASSIST_01 should hit). UICommon builder demoted to
fallback for whichever piece is still missing. Displayed-name cross-check unchanged.
Expected: Laeticia reads "Ultime Camui, HP-focused, cooldown 60 seconds. [description]."

### Assault Action skill readout v2 (2026-07-11) — STALE-CACHE FIX (superseded by v3)

v1 BUG (user + log 14:31–14:32): both characters read "Fire Bolt"; after user reassigned
Celine to Eruption, ONE read said Eruption then everything snapped back to Fire Bolt.
Log signature: Laeticia's first-ever lookup returned name='' (and effect-message fallback
'ASSIST_0019' → '' — that key is native-only, fallback REMOVED); afterwards every lookup
returned whatever skill info the game built last. Cooldown (60 vs 72) and type stayed
correct per character throughout. DIAGNOSIS: ConstAssistParameter.AssistBattleSkillID is
INVALID for these assists; CreateBattleSkillInformationData(INVALID, …) returns stale
cached data instead of failing.
v2 FIX: skill ID now read from the character's LIVE
CharacterParameter.AssistBattleSkillID (pm.UserParameter.GetCharacterParameter(
assistParam.PlayerID)) — the player-assigned assault skill, so Celine=Eruption reads
correctly; const table only as fallback (fixed assist-only chars). The info builder is
never called with an INVALID ID. Safety net: resolved skill name is cross-checked against
the on-screen name (info panel assistName / equip data assistName); on mismatch the
displayed name wins and the description is dropped + logged (never read the wrong skill's
text). Debug line now logs skillID + fromConst.

### Assault Action skill readout v1 (2026-07-11) — BUILT + DEPLOYED, superseded by v2 above

User confirmed the same day: Formation, Party Formation, Assist Formation (both screens),
and Tactics (both screens) all work — Operations child screens fully closed. Remaining gap
(user report + log 13:20–13:21): the assault character picker read only "Laeticia, currently
set. 1 of 2." — no way to know what the assault skill does (Laeticia is assist-only, never
visible in the battle-skill menus like party members are).

New: `BuildAssistSkillSummary(AssistID, typeText, coolTime)` in `CampMenuHandler.Party.cs`:
- assistID (from `UICampAssistSettingCharacterListItemData` / `UICampAssistEquipListItemData`)
  → `ParameterManager.GetAssistParameter(AssistID)` → `ConstAssistParameter` with
  `AssistBattleSkillID` + `PlayerID` (PascalCase properties confirmed present).
- Skill name + description via `UICommon.CreateBattleSkillInformationData(skillID, playerID,
  false)` — same proven path as BattleResultHandler.
- Description fallback: `assistEffectMessageID` (lowercase field, no PascalCase twin; const
  data) via `TextManager.GetMessage(id, System)` — debug-logged so the test shows which
  source is right.
- Cooldown: `UICommon.CalcAssistCoolTime(skillID, playerID)` (game's own calc); the Equip
  rows pass their data's `coolTime` instead.
- Skill TYPE (attack/support): character picker reads the on-screen info panel
  `assistSelector.assistDescriptionPresenter.battleSkillType.text` (GameText, refreshed by
  the game's native UpdateAssistDescription on cursor move); Equip rows use data's
  `battleSkillType` string.
- Announcement: "Laeticia, currently set. [Skill], [type], cooldown [N] seconds.
  [Description]. 1 of 2." Slots: "[Button]: [Char]. [same summary]. X of Y."
  Summary parts joined with string.Join; missing parts skipped with reason logged.
- Loc: camp_assist_slot/slot_empty/char/char_current lost their embedded ". N of M."
  (now via TextUtil.AppendPosition); new key camp_assist_cooldown.
Build: 0 warnings / 0 errors, DLL deployed. Test checklist under "Pending Tests
(Assault Action Skill Readout)".

### World-map auto-walk wedged in body-width pinch (Lacuer City) — FIX, PENDING USER TEST (2026-06-21)

- **Symptom:** Auto-walk to Lacuer City failed identically every attempt — player got
  physically stuck at world ~(789.8, 51.4, -316.3), then "Cannot reach Lacuer City". User
  confirmed they could not squeeze through there even walking manually, and that an
  ALTERNATIVE wider route exists (reached Lacuer by approaching from another direction).
- **Diagnosis (from log 26-6-21_15-30-40, lines ~11625-11644 et al.):** the pinch is a gap
  between two CharaWalls (layer 23 Col_Obstacle) at 0.51m and 0.81m. Player capsule radius
  is 0.50m (CLAUDE bounds 1.0m wide), so a 0.50m gap = zero margin → wedge. Two flaws in
  WorldmapPathfinder.cs: (1) the clearance penalty is capped at MaxClearancePenalty=3.0 —
  far too weak to beat a long detour, so A* takes the deadly shortcut; (2) the grid's hard
  floor MinPassableClearance=0.50m equals the player radius, so body-width gaps are treated
  as passable at all. Stuck-recovery then blocks a 2m radius (nuking the only narrow
  corridor) and gives up.
- **Fix (no grid regen — clearance is already baked per-cell):** tiered A* in
  `WorldmapPathfinder.FindPath`. First pass requires every cell >= `PreferredMinClearance`
  (0.60m = radius + 0.10m margin) via a hard cutoff in `AStarSearch` (new `minClearance`
  param, folded into the existing GetClearance block). If that finds no route, it falls back
  to a second pass with `minClearance=0f` — IDENTICAL to the previous behavior (grid's 0.50m
  floor). So a wider route is preferred when one exists, but **nothing reachable today can
  become unreachable** (this was the failure mode of every prior hard-floor attempt). Fallback
  is logged: `no route at 0.60m clearance — falling back to the 0.50m floor`.
- **Both the initial path and the stuck-recalc go through FindPath**, so one change covers
  all callers (pre-validate/exit in NavigationHandler.Worldmap.Pathfinding.cs; recalc in
  NavigationHandler.Worldmap.cs).
- Build 0/0, DLL deployed. Files: WorldmapPathfinder.cs.
- **WATCH IN TEST (F12 debug ON):** walk to Lacuer City from the failing spot — expect the
  wider road, not the pinch. If 0.60m still threads somewhere too tight, the margin is a
  one-line constant nudge. If a known-reachable place ever reports unreachable, check the log
  for the fallback line. The clearance number may want tuning once tested on several routes.

### Fix: Enhance→Skill specialty filter showed wrong SP costs — TESTED & WORKING (2026-06-21)

- **Symptom:** Camp → Enhance → Skill, then Square (specialty list) → Triangle (narrow to a
  specialty's component skills, e.g. Oracle = ESP/Piety/Purity). In this filtered view the SP
  level-up cost was wildly wrong (Purity read 200+/no value instead of 8); some skills (ESP)
  read no SP requirement at all. Costs were correct in the unfiltered full skill list.
- **Root cause:** `SkillInfoPresenter_Set_Postfix` (CampMenuHandler.Formation.cs) reads name/level/
  desc from the fresh presenter `data`, but read SP cost / max-level from
  `_skillSelector.itemDataList[currentIndex]`. `currentIndex` is relative to the *visible* list.
  The specialty filter swaps the visible list to a separate `narrowDownItemDataList`, so indexing
  the full `itemDataList` pulled the cost of whatever skill happened to sit at that slot in the
  full list (Piety@slot2 → Biology's 235; ESP@slot1 → Determination maxed → 0). Confirmed exactly
  from the debug log.
- **Fix:** when `_skillSelector.narrowDownSpecialSkillID != SpecialSkillID.INVALID`, read per-row
  data from `narrowDownItemDataList` instead of `itemDataList`. Name/level/desc unaffected (same
  item type); only the cost/max lookup is corrected. No change when no filter is active.

### Item Creation special skills complete + pickpocket stale-fix (2026-06-21)

- **Replication & Remaking now fully read — CONFIRMED WORKING by user.** Both are
  "item-list-first" special skills: their first screen is an inventory item picker
  (`itemListSelector`), not the generic action selector, so `ResolveFocusedActionSelector`
  never saw them. Added a shared poller (`PollItemListSkills` / `PollItemPicker` /
  `TryPollItemSkillCreateMode` / `SeedItemPickersOnEntry` in
  CampMenuHandler.ItemCreation.ActionList.cs) covering both. Item picker reads name/qty/
  position; the copy/remake count adjuster reads via the shared `PollCreateMode` driven off
  the presenter's `createCountParent` (the selector's own count flag is stale).
  - **Key gotcha:** the picker item lists stay populated across camp close/reopen, so they're
    seeded on entry (`SeedItemPickersOnEntry`) and `PollItemPicker` returns FALSE on "no move"
    — a stale picker must NOT claim the frame, or it blocks the other item-list skill AND the
    generic action poller (this caused a Remaking regression + earlier Master Chef silence).
  - Remaking note: pressing confirm on equipment "does nothing" is a GAME mechanic, not a mod
    gap (item list reads fine). Not investigated further.
- **Master Chef / Blacksmith / Music (no-item super specialties) — CONFIRMED WORKING.** Their
  category lists (Seafood/Fruit…, Normal/Use a Tool, Compose/Perform…) are read by the generic
  action fallback once the stale-picker blocking was fixed. An interim `AnnounceNoItemCategory`
  hook band-aid was added then REMOVED (it double-announced); the action fallback is the proper
  handler (reads name + needs + position).
- **Pickpocket stale announcement — FIXED, PENDING USER TEST.** "Strength Bottle"/etc. was
  announced mid-cutscene/dialogue/shop because PickpocketHandler keyed off the selector's own
  `gameObject.activeInHierarchy` (stale-true when closed). Switched detection to
  `selectChoicePresenter.gameObject.activeInHierarchy` — the visible choice presenter, the same
  reliable signal DialogueChoiceHandler uses (PickpocketHandler.cs).

### Housekeeping + status confirmations (2026-06-20)

- **Dead world-map LIDAR removed.** Deleted the disabled `ApplyWorldmapMovement_Lidar` method
  and its 4 unused constants (LidarRayCount/Range/ActivationRange + WmLidarLayerMask) from
  NavigationHandler.Worldmap.cs (−174 lines). `WmObstacleLayerMask` kept (used by the live
  pathfinder). Build 0/0. NOTE: this was the OLD shelved lidar; the NEW soft walk-assist
  (SpatialSensor.cs) is unrelated and stays.
- **"Island system" dead code:** confirmed already removed in a prior refactor (user recall +
  grep — only legitimate "island" comments remain in DebugHotkeys F11 diag + Build.cs). No action.
- **Quick Heal menu (D-pad Right): CONFIRMED WORKING by user (2026-06-20).** v2 (name/amount/
  result + L3 key) done. No longer a pending item.
- **Guild menu: accepted as-is by user.** Mission LIST reads (name/status/position). The guild
  master's accept/report command menu + mission description/reward at the guild stay native-
  silent — "as good as it's going to get; maybe revisit one day, likely unfixable." KNOWN_ISSUES.md
  updated to reflect the partial-read reality (was stale, said "does not read").
- **Walk assist (SpatialSensor): COMPLETE & confirmed smoother by user** (see entry below).

### Soft spatial-awareness walk assist (LIDAR v2) — DONE, confirmed smoother (2026-06-20)

User wants a "soft lidar" so the auto-walking player stops getting stuck on NPCs in
towns/dungeons, WITHOUT veering off the route or failing to arrive. Foundation for a
future exploration mode + manual-walk assist.

PRIOR-ART CHECK: there IS an old 36-ray world-map LIDAR (`ApplyWorldmapMovement_Lidar`
in NavigationHandler.Worldmap.cs, lines ~480-635) but it is DEAD CODE — explicitly
"preserved but disabled"; all call sites use the plain `ApplyWorldmapMovement`. The
world map was solved with the CalcHeight A* + safe approach/exit points instead. Kept
as reference. Field/town auto-walk had NO sensing at all — NavMesh waypoints only, with
reactive post-stuck recalc + big 3/5/8m detours (the "too aggressive" behavior).

NEW (this session): `SpatialSensor.cs` — reusable soft-steering component. HYBRID detection
(user-chosen): position-based repulsion from cached FieldNpcCharacter transforms (NPCs +
enemy symbols; player/followers excluded) PLUS a short 3-ray forward wall fan on L22|L23.
Returns the desired heading nudged away from obstacles, HARD-CAPPED at ±35° (Vector3.
SignedAngle clamp), decaying to zero when clear — can't redirect or change destination.
Target transform passed as `exclude` so it never steers off the NPC/chest being approached.
Rescans dynamic bodies every 30 frames; self-prunes dead transforms.

Wiring: NavigationHandler.cs field walk loop (~line 1049) nudges `moveDir` via
`_spatialSensor.Steer(...)` before WorldDirToCameraStick, gated on ModSettings.WalkAssistEnabled
(default ON). New F4 toggle "Walk assist" (A/B test). Throttled debug line "NAV walk-assist:
steering around a nearby obstacle." Existing stuck-detection/detour stays as heavy fallback.
Files: SpatialSensor.cs (new), NavigationHandler.cs, ModSettings.cs, ModMenuHandler.cs, Loc.cs.
Build 0/0, deployed.

WATCH IN TEST (F12 debug ON): (1) auto-walk through a town with NPCs in the path — player
should slip around them instead of grinding to a stuck stop; expect the throttled walk-assist
log lines. (2) Confirm it still ARRIVES at every target (NPCs, chests, exits) and doesn't
veer in tight corridors/doorways. If corridor veering shows up, flip `UseWallRays=false` in
SpatialSensor.cs (one line) to fall back to NPC-only steering. (3) F4 → "Walk assist" toggles
it so you can compare on/off.

TEST 1 RESULT (2026-06-20, Latest.log 12:58–13:00, map MF_0009_01A): walk-assist IS engaging
but player still went fully stuck (moved 0.00) on ONE specific NPC — `cn_0031_01` (pickpocketable
town NPC; Talk|Pickpocket FieldPrompt confirms player wedged at contact range). Same NPC blocked
routes to Fishing spot AND Story event (x2). The OLD heavy fallback (stuck recalc → 3m detour)
freed the player each time after ~6–10s; targets STILL arrived. So soft cap (±35°) is too gentle
to squeeze past a hard blocker in that chokepoint — by design the detour should take over, and did.
UNKNOWN from that log: is she stationary-in-narrow-gap or wandering into the path? Old logging too
thin to tell (user is blind, can't observe).

DIAGNOSTIC BUILD (2026-06-20, build 0/0, deployed): SpatialSensor now emits a throttled (~3/sec)
`WALK-ASSIST:` line whenever a body is ahead — logs nearest obstacle name + world pos, player dist,
desired heading, nudge degrees, and `CAPPED` flag when the ±35° limit is the limiter. Reading the
obstacle pos across lines reveals if she MOVES; steady small dist = pressed-against = narrow gap.
Removed the old generic "steering around a nearby obstacle" line (replaced by the rich one).
TEST 2 RESULT (2026-06-20, Latest.log 13:08, map MF_0009_01A): DEFINITIVE. Blocker NPC
`NPC_0009_01a_107_WOMAN1` at (-13.0,20.0) — position ROCK-STEADY for the full 5s wedge → she is
STATIONARY. Player frozen at (-12.9,19.0), d=0.96m, nudge=-35° CAPPED every line. Wall on the
sidestep side (couldn't pass left). So: narrow chokepoint, soft ±35° too gentle to sidestep a
sub-metre blocker. Old recalc fired first (useless — NavMesh is NPC-blind, returns same route),
THEN the 3m detour freed the player after ~5s; user cancelled, fed up.

FIX — ADAPTIVE SIDESTEP (Option 1) — BUILT, build 0/0, deployed (2026-06-20):
- SpatialSensor cap is now ADAPTIVE. Gentle BaseCap=35° in the open. When wedged (obstacle ahead +
  no >0.30m progress), after WedgeGrace=0.5s the cap ramps 35°→MaxWedgeCap=70° over WedgeRamp=0.7s
  and speed drops to WedgeSpeedScale=0.6, so the sensor itself threads past the blocker smoothly.
  Progress (>0.30m) resets it to gentle. New `LastSpeedScale` (caller scales stick) + `IsHardWedged`.
- FAST HAND-OFF: if still no progress at HandoffDelay=1.6s, `IsHardWedged`=true. NavigationHandler
  then SKIPS the NPC-blind recalc and calls TryStartObstacleAvoidance directly (physical 3m detour),
  respecting MaxAvoidanceAttempts. So worst-case recovery ~1.6s vs old ~4s, and the jarring detour is
  only reached if the smooth sidestep genuinely fails.
- Sensor resets wedge/obstacle state on auto-walk start + StopAutoWalk.
- Diagnostic enriched: WALK-ASSIST line now also logs cap=, wedge=Ns, and HARD-WEDGE flag.
Files: SpatialSensor.cs (adaptive cap + speed + IsHardWedged), NavigationHandler.cs (speed scale +
fast-escalation block), NavigationHandler.AutoWalk.cs (Reset on start/stop).
WATCH IN TEST (F12): same cn_0031_01 chokepoint. Expect: brief CAPPED at 35°, then cap= climbs
toward 70° and the player slips past smoothly WITHOUT the 3m detour; if it can't, expect HARD-WEDGE
then "escalated straight to detour" within ~1.6s. Confirm no veering elsewhere / still arrives.

TEST 3 RESULT (2026-06-20, Latest.log 14:51) — ADAPTIVE SIDESTEP CONFIRMED, kept. User: "seems
smoother." Trace: cap widened cleanly 35→46→63→70° as wedge=0→1.4s, HARD-WEDGE at ~1.6s →
"escalated straight to detour (skipped the NPC-blind recalc)" → player freed, went downstairs,
reached target floor, progressing (game closed mid-walk). Recovery ~1.6s vs old ~4s = the smoothness.
HONEST FINDING: the adaptive SIDESTEP itself did NOT free the player — the DETOUR did. Two reasons
from the log: (1) the steer blend only ever asks ~42°, so the 70° cap was never exercised
(SteerStrength limits it); (2) more fundamentally, NPC is in a CORNER — she sits slightly to the
player's right (east), so "push away from her" steered the player LEFT (west) into a WALL (player
moved 0.0m). The detour succeeded by going EAST (+1.4x) — the open gap was AROUND her on the
blocked-looking side, the opposite of where soft repulsion pushes. So simple repulsion can't solve a
cornered chokepoint; the detour's multi-direction NavMesh search is the right tool and now fires fast.
DECISION (user): LEAVE AS IS — works + smoother, detour is correct for cornered blockers. Did NOT
shorten HandoffDelay (1.6s) or add open-side probing (would reinvent the detour). Walk-assist
feature COMPLETE for now. Future, if revisited: trim HandoffDelay to ~1.0s to cut wall-grind, or
make sidestep choose the OPEN side (bigger change). F4 "Walk assist" toggles the whole thing.

### Session 2026-06-16 — stale-announcement + readout fixes (DEPLOYED, build 0/0)

Three fixes, all built and copied to Mods:
1. **Item Creation stale result** (CampMenuHandler.ItemCreation*.cs): highlighting ItemCreation
   in camp re-announced the last-created item. The IC result selector retains data across
   sessions; `_icResultSeenSig` was reset to null on open, so DetectNewResult saw the leftover
   as "new". Fix: seed `_icResultSeenSig` with the selector's current signature on open
   (extracted `GetResultSignature()` helper, shared with DetectNewResult). USER CONFIRMED FIXED.
2. **Guild false "Guild." on shop open** (GuildHandler.cs): guild building shares its UI
   hierarchy with the shop, so `gameObject.activeInHierarchy` read true when only the shop was
   open. Fix: poll `IsOpened` (UIStackSelectorWindowBase) like ShopHandler/GameOver do.
3. **Shop double period** (ShopHandler.BuildItemDetails + TextUtil): equipment readouts produced
   "...straight blade.. None" because parts were joined with ". " over a description already
   ending in ".". Fix: new `TextUtil.JoinSentences()` strips a trailing period per fragment
   before joining. (Equipment stat reading was NOT broken — user's SR was just reading fast.)

### Guild mission menu readout — SOLVED (source confirmed), PENDING READOUT TEST (2026-06-16)

**BREAKTHROUGH — the old "native wall" was a wrong target, not a real wall.**
A scene-wide selector scan (GuildDiagnostics.cs, debug-only) while the guild master's
accept-missions menu was open showed:
- `ui_mission_selector` (UIMissionWindow.missionListSelector) — DEAD: base count 0,
  index frozen at 0, indexDataController current category empty. This is the only thing
  every prior attempt (and the 2026-03-16 "exhaustive" test) ever read.
- `ui_quest_selector` (UIQuestSelector) — LIVE: currentDataList populated (count 7→6→…
  as missions were accepted), currentIndex tracked the cursor 0→6 and back.
=> The guild renders its mission list through the QUEST selector, the SAME selector the
camp Quest screen already reads. UIMissionWindow.IsOpened never even fired for this flow.

First attempt (porting the camp MISSION readout to missionListSelector) was therefore a
repeat of a known-empty path and correctly read nothing — replaced.

Current implementation (build clean):
- New `QuestReadout.cs` — shared quest-list helpers (BuildItemAnnouncement, GetStatusText)
  used by both camp and guild (DRY). `CampMenuHandler.Quest.cs` now delegates to it.
- New `MissionReadout.cs` — same DRY refactor for the camp Mission screen (kept).
- `GuildHandler.cs` — `PollGuildQuests()` finds UIQuestSelector via UiFinder (active +
  populated), gated on `!IsCampOpen`, announces "Guild missions." on entry then
  name + status + position per item via QuestReadout. Fish-collector-style polling.
- `GuildDiagnostics.cs` — TEMPORARY, debug-only, still wired for one confirming test;
  REMOVE once readout confirmed.

**TESTED & WORKING (2026-06-16):** mission list reads name + status + position
(e.g. "Customization Mission 1, Available. 1 of 7." / "Ready to report"). Diagnostic
removed (GuildDiagnostics.cs deleted).

**Remaining gap (accepted by user — "stop here, keep the win"):**
- The guild master's FIRST command menu (accept vs. report) does NOT read. Diagnostics
  proved it is native-rendered: when it opens, the guild's shared UI hierarchy wakes
  EVERY sibling selector (shop/fishcollector/mission/quest) at once, and navigating it
  moves NO managed cursor — not a list selector, not the choice selector. Only
  UIConversationWindow owns a UISelectChoiceSelector and it was empty (n=0, placeholder)
  with its presenter never active. No alternate managed field exists. Left silent.
- Mission DESCRIPTION / completion REWARD at the guild not read (native); user is fine
  reading these from the camp Quests menu instead.

**FUTURE IDEA — fixed message for the command menu:** since the accept/report menu can't
be read live (native, no managed cursor), consider announcing a fixed/static cue when the
guild menu opens — e.g. "Guild menu. Choose accept a mission or report a mission." It would
NOT track which option is highlighted, only tell the user what the menu offers and the
order, so they can operate it by position. Caveats to resolve first: (1) confirm the option
set/order is consistent across guilds and game progress (user was unsure of exact options);
(2) need a reliable open-detection trigger for THIS menu (the shared hierarchy wakes all
sibling selectors, so can't gate on active-state alone — would need a distinct signal, e.g.
the conversation/event that opens it). Low effort if those two are pinned down; flagged as a
possibility, not committed.

--- prior analysis (kept for reference) ---
The 2026-03-16 "CONFIRMED NATIVE WALL" verdict for GuildHandler was in doubt. Two
findings (2026-06-16):
1. `UIMissionListItemData` (: ListItemDataBase) has a PLAIN string field `missionName`
   (+ `stateMessage`, `isReleased`, `isAchieved`, `isClear`, `missionState`,
   `missionParameter`) — no native text resolution needed.
2. The CAMP Quest&Mission menu ALREADY reads this successfully: `CampMenuHandler.Mission.cs`
   does `_missionSelector = window.missionListSelector` → `.TryCast<UIListSelectorBase>()`
   → poll `currentIndex` → `currentDataList[idx].TryCast<UIMissionListItemData>().missionName`
   (line ~96-99). `UIMissionListSelector : UIHelpListSelectorBase : UIListSelectorBase`, so
   it has the standard currentDataList/currentIndex we poll everywhere.
   The GUILD's `UIMissionWindow.missionListSelector` is the SAME TYPE — GuildHandler just
   never got ported to this pattern (it stayed on the presenter/TMPro path that genuinely
   IS native-empty).
Why the old test may have been wrong: it read presenters/raw TMPro (really native-empty),
not missionListSelector; AND guild open-detection used activeInHierarchy (shared with shop),
so probes may have hit a window that wasn't actually the open/active one (fixed today → IsOpened).

PLAN: In GuildHandler, mirror CampMenuHandler.Mission.cs. First add DIAGNOSTICS ONLY —
log `missionListSelector` non-null, `currentDataList.Count`, and `[0].missionName` when guild
opens — to CONFIRM data exists before building the full readout. If populated: poll currentIndex,
announce missionName + GetMissionStatusText-style state + position (+ reward via the existing
rewardItemDataList path if present). Watch for camp/guild sharing one UIMissionWindow instance
(GuildHandler already bails when CampMenuHandler.IsCampOpen). See
[guild-mission-readout-reattempt](memory note) for the detailed approach.

### Notification overhaul (choking, talents, discard prompt, mission rewards) — TESTED & WORKING (2026-06-15)

User confirmed the full set working in-game. Cleaned up (removed verbose per-entry OverflowItem
diag + its unused index param) and committed. Summary of the session's shipped fixes:
- ScreenReader Priority{Normal,High} + 1.5s protection window so reward/unlock popups aren't choked
  by the routine readout that races them; High messages chain instead of overwriting.
- Reward popup (OverflowItemPresenter.SetItem) resolves every entry: name, else itemID, else
  factorID (talent), else sp/bp.
- Talent discovery announced from data via CharacterParameter.OpenSecretTalent (no hookable popup);
  deduped per character+talent. (Fires at craft-confirm, ~earlier than the visual popup — accepted.)
- Unlock dialogs: only OK-type ("X can now be used") are High priority; YesNo confirms stay Normal.
- Inventory-full discard prompt (OverflowItemPresenter doubles as a dialog): Set/SelectChoices hooked;
  message read via deferred poll (text is empty at Set time). Item readout suppressed while the
  discard prompt is active (no redundant "Ruby x3").
- Mission reward preview on highlight: BuildMissionReward reads UIMissionListSelector.rewardItemDataList
  (game-resolved rewardName + itemCount), appended as "Reward: ...".
NOT changed (noted, user didn't flag): "CLEAR" still speaks on mission claim; GiveRewardWithWindow
can't resolve its gem item-IDs (TextManager limitation).

### Mission reward on highlight + discard de-clutter — BUILT (superseded by summary above) (2026-06-15)

User confirmed the deferred discard prompt now READS. Two follow-ups requested:
1. Remove the "Ruby x3" item readout from the claim/discard announcement.
2. Read the mission's reward when the mission is HIGHLIGHTED in the list instead.
DONE:
1. OverflowItemPresenter_SetItem_Postfix now returns early (no announcement) when
   _pendingDiscardPrompt != null — i.e. the popup is the inventory-full discard prompt (Set(YesNo)
   fires before SetItem, so the flag is set). Normal GET popups (chests) are unaffected.
2. Mission reward preview: UIMissionListSelector.rewardItemDataList (List<UIMissionRewardItemListData>)
   carries the highlighted mission's rewards with rewardName ALREADY RESOLVED by the game + itemCount
   — bypasses our unresolvable item-ID problem (gems like Ruby=ITEM_0431 never resolve via TextManager;
   only 3 MessageTypes exist). BuildMissionReward() reads that list and appends "Reward: Ruby x3, ..."
   to the mission readout. Loc mission_reward "Reward: {0}.". The game refreshes rewardItemDataList via
   UpdateRewardInformation (CallerCount 5) on navigation, so reading it during the index-change poll
   reflects the current mission (diag logs reward= to confirm no 1-frame lag).
Files: NotificationHandler.cs, CampMenuHandler.Mission.cs, Loc.cs. Build 0/0, deployed.
WATCH IN TEST: confirm reward matches the highlighted mission (no lag/off-by-one). If lagged, force
_missionSelector.UpdateRewardInformation(item) before reading. "CLEAR" from GiveRewardWithWindow
still fires on claim (not removed — user didn't flag it; revisit if noisy).
TEST (F12 ON): open Missions, scroll the list — each should read "name, status. X of Y. Reward: ...".
Then claim a reward with FULL inventory — should NOT say "Ruby x3"; just the discard question + Yes/No.

### Discard prompt: message empty at Set() — DEFERRED-READ FIX (reads; deferred poll) (2026-06-15)

RETEST (log 16:09–16:10): the Set(YesNo) hook DID fire but with msg='' (line 182) — so it read
just " No". Root cause: UIOverflowItemPresenter.Set fires BEFORE the prompt text is populated
(Set is first in the sequence: Set → SetItem → Reward; no SetMessage with text ever logged). So
reading message/description at Set() time is always empty. NOT a choking issue — the text simply
wasn't there yet. (The yes/no navigation read fine, lines 194-217.)
FIX: Set(YesNo) now DEFERS — stores the presenter + choice + a 0.5s deadline; PollPendingDiscardPrompt()
(new, called from Update each frame) reads message THEN description THEN cached SetMessage until one
is non-empty, then announces "<text>. <choice>" (High priority). Falls back to Loc
overflow_discard_fallback "Inventory full. Discard?" if nothing populates by the deadline (so the
user ALWAYS hears something useful even if the text lives in the prefab). Fixed a finally-clears-
on-wait bug so the poll actually persists across frames.
Files: NotificationHandler.cs (defer + PollPendingDiscardPrompt), Loc.cs (overflow_discard_fallback).
Build 0/0, deployed.
DIAGNOSTIC: new [GAME] OverflowDialogText log shows msg='…' + timedOut. If timedOut=True with the
fallback text, the discard string is NOT in message/description and lives elsewhere (next step).
OPEN (user requests, not yet done): (1) mission reward (Ruby x3) should read when a mission is
HIGHLIGHTED in the list, not as a claim popup — separate reward-preview feature. (2) redundant
"CLEAR" from GiveRewardWithWindow (5 unresolvable item keys) — consider suppressing when overflow
already named the item.
TEST (F12 ON): claim a mission reward with FULL inventory — expect to hear the item, then the
discard question (real text or "Inventory full. Discard?") with Yes/No. Send log (OverflowDialogText).

### Inventory-full discard prompt unread — v1 (Set-time read, empty msg) (2026-06-15)

RETEST (log 15:30–15:33): v3 talent timing CONFIRMED speaking (line 393 "Learned talent Nimble
Fingers." right after Implement IC? Yes). User notes it fires EARLIER than expected — that's
inherent: OpenSecretTalent runs at craft-CONFIRM (15:32:05), the visual popup+SP/Fol come at
craft-END (~15:32:14, 9s later). Announced-but-early. Left as-is pending user call on deferring.
NEW BUG (mission reward, inventory full): claiming a mission reward when the bag is full shows a
UIOverflowItemPresenter in DISCARD mode — item list + "inventory full, discard?" + Yes/No. SetItem
fired ("Ruby x3" read, line 470) but the discard MESSAGE and the Yes/No prompt fired NO hook
(UIOverflowItemPresenter has its OWN SetMessage/Set(DialogType,DialogChoices)/SelectChoices — the
presenter doubles as a dialog — none were patched). So the discard question + choices were silent.
FIX: patched UIOverflowItemPresenter.SetMessage (cache text), .Set(DialogType,DialogChoices)
(announce message + focused button when type==YesNo, High priority, skip-next-select flag), and
.SelectChoices (announce focused Yes/No on navigation). Mirrors the UIDialogPresenter handling;
new GetOverflowChoiceLabel reads presenter.yes/no/ok. Simple reward toasts (type None/OK) ignored
here (SetItem already reads them). Files: NotificationHandler.cs. Build 0/0, deployed.
ALSO SEEN (pre-existing, not fixed): mission GiveRewardWithWindow (Reward count=5 msg='CLEAR')
can't resolve its item keys (ITEM_0430/0431/0429/0433/1605 → TextManager empty) so it reads only
"CLEAR"; the real item name comes from the overflow SetItem instead ("Ruby x3"). Redundant CLEAR.
TEST (F12 ON): claim a mission reward with a FULL inventory — expect "Ruby x3" then the discard
question and "Yes"/"No" as you move the cursor. Send log (want OverflowDialog + OverflowDialogChoice).

### Talent buried by over-protection — FIX v3 (talent speaks; timing-early noted) (2026-06-15)

RETEST of v2 (log 15:21–15:23): super-skill unlock choking FIXED (user confirmed). But the talent
"Learned talent Nimble Fingers." (line 310, 15:21:55.263) WAS spoken yet user didn't hear it.
ROOT CAUSE = my own over-protection: v2 made EVERY DialogPresenter.Setup High priority, incl. the
routine "Implement IC?" Yes/No confirm. That confirm (15:21:54.278, High) opened a 1.5s protection
window; the talent fired 1s later (15:21:55.263) and, being High AND inside an active window,
QUEUED (interrupt=false) — landing THIRD behind "Implement IC? No" and "Yes", then result spam.
Spoken but buried.
FIX v3: gate Dialog High-priority on DialogType. OK-type dialogs (informational "X can now be
used") = High + announce message only (also drops the bogus trailing "No"). YesNo dialogs
(interactive "Implement IC?") = Normal + keep focused-button readout. Now the talent (High) fires
with no active window → interrupts "Yes" and plays FIRST, with the crafting result queued behind.
DialogType{None,YesNo,OK}; Setup(message,type,choice) — postfix now binds `type`.
Files: NotificationHandler.cs (DialogPresenter_Setup_Postfix). Build 0/0, deployed.
TEST (F12 ON): craft 10 silver accessories to trigger a talent discovery — expect to hear
"Learned talent <X>." right after confirming, BEFORE the crafting results, not buried. Confirm
"Implement IC?" Yes/No still navigates normally and unlock popups still read fully. Send log.

### Notifications choked + talent unlock unhooked — v2 (superseded by v3 above) (2026-06-15)

RETEST of v1 (log 15:07–15:10) showed two real causes, NOT the overflow-field gap:
1. CHOKING (proven): unlock dialogs DO announce, then the routine skill readout fires ~40ms
   later with interrupt=true and CUTS THEM OFF. Lines 771 "Specialty Oracle can now be used"
   → 773 "Purity Level 1…" 39ms later; 948 "IC Replication can now be used" → 950 "Imitation
   Level 1…" 42ms later. User heard only the skill readout.
2. TALENT UNLOCK has NO hookable presenter: every OverflowItem entry this session was a plain
   named resource (SP/BP/FOL/item itemID=90) — factorID ALWAYS INVALID. No Dialog line for a
   talent either. So talent discovery routes through a presenter the mod can't see.
FIX v2:
- ScreenReader.cs: added Priority {Normal,High} + a 1.5s protection window. A High announcement
  sets _protectUntil; within it, routine (Normal) output and later High output QUEUE
  (interrupt=false) instead of choking the protected message. Reward/unlock popups now survive
  the racing skill readout; a sequence of rewards chains instead of overwriting.
- NotificationHandler.cs: unlock Dialog + reward Overflow announcements now use Priority.High.
- TALENT (data-level hook, bypasses UI): patched CharacterParameter.OpenSecretTalent(SpecialSkillID)
  [CallerCount 11] postfix. __result is the discovered TalentID (INVALID if none). Announces
  "Rena learned talent Nimble Fingers." (High priority), deduped per characterID:talentID via
  _announcedTalents HashSet. Name via new CampMenuHandler.ResolveTalentName(TalentID) (reuses the
  status-screen TalentDisplayOrder map). Char name from CharacterParameter.CharacterName.
- Loc.cs: talent_learned "Learned talent {0}.", talent_learned_named "{0} learned talent {1}.".
Files: ScreenReader.cs, NotificationHandler.cs, CampMenuHandler.Status.cs, Loc.cs. Build 0/0.
NOTE: v1 overflow enrichment (itemID/factorID/sp/bp resolution + per-entry diag) is KEPT — still
correct, just wasn't the talent path. The per-entry diag confirmed factorID is never set here.
TEST (F12 debug ON): in Enhance→Skill, raise a specialty to trigger "X can now be used" — should
now read FULLY (not cut off by the skill readout). Use a specialty enough to discover a talent —
should hear "<name> learned talent <X>." Send log: want the [GAME] TalentLearned line + no choking.

### Reward popup ("GET!") drops talents & unnamed items — v1 (superseded by v2 above) (2026-06-15)

User report: several award notifications didn't read; mission reward "received something but
didn't say what"; talent unlock "read the SP I got but not that I unlocked a talent"; Rena skill
enhancement gave many award toasts that were silent or contextless.
LOG ANALYSIS (Latest.log 14:39–14:44): only 3 popups fired hooks, ALL via UIOverflowItemPresenter
(GET! popup): "SP x100, BP x100", "GET! Spectacles", "GET! FOL x100". FieldInfoStack and
GiveRewardWithWindow never fired. Root cause: OverflowItemPresenter_SetItem_Postfix read ONLY
OverflowResourceData.name + count and SILENTLY SKIPPED any entry with an empty name. But entries
also carry sp, bp, itemID, and factorID — talents are "factors" (FactorID FACTOR_xxx) with NO
plain name, so talent-unlock entries were dropped; items with only itemID set were dropped too.
FIX: BuildOverflowEntryText() resolves each entry — name, else itemID→TextUtil.ResolveItemName,
else factorID→TextUtil.ResolveFactorName (new: GetFactorParameter(id).messageID→GetFactorMessage),
else raw sp/bp. Talents announced as "Talent {name}". Per-entry DIAGNOSTIC logs ALL raw fields
(name/count/sp/bp/itemID/factorID/isUnique) so the next test confirms the talent path.
Files: NotificationHandler.cs (BuildOverflowEntryText + ConstFactorParameter ctor), TextUtil.cs
(ResolveFactorName), Loc.cs (overflow_talent "Talent {0}"). Build 0/0, deployed.
CAVEAT: not yet proven the talent-discovery toast routes through THIS presenter (it produced no
log line in the captured session). If the diagnostic shows no factor entry on a talent unlock,
the discovery uses a separate presenter and we hook that next.
TEST (F12 debug ON): unlock a talent / complete a mission / enhance skills, then send the log.
Want: every reward reads its name, talents read "Talent X", and the per-entry diag lines.

### IC Super Specialties: requirements read stale — TESTED & WORKING (2026-06-15)

User confirmed each super specialty now reads its OWN requirement, stable on re-visit. Temporary
debug line removed; committed.

User report: in IC → Super Special Skills tab, the "Requires:" text was the SAME for every
super specialty (and the same item read different requirements at different times). Log
(13:47–13:48) confirmed: skill NAME + DESCRIPTION were correct per row, but the requirement
text lagged — Bunny Call (idx 3) read "Music … Art" early then "Customization … Alchemy" later.
Root cause: TryPollSuperSpecialtyTab read conditions from the SHARED
infoPresenter.superSpecialSkillLearningPresenter, a sub-presenter the game refreshes on a
different cycle than skillName/skillDescription, so it was one navigation behind.
v1 FIX WRONG (log 13:58): cast currentDataList[idx] to UISuperSpecialSkillSelectItemData — that
type belongs to a DIFFERENT selector (UICampSuperSpecialSkillSelector), so the cast returned null
and NO requirement was read at all (just a trailing double period). UISelectorBaseData/
UICampSelectSpecialSkillSelectorData is NOT a ListItemDataBase, and the SuperSpecialSkillID enum
order does NOT match the list (FISHING is absent at idx 8 → index→ID mapping impossible).
v2 DIAG RESULT (log 14:10): rowType=UICommonListItemData (generic — text only, NO skill id),
cacheSSID=INVALID (cacheData does NOT track the cursor). So GetNeedCondition got nothing and it
fell back to the lagging presenter every time (confirmed lag = strictly off-by-one: each entry
shows the PREVIOUS entry's requirement). Also confirmed the list is enum order minus unavailable
skills (FISHING absent) → index→id mapping impossible.
v3 DIAG RESULT (log 14:23): name→ID map WORKS (every row resolved: Orchestra→ORCHESTRA, etc.),
but GetNeedCondition(id) is the WRONG method — returns the battle ACTIVATION condition
("4 or more instruments" for Orchestra) or empty, NOT the learning requirement. So it kept
falling back to the lagging presenter.
v4 (BUILT, AWAITING TEST) — plan-mode solution (plan: encapsulated-coalescing-thompson.md):
Keep the proven name→ID map. With the correct ssid, construct the game's own requirement data
object `new UISkillLearningSuperSpecialSkillInformationData(ssid)` and read its
condition1SkillName/condition2SkillName — computed on demand from the id, so they CANNOT lag.
Pair with the STATIC count/level descriptions from
infoPresenter.superSpecialSkillLearningPresenter.condition1Description/condition2Description
(those never change → no lag) via the existing AppendLearningConditions string overload.
Falls back to the presenter read on failure. Removed the GetNeedCondition path and the heavy
DIAG; kept a concise `CampSS tab2: ssid=… cond1=… cond2=…` debug line.
Files: CampMenuHandler.SuperSpecialty.cs. Build 0/0, deployed.
TEST (F12 debug): IC → R1 to Super Special Skills → scroll all 10 AND back up → each entry must
read its OWN requirement, stable on re-visit (Orchestra=Music/Art, Group Appraising=Appraising/
Crafting, Blacksmith=Customization/Alchemy, …). Confirm same item = same requirement every time.

### Two fixes: Enhance→Skill char switch + IC consumable readout — TESTED & WORKING (2026-06-15)

Build 0/0, deployed. User-confirmed working in-game; final log (13:32–13:34) verified.
Highlights: Writing → "Needs Fountain Pen" (not the book); Alchemy → "Needs Iron"
(the display path also corrected Alchemy's self-referential consumeItemID); Machinist →
"Needs Mechanic's Toolbox"; Art → "Needs Magic Canvas"; skill L1/R1 switch reads the
character name cleanly once per switch.

**1. Camp → Enhance → Skill: L1/R1 character switch announcement.**
v1 (prepend name inside the presenter) was AUDIBLY broken: log 13:09 confirmed the names
("Rena.", "Claude.", "Celine.") WERE produced, but UISkillInformationPresenter.Set fires
TWICE per change ~6ms apart — fire 1 carries the name + STALE SP balance, fire 2 carries
fresh balance + NO name and interrupts fire 1, so the user only hears the nameless version.
v2 = DEFERRED FLUSH: the presenter now only CACHES the text (no name) + currentPlayerID +
timestamp; UpdateSkillSelector flushes it once SkillFlushDelay (0.05s) passes with no new
fire, prepending the character name only when currentPlayerID changed since the last flush.
Coalesces the double-fire (later fresh-balance fire wins) and also kills the routine
double-announce. _skillFlushedOnce gate = no name on first entry.
Files: CampMenuHandler.Formation.cs.

**2. IC → Machinist: consumable material name.**
v1 read the consumable but as a RAW ID ("Needs 0456" / "Needs 0001" / "Needs 0043"):
consumeItemID is populated, but GetItemParameter+TextManager returns a numeric placeholder
key for these (same limitation as fish names). v2 = ResolveConsumeItemName() asks the game's
own UICampSpecialSkillActionSelectorBase.CreateConsumeItemData(id, count), which builds the
exact UISpecialSkillConsumeItemListData shown on screen (resolved itemName + haveCount);
falls back to ResolveItemName, and IsNumericName() suppresses any still-numeric result
(silent rather than reading digits). Debug log `CampIC: consume resolve id=N -> 'name'` added.
Files: CampMenuHandler.ItemCreation.cs (ReadConsumeRequirement/ResolveConsumeItemName/IsNumericName),
.ItemCreation.ActionList.cs, Loc.cs (ic_consumes "Needs {0}", ic_consumes_qty "Needs {1} {0}").

v2 RESULT (log 13:09–13:24): (1) Skill char switch CONFIRMED clean. (2) Machinist/Art/Cooking/
Crafting/Alchemy consumables CONFIRMED reading real names (Mechanic's Toolbox, Magic Canvas,
Seafood, Silver, Iron, …) via CreateConsumeItemData. ONE skill still wrong → see v3.

**v3 — Writing consumable fix — TESTED & WORKING (2026-06-15).**
User report: in Writing, the readout said "Walls of the Soul. Needs Walls of the Soul" — i.e. it
named the BOOK being written, not the real tool (Fountain Pen). Log confirmed consumeItemID for
Writing items is SELF-REFERENTIAL (id=174 'Walls of the Soul' == the product), so the
consumeItemID→CreateConsumeItemData path resolves the product, not the consumable. Writing goes
through the FALLBACK path (PollActionListFallback), not the creation hook.
Fix: ReadConsumeRequirementFromDisplay() reads the on-screen consume display directly —
_icActionPresenter.consumeItemPresenter.consumeItemPresenterList → each active row's
itemNamePresenter.itemName (GameText). That is what the game actually shows (Fountain Pen), so
it is authoritative for every skill. The FALLBACK path now prefers it
(`ReadConsumeRequirementFromDisplay() ?? ReadConsumeRequirement(item)`). The CREATION-HOOK path
(Art/Machinist) keeps consumeItemID — it already reads correct names AND the display may not be
refreshed yet inside that Harmony postfix (SetConsumeItem is CallerCount(0), native-only, so it
can't be hooked; reading it live is only safe from the per-frame poll).
Files: CampMenuHandler.ItemCreation.cs (ReadConsumeRequirementFromDisplay),
.ItemCreation.ActionList.cs.

TEST RESULT (log 13:33): CONFIRMED — "Walls of the Soul. Needs Fountain Pen, unavailable." and
all other books read "Needs Fountain Pen". No regression on Machinist/Art/Cooking/Crafting, and
Alchemy (Silver/Gold/Sapphire/Ruby) now correctly reads "Needs Iron" via the display path.

### Camp "Use item on character" target picker — TESTED & WORKING (2026-06-15)
User report: using an item ON a character (e.g. reading a skill book) did not announce
which character was highlighted.
v1 (WRONG selector) announced nothing. Diagnostic log (12:28) RESOLVED which selector:
  - currentState DOES flip SelectItem -> SelectCharacter (confirmed).
  - The active picker is _itemSelector.selectCharacterSelector
    (UICommonSelectCharacterListSelector), NOT UICampItemCharacterStatusSelector
    (that rich roster was never active). playerIDList=[CLAUDE,RENA,CELINE].
v2 (CampMenuHandler.ItemTarget.cs): gated on currentState==SelectCharacter; reads
selectCharacterSelector. That type has NO currentIndex and a visual-only cursor, so the
highlighted index = match currentSelectPresenter within selectItemPresenterList (pointer
compare); map idx -> playerIDList[idx]; name via ParameterManager.GetCharacterFirstName,
HP/MP via UserParameter.GetCharacterParameter (HitPoint/HitPointMax/MentalPoint/MentalPointMax).
Announces heading on entry then "Name. HP x of y. MP x of y. n of total." per cursor move.
Loc keys: camp_item_target_screen/_hp/_mp.
KNOWN GAP: if the cursor sits on a non-character "all allies" option, idx not found -> silent
(acceptable for v1; revisit if items with an All target need it — isSelectedAll available).
TEST: Camp -> Item -> skill book (Engineer's Handbook) -> confirm -> expect "Use on which
character?" then each member read as you move up/down.

**Last completed:** Status Talents readout FIX — announces OWNED talents only via HasTalent. TESTED & WORKING (2026-06-15). Committed.
**Currently working on (history):** Pickpocket success-rate DIAGNOSTIC (debug F7) — v2, RETEST PENDING on a FRESH never-robbed NPC (2026-06-15).
  v1 FINDING (log 09:55–09:57): on-screen rate vs game-internal rate (GetPickPocketSuccessRates) DISAGREE.
  On-screen: common ~1%, rare/SR 0%. Internal: common ~1.4%, rare ~70%, SR ~47% (note: internal is
  inverted vs difficulty — rarer = higher — so internal may NOT be the true success metric). Setup is
  confirmed good (Thief's Glove owned, Pickpocketing lv7, Nimble Fingers on Claude, PA mode active).
  Hypothesis: tested NPCs may be tapped-out (attempt cap reached; user has 0 Relax Perfume; "Use Relax
  Perfume" option present). v2 adds UserParameter.GetPickPocketExecutionCount / GameDefine.maxPickPocketExecutionCount
  + per-item canDecision (stealable now?) to distinguish tapped-out from genuinely-low.
  v2 RESULT: fresh NPC (WOMAN1, attempts 0/3) common items shown 1% = internal 1.4%, stealable. User
  failed a steal => 1% is REAL on fresh commons. "Rarer=easier" RETRACTED (rare raw numbers came from
  tapped-out NPCs, unreliable). Expected ~40-50% with Nimble Fingers.
  v3 HYPOTHESIS (decompile-backed): "Nimble Fingers" = TalentID.DEXTERITY. Game's FUNCTIONAL check is
  CharacterParameter.HasTalent(DEXTERITY) — a DIFFERENT source than the status screen (UITalentData =
  display text only). Field pickpocket uses the on-field CONTROL PLAYER (FieldManager.GetControlPlayer()
  .CharacterParameter), and reads talent + GetSpecialSkillLevel(PICKPOCKET=14) from THAT char (per-char,
  not party-wide). If actor != Claude (e.g. wrong leader / PA split), rate collapses to ~1%.
  v3 DIAGNOSTIC (built, RETEST PENDING): F7 now logs actor name/CharacterID, actor HasTalent(DEXTERITY),
  actor pickpocket level, party leader, AND Claude's HasTalent+level. Verdict names the actor and whether
  it has Nimble Fingers. Files: PickpocketHandler.cs (AppendActorReport/NameOf).
  *** RESOLVED (2026-06-15) *** F7 log 189-191: Actor=Claude, Leader=Claude, PickpocketLevel=7, but
  HasTalent(DEXTERITY)=FALSE. Claude does NOT actually have Nimble Fingers — that's the 1% cause.
  ROOT of the confusion: the status Talents screen (UICampStatusSelector.UpdateTalent -> List<UITalentData>)
  lists ALL talent NAMES and encodes ownership in the COLOR field (owned=highlighted, unowned=greyed).
  Mod's TalentPresenter_Set_Postfix (CampMenuHandler.Status.cs:384) reads talentName only, ignores color,
  so it announces greyed/unowned talents too (OCR has same blind spot). Dexterity==Nimble Fingers.
  CURE for user: have Claude use Crafting specialty ~8-9x to unlock the talent (randomized at start).

**Talent readout FIX — TESTED & WORKING (2026-06-15), OWNED-ONLY:**
  Status talent screen announces only talents the character actually HAS, via
  CharacterParameter.HasTalent(TalentID). Prefix Diag_StatusSelector_UpdateTalent(PlayerID) on
  UICampStatusSelector.UpdateTalent captures the character; TalentPresenter_Set_Postfix rewritten to
  ignore the colour-coded name list (colour = ownership, invisible to screen reader/OCR) and use
  HasTalent + TalentDisplayOrder map. 10 talent names in Loc.cs (talent_*).
  Files: CampMenuHandler.Status.cs, CampMenuHandler.Patches.cs, Loc.cs.
  WHY IT MATTERED: this surfaced that Claude never had Nimble Fingers (DEXTERITY) — the real cause of the
  1% pickpocket rate. The old readout announced greyed/unowned talents, masking it.
  USER NEXT (gameplay, not a mod task): grind Crafting on Claude to acquire Nimble Fingers, then pickpocket improves.

**Debug F7 pickpocket diagnostic: REMOVED (2026-06-15)** — investigative scaffolding, reverted after it
  did its job. PickpocketHandler.cs restored to the announce-only poll loop; DebugHotkeys.cs F7 wiring
  and Main.cs constructor arg reverted.
  Investigates user report that all NPC steal rates show <=1% despite Thieves Gloves +
  Nimble Fingers + Pickpocket lvl 7. F7 (debug mode) on an open pickpocket window logs
  per item: rarity (N/R/SR/SSR), base probability (GetFactorProbabilityParameter), shown
  rate, and raw rate (SpecialSkill.GetPickPocketSuccessRates). Speaks a verdict: common
  item far below base => bonuses not applying / attempts / mood; low rate on rare item =>
  working as intended. Files: PickpocketHandler.cs (LogPickpocketDiagnostic), DebugHotkeys.cs, Main.cs.
**Last completed:** Fish Collector ("Reel") menu — fish-name resolution via condition panel. TESTED & WORKING (2026-06-15)
**Quick Heal Menu (D-pad Right): DONE — CONFIRMED WORKING by user (2026-06-20).** v1 + v2 (name/amount/result fixes + L3 key) both verified in-game.
**Last completed:** Item Creation — Appraise result announcement (2026-06-13) — TESTED & WORKING

### Fish Collector menu — TESTED & WORKING (2026-06-15)

**Files touched:** FishCollectorHandler.cs, Loc.cs, TextUtil.cs
**Build:** succeeds, DLL copied to Mods. Diagnostics removed.

**Root cause of "reads item IDs not fish names" (CONFIRMED via 2026-06-15 log):**
Fish names are NOT in any readable data table. For a qualifying fish ID (e.g. 1636):
- `GetItemParameter(1636).itemNameID` = literal placeholder "ITEM_1636".
- `TextManager.GetMessage("ITEM_1636"/"1636", Item)` = empty. Only 3 MessageTypes exist
  (System/Skill/Item) and fish aren't in any. So the old ID->name path always failed.
- The old `_fishNames` cache stayed empty because the exchange-amount flow never opens
  the select-fish screen (the game auto-consumes fish; you can't pick which — GAME design,
  confirmed with user, not a mod bug). Rewards split into:
    * "any fish" (e.g. Seafood): rewardParam.fishItemID = [0,0,0].
    * "specific fish" (e.g. Life in Nature): fishItemID = [1636,1637,1638].

**The fix (NEW APPROACH):** read the on-screen "Goal conditions" panel directly.
`UIFishCollectorExchangeSelector.conditionPresenterList` is a public ordered
`List<UIFishCollectorConditionListItemPresenter>` on the SAME selector we already poll.
Each active row exposes GameText: `type` (game's own resolved fish name), `useCount`
(needed), `haveCount` (owned). `BuildRequirementFromConditions()` reads those rows; if
none are active, falls back to "any kind". No cache, no ID decoding, no disk persistence.

**Per-row text format (confirmed from log):** condition rows carry `type` (resolved name,
e.g. "Krosse Carp", or catch-alls "All fish" / a size "Large") and `haveCount` (how many
the player owns). `useCount` is ALWAYS 0 while browsing (it's the in-trade allocation), so
it is NOT announced. Wording: collector_fish_req = "{0} have {1}" → e.g.
"Costs 3 fish: Otiph Carp have 0, Castle Gate Carp have 0, Krosse Carp have 8."

**Removed:** old ID-based BuildRequirement, _fishNames cache + select-fish learning loop,
LogExchangeDiag, LogSelectFishDiag, COLLECTOR FISH PROBE, COLLECTOR COND ROW. Loc keys
collector_need_have and collector_fish_owned (unused). Added collector_fish_req.
NOTE: TextUtil.ResolveItemName(itemID) is still used by CampMenuHandler.ItemCreation.Material.cs
— keep it.

**Punctuation fix:** TextUtil.AppendPosition now collapses a trailing "." before the
". N of M." suffix, so segments that end in a period no longer produce a double period
(".. 1 of 5."). This applies to ALL handlers that use AppendPosition.

**Player-facing known issues:** see `KNOWN_ISSUES.md` (ships with the mod) — currently:
guild mission menu unreadable, world-map auto-walk town collision, IC default character not
named on entry. Keep this doc in sync as limitations are found/fixed.

### Item Creation — Appraise result announcement (2026-06-13) — TESTED & WORKING

User confirmed via log: appraisals read cleanly — "Sandals. Success. 1 of 1.",
"Water Ring. Success. 1 of 1.", "?JEWELRY. Failure. 1 of 1.", "Talisman. Success. 1 of 1."
Trigger fired on all attempts, no doubled status, no double-announce.

Appraisal results were silent: after "Implement IC? -> Yes", the shared result selector
(UICampSpecialSkillResultSelector, sid=APPRAISAL) DOES populate (log: cur=1, e.g.
"Sandals/Success", "?ARMOR/Failure"), but appraisal bypasses the create-count flow that
normally schedules the result announcement, so _icResultState.LastIndex stayed 0 == result
idx 0 and UpdateICResult's poll bailed.

Fix (CampMenuHandler.ItemCreation.cs): `DetectNewResult()` (called from UpdateICResult)
watches the result list's first-item signature (count:itemName:isSuccess); when it changes
to a new non-empty value it schedules the announcement via the shared _icResultReadyTime
(+0.5s). Coexists with the create-count path (single _icResultReadyTime -> one announce, no
double for regular creation). Also dedup: skip the result-text field when it equals the
success/failure status (appraisal stores "Success"/"Failure" in both) so it reads
"Sandals. Success. 1 of 1." not doubled. Debug DIAG `CampIC_Result DIAG` left in place.

Known limitation: two appraisals in a row with an identical result string won't re-announce
the second (no content change). Distinct results always read.

### Item Creation action-list focus-tracking (2026-06-13)

Fixed several Create sub-menus reading NO options (Writing, Appraise, Alchemy, Compounding,
Machinist) and wrong "X of N" positions on the ones that did read (Cooking, Art).

Root cause: all ~28 special-skill selectors report activeInHierarchy == true for the whole
IC session (stale-active); `isPause`/`isDisableInput` don't distinguish focus either. Old
code picked the first "active" selector and cached `_icActionListBase` once (cleared only in
an onHidden that never fired), so the reader was stuck on whichever list was opened first.
Has-items skills (Cooking/Art) still spoke via the creation hook but read position from the
stale list ("4 of 5"); no-items skills fell to the fallback poll on the stale list → silence.

Fix (CampMenuHandler.ItemCreation.cs): `ResolveFocusedActionSelector()` finds the focused
skill each frame as the selector whose action list just became populated (entry) or whose
cursor index changed (navigation) — lists you're not on never move, and menu pre-load moves
no cursor, so it's a clean signal. On focus switch, re-point `_icActiveSelector` /
`_icActionListBase`, reset `_icActionState.LastIndex = -1`, and seed `_icLastCharTab` to the
selector's current tab (so entry no longer blurts the character name). Seeded on camp open
via `SeedActionFocusTracking()` to avoid spurious reads when scrolling past the IC root item.
Log confirms correct names + positions for Appraise/Writing/Crafting/etc.

Known minor: first item on entering a has-items skill reads without "X of N" (game hook fires
one frame before focus is set). Debug DIAG (`CampIC_Action DIAG` / `focus -> #N`) left in place.

DECISION (2026-06-14): entry intentionally does NOT name the default character; only L/R
switching announces it (via TrackCharacterTab). Attempted prefixing the character name to the
first action announcement, but the log proved it CANNOT be name-first for craftable skills:
the game's recipe hook (UIItemCreationInformationPresenter.Set) fires ~18ms BEFORE the
poll-based focus-switch detection, so the name isn't captured in time (e.g. `creation hook:
Seafood` at 45.467 precedes `focus -> #2` at 45.485). Only a name-AFTER-recipe order would
work consistently; user preferred the previous behavior over that, so the attempt was
reverted (CampMenuHandler.ItemCreation.cs restored to its committed state). L/R announcement
confirmed working in-game.

### Jump-down prompt cue — TESTED & WORKING (2026-06-13)

User confirmed: "It works perfectly." Audio cue + once-only speech fire when the "X Jump"
prompt appears at a one-way ledge; both toggles work independently in the F4 menu.

Built on the confirmed UIFieldOperationPresenter.Set hook (see test result below). When the
"X Jump" prompt appears above the player at a one-way ledge, the mod now plays an audio cue
AND speaks it once ("Press Cross to jump down."). Both are independently toggleable in the
F4 mod menu (user request: sound-only / speech-only / both / neither).

- **FieldPromptHandler.cs** (renamed from FieldPromptDiagnostics.cs): Set postfix detects the
  jump prompt by ACTION WORD ("Jump") parsed from operationList (NOT isPlayer — it's false).
  Announces ONCE on appearance (cue if JumpPromptSoundEnabled + speech if JumpPromptSpeechEnabled).
  Hide is native-only ([CallerCount(0)]) so Update() polls the presenter (activeInHierarchy +
  jump text still present) to clear the flag, allowing re-announce on a later re-appearance.
  Still logs every prompt under [GAME] FieldPrompt in debug mode for cataloguing Talk/Open/etc.
  Parses button glyph from "<sprite name=Cross>Jump" → speech names the button (controller-aware).
- **AudioCuePlayer.cs**: LoadJumpSound / PlayJumpCue / IsJumpSoundLoaded (mirrors dodge cue;
  volume = ModSettings.JumpPromptSoundVolume, winmm.dll playback).
- **ModSettings.cs**: JumpPromptSoundEnabled (def true), JumpPromptSoundVolume (def 0.8, JSON
  only — not in menu yet), JumpPromptSpeechEnabled (def true). Persisted in settings.json.
- **ModMenuHandler.cs**: two new F4 toggles — "Jump prompt sound", "Jump prompt speech".
- **Loc.cs**: jump_prompt "Press {0} to jump down.", jump_prompt_no_button, + 2 menu labels.
- **Main.cs**: loads Jump.wav from UserData/SO2RAccess/Sounds; _fieldPromptHandler wired into
  InitializeHandlers / ApplyPatches / UpdateHandlers.
- **Jump.wav**: generated placeholder cue (descending G5->C5 two-tone, 16-bit mono PCM, ~0.3s)
  written to UserData/SO2RAccess/Sounds/Jump.wav. RELEASE NOTE: ship this WAV with the mod
  (or replace with a nicer cue). User can swap the file freely.
- Build clean (0/0), deployed to Mods.

CLEANUP (2026-06-13, post-test /simplify pass):
- Removed duplicated _spritePrefixes + StripControllerPrefix from FieldPromptHandler;
  now reuses NotificationHandler.StripControllerPrefixPublic (new public wrapper). DRY.
- Collapsed the three-way Set-postfix branch (appearing/staying/replaced) into the
  announce-once + always-track-presenter form; same behavior, less code.
- IsJumpStillShowing now substring-checks the RAW operationTextList text for "Jump"
  (action word survives tag-stripping) instead of running StripTagsPublic every frame —
  cheaper on the per-frame hide-poll path.
- SKIPPED (deliberate, not over-engineering for now): generalizing to an action-keyed
  registry for future Talk/Open/Examine cues. Jump-only is the right scope; the diagnostic
  log still captures other prompts when encountered. English-literal "Jump" filter and
  static-field handler pattern are intentional/consistent with the codebase.

OPEN ITEM FOR RELEASE: Jump.wav currently lives only in UserData/SO2RAccess/Sounds (a
generated placeholder cue). It must ship with the mod's Sounds for distribution, or be
replaced with a nicer cue. Known minor risk (untriggered so far): if the prompt flickers
on/off at the ledge boundary the cue could repeat — add a debounce if it surfaces.

### NEXT SESSION: Jump-down prompt sound cue — HOOK IDENTIFIED, diagnostic deployed (2026-06-13)

Auto-walk now reliably parks the player AT a one-way ledge (see "One-way jump-down
ledges" below). Build an audio cue there. Confirmed facts (user, 2026-06-13):
- Descending a ledge REQUIRES a manual X (Cross) button press — it is NOT automatic.
- The game shows a visual indicator above the player's head reading "X Jump" when at a
  ledge. So the cue is a "press X to jump down here" prompt, not just an alert.

**CORRECTION (2026-06-13 code analysis):** the jump prompt is NOT a UIFieldIconSelector
icon. UIDefine.FieldIconType has only { LocationPoint, Fishing } — neither is the jump
prompt. The "X Jump" indicator is a field "operation" (button-guide) prompt. Full analysis
in docs/game-api.md Section 18. Key facts:
- Render path: UIFieldController.ShowOperation(...) [CallerCount(2)] →
  UIFieldOperationPresenter.Set(operationList, followTransform, canvas, ref worldOffset,
  isCancelLocalPosition, isPlayer, textColorList) [CallerCount(7)] — HOOKABLE.
- BEST HOOK = UIFieldOperationPresenter.Set. operationList = raw strings; isPlayer=true means
  it's anchored over the player (the jump case). presenter.operationTextList (List<GameText>)
  exposes the actual rendered text, so we can read the literal "Jump" + button glyph.
- Hide = native-only (HideOperation / Hide both [CallerCount(0)]) → a hook will NOT fire on
  hide; detect disappearance by polling presenter.gameObject.activeInHierarchy.
- The presenter is SHARED with other button guides (talk/interact prompts), so the cue must
  filter on the prompt content (jump) and/or isPlayer=true, not just "a prompt appeared."

**DIAGNOSTIC DEPLOYED — AWAITING IN-GAME TEST.** New file FieldPromptDiagnostics.cs patches
UIFieldOperationPresenter.Set (postfix) and logs every operation prompt under [GAME] when
debug mode is on. Wired into Main (field + InitializeHandlers + ApplyPatches). Announces
nothing, changes no behaviour. Build clean (0/0), deployed to Mods.
HOW TO TEST: F12 to enable debug, walk to a one-way ledge (and around town past NPCs/chests),
send the log lines tagged `[GAME] FieldPrompt = ...`. We want to see the exact raw/display
strings + isPlayer for the jump prompt vs other prompts, to pick the filter and decide
audio-cue-vs-speech per prompt type.

**TEST RESULT (2026-06-13, Latest.log 17:17) — JUMP PROMPT CONFIRMED.** Captured prompts:
- Jump:  isPlayer=False anchor='cp_0001_01(Clone)' raw=`[0]=<sprite name=Cross>Jump`  display='CrossJump'
- Save:  isPlayer=False anchor='ob_1001_02a(Clone)' raw=`[0]=<sprite name=Cross>Save`  display='CrossSave'
KEY FINDINGS:
- Jump prompt DOES flow through UIFieldOperationPresenter.Set. Format = single entry,
  `<sprite name=BUTTON>ACTION` (button glyph tag + action word).
- isPlayer is FALSE even for the jump prompt (anchor cp_0001_01 = player char object). So
  DO NOT filter on isPlayer — filter on the ACTION WORD ("Jump") in operationList[0].
- StripTags merges sprite name into the word ("CrossJump"). For speech, parse button name +
  action separately (e.g. regex split the sprite tag from the trailing word).
- Only Jump + Save captured (user only triggered those). Talk/Open/Examine not yet seen — can
  capture more if we want the full inventory, but the jump target is confirmed.

NOTE: corner toasts (item/EXP/Fol/level-up/skill/talent) are ALREADY announced via
NotificationHandler (UIFieldInformationStackSelector.ShowInformation) — not part of this work.
Other uncovered above-head families catalogued in Section 18: ShowEmotion (17 EmotionTypes,
the !/? bubbles) and ShowSymbolName/ShowMode area banners.

### One-way jump-down ledges (2026-06-13) — TESTED, WORKING

CONFIRMED in-game (log 12:28–12:32): F11 reported 9 one-way drops; end-dungeon Recovery
save point (118.3,5.9,177.5) traversal=True (legit detour exists) and the player ARRIVED
("Arrived at Recovery save point (above)" 12:32:39) across ~7 battle resumes — no
wall-climb stuck loop. Chests up one-way ledges stay reachable via navMeshComplete=True
(IsReachable OR). No regressions observed.

Bug: auto-walk to "Recovery save point (above)" got stuck walking into a wall
(log 11:14:22–11:14:45). Root cause: TraversalGraph edges were bidirectional, so a
ledge the player JUMPED DOWN during recording became a two-way "staircase" and A*
routed the player back UP a wall they can only descend. Logged path climbs 3.4m over
~0.6m horizontal — a ~80° face — vs real ramps at 0.6m over 4m.

Fix (TraversalGraph.cs, public API unchanged):
- IsSteepDrop(a,b): a near-vertical edge (|Δy|>=DropMinDy 1.0m AND Δy/Δxz>=DropMinRatio
  1.2). Ledges score ~4–6; steepest ramp ~0.15 — no overlap, stairs unaffected.
- Adjacency is now DIRECTED. Steep edges are downhill-only by default (you can fall, not
  climb). Connect(a,b,observedFrom) replaces AddEdge: gentle→both ways; steep→high→low,
  plus low→high ONLY if the player was observed travelling uphill (climb point).
- Observed-climb awareness: _climbEdges set, persisted as optional JSON "ClimbEdges".
  Old files (no ClimbEdges) → all steep edges downhill-only (correct for Krosse Cave,
  no ladders). New recordings auto-detect ladders because the player climbed them.
  NO re-recording needed for this fix; works on existing + embedded data.
- IsReachable → directed BFS (was undirected components). FindPath A* respects direction
  automatically. Removed _comp/EnsureComponents.
- F11 diagnostic (NavigationHandler.cs LogTraversalDiagnostic) now logs DropSummary
  (count + sample coords of one-way drops) and save-point reachability, not just chests.

Why downhill stays routable: a target below a ledge remains reachable and the router
walks the player TO the ledge (where the future jump-prompt cue will hook). A target only
reachable by climbing up is now honestly unreachable (drops off the nav list) instead of
producing a wall-climb. Resume-after-battle falls through to partial-walk + honest
"cannot reach (above)" rather than the stuck loop.

TEST (F12 debug, then F11 in Krosse Cave MF_0008_01A end-of-dungeon save):
1. Log should report >=2 one-way drops near the known ledges.
2. Note the save point's traversal= value (reachable via detour, or genuinely not).
3. Auto-walk to the save: no wall-climb/stuck loop — either routes around or says
   cannot reach.
4. Auto-walk to a target below/beyond a downward ledge: player should still be routed TO
   the ledge top and stop there (descent needs manual X) — confirms jump points stay
   reachable for cue development.
5. Sanity: normal same-floor auto-walk still works.

### QOL: Field auto-walk battle-resume + bonus gauge percentage (2026-06-13) — TESTED, WORKING

Two user-requested quality-of-life features. Build clean, deployed, both confirmed working in-game.
Status: (1) field battle-resume CONFIRMED WORKING; (2) bonus gauge percentage CONFIRMED WORKING
(toggle in F4 menu, default OFF; speaks "Gauge 5/10/15..." every 5% as the gauge fills).

**1. Field auto-walk resumes after a battle.**
The world map already resumed after battle (`_wmResume*` in NavigationHandler.Worldmap.cs);
field maps just called `CancelAutoWalk()` on any interruption. Field maps now mirror that,
but battle-gated so dialogue/cutscenes/menus do NOT trigger a resume.
- On a field interruption, `SaveFieldResume()` snapshots target/label/category/transform/
  facePosition/isCounter/eventRef/triggerBounds/mapId, then CancelAutoWalk (NavigationHandler.cs ~749).
- `UpdateFieldResume()` (called from Update before the `!_isAutoWalking` return) classifies:
  `IsBattleActive()` (BattleManager + battlePlayerList, same signal as BonusGaugeHandler) sets
  `_fieldResumeBattleSeen`. When field is free again: battle seen → `ResumeFieldAutoWalk()`
  (re-routes from current pos via CalculateAndStorePath, announces nav_autowalk_resuming);
  no battle within `FieldResumeDiscardDelay` (0.6s) of becoming free → discard.
- Discards if map changed (story warp). Pending cleared on a new explicit AutoWalkTo.
- All new code in NavigationHandler.AutoWalk.cs (Field Battle-Resume region). Relies on the
  proven fact that IsFieldFree() is false during battle (same as world map resume).
- FIX (2026-06-13, log 10:54): first test resumed correctly ("auto-walk resumed after battle")
  but a single-frame IsFieldFree flicker ~21ms later re-triggered SaveFieldResume+Cancel and the
  walk was discarded as non-battle. Root cause: field path had NO flicker tolerance (cancelled on
  the first non-free frame). Fix: added `_fieldFreeFailCount` (>10 frames) mirroring the world
  map's `_wmFieldFreeFailCount`. Brief blips now zero the stick for a frame instead of cancelling.
  RE-TESTED 2026-06-13 — resumes correctly after battle; non-battle interruptions do not resume.

**2. Bonus gauge exact percentage to screen reader (toggle, default OFF).**
New setting `ModSettings.BonusGaugePercentAnnounceEnabled`. Mod menu (F4) toggle
`mod_menu_label_gauge_percent`. When on, speaks "Gauge N." every 5% as the gauge climbs
(`gauge_percent` Loc key, SayQueued). Independent of the 25/50/75 beep cue — both can be on.
- BonusGaugeHandler.Update restructured: level/ratio now read BEFORE the sound gating so the
  spoken percentage works even when the beep volume is 0 / sound not loaded. Bails only if
  BOTH beep and percent are off.
- `_lastAnnouncedGaugeBucket` (5% bucket) tracks last spoken; seeded on battle entry (no backlog),
  reset to -1 on level change and in break postfix + Reset(). Capped below 100% (break hook owns 100%).

**Test plan:** (1) Auto-walk to a field target, walk into an enemy en route → after the battle it
should announce "Resuming walk to X" and continue; talking to an NPC mid-walk should NOT resume.
(2) Turn on "Bonus gauge percentage announcement" in F4 menu, enter battle, fill the gauge → hear
"Gauge 5/10/15..." every 5%.

**Last completed:** Traversal recording + routing + release bundling (2026-06-12)

### Dungeon Navigation — OBSERVED TRAVERSALS (2026-06-12) — WORKING

After exhausting every static approach (NavMesh = 12% coverage + fragmented;
CalcHeight FAILs in dungeons; OffMeshLink stripped from IL2CPP build; collision
raycasts can't tell walls from ramps), the reliable solution is to record where a
player ACTUALLY walks and route over that. See the detailed "PIVOT TO
OBSERVED-TRAVERSAL NAVIGATION" entry lower in this file.

- **TraversalGraph.cs:** breadcrumb every ~1m of real movement, A* routing, per-map
  JSON persistence. User tested: walked the whole of Krosse Cave; 1035+ breadcrumbs,
  94% one connected component, multi-floor. Load/save/connectivity all verified.
- **Release bundling (DONE):** recorded JSONs live in project `traversals/`, embedded
  into the DLL via `<EmbeddedResource Include="traversals\*.json" />`. TraversalGraph.Load
  uses UserData first, else the embedded copy. Verified 4 maps embedded as
  `SO2RAccess.traversals.MF_000X_01A.json`. To refresh: copy
  UserData\SO2RAccess\traversals\*.json into project traversals\ and rebuild.
  (Tiny near-empty maps MF_0005/0007 can be pruned from project traversals\ before release.)
- **Cleanup done:** deleted dead experimental files (DungeonNavGraph, DungeonGraphDiagnostics,
  OffMeshLinkSpike); island/bridge runtime calls removed from Update.
- **TODO next session:** (1) jump-prompt audio cue — ledge "press X to jump" shows via
  UIFieldIconSelector.ShowFieldIcon (UIDefine.FieldIconType); hook it. (2) Fully delete the
  island/multi-segment system (IslandScanner/IslandNavigator/NavMeshIslandDiagnostics +
  ~45 refs in NavigationHandler.AutoWalk.cs: StartMultiSegmentWalk, CheckSegmentTransition,
  GetExitIslandSet, exit-avoidance, _routeSegments). (3) Blind-exploration mode for unmapped areas.

### Field Shortcut & Specialty Sub-menus — DONE (2026-03-26)

- Train switch selector: reads party member names + ON/OFF state + "Turn all on/off" items
- Scout action menu: reads "Look for enemies" / "Avoid enemies" / "Do nothing" options
- Pickpocket field menu: reads item names + success rate percentages
- Fix: creation hook with empty dataList no longer blocks fallback polling
- Fix: _icActiveSkillCategory gates Train/Scout polls to prevent stale-state cross-interference

### Cross-Island Navigation System — IN PROGRESS (2026-06-12)

Core architecture built. Ground verification approach identified but has bugs to fix.

**What's built and working:**
- IslandScanner.cs: NavMesh scanning, BFS island grouping, gap detection
- IslandNavigator.cs: Data structures, JSON persistence, BFS route planning
- Multi-segment auto-walk: RouteSegment execution, crossing phase, segment transitions
- NavigationHandler: Deferred island scan (1.5s delay), bridge recording polling at 4Hz
- Island-aware filtering in Build.cs, 10 Loc keys, F11 field map diagnostics
- Ground verification via Physics.RaycastAll + surface normal check

**Bug fixes applied (2026-06-12) — PENDING IN-GAME TEST:**
1. [FIXED] Raycast hits wrong floor: VerifyGapsWithGround now raycasts from
   `expectedY + offset` where expectedY = Lerp(ptA.y, ptB.y, t), and picks the
   ground hit CLOSEST to expectedY (not the highest). Upper-floor overhangs are
   no longer selected over the ramp. (IslandScanner.cs)
2. [FIXED] Bridge deduplication: MergeBridges now seeds a HashSet of the fresh
   scan's island pairs (unordered, via NormalizePair) and skips any cached bridge
   whose pair already exists. Bridges no longer grow on scene reload. (IslandNavigator.cs)
3. [FIXED] MaxGroundStepY raised 3f -> 5f for gradual ramps. (IslandScanner.cs)
4. [FIXED — REVISED] Player warps to world map mid-walk. LOG ANALYSIS (11:34:39,
   route to "Opened chest 3", 3 segments): warp happened during the NORMAL
   segment-0 walk, NOT the crossing phase. Segment-0 path ran from (-8,46) to a
   bridge edge at (0,0.1,7.6), passing the EXPEL town gate at ~(0,~12) on the way.
   Two-part fix:
   (a) ROOT CAUSE — route planning now avoids passing THROUGH islands that contain
       a map exit. GetExitIslandSet() maps each FieldMapjumpCollision to its island;
       PlanRoute/BfsIslandPath skip those as transit (still allowed as final dest).
       Falls back to allowing them only if no safe route exists. (IslandNavigator.cs)
   (b) SAFETY NET — exit-zone steering (AvoidExitZones) now applied to ALL auto-walk
       movement during a cross-island route (normal segment walk + obstacle detour),
       not just the crossing phase. Steering is now TANGENTIAL (arcs around the gate)
       instead of pure repulsion, so a head-on approach slips past instead of stalling.
       (NavigationHandler.cs/.AutoWalk.cs)
   Cache cleared for single-target walks so normal walks are unaffected.
5. [WON'T FIX — limitation] FieldStairs empty on Krosse Cave — ramps are plain
   geometry, not game objects. Ground verification (fix #1) is the universal bridge
   source, so FieldStairs is not needed. Diagnostic logging retained for reference.

**ROOT CAUSE FOUND (2026-06-12, log 11:43) — island routing was firing when it
shouldn't.** Krosse Cave scans into 16 flood-fill islands; bridges connect only
{0,1,4,6,7,11,13}. Player spawns on island 1 (entrance sliver); its ONLY bridge is
1<->0 whose crossing point (0,7.6) is on the far side of the EXPEL gate. So every
route off island 1 walks south through the gate. Chest 1 is on island 3, which has
NO bridge -> false "unreachable" even though the chest was physically opened.
KEY INSIGHT: flood-fill island IDs are FINER than real NavMesh connectivity. A
single walkable floor gets split into many islands, so "different island" does NOT
mean "needs multi-segment routing."
FIX: AutoWalkTo now calls HasCompleteNavMeshPath(player, target) BEFORE island
routing. If NavMesh returns a COMPLETE path, it does a normal single-path walk and
skips island routing entirely (logs "NavMesh path is COMPLETE"). Island routing now
only fires for genuine NavMesh disconnection (partial/invalid path — separate floors
via ramps). This fixes both symptoms: chest 1 no longer false-unreachable, and far
chests route directly (deeper into the cave) instead of south through the entrance.
(NavigationHandler.AutoWalk.cs)
NOTE: if a chest is genuinely on a partial-path floor AND its island has no bridge,
it can still be unreachable — that's a real bridge-gap to address separately, but the
common case (complete NavMesh path) is now handled. Watch the F12 log for "COMPLETE".

**FALSE-ARRIVAL FIX (2026-06-12, log 11:56) — the actual user-facing bug.**
Log proved every false "Arrived" came from arrival being announced against the
WRONG position: e.g. "Arrived at Unopened chest 2" with ARRIVAL DIAG distXZ=1.20
to target (17.88,120.64) — a multi-segment bridge waypoint — while the real chest
was 81m away. Also "Arrived near ... target is above/below you" fired with
playerY==targetY (identical) from StartMultiSegmentWalk hardcoding
_autoWalkDifferentFloor=true.
FIX — single arrival authority: new IsAtRealTarget(playerPos) checks the REAL
final target (_routeFinalTarget for multi-segment, else _autoWalkTarget) in full
3D — within arrival radius horizontally AND |vertGap| <= 2.0m. All three
announcement sites now gated through it:
  1. Proximity arrival: only fires when _routeSegments==null AND on the target's
     level (never on a bridge waypoint, never from another floor).
  2. Path-exhausted: announces real arrival only if IsAtRealTarget; otherwise an
     honest "Could not reach {0}. Stopped/above/below ..." message.
  3. Different-floor partial handler: same — never says "Arrived", says
     "Could not reach ... above/below" instead.
  4. Multi-segment segment-path exhaustion now hands off to CheckSegmentTransition
     (crossing) instead of leaking into the normal arrival handlers.
New Loc keys: nav_autowalk_cannot_reach[_above|_below]. Result: the mod can no
longer claim arrival unless the player is genuinely at the real chest.
(NavigationHandler.cs, Loc.cs)

**WALK-OUT ROOT CAUSE + HARD EXIT BARRIER (2026-06-12, log 12:29).**
Mechanism: player's NavMesh island is huge (cave deep z=86 down to entrance z=7.6).
For an un-navigated chest (no complete path), island routing picks a bridge whose
crossing point is (0,0.1,7.6) — PAST the exit gate (~z=10.5). Segment-0's NavMesh
path therefore runs from deep cave straight down through the gate → Overworld.
Exit-zone steering can't help (the bridge destination is on the far side of the gate);
"avoid exit island 1" can't help (the bridge belongs to island 0, gate sits between 0/1).
Only inferred-bridge routes do this; complete-path direct walks go deep correctly.
FIX (hard barrier): PathCrossesMapExit(corners) samples every NavMesh path densely and
tests against all FieldMapjumpCollision bounds (+0.5m). CalculateAndStorePath now rejects
any path that crosses a map exit UNLESS the target itself is an exit (_autoWalkAllowExit,
set from IsExitCategory). Rejected routes announce nav_autowalk_route_exits ("Cannot reach
{0} without leaving the area"). Centralized in CalculateAndStorePath so it covers single
+ all multi-segment paths. (NavigationHandler.cs/.AutoWalk.cs, Loc.cs)
CONSEQUENCE: chests whose only inferred route is through the gate now say "cannot reach
without leaving the area" instead of walking the player out. Honest, but does NOT yet make
them reachable — that needs reliable OBSERVED-crossing bridges (next step, see
feedback-navigation-reliability memory + confirmed/unconfirmed design).

**NAVMESH FRAGMENTATION / SNAP-TO-SLIVER FIX (2026-06-12, log 13:05).**
Smoking gun: standing at (0.5,1.1,18.5) the mod found a 59-waypoint COMPLETE path that
runs THROUGH the player's later z=40 spot and goes deep to (108.6,8.3,82.2) — so the main
dungeon IS one connected, auto-walkable NavMesh. Yet standing at (-2,1.5,40) the same deep
chests returned "no complete path" → island routing → refused at the gate (every unopened
chest, save, event). Root cause: the dungeon NavMesh is fragmented into slivers, and the
player's exact footing sometimes snaps onto a tiny disconnected sliver ~1-2m off the main
floor; from there CalculatePath can't see the connection that obviously exists.
FIX: TryFindCompletePath probes ~13 nearby points (same floor, ≤3m) and accepts the first
snap that yields a COMPLETE path. HasCompleteNavMeshPath and CalculateAndStorePath both use
it — preferring a complete path, falling back to partial only when allowed. Logs "complete
path found via probe offset ..." when a sliver snap was rescued. Still 100% reliable (requires
a genuine complete path); turns many false "no route" cases into real deep walks that never
go through the gate. (NavigationHandler.AutoWalk.cs)
EXPECTATION: most chests on the connected main mesh should now route directly and correctly.
Truly disconnected chests (separate NavMesh joined only by un-baked ramps) will still need the
observed-bridge approach; those remain "cannot reach without leaving the area".

**NEW DIRECTION — CUSTOM COLLISION WALKABILITY MAP (2026-06-12, plan approved).**
Root cause accepted: the baked NavMesh is fragmented and unfixable at runtime
(NavMesh.AddLink/bake are IL2CPP stubs). The player actually moves by collision
physics (Il2CppCommon.Physics2), so the TRUE walkable space is the collision
geometry (ramps the NavMesh omits). Plan: build our OWN walkability graph from
downward collision raycasts → flood-fill components → reachability + A* paths that
follow real ramps and never route through the exit gate. Replaces island/bridge
system for field maps; world map unchanged. Full plan:
C:\Users\Jaco\.claude\plans\woolly-frolicking-cray.md (Phases 0-5).
Two design pillars (from our own past notes): do NOT use CalcHeight on dungeon
floors (use raw Physics.RaycastAll + normal.y>=0.4); do NOT trust wall masks
(connect cells by FLOOR CONTINUITY / step-delta, not wall-presence).

**PHASE 0 (validation diagnostic) — BUILT, AWAITING IN-GAME TEST.**
New file DungeonGraphDiagnostics.cs. Wired to F11 (debug mode) on field maps,
alongside the existing island diagnostic. Builds the collision walkability graph
in-memory (1.5m cells, RaycastAll-down, 8-neighbour step-delta connect, flood-fill),
marks map-exit nodes, then logs for EVERY treasure chest: reachable (same component
as player) / component / snapY / hops / viaExit. Also logs RaycastAll-vs-CalcHeight
primitive test and an OverlapSphere wall-mask probe. Changes NOTHING in live nav.
GO/NO-GO GATE: do the previously-"unreachable" UNOPENED chests log reachable=true
with viaExit=false? If yes → build Phases 1-5. If no → they're truly disconnected.
HOW TO TEST: enter Krosse Cave, ensure debug on (F12), press F11, send the log
lines tagged [DUNGEONGRAPH].

**PHASE 0 RESULT — PASSED DECISIVELY (2026-06-12, log 16:02).** Krosse Cave: graph
built 31612 nodes / 184 components / 240ms. ALL 14 chests (incl. all 9 unopened ones
the NavMesh called unreachable) reported reachable=True, comp=1 (= player), viaExit=False,
snapY matching targetY within 0.1m. CalcHeight=FAIL everywhere (confirms raw-RaycastAll
choice). Approach proven.

**PHASES 1+2+4 — BUILT & INTEGRATED, AWAITING IN-GAME TEST.**
- DungeonNavGraph.cs (NEW): collision walkability graph — Build (coarse-AABB then 1.5m
  RaycastAll fine grid, 8-neighbour step-delta connect, flood-fill components, exit-node
  flags), SnapToNode (Y-weighted), IsReachable (same component), FindPath (A* with binary
  min-heap, excludes exit nodes unless target is an exit) → Vector3[] corners.
- Integration (NavigationHandler.cs/.AutoWalk.cs/.Build.cs): _dungeonGraph field; deferred
  Build in CheckDeferredIslandScan (on map load); UseDungeonGraph() predicate
  (!worldmap && graph ready). When active it supersedes island routing: IsReachable (nav
  list filter + pre-walk), AutoWalkTo (island block guarded off), CalculateAndStorePath
  (new DungeonCalculateAndStorePath via graph A*). NavMesh/island kept as fallback only.
- Old island/bridge/multi-segment code still PRESENT but bypassed — retire in Phase 5
  after this is verified. Disk caching (Phase 3) deferred — 240ms build is acceptable.
HOW TO TEST: restart game, enter Krosse Cave, wait ~2s for "DUNGEONGRAPH: built" in log,
open nav list, pick a previously-unreachable UNOPENED chest, auto-walk. Expect it to
follow real ramps to the chest and announce real arrival (no walk-out, no false arrival).
Watch for the player clipping/sticking on walls (the one known risk of continuity-only
connection — would need a wall-refinement pass).

**PIVOT TO OBSERVED-TRAVERSAL NAVIGATION (2026-06-12) — collision graph retired.**
Why: exhaustive iteration proved NO static walkability source is reliable in dungeons —
NavMesh covers only 12% of walkable floor AND is fragmented; CalcHeight FAILs in dungeons;
OffMeshLink is stripped from the IL2CPP build (can't extend NavMesh); collision raycasts
can't distinguish walls from ramps/wall-tops (every heuristic rammed walls or over-segmented).
The ONLY 100%-reliable walkability signal is the player's ACTUAL movement (physics).
NEW SYSTEM (TraversalGraph.cs): records a breadcrumb every ~1m as the player walks a field
map (manual or auto), links consecutive + nearby breadcrumbs into a graph, persists per map
to UserData/SO2RAccess/traversals/{mapId}.json. Auto-walk/reachability route over breadcrumbs
(A*) — guaranteed walkable because a real player walked them. No raycasts, no wall guessing.
INTEGRATION: NavigationHandler records in Update (CheckTraversalRecording, gated on IsFieldFree;
BreakTrail on cutscene/menu; autosave every 10s + on map change via StartMap). IsReachable =
complete NavMesh path (towns) OR traversal-connected (dungeons). CalculateAndStorePath = complete
NavMesh path, else traversal A* path, else partial (counters). Island/multi-segment + collision
DungeonNavGraph fully bypassed (files kept, unused — delete later). F11 = LogTraversalDiagnostic
(breadcrumb count + per-chest navMeshComplete/traversal reachability).
TEST PLAN (user): sighted player walks the whole dungeon (records breadcrumbs, autosaves),
then load save + auto-walk over the recorded routes. Caveat: only reaches where the sighted
player actually walked; player must start near a breadcrumb (<6m).
NEXT (future): true blind-exploration mode for discovering unmapped areas.

**Build:** Succeeds (0 warnings, 0 errors), deployed to Mods folder.

**Key findings (documented for next session):**
- Krosse Cave (MF_0008_01A): 16 islands, 9 significant, single scene, no internal triggers
- CalcHeight does NOT work on dungeon floors (world-map-specific)
- GameRenderManager.LayerMaskHeight too restrictive — misses ramp geometry
- Physics.RaycastAll with normal.y>0.4 works but needs floor-aware origin height
- NavMesh not ready on scene load — 1.5s deferred scan required
- FindIsland via CalculatePath to island centers works (bounding box was unreliable)

**Files:** IslandScanner.cs, IslandNavigator.cs, NavMeshIslandDiagnostics.cs (new);
NavigationHandler.cs/.AutoWalk.cs/.Build.cs, Loc.cs, Main.cs (modified)

### Quick Heal Menu (D-pad Right) — v1 TESTED OK; v2 fixes BUILT, AWAITING TEST (2026-06-14)

**v1 test (log 09:03):** WORKED — open heading, Yes/No cursor, and NumPad 0 party status
all fired correctly. Three issues found + fixed in v2:
1. Party status read empty names (", HP 899 of 1039...") — `label` is empty on the status
   data. v2 resolves the name from `playerID` via
   `ParameterManager.Instance.UserParameter.GetCharacterParameter(playerID).CharacterName`
   (same path as CampMenuHandler.Formation), title-case enum fallback.
2. "recovering N" was wrong — `changeHp`/`changeMp` are the PROJECTED post-recovery TOTAL,
   not a delta (changeHp == hpMax for all; healers' changeMp < mp because they SPEND MP
   casting). v2 reports the real gain `changeHp - hp` (and MP only when changeMp > mp).
3. Heal result was SILENT (user confirmed). v2 adds a result announcement.

**v2 result announcement:** Harmony postfix on `GameManager.QuickRecovery(List<...Order>)`
([CallerCount(2)], the execution point — the menu's OnDecision is native-only and
un-hookable). Postfix sets a static flag; Update consumes it (even after the menu closed)
and announces from a per-frame projected-outcome snapshot: "Recovery complete. {name} HP
now {changeHp}. {healer} used {mp-changeMp} MP." Recovery is spell/MP-based (no items —
`QuickRecoveryUser.recoverySpellList` + `consumeMentalPoint`), so the healers (changeMp <
mp) are the "what was used". Gated to a snapshot taken within 2s so the shared camp quick
recovery does NOT trigger the field result.

**v2 files:** QuickRecoveryHandler.cs (rewritten — name resolution, accurate amounts,
snapshot, ApplyPatches + postfix); Main.cs (ApplyPatches wiring); Loc.cs (3 result keys).
Build 0/0, deployed.

**Party status key:** NumPad 0 (keyboard) OR L3 / left-stick click (gamepad). L3 requires
L1 NOT held so it doesn't clash with the L1+L3 mod-menu toggle. Read via Gamepad.current
in the handler, gated on the menu being active.

PENDING v2 TEST: (1) party status now reads names + correct recovery amounts; (2) confirm
a heal (press Yes) → expect "Recovery complete. ..." Look for the log line
`QuickRecovery: GameManager.QuickRecovery executed.` — if it's ABSENT after a confirmed
heal, the method fires native-only (like PlayVoice) and we need a different result trigger.

New `QuickRecoveryHandler.cs` reads the field Quick Recovery overlay
(`UIFieldQuickRecoverySelector`), opened by pressing Right on the D-pad. The game owns
the key; the handler only detects the overlay and reads it (pickpocket pattern — pure
polling, no Harmony patches).

- **Detection:** `FindObjectOfType<UIFieldQuickRecoverySelector>()` (throttled 1/s when
  null); active gate = `gameObject.activeInHierarchy == true && recoveryDataList.Count > 0`
  (the field overlays stay activeInHierarchy=true when hidden — data-count gate, same as
  pickpocket). If still stale in the log, fall back to camp-style gating.
- **On open (brief, per user):** announces `"Quick Recovery. Recover party? Yes. Press
  NumPad 0 for party status."` (skips first frame to avoid stale blurt).
- **Cursor:** polls `currentChoice` (UIDefine.DialogChoices None/Yes/No/Cancel) — the only
  managed-readable navigable state. Announces "Yes"/"No" on change. Navigation is
  native-only (OnUp/OnDown CallerCount 0), hence polling.
- **On-demand party status:** NumPad 0 (free; 1/2/4/5/6/8 are nav/pause) reads each member
  from `recoveryDataList`: name, HP x of max (+ "recovering N" when `changeHp>0`), MP same.
  Full-health members read as "{name}, full health". Read via
  `Keyboard.current[Key.Numpad0]` inside the handler, gated on the menu being active.
- **Result after Yes:** NOT in v1 (per user — test first). The corner-toast
  `NotificationHandler` may already announce HP gains / item use. If silent, add a readout
  from `QuickRecoveryResult` (quickRecoveryTargetList/quickRecoveryUserList) as follow-up.
- **Files:** QuickRecoveryHandler.cs (new); Main.cs (field + init + Update, mirrors
  pickpocket near line 968); Loc.cs (8 `quickheal_*` keys). Build 0/0, deployed.
- **Baked debug logging:** open frame logs choice/member-count/isPause/isDisableInput;
  choice changes + party reads logged under [STATE].

PENDING USER TEST (F12 debug on, on a field, press D-pad Right):
1. Open announces the heading. 2. Up/down → "Yes"/"No" on each change. 3. NumPad 0 reads
party HP/MP. 4. Confirm Yes — note whether the result is already spoken (corner toast) or
silent. 5. Cancel/close → quiet; reopen re-announces cleanly (no stale blurt).
6. Send the [STATE] `QuickRecovery:` log lines — confirms currentChoice behaviour,
recoveryDataList contents, and that there's no hidden native-only character-select cursor.

KNOWN UNCERTAINTY: if the menu has a per-character selection cursor, its index may be
native-only (like the guild wall); Yes/No + on-demand party status still make it usable.
Not expected from the decompile.

### World Map Cached Grid System — WORKING (resolved 2026-06-12)

Grid format WMGH. Salva↔Krosse routing issue resolved by user during break.
Full investigation record in docs/worldmap-pathfinding.md and memory file worldmap-navigation.md.

### Fishing Accessibility (2026-03-18) — WORKING

- **What works (tested 2026-03-18):**
  - Fishing spots appear in Interactables nav category via `FindObjectsOfType<FieldFishingWaterPlace>()`
  - Auto-walk navigates player to the water's edge and arrives close enough to interact
  - Player faces the water on arrival via FacePosition (collider center, separate from walk target)
  - Catch result announcements: Harmony postfix on `UIFieldFishingResultPresenter.Set()` (CallerCount 1)
    announces "Caught: [fish name], [size], [new record/max size/new]." — deduped (game calls Set ~19x per catch)
  - "Fish got away" already caught by existing dialogue system
  - Game's built-in audio/vibration cues are sufficient for the minigame itself (no custom cues needed)
  - User completed Fishing Mission 1 successfully
- **Previous bugs fixed (2026-03-18):**
  - **Arrival too far:** LiveTransform tracked collider center (in water, off NavMesh), making arrival
    distance ~2m instead of using the NavMesh walk target. Fix: FacePosition field on NavItem stores
    water center for facing only; LiveTransform left null so arrival uses static Position.
  - **Catch result spam:** Set() hook fired ~19 times per catch. Fix: dedup guard (same text + 2s window).
- **Files:**
  - `NavigationHandler.Build.cs` — `BuildFishingSpots()` method
  - `NavigationHandler.cs` — `BuildFishingSpots()` call in scan, fishing result hook in ApplyPatches
  - `NavigationHandler.Patches.cs` — `FishingResultSet_Postfix()` for catch announcements
  - `Loc.cs` — 6 keys: nav_fishing, nav_fishing_n, fish_caught, fish_new_record, fish_new, fish_max_size

### Item Creation Sub-screen (2026-03-19) — CONFIRMED WORKING (2026-03-20)

- **What works (confirmed 2026-03-20):**
  - Skill selection: skill name, description, level, tab switching — all working
  - Action list: category name, creation hook, character tab — working
  - "????" item names: fixed, now says "Unknown" (SanitizeItemName helper)
  - Create mode: after selecting a material (e.g. Silver), announces "Create [count].
    Success rate: [X] percent." Count changes announced as user adjusts with D-pad.
    Detection via `actionPresenter.currentCreateCount` (-1 = inactive, >0 = Create visible).
  - Result screen: fully working — item name, success/failure, position
  - Stale suppression: all IC sub-screens (skill, action, result) properly seed
    LastIndex and tab values on camp open. Scrolling past IC in root menu is silent.
  - **Field shortcut IC (D-pad Down on field):** fully working (2026-03-19).
    Game reuses `UICampWindow` with `OpenCampState=SelectSpecialSkill`.
    Detected via `UICampWindow.OpenCampState` property in Open postfix.
    `_isFieldShortcutIC` flag + `IsICActive()` helper unlocks all 4 IC gates
    (polling + 3 hooks). Announces "IC Specialty." on open. Flag cleared on
    window close, skill selector hidden, or root menu activation.
  - **Result announcement fix (2026-03-19):** single-item results at index 0
    were not announced due to stale seed. Fixed by resetting result index on
    create mode exit with 1.5s delay to sync with result animation.
- **Material selection screen** (`UICampSpecialSkillAddMaterialSelector`) — ✅ DONE,
  user confirmed 2026-07-11 it was tested and working long ago. (Historical notes:
  sub-selectors have stale `activeInHierarchy=true`; the `Set` hook (CallerCount 1)
  does not fire — native-only call.) Item Creation is fully accessible; no remaining
  IC work.
- **Files:**
  - `CampMenuHandler.ItemCreation.cs` — all IC logic (skill, action, create mode, result, field shortcut flag)
  - `CampMenuHandler.cs` — selector caching in Open postfix, shortcut detection, 3 Harmony patches, Update call
  - `Loc.cs` — 18 localization keys (ic_screen, ic_shortcut_screen, ic_tab_*, ic_skill_*, ic_action_*, ic_result_*, ic_unknown_item)

### Skill Development Screen Fix (2026-03-19) — CONFIRMED WORKING (2026-03-20)

- **Bug:** Specialty skills (Scouting, Familiar, Art, etc.) showed wrong SP cost and max level.
  - SP cost frozen at initial value (e.g. always "1" for Scouting, even when actual cost was 20+)
  - Max level flag stale — some specialties showed no cost (implying max) when still levelable
  - Knowledge skills (Determination, Biology, etc.) were correct — game refreshes their data
- **Root cause:** `itemDataList` on `UICampSkillSelector` is stale for specialties after leveling.
  The game updates the visual display but doesn't refresh the data objects for specialties.
  `consumeSP` and `isLevelMax` stayed frozen at list-build-time values.
- **Fix** (in `CampMenuHandler.Formation.cs`, `SkillInfoPresenter_Set_Postfix`):
  - Specialties: calls `UICommon.CalcNeedSpecialSkillForLevelUp(charaParam, specialSkillID)` fresh
    each time the hook fires. Sums `consumeSP` from returned list for total cost. Empty list = max.
  - Knowledge skills: still uses `itemData.consumeSP` (reliable), but verifies `isLevelMax` against
    `ConstSkillParameter.levelupSp.Count` instead of trusting the stale flag.
  - Gets current character via `_skillSelector` → `UICharacterTabListSelectorBase.currentPlayerID`
    → `ParameterManager.Instance.UserParameter.GetCharacterParameter()`
- **Files changed:** `CampMenuHandler.Formation.cs` (postfix logic), `CampMenuHandler.cs` (5 new RuntimeHelpers)
- **Build:** Succeeds, deployed to Mods folder
- **Tested (2026-03-20):** All confirmed working — SP costs accurate, reads correctly.

### Camp Quest & Mission Lists (2026-03-16) — COMPLETE

- **Quests** (camp → Quests and Missions → Quests):
  - Polls UIQuestSelector (UIListSelectorBase) for cursor + data
  - Announces: quest name, status (Available/In progress/Ready to report/Completed), position
  - New quests marked with "New"
  - Confirm press reads full description (title + description + rewards)
  - Hook: GameUIManager.OpenQuestWindow captures UIQuestWindow reference
- **Missions** (camp → Quests and Missions → Missions):
  - Polls UIMissionListSelector (UIListSelectorBase) for cursor + data
  - Announces: mission name, status (Complete/Incomplete/In progress/etc.), position
  - Category changes (Beginner/Expert/Specialist/Legend) announced on switch
  - Hook: GameUIManager.OpenMissionWindow captures UIMissionWindow (camp-only)
- **Guild handler fix:** Skips detection when camp is open (IsCampOpen guard)
  to prevent false "Guild." announcements on camp quest/mission screens
- **Files:** CampMenuHandler.Quest.cs, CampMenuHandler.Mission.cs, GuildHandler.cs, Loc.cs

### Guild Mission Menu (2026-03-15 → 2026-03-16) — NATIVE CODE WALL

- **What works:**
  - Window open/close detection via gameObject.activeInHierarchy
  - "Guild." announced on open
  - Dialog system catches "Mission accepted.", provisions, "There are no more missions"
- **What's blocked — EXHAUSTIVELY TESTED (2026-03-16):**
  The entire guild UI operates in native C++ that is invisible to managed code.
  Every approach below was tested with diagnostic dumps across full guild sessions:
  - currentDataList: always empty (0 items)
  - currentIndex: stuck at 0, never changes when user navigates
  - windowState: stuck at None, never transitions to List
  - FindObjectsOfTypeAll<UIMissionListItemPresenter>: found 14, ALL with empty text/state
  - All 59 TMPro components: only template/placeholder text, never updated
  - GetParsedText() internal buffer: same as .text (no hidden data)
  - textInfo.characterCount: 0 on all components
  - informationSelector.missionName: Japanese placeholder "ミッション名" only
  - ParameterManager bypass: shows 93 missions when guild shows 4 (wrong filtering)
  - Mission name text keys (MISSION_023 etc.): don't resolve via TextManager
  The game renders mission text through a native pipeline that bypasses Unity's
  managed TextMeshPro entirely — text is drawn on screen but never written to
  any managed field.
- **Current state:** GuildHandler detects window open/close, announces "Guild.",
  and relies on dialogue system for accept/provisions. Individual mission names
  and cursor tracking are not possible from managed code.
- **Files:** GuildHandler.cs, Main.cs, Loc.cs (guild_screen only)

### NavMesh Partial Path Progress Fix (2026-03-15) — TESTED OK (2026-03-16)

- **Problem:** The Krosse Guild entrance was filtered as unreachable from the upper level.
  NavMesh surfaces are disconnected (PathPartial) but Y difference is only ~1.0m — below
  the 2.0m FloorChangeThreshold, so the "different floor" exception didn't apply.
- **Fix:** IsReachable now accepts PathPartial when the partial path endpoint gets at least
  30% closer to the target than the player's start position. This catches disconnected
  NavMesh surfaces regardless of Y difference, without showing truly unreachable targets.
- **Also:** AutoWalkTo now always allows partial paths (allowPartial: true) since
  IsReachable already filtered out truly unreachable targets.
- **Test:** Confirmed working — Krosse Guild appears from upper town, auto-walk reaches it.

### Dialogue Choice Menu Fixes (2026-03-15) — Tested and confirmed

1. **Stale index on open:** 1-frame defer lets game reset selectChoiceIndex before announcing.
2. **Opening heading:** Menu now announces "Choice, N items." followed by the initial item
   in a single combined string (no double-read from screen reader interruption).
3. **Correct item count:** Uses `choiceMessageIDList.Count` (actual active choices) instead of
   `MaxChoiceIndex` (pre-allocated presenter slots). Was showing "X of 9" for 2-item menus.

### Auto-Walk Bug Fixes (2026-03-15) — Tested and confirmed

1. **Obstacle avoidance NavMesh sampling fix:** `TryStartObstacleAvoidance` now samples
   `_autoWalkTarget` onto NavMesh before checking detour paths. Previously, different-floor
   targets caused `PathInvalid` for all detour candidates (raw Y=8.8 wasn't on NavMesh surface).
2. **IsReachable accepts partial paths for different floors:** Targets on different floors
   (connected by stairs) get `PathPartial` — now accepted instead of being filtered from nav list.
3. **Chest IsAcquired fix:** Switched from `chest.isAcquired` (backing field, stale at distance)
   to `chest.IsAcquired` (property, calls native getter). Also fixed numbering loop using
   `StartsWith` instead of exact `==` so floor-suffixed labels ("Opened chest (above)") are
   correctly recognized as opened.
4. **Interactable arrival radius:** Added `InteractableArrivalRadius = 1.3f` for chests, save
   points, and interactables. Previously used 1.8f (NPC radius) — too far for chest interaction.
5. **Stuck loop prevention:** Max 3 obstacle avoidance attempts before cancelling with
   "Path blocked" message. Avoidance counter no longer resets on "progress" (detour movement
   counted as progress, causing infinite loops when path was truly blocked by guards).
6. **Quest marker filtering:** Discovered location points filtered by `effectComponent == null`
   (sparkle removed after discovery). `IsEnd` and `isEnd` properties don't work for this.
7. **Diagnostic cleanup:** Removed verbose NAV DIAG per-frame logging and marker diagnostic fields.

### Dialogue Choice Menu Stale Index Fix (2026-03-15) — Pending test

`selectChoiceIndex` returned a stale value from the previous menu on the activation frame,
causing the wrong item to be announced on open. Fix: defer `ActivateChoiceMenu` by one frame
after the presenter becomes visible, letting the game reset the index to 0 first.
Same one-frame deferral pattern used in dialogue voice detection.

### Auto-Walk Overhaul — Summary of Changes (2026-03-10)

**Core change:** Replaced `transform.position` direct movement with `GetLeftStick()` postfix
input injection. The game's own movement pipeline now handles physics, colliders, animations,
triggers, party AI, and terrain — all naturally.

**What was done:**
1. **GetLeftStick postfix** (NavigationHandler.Patches.cs): Harmony postfix on
   `GameInputManager.GetLeftStick()` overrides stick input with synthetic direction
   toward current waypoint. `WorldDirToCameraStick()` converts world-space direction
   to camera-relative stick coordinates.
2. **Removed old workarounds:** PlayMoveAnimation prefix, CacheEventTriggers/CheckEventTriggers,
   TryEnterFieldExit, InteractDist snapping, manual transform.rotation, Y interpolation,
   _staticIsApproaching field, CachedEventTrigger struct, DirectWalkMaxDistance constant.
3. **Counter NPC detection fix** (NavigationHandler.Build.cs): NPCs with contactDistance >= 1.0
   are now flagged as counter NPCs (skip reachability filter, use partial path). This fixes
   the castle receptionist (WARRIOR1b, contactDistance=1.50, type=NORMAL) disappearing
   from the nav list.
4. **Pre-walk path validation** (NavigationHandler.AutoWalk.cs): Before walking, SphereCast
   validates every segment of the NavMesh path against actual physics colliders. If a segment
   is blocked, a temporary NavMeshObstacle is placed at the midpoint and the path is
   recalculated (up to 4 attempts). All obstacles stay during retries so each recalculation
   routes around ALL found barriers. Obstacles are destroyed after path is accepted.
5. **WaypointArrivalThreshold** increased from 0.3 to 0.8 for physics-based movement.
6. **World map unchanged** — still uses transform.position (different physics model).

**Files modified:**
- `NavigationHandler.Patches.cs` — GetLeftStick postfix, removed PlayMoveAnimation prefix
- `NavigationHandler.AutoWalk.cs` — WorldDirToCameraStick(), path validation with SphereCast,
  StopAutoWalk() helper, removed CacheEventTriggers/CheckEventTriggers/TryEnterFieldExit
- `NavigationHandler.cs` — Update() uses stick injection, simplified arrival, new ApplyPatches
- `NavigationHandler.Build.cs` — contactDistance-based counter NPC detection

**Pending tests (user will test 2026-03-11):**
- [ ] Basic NPC auto-walk (run toward NPC, proper animation/footsteps)
- [ ] Wall collision (player stops at walls, doesn't clip through)
- [ ] Door interaction (stops at closed doors like Krosse Castle guard gate)
- [ ] Event triggers fire naturally (story triggers, PA triggers)
- [ ] Map exits trigger naturally (building entrances, town gates)
- [ ] Counter NPCs (receptionist appears in list, walks to counter edge)
- [ ] Path validation rerouting (Krosse town → castle should find clear path)
- [ ] Moving NPCs (path recalculation, arrival)
- [ ] Stuck detection still works
- [ ] Party members follow naturally
- [ ] World map auto-walk still works
- [ ] Cancel auto-walk (NumPad 5 / L1)
- [ ] Gamepad auto-walk (L1 + LStick)

**Known issue from first test:**
- King/Soldier in Krosse Castle unreachable — this is correct behavior (guard blocks
  corridor until receptionist grants audience). Not a bug.
- Krosse town → castle path initially went through a dead-end area where NavMesh and
  game colliders disagreed. Path validation (SphereCast + NavMeshObstacle rerouting)
  was added to fix this. Needs re-testing.

### SphereCast Removal (2026-03-15)

**Problem:** LayerMaskWall fix (2026-03-13) still caused widespread false "Cannot reach" errors.
Testing showed two types of colliders blocking valid paths:
- Layer 15 (`collider`) — invisible collision volumes throughout scenes, player walks through fine
- Layer 22 (`Col_Obstacle_Col*`) — named "obstacle" but not actually impassable

Both layers are included in GameRenderManager.LayerMaskWall but do not block player movement.
NavMesh paths are inherently walkable — SphereCast validation was redundant and harmful.

**Fix:** Removed SphereCast path validation entirely. CalculateAndStorePath now trusts the
NavMesh path directly. Stuck detection (2-second timer, recalculates from current position)
remains as the safety net for genuine obstacles encountered at runtime.

**Removed code:**
- `FindBlockedSegment()`, `GetSegmentMidpoint()`, `CreateTempNavMeshObstacle()` methods
- `MaxPathValidationAttempts`, `PathValidationRadius` constants
- `_wallLayerMask`, `_wallLayerMaskResolved`, `GetWallLayerMask()` from NavigationHandler.cs
- Wall mask cache reset in `CheckFieldmapChange()`

**Files modified:**
- `NavigationHandler.AutoWalk.cs` — simplified CalculateAndStorePath, removed validation methods
- `NavigationHandler.cs` — removed wall mask fields/method/reset

**Pending tests (user will test):**
- [ ] Auto-walk to nearby NPC — should no longer say "Cannot reach"
- [ ] Auto-walk to building entrances (Inn, Church, etc.) — should work
- [ ] Auto-walk to distant exits (Krosse Castle gate) — should work
- [ ] Stuck detection still triggers if player gets physically blocked
- [ ] Indoor areas still navigable
- [ ] Previous test checklist items from 2026-03-10 also still apply
- [ ] **NEW: Obstacle avoidance** — if auto-walk gets blocked (e.g. enemy in path), it should try walking around instead of giving up. Walk toward an enemy-blocked path to test.
- [ ] **NEW: Camera follow** — camera should gently rotate to face walking direction during auto-walk. If camera rotates the WRONG way (away from path), report it — sign flip needed.
- [ ] Camera follow should NOT affect world map auto-walk (world map has fixed camera)

## Codebase Analysis Progress

### GATE: Tier 1 MUST be complete before Phase 2 (Framework)!

- [x] 1.1 Structure overview (namespaces, singletons) → documented in game-api.md
- [x] 1.2 Input system — ALL game key bindings documented in game-api.md "Game Key Bindings"
- [x] 1.2 Input system — Safe mod keys identified and listed in game-api.md "Safe Mod Keys"
- [x] 1.3 UI system (base classes, text access patterns — TextMeshPro, UIPresenterBase)
- [x] 1.4 State management — singleton pattern, task-based input architecture documented
- [x] 1.5 Localization: English only — SKIPPED (single language)

### GATE: Relevant Tier 2 items MUST be done before implementing each feature!

- [ ] 1.6 Game mechanics (analyzed as needed per feature)
- [ ] 1.7 Status/feedback systems
- [ ] 1.8 Event system / Harmony patch points
- [ ] 1.9 Results documented in `docs/game-api.md`
- [ ] 1.10 Tutorial analysis (when relevant)

## Game Key Bindings (Original)

<!-- CRITICAL: Fill this during Tier 1 analysis! Every key the game uses.
Without this list, mod keys WILL conflict with game controls. -->

- (not yet documented — MUST be done before Phase 2)

## Implemented Features

- **Config menu announcements** (`ConfigMenuHandler.cs`)
  - "Config, N of Total: Category" when the config category menu opens or focus moves
  - "[Setting]: [Value], N of Total" when a submenu opens or focus moves
  - "[Value]" announced alone when left/right adjusts a setting value
  - Unavailable/button-only items announced without value
  - Label source: Strategy 2 — GameText inside each selector's own hierarchy (not sibling walk)

- **Title menu announcements** (`TitleMenuHandler.cs`)
  - "Press any button to start" when the title screen appears
  - "[Item]" when the menu opens or focus moves (no prefix)
  - Adds ", unavailable" for greyed-out items (e.g. Load Game with no save)

- **Gamepad binding menu announcements** (`GamepadMenuHandler.cs`)
  - "[Action]: [Button], N of Total" on navigation up/down
  - "[Action]: unassigned, N of Total" if no button assigned
  - "Press a button to assign." when confirm pressed on an action
  - Re-announces current item after button assignment
  - Button names read from `icon` GameText (sprite tag stripped of controller-type prefix)
  - Handles PS4/PS5/Xbox/Switch/PC controller types automatically

- **Keyboard binding menu announcements** (`KeyboardMenuHandler.cs`)
  - "[Action]: [Key], N of Total" on navigation up/down
  - "[Action]: unassigned, N of Total" if no key assigned
  - "Press a key to assign." when confirm pressed on an action
  - Re-announces current item after left/right category change

- **Load game menu** (`LoadGameHandler.cs`)
  - "Load game." announced when the screen opens
  - "[Slot label]. [Hero], Level [N], [Difficulty]. [Location]. Play time: [time]. [N] of [total]." on navigation
  - "Auto save. ..." for auto-save slots
  - "[Slot label]. Empty. [N] of [total]." for empty slots
  - Data read from UISaveLoadListItemData fields (pre-formatted strings from the game)

- **NPC/story dialogue** (`DialogueHandler.cs`)
  - "[Name]: [text]" when a dialogue line appears (NPC has a name shown)
  - "[text]" when no speaker name is present (narration, anonymous lines)
  - Fires on each new page as player advances through dialogue
  - Hooks: UIConversationPresenter.SetMessage(message, talkerName, voiceID, isWait, ref Rect)
  - TMP markup tags stripped before announcing

- **Tutorial boxes** (`NotificationHandler.cs`) ✓ TESTED
  - "Tutorial. [title]. [description]. Controls: [operation]" on each tutorial page
  - Button sprite tags converted to readable names (e.g. `<sprite name=PS4_Cross>` → "Cross")
  - Operation/controls text from data.operation field appended when present
  - Hooks: UITutorialInformationPresenter.SetInformation(UITutorialInformationData)

- **Dialog popups** (`NotificationHandler.cs`)
  - Yes/no and OK dialogs: "[question] [initial choice]" on open, "[choice]" on navigation
  - Description popups (e.g. acquired battle art) announced as "[name]. [description]"
  - Hooks: UIDialogPresenter.Setup + UIDialogPresenter.SelectChoices + UIDialogWindow.SetupDescription
  - Setup and SelectChoices coordinated via flag to avoid SelectChoices cutting off the question

- **New game settings screen** (`NewGameSettingsHandler.cs`)
  - "New game settings." on screen open
  - "[Label]: [Value]" on up/down navigation (e.g. "Difficulty: Galaxy")
  - New value announced alone on left/right change
  - "Editing name. Type your name and press Enter." when Name row is confirmed
  - Fallback labels if presenter text is empty
  - Hooks: UITitleSelectVoiceSelector.Show, OnUp, OnDown, UpdateCurrentPresenter, OnDecision

- **Protagonist selection screen** (`HeroSelectHandler.cs`)
  - "Protagonist selection." when the screen opens
  - "[Name]. [Description]" on open (initial focus) and left/right navigation
  - Description text read from heroDescription GameText field
  - Hooks: UITitleSelectHeroSelector.Show + UITitleSelectHeroSelector.OnSelected

- **Camp menu root announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - "Camp menu." when the camp opens
  - "[Item], N of total." when navigating the root menu (Status, Item, Equip, BattleSkill, Formation, etc.)
  - Greyed-out items announced with ", unavailable"
  - Re-announces current item when returning from a sub-screen
  - Root menu type: UICampMenuSelector (field menuSelector on UICampWindow)
  - Item data: UICampMenuItemData.menuItem (UIDefine.CampMenuItem enum), canDecisioned (availability)
  - Approach: polling currentIndex from Main.UpdateHandlers() — navigation is native-only, no Harmony hook fires
  - Item names currently use enum.ToString() (e.g. "BattleSkill") — can be refined with Loc entries

- **Camp item sub-screen announcements** (`CampMenuHandler.Items.cs`) ✓ TESTED — UPDATED
  - Now reads: Name x[quantity]. Effect. Description. Factor info. Position.
  - Effect text from UIItemInformationData.itemEffectInformation (what the item actually does)
  - Factor name + description for crafted/enhanced items
  - Quantity shown as "x5" instead of bare number
  - Double period fix: AppendSentence strips trailing periods from game text
  - Hook: UIItemInformationPresenter.Set caches effect/factor data for polling

- **Post-battle result announcements** (`BattleResultHandler.cs`) ✓ RETESTED (2026-03-21)
  - Announces SP and BSP totals after EXP/Fol
  - Level-ups include per-character BSP gained and learned battle skills
  - Learned skills now announce with description: "Learned Fire Bolt: Unleashes a fiery projectile."
    (via UICommon.CreateBattleSkillInformationData; falls back to name-only if description unavailable)
  - Bonus announcements: chain bonus (after totals), per-character Training and Open Eyes bonuses ✓ TESTED
  - Skill names resolved via ParameterManager → TextManager chain

- **Battle target announcements** (`BattleTargetHandler.cs`) ✓ TESTED
  - Hold L2 to enter target change mode; announces current enemy info
  - Cycles targets with directional input; each new target announced
  - Single-enemy battles: L2 re-reads current target's info (TargetChangeMode state detection)
  - Announces: name, HP %, shield %, leader type, active buffs/debuffs
  - HP shown as exact values if Spectacles item used on that enemy (IsSeeThroughEnemy check)
  - Enemy names resolved via ConstEnemyParameter.charaNameID → ParseCharaNameID fallback
  - Duplicate enemy names numbered (e.g. "Lizardaxe 1", "Lizardaxe 2")
  - Detection: SetControlPlayerTarget hook (CallerCount 7) + polling as backup
  - Spectacles is the ONLY see-through mechanism (no Analyze spell in this game)
  - R2 ally switching: announces controlled ally name, HP, MP, buffs/debuffs ✓ TESTED
  - Polls controlPlayerIndex; ControlPlayerChangeMode (state 6) detects first R2 press
  - Index silently seeded at battle start to avoid unwanted announcement

- **Camp equip sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - Slot list reads category before item name: "Weapon: Swift sword, 1 of 7."
  - Empty slots read "Greaves: None, 5 of 7." instead of being silent
  - Fixed: item list detection now uses currentState instead of activeInHierarchy

- **Camp skills sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED

- **Save game screen detection** (`LoadGameHandler.cs`) ✓ TESTED

- **Shop menu announcements** (`ShopHandler.cs`) ✓ TESTED
  - "Shop." when shop opens, root menu reads Buy/Sell/Cancel with position
  - Item browsing reads name + Fol price + description + position (buy and sell modes)
  - Quantity selection reads count + total Fol on change
  - Item details: description, equipment category, non-zero stats (ATK/DEF/INT/STM/LCK/POW/GUTS/HIT/EVD/CRT), factor effects
  - Descriptions sourced from UIItemInformationPresenter.Set hook (game doesn't populate itemDescription on shop list data)
  - Equipment stats from ParameterManager.GetItemParameter(itemID), factors from GetFactorParameter/GetFactorMessage

- **Item acquisition popups** (`NotificationHandler.cs`) ✓ TESTED
  - Treasure chest and quest reward popups now read aloud
  - Announces the game's message text plus each item name and count
  - Hook: UIOverflowItemPresenter.SetItem (CallerCount 3, fires when popup is populated)

- **Battle dodge warning audio cue** (`BattleCounterHandler.cs`, `AudioCuePlayer.cs`) ✓ TESTED
  - Plays Dodge.wav when an enemy is about to hit the player (dodge warning)
  - Hook: BattleCharacter.DoAttackNotify postfix — the game's own visual flash trigger
  - Only fires when target.IsControlPlayer() — ignores attacks on party members
  - Audio: WAV loaded from UserData/SO2RAccess/Sounds/Dodge.wav via winmm.dll (unmanaged memory)
  - Settings: ModSettings.DodgeSoundEnabled (on/off) and DodgeSoundVolume (0.0-1.0, default 0.8)
  - Volume-adjusted WAV cached in unmanaged memory, rebuilt only when volume setting changes
  - Refactored: shared TryParseWav() and ScalePcmSamples() helpers (dodge + save sound use same code)

- **Enemy proximity audio cue** (`EnemyProximityHandler.cs`, `SpatialAudioPlayer.cs`) ✓ TESTED
  - Looping spatial WAV cue warns of nearby field enemies
  - Volume scales with distance: full at 3 units, silent at 25 units
  - Stereo panning based on enemy direction relative to player's facing
  - Tracks closest enemy only; scans every ~60 frames via FindObjectsOfType<FieldEnemy>()
  - Audio engine: waveOut API (winmm.dll) with double-buffered stereo output
  - Separate from AudioCuePlayer (no conflict with dodge warning)
  - WAV file loaded from disk (UserData/SO2RAccess/Sounds/Enemy_proximity.wav) — swappable
  - UserVolume property ready for future mod settings menu
  - Deadlock bug fixed: Stop() sets _playing=false before waveOutReset to prevent callback deadlock
  - WAVEHDR_FLAGS offset corrected (24, not 16 on x64)
  - Stops on: battle, camp, shop, scene change. Resumes automatically on field.

- **Game over (battle loss) menu** (`GameOverHandler.cs`) ✓ TESTED
  - "Game over." announced when the battle loss screen appears
  - "Retry, 1 of 2." / "Title, 2 of 2." as player navigates up/down
  - Polling-based (native navigation, same pattern as shop/camp)
  - FindObjectOfType<UIGameOverWindow> with IsOpened polling

- **Battle command menu (Triangle)** (`BattleMenuHandler.cs`) ✓ TESTED
  - "Battle menu." announced when menu opens via Triangle during battle
  - Root menu: "Items, 1 of 4.", "Spells, unavailable, 2 of 4.", "Strategy, 3 of 4.", "Escape, 4 of 4."
  - Items sub-menu: Recovery/Combat tabs, item name + count + effect description + position
    - Effect text read from itemInformationPresenter's GameText (direct UI read fallback)
    - Item count from ItemManager.GetItemCount(itemID)
  - Spells sub-menu: per-character tabs, spell name + MP cost + range + effect + position
    - Character name resolved via ParameterManager → TextManager chain
  - Target selection: enemy/ally targeting after skill/item pick
    - Enemy: name + HP% (or exact with Spectacles) + position; reuses BattleTargetHandler helpers
    - Ally: name + HP/MP + position; self-targeting detected (single entry)
    - AoE: "All enemies" / "All allies" announced once
  - Strategy/Tactics sub-menu: character list + operation selection
    - Character: name + current operation assignment + position
    - Operation: name read from UICommonListItemPresenter.textMesh + "Currently set" indicator + position
  - Phase detection: UIStackSelectorWindowBase.GetPeekSelector() (OpenBattleState does NOT change for sub-screens)
  - All selectors have activeInHierarchy=True permanently — peek-based detection required
  - 4 hooks: SpellInfoData, EffectRange, UseDescription, OperationInfo
  - Tactics operation selector also matched in IdentifyPhase (may be pushed onto stack separately)

- **Battle status announcements** (`BattleStatusHandler.cs`) — PARTIALLY TESTED (damage dealt + ally HP warnings confirmed working; ailments need more game progress)
  - Ally health below 50%: "[Name], health below 50 percent." (queued, non-interrupting) ✓ TESTED
  - Ally health below 25%: "[Name], health critical." (queued) ✓ TESTED
  - Ally knocked out: "[Name], knocked out." (queued) ✓ TESTED
  - Ally negative status ailment: "[Name], [ailment]." (queued, e.g. "Claude, Poison.") — PENDING (needs more game progress)
  - Player damage dealt: "[N] damage." per hit by the controlled character (queued) ✓ TESTED
  - HP threshold tracking: only announces downward transitions (not on healing)
  - Ailment tracking: per-ally set, cleared on removal so re-application announces again
  - Hooks: BattleCharacter.DoCollisionReceiveAction (CallerCount 2, prefix+postfix), CharacterParameter.SetBuffDebuffState (CallerCount 19, postfix)
  - CRASH FIX: original DoDamage hook used ref DamageResult (IL2CPP value type) which corrupted Harmony trampolines. Replaced with DoCollisionReceiveAction — attacker obtained via attackCollision.OwnerCharacter.
  - All 3 features toggled independently in mod settings menu (F4 / L1+L3)
  - Settings: AllyHealthWarningEnabled, AllyStatusAilmentEnabled, PlayerDamageDealtEnabled (all default On)

- **Battle pause menu** (`BattlePauseHandler.cs`) ✓ TESTED
  - "Battle status." announced when pause menu opens (Start/Options during battle)
  - Tiered info system: basic (auto), weaknesses, resistances, status, equipment, cooking, music, leader
  - Keyboard: NumPad 8/2 tier cycling, NumPad 4/6 character cycling
  - Gamepad: R1/L1 tier cycling, D-pad native character cycling (all directions, polling announces)
  - Allies: "Name. HP X of Y. MP X of Y. N of Total."
  - Enemies: "Name. HP X of Y." (with Spectacles) or "HP unknown." (without)
  - Empty tiers auto-skipped; tier resets on character change
  - Hooks: SetHp, SetMp, SetElemental, SetAllBuffList, SetTargetName on UIBattlePauseCharacterPresenter
  - HP/MP read directly from CharacterParameter (not hook caches — fixes timing bug)
  - Ally name: ParameterManager chain (BattlePlayerParameter → charaNameID → TextManager) as primary
  - RefreshPauseUI called synchronously before BuildTiers (fixes stale cache)
  - Buff categorization via icon sprite matching (GetIconSprite → category map)
  - BattleTargetHandler helpers reused (enemy names, spectacles, status conditions)
  - Bugs fixed (2026-03-04):
    - HP/MP all zeros: direct CharacterParameter reads instead of hook caches
    - Ally name empty: ParameterManager chain resolves "Claude" via charaNameID
    - D-pad conflict: game uses ALL D-pad directions for character cycling natively;
      tier cycling moved to L1/R1 shoulder buttons (free during pause)

- **Camp status talents sub-screen** (`CampMenuHandler.cs`) ✓ TESTED
  - Hook: UITalentPresenter.Set(List<UITalentData>) — CallerCount(1)
  - Announces "Talents." heading + comma-separated talent names
  - Hook fires on status open (page 0), data CACHED; announced when pageIndex changes to 1
  - If character changes while on talent page, hook fires and announces immediately
  - Stats announcement gated to page 0 (prevents stats reading on talent page)

- **Location discovery notifications** (`NotificationHandler.cs`) ✓ TESTED
  - Hook: UIFieldLocationPointPresenter.Set(string name, string description) — CallerCount(1)
  - Announces "Discovered [name]. [description]" when a location marker popup appears
  - Rewards now handled separately by stacked field notification queue (no longer inline)

- **Map name announcement on area change** (`NavigationHandler.cs`) ✓ TESTED
  - Polls FieldManager.Instance.currentFieldmapID each frame
  - When fieldmap changes, resolves name via ParameterManager/TextManager and announces
  - Skips first detection (game load) to avoid announcing on initial scene
  - Reuses existing ResolveMapName logic (overrides, game data, fallback)

- **Stacked field reward notifications** (`NotificationHandler.cs`) ✓ TESTED
  - Hook: UIFieldInformationStackSelector.ShowInformation — CallerCount(15)
  - Queues rapid-fire notifications (EXP, Fol, items, level-ups, talents, etc.)
  - Announces all queued messages combined after 0.5s delay to prevent interruption
  - Supports item-style notifications with getText/count/unit fields

- **Reward announcements for managed-code rewards** (`NotificationHandler.cs`)
  - Hook: GameManager.GiveRewardWithWindow — CallerCount(6)
  - Announces EXP/Fol/SP/BP/items when rewards given via managed code (missions, etc.)
  - Does NOT fire for location point rewards (native-only flow)

- **Dialogue choice menus** (`DialogueChoiceHandler.cs`) ✓ TESTED
  - Choice menus during private actions, story events, and dialogue sequences now announced
  - Hook: UISelectChoiceSelector.ShowSelectChoiceMessage (CallerCount 5) — captures menu open
  - Announces prompt/title text + initial choice with position on open
  - Polling: selectChoiceIndex tracked each frame (native-only navigation, no Harmony hooks fire)
  - Navigation announces current choice text + "N of total" position
  - Choice text read from UISelectChoicePresenter.choicePresenterList[i].message.text
  - Deactivates when presenter goes inactive (choice confirmed or cancelled)

- **Save notification and audio cue** (`SaveNotificationHandler.cs`, `AudioCuePlayer.cs`, `ModSettings.cs`) ✓ TESTED
  - Hook: UIDialogWindow.SetupAutoSaveAnnounce (CallerCount 2) — reads new game save notification dialog
  - Hook: GameSaveManager.Save prefix (CallerCount 3) — detects manual save start
  - Hook: GameSaveManager.OnSaveSuccess postfix (CallerCount 1) — detects save completion
  - Polling: GameSaveManager.IsSaving() as backup (auto-saves)
  - Audio: plays Save_sound.wav from UserData/SO2RAccess/Sounds/ via winmm.dll (unmanaged memory)
  - Settings: ModSettings.SaveSoundEnabled (on/off) and SaveSoundVolume (0.0-1.0, default 0.5)
  - Settings persisted to UserData/SO2RAccess/settings.json (created automatically)
  - Ready for future mod settings menu integration

- **Fol readout** (`Main.cs`)
  - F3 (keyboard) or L1+R3 (gamepad) announces current Fol
  - Uses EventManager.Instance.GetMoney() to retrieve current money
  - Works anywhere in the game (field, menus, battle)

## In-Progress / Pending Test

- **World map fishing spots + POI survey** (2026-07-11, REBUILT + DLL DEPLOYED) — PENDING TEST M2
  - TEST M RESULT (2026-07-11 19:23–19:25 log): NO fishing spots on world map — ROOT CAUSE FOUND:
    the world map has ZERO FieldFishingWaterPlace objects (`NAV:WM:SURVEY fishingSpots=0` right next to
    Krosse). World-map fishing spots are painted into the game's NATIVE world grid
    (`WorldGridData.fishingWaterPlaceID` per cell) — no scene objects to scan. A field map (MF_0006_01A)
    found its spot fine, so the object scan stays correct for fields.
  - FIX: world map now reads the game's fishing DATABASE instead:
    `ParameterManager.GetFishingWaterPlaceParameterList(fm.currentFieldmapID)` →
    ConstFishingWaterPlaceParameter (WaterPlaceID, Position, Size, IsPlacementFishingSpot).
    BuildFishingSpots is now a dispatcher: CollectFieldFishingSpots (objects+NavMesh, unchanged behavior)
    vs CollectWorldmapFishingSpots (database + `WorldmapPathfinder.TryGetNearestWalkableWorld` grid
    shore-snap, ~50m, current travel mode); shared sort/region-filter/numbering tail.
    If the per-map lookup is EMPTY, debug mode dumps the whole DB (`NAV:FISHING:DB` with each spot's
    fieldmapID) so a wrong map-ID assumption shows as evidence.
  - SURVEY RESULTS (Test M log, complete):
    - Symbols: ONLY CITY/DUNGEON + 3 INVALID `ob_*` objects → Locations coverage already complete.
    - `locationPoints=6` ON THE WORLD MAP at (-180,402), (785,-354), (-560,396), (-861,-417), (-496,-56),
      (80,-220) — discovery landmarks, candidates for a Markers category on the world map (NOT YET ADDED —
      discuss with user; BuildMarkers currently field-only).
    - savePoints/stairs/doors/minimapAreas all 0 on world map.
  - TEST M2 RESULT (19:33–19:35 log): list ✅ (30+ spots from DB, honest cross-landmass filtering,
    "Fishing spot 1" near Krosse dist 47.5), auto-walk ✅ arrived 3× — but NO fishing prompt ever fired
    (zero FieldPrompt lines) and no interact. TWO GAPS FOUND: (a) world-map arrival never applied
    FacePosition (field-only code — player faced along path, not water; game only prompts when FACING
    water); (b) center-snap target landed on a bank (Y=20.6 vs water 16.2) where the prompt can't fire.
  - FIX ROUND 2 (BUILT + DEPLOYED) — PENDING TEST M3:
    - Game-truth targeting: at walk start, `ComputeWorldmapFishingTarget` samples the water box perimeter,
      snaps samples to walkable cells, and keeps only stands where the GAME's own probes say fishable:
      `FieldManager.IsWorldmapFishingPoint(probe)` / `GetContactFishingWaterPlaceID(ref probe)` at the
      game's `worldmapFishingFrontDistance` ahead (probes tried at stand + water height). Nearest verified
      stand wins; falls back to the coarse shore point (never removes a target). Water box passed via
      NavItem.TriggerBounds.
    - World-map arrival now faces FacePosition before stopping (new FaceAutoWalkFacePosition; distance
      arrival only — prompt arrival must NOT re-face or it could dismiss the prompt).
    - Fishing prompt = arrival truth: world-map walk to an Interactable arrives THE MOMENT the fishing
      prompt shows (FieldPromptHandler.FishPromptShowing), like locations with the enter prompt.
    - Fishing prompt announced: FieldPromptHandler detects operation prompts whose action CONTAINS "fish"
      (case-insensitive), speaks "Press {button} to {action}" (game's own words, enter-prompt F4 speech
      toggle), logs it; hide-poll refactored (IsActionStillShowing shared by jump+fish).
    - CAT_INTERACTABLE added to the world-map tight arrival radius set (1.3m like chests/saves).
  - TEST M3 RESULT (2026-07-11 20:03–20:07 log): 3 distinct failures + 1 hidden success:
    (a) From the M2 bank position, Fishing spot 1 refused: the stand search commits to the single
    NEAREST fishable stand (a `distSq >= bestDistSq` prune skipped probing all others) and that stand
    sat behind a live `Col_Obstacle` rock the route sweep honestly refused (grid says passable —
    bake/live mismatch; re-plan only marks the exact hit waypoints so each round clipped the same
    rock face). Walking toward Salva first made the search pick a DIFFERENT stand (-65,-102) → walk ✅.
    (b) Fishing spot 3 stand verified on the OPPOSITE river bank (region 362) → honest "different
    connected regions" refusal — stand search had no region awareness.
    (c) Prompt NEVER announced even though the user fished successfully at 20:06:45 ("Caught: Krosse
    Carp") — the game's fishing prompt is NATIVE UI: the UIFieldOperationPresenter.Set hook fired ZERO
    times all session while the real prompt was on screen. Hook-based fish detection was dead code.
    (d) List said "39 meters" while standing at the spot: list distance was to the coarse center-snap
    point, not the shore the walk actually goes to.
  - FIX ROUND 3 (BUILT + DEPLOYED 2026-07-11) — PENDING TEST M4:
    - Fish prompt by POLLING (FieldPromptHandler.UpdateFishingContactPoll): per-frame
      `FieldManager.GetContactFishingWaterPlaceID() > 0` = the exact game condition behind its own
      prompt (native UI fires no hooks — matches the known camp-menu pattern). Edge-triggered announce
      "You can fish here." (new Loc `fish_prompt`, button-neutral since there is no glyph to read;
      enter-prompt F4 toggle). FishPromptShowing now actually works → walk-stop-on-prompt now live.
      Removed dead hook path (FishAction, TryFindActionContaining, AnnounceFish(button,action)).
      Dropped the bogus `GetContactFishingWaterPlaceID(ref probe)` stand probe (that overload reads
      the PLAYER's contact, its ref param is an out hit-point — it never probed our point).
    - Multi-candidate stand search (`ComputeWorldmapFishingStands`): returns ALL game-verified stands,
      nearest-first, thinned to ≥16m apart (WmSweepEndpointExemptDist — closer stands share the failed
      approach); prune removed so every shore cell is probed. AutoWalkTo tries up to 3 stands
      (MaxFishingStandAttempts) before announcing unreachable, logging each refusal.
    - Region-aware: stands in a different connected region than the player (far bank) are rejected
      (WorldmapPathfinder.GetStartRegionIds/GetRegionId, fail-open on region 0/unknown).
    - List honesty: Distance = player→nearest edge of the water box (was center-snap point; read
      "39m" while standing at the shore). Build-time far-bank rescue: if the center snap lands in a
      non-player region, Position re-picked as nearest player-side shore cell
      (TryFindShoreOnPlayerSide) so the reachability filter can't hide same-side spots.
    - Shared perimeter helpers: WaterBoxEdgeStep / ForEachWaterBoxEdgePoint / ForEachWaterBoxShoreCell
      (walk-time search + build-time rescue use the same shore-cell enumeration).
  - 🔴 TEST M4 RESULT (2026-08-29): FAILED — user: "failed miserably"; OPENING THE NAV MENU
    ON THE WORLD MAP FROZE THE GAME with this build (task-manager kill).
  - 🟡 M4 FREEZE DIAGNOSED + FIXED 2026-08-29 (same session; built clean, deployed, NOT committed):
    Latest.log survived and showed the fishing list build slowing catastrophically on REMOTE spots
    only: local spots = ms each; id=4 (500×600m box, other continent) = 15s; id=33 = 4s; log ends
    mid-build after id=34 with more remote spots left → perceived freeze, not a crash/livelock.
    Cause chain (multiplied): far-bank rescue TryFindShoreOnPlayerSide runs exactly when the shore
    point's region ≠ player regions (= every other-continent spot) and samples the whole box
    perimeter (~200 points on big boxes) × each ocean-facing sample ran the full FAILED ~50m snap in
    TryGetNearestWalkableWorld × SnapToPassable scanned the whole (2r+1)² square per ring (~1.4M
    iterations per failed snap). Fix: (1) SnapToPassable ring-walks only the ~8r ring cells (same
    visit order, same result cell, all callers faster); (2) TryGetNearestWalkableWorld gained
    maxSnapMeters (default 50m unchanged for all other callers); (3) ForEachWaterBoxShoreCell passes
    new const ShoreSnapMaxMeters=6m — RESULT-IDENTICAL because the sampler already rejected snaps
    wandering >6m (the 50m search was provably wasted work). No detection weakened. Retest = M5,
    same checklist below; expect world-map nav menu to open in ~1s.
  - ✅ M5 RESULT (2026-08-29): freeze GONE (full session 18:58–19:06, clean shutdown). BUT detection
    broken: false "You can fish here" right at the Krosse exit (spot id=25 volume overlap), and
    auto-walk "arrived" where cross did nothing — world map AND town. Both from the same signal:
    GetContactFishingWaterPlaceID = volume contact, not "can fish"; it also drove the arrival stop.
  - 🟡 FIX 2026-08-29 (user-proposed, deployed, NOT committed): FishPromptShowing now = the game's
    fishing BUBBLE over the player's head (UIFieldIconSelector poll, sprite == FieldIconType.Fishing,
    activeInHierarchy + canvasGroup alpha ≥ 0.5; selector via throttled FindObjectOfType incl.
    inactive). Contact ID demoted to debug diagnostic: FISHDIAG log line on every state change with
    bubble/contactID/visible-icon-names/player pos. UNVERIFIED assumptions the M6 log will confirm:
    (a) bubble icon really appears via iconPresenterList (not a separate path), (b) sprite index 1 =
    Fishing sprite (logged as fishingSpriteId at selector cache), (c) bubble appears in both town and
    world map. If detection stays silent while the user SEES no fish but can fish → check FISHDIAG
    icons=[] catalog for what sprites ARE visible.
  - ✅ M6 RESULT (2026-08-29, full debug log 19:19–19:22): bubble detection WORKS. Town: auto-walk
    arrived at pond stand (distXZ=1.27 of FishingWaterPlace trigger) → bubble 28ms later → announce;
    user fished repeatedly. Krosse exit: contactID=25 but bubble=False → SILENT ✅. Found: (a) world
    map list read "Fishing spot 1, 0 meters" (player inside spot 25's land-spanning AABB; real stand
    83m); (b) game BLINKS the bubble → re-announce spam (= the user's suspected town false positive);
    (c) the world-map fishing walk was CANCELLED silently by a nav-menu open 23m short of the
    verified stand — walk logic itself was fine (2 battle interrupts resumed cleanly, waypoints=68).
  - 🟡 FIXES 2026-08-29 (deployed, NOT committed): inside-box list distance falls back to
    walk-target distance; bubble announce latch (re-announce only after moving >3m from announce
    position); menu-open cancel now speaks "Walk to {label} cancelled." (Loc
    nav_autowalk_cancelled_menu; NumPad 1/stick cancels stay silent).
  - TEST M7 (user, F12 ON): (1) world map near Krosse: fishing spot 1 should read ~70m, not 0m;
    (2) auto-walk to it and DON'T open the menu mid-walk (battles are fine — resume works): expect
    arrival at the stand, bubble, "You can fish here.", cross starts fishing. If arrival is silent →
    facing may not stick on world map (known unverified) — log will show FISHDIAG at the stand;
    (3) town pond: stand still at spot ≥30s: exactly ONE announcement despite bubble blinks; walk
    5m away and back: announces again; (4) open nav menu during any auto-walk: hear "Walk to X
    cancelled." then the list. Then the old M4 checklist below still applies (edge distances, stand
    retry, spot 3 far bank):
  - TEST M4 checklist (kept for M6): (1) from anywhere near Krosse: nav list distances should now track
    the water edge (standing at a spot reads ~2m); (2) walk to Fishing spot 1 from the bank position
    that failed — should now retry the next stand and arrive; (3) on arrival EXPECT "You can fish
    here." within ~1s — if silent, the arrival FACING is not sticking (transform.rotation may be
    overridden on world map) → fix facing next; (4) Fishing spot 3 (across the river) should either
    walk to a same-side stand or stay honestly refused; (5) prompt announce should also fire when
    walking up to water manually, and clear silently when walking away.
  - KNOWN LIMIT: if all ≤3 tried stands are wedge-blocked (rock between player and shore), refusal
    still possible from pathological start positions (e.g. the M2 bank if only one stand exists there).
    Re-plan wedge-marking is point-thin (rock wider than marks) — improving that is a separate,
    discuss-first item (more rounds vs radius marking; do NOT blind-tweak).
  - OPEN QUESTION for later: the "popup message" (water place info panel, UIFieldController.
    ShowFishingWaterPlaceInformation) — if the game shows a spot-info panel and the user wants it read,
    hook it next; the button prompt announce may already cover what they meant.
  - Files: NavigationHandler.List.cs (world-map scan branch), NavigationHandler.Build.cs (dispatcher +
    field collector), NavigationHandler.Build.Worldmap.cs (worldmap collector + far-bank rescue +
    symbol survey), NavigationHandler.Worldmap.Pathfinding.cs (stand search + perimeter helpers),
    NavigationHandler.AutoWalk.cs (multi-stand retry), NavigationHandler.Worldmap.cs (prompt arrival +
    FaceAutoWalkFacePosition), FieldPromptHandler.cs (contact polling), WorldmapPathfinder.cs
    (TryGetNearestWalkableWorld), Loc.cs (fish_prompt)

- **Camp status sub-screen announcements** (`CampMenuHandler.cs`) ✓ TESTED
  - Hook-driven detection: both activeInHierarchy and root-menu-hidden approaches failed;
    now uses UICampStatusSelector.UpdatePresenter hook as trigger

- **Navigation key remap + gamepad support** (`NavigationHandler.cs`, `Main.cs`) ✓ TESTED
  - Keyboard: NumPad 5 open/close, NumPad 1 auto-walk/cancel, F5 no longer used
  - Gamepad: L1 hold opens nav, D-pad Up/Down=category, Left/Right=items, LStick up=auto-walk
  - L1 suppresses D-pad directions + ShortCut actions + FieldCameraLeft while held in field
  - L1 does not activate in camp menu, battle, or dialogue (IsFieldFree check)
  - D-pad auto-repeat while held (400ms initial, 150ms interval)
  - Steam Input must be disabled for gamepad detection to work
  - Bug fixed: field shortcuts (Quick Heal) now blocked — ShortCut actions (39-42) added to suppression list

- **Navigation Events category** (`NavigationHandler.cs`) ✓ TESTED
  - New "Events" category added to nav list (5th category after Markers)
  - Scans FieldEventCollision objects, filtered by IsEventActivate() (only active triggers shown)
  - Classified as "Story event", "Private action", or "Side event" (generic events dropped — no content)
  - PAs and sub-events with isDisableIcon=true skipped (game hides them)
  - Side events annotated with hints: "(reward)", "(battle)", or "(reward, battle)" when applicable
  - Plain "Side event" (no hint) = needs user testing to determine relevance
  - Numbered by label type in distance order (e.g. "Story event 1", "Side event (reward) 2")
  - NavMesh reachability filter applied; static transforms (LiveTransform = null)
  - PA NPCs (code names starting with `pa_`) also listed under Events as "Private action (Name)"
  - Name parsed from code name last segment (not dialogue-derived, which can be wrong speaker)

- **Navigation Enemies category** (`NavigationHandler.cs`) — TESTED, WORKING
  - New "Enemies" category added to nav list (7th category)
  - Uses FindObjectsOfType<FieldEnemy>() to scan field enemy symbols
  - Enemies excluded from NPC category via TryCast<FieldEnemy>() filter
  - Name resolution: EncountID → encounter params → partyID → enemy params → charaNameID
  - TextManager doesn't resolve enemy names on field (not loaded); falls back to parsed charaNameID
  - e.g. CHARA_LIZARDAXE → "Lizardaxe", shown as "Lizardaxe, medium 1"
  - Difficulty from EnemySymbolType: weak, medium, strong, raid
  - Sorted by distance, NavMesh reachability filtered, duplicate labels numbered
  - Live transform tracking for auto-walk to enemies

- **Navigation Save Points category** (`NavigationHandler.cs`) ✓ TESTED
  - New "Save Points" category added to nav list (6th category)
  - Uses FieldManager.Instance.FieldSavePointList (game-managed list)
  - Labels: "Save point" or "Recovery save point" based on IsRecovery property
  - Numbered when multiples of the same type exist (e.g. "Save point 1", "Recovery save point 2")
  - NavMesh reachability filter applied; live transform tracking for auto-walk

- **Navigation Stairs category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Stairs" category added to nav list (8th category)
  - Uses FieldManager.Instance.FieldStairsList (game-managed list)
  - Labels: "Stairs up" or "Stairs down" based on isUpperStage property
  - Numbered when multiples of the same direction exist (e.g. "Stairs up 1", "Stairs down 2")
  - NavMesh reachability filter applied; static transforms (LiveTransform = null)

- **Navigation Doors category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Doors" category added to nav list (9th category)
  - Uses FieldManager.Instance.FieldDoorList, filtered to StoneDoor type only
  - Labels: "Stone door, open" or "Stone door, closed" based on doorState
  - AutoDoor and Default door types excluded (auto-doors are ambient, not useful as nav targets)
  - Numbered when multiples of same state exist
  - NavMesh reachability filter applied; static transforms

- **Navigation Warp Points category** (`NavigationHandler.Build.cs`) — PENDING TEST (needs dungeon)
  - New "Warp Points" category added to nav list (10th category)
  - Source: FieldManager.Instance.FieldGimmickManager.FieldGimmickList
  - Identifies 3 gimmick types via TryCast:
    - FieldGimmick09 (warp panels) → "Warp panel"
    - FieldGimmick17 (magic circles) → "Magic circle" (filtered by IsEnable + not disabled)
    - FieldGimmick03 (moving platforms) → "Platform"
  - Each type numbered separately if multiples exist
  - NavMesh reachability filter applied; static transforms

- **Enhance menu sub-screen announcements** (`CampMenuHandler.BattleSkill.cs`, `CampMenuHandler.Formation.cs`) ✓ TESTED
  - Camp → Enhance shows 3 sub-items: Skill, CombatPoint, BattleSkillPoint
  - Gate checks expanded from "BattleSkill" to also accept "BattleSkillPoint" and "CombatPoint"
  - Hook-based deferred detection: activeInHierarchy always true, so heading + inner selector
    caching deferred to UIBattleSkillInformationPresenter.Set hook on first fire
  - Combat skills (CombatPoint): BP balance/cost per skill, max level indicator, toggle mode (Square)
  - Battle skills (BattleSkillPoint): BP balance/cost per skill, max level indicator
  - Skills (Skill): SP balance/cost per skill, max level indicator
  - Balance shown per-skill as "BP: 28 / 5" or "SP: 100 / 5" (not on heading)
  - Toggle mode (Square button): announces "Toggle mode", skill active/inactive status on navigate and confirm
  - Double punctuation fix: AppendSentence helper strips trailing periods from game text

- **Camp formation sub-screen announcements** (`CampMenuHandler.cs`) — ✓ CONFIRMED WORKING (user, 2026-07-11)

- **Camp operations child screens** (`CampMenuHandler.cs`) — ALL CONFIRMED WORKING (2026-07-11)
  - Operations root menu reads its items (Formation, Party Formation, Assist Formation, Tactics) ✓
  - Formation: announces formation name, effect, sphere count, bonus details ✓ TESTED
  - Party Formation: cursor tracking via cursorTarget position matching, per-slot data from SetData hook ✓ TESTED
  - Assist Formation: polls UICampAssistSettingSelector (Equip slots + character picker) ✓ TESTED 2026-07-11
  - Tactics: polls UICampOperationSelector (character + operation states), hook for operation info ✓ TESTED (incl. operation picker, 2026-07-11)

- **Equipment Wizard handler** (`EquipWizardHandler.cs`) — CONFIRMED WORKING (2026-06-14)
  - New polling handler: FindObjectOfType<UISystemWindow>, polls IsShowingEquipWizard
  - Announces heading + description text + equipment comparison (old → new for changed slots)
  - Yes/No/Reject All menu navigation with position
  - Tracks equipWizardDataIndex for multi-character wizard advances
  - Loc keys added: equip_wizard_heading, equip_wizard_change, equip_wizard_position, menu options

- **First-item fix across all camp menus** (2026-03-07) — PENDING TEST
  - Root cause: Harmony hooks fire during game's Update, but polling flags gating them are set in
    OnLateUpdate — always one frame too late for the first item in any list.
  - Fix: replaced stale polling flags with live game state reads or removed redundant gates.
  - Files changed: CampMenuHandler.Equip.cs, CampMenuHandler.Formation.cs,
    CampMenuHandler.BattleSkill.cs, CampMenuHandler.Party.cs
  - Equip first item confirmed working by user. Other menus pending test.

- **Double-period fix** (2026-03-07) — PENDING TEST
  - Game text fields (item names, descriptions, skill names) often end with periods.
    Manual `.Append(". ")` created double periods. Fixed by using AppendSentence() helper
    which strips trailing periods before appending ". ".
  - Files changed: CampMenuHandler.Equip.cs, CampMenuHandler.Party.cs,
    CampMenuHandler.Formation.cs, CampMenuHandler.BattleSkill.cs
  - Equip item names confirmed fixed by user. Other menus pending test.

## In-Progress Features

- **Field Navigation — Phase 2 (audio list + auto-run)** (`NavigationHandler.cs`) ✓ COMPLETE AND TESTED
  - F5: open/close navigation list; also cancels auto-run if active
  - NumPad 8/2: navigate up/down within category
  - NumPad 4/6: switch category (NPCs, Chests, Exits, Markers, Events, Save Points, Enemies, Stairs, Doors, Warp Points, Locations)
  - NumPad 5: auto-run to selected item; press again to stop following
  - Items sorted by distance (closest first) within each category
  - Party members filtered (dist < 2 units)
  - NPC names: parsed from ConstNpcParameter code name; functional NPCs qualified e.g. "Equipment shop (Hahn)"
  - Chests: numbered by type in distance order (Unopened chest 1, Unopened chest 2 etc.)
  - Exits: labelled by icon type + game map name (e.g. "Building entrance to Arlia Village", "Town gate to Overworld")
  - **NavMesh pathfinding** (Phase 2.5): auto-walk uses NavMesh.CalculatePath() for wall-respecting paths instead of straight-line movement. Unreachable targets filtered from nav list on F5. Path recalculates every 1.5s for moving NPCs. Terrain height followed via waypoint Y interpolation. Falls back gracefully if no NavMesh in scene.
  - Auto-run: NavMesh waypoint-following at player's actual run speed (GetMoveSpeed(true) = 6.5); live transform tracking so wandering NPCs are followed
  - Run animation + footsteps: Harmony prefix blocks game from resetting Run to Unique/Idle each frame; PlayMoveAnimation(Run) called once at start
  - Auto-run NPC arrival: proximity-lock mode — player held 1 unit from NPC, facing them, until NumPad 5 pressed
  - Auto-run static arrival (exits, markers): stops and announces "Arrived"
  - Scene change cancels auto-run silently

## Pending Tests (Camp Item Sub-screen — updated format)

- [x] Camp item screen: "Items." announced when opening item screen ✓
- [x] Camp item screen: quantity reads as "x5" (not bare number) ✓
- [x] Camp item screen: effect text reads (e.g., "Restores a small amount of HP") ✓
- [x] Camp item screen: description reads after effect ✓
- [x] Camp item screen: no double period at end of description ✓
- [x] Camp item screen: factor info reads for crafted/enhanced items (if available) ✓
- [x] Camp item screen: returning to root menu re-announces root item ✓
- [x] Camp item screen: no stale announcement on camp re-open ✓

## Completed Tests (Camp Menu Root)

- [x] Camp menu: "Camp menu." announced when opening the menu ✓
- [x] Camp menu: "[Item name], N of total." announced on up/down navigation ✓
- [x] Camp menu: position count is correct ✓

- [x] Title menu navigation — all passing ✓
- [x] Config menu categories — all passing ✓
- [x] Config submenu settings (sliders + options) — all passing ✓
- [x] Keyboard menu: action names read correctly ✓
- [x] Keyboard menu: "Press a key to assign." announced on confirm ✓
- [x] Keyboard menu: assigned key name reads correctly ✓
- [x] Hero select: "Protagonist selection." announced on screen open ✓
- [x] Hero select: "Claude. [description]" announced on open (default selection) ✓
- [x] Hero select: "Rena. [description]" announced when navigating to Rena ✓
- [x] Hero select: description text is meaningful (not empty) ✓
- [x] New game settings: "New game settings." announced on open ✓
- [x] New game settings: "[Label]: [Value]" on up/down navigation ✓
- [x] New game settings: new value announced on left/right change ✓
- [x] New game settings: "Editing name." announced when Name row confirmed ✓
- [x] Gamepad menu: button assignments read correctly for all actions ✓
- [x] Gamepad menu: "Press a button to assign." announced on confirm ✓
- [x] Gamepad menu: updated button announced after assignment ✓
- [x] Load game: "Load game." announced on screen open ✓
- [x] Load game: slot details announced on navigation ✓
- [x] Load game: empty slots announced correctly ✓
- [x] NPC dialogue: text reads on each new line ✓
- [x] NPC dialogue: speaker name prepended when present ✓
- [x] Tutorial boxes: title and description announced ✓
- [x] Tutorial boxes: each page announces on navigation ✓
- [x] Description popup (Phase Gun Art): name and description announced ✓
- [x] Yes/no dialogs: question + initial choice announced on open ✓
- [x] Yes/no dialogs: navigating between options reads each option ✓
- [x] Navigation Phase 1: F5 scan announces NPC/chest/exit/marker counts ✓
- [x] Navigation Phase 1: NPC type labels resolve (Item shop, Innkeeper, NPC etc.) ✓
- [x] Navigation Phase 1: Chest opened/unopened status correct ✓
- [x] Navigation Phase 1: Exit destination map codes visible in debug log ✓
- [x] Navigation Phase 1: Distances plausible ✓
- [x] Navigation Phase 2: list opens/closes with F5 ✓
- [x] Navigation Phase 2: NumPad 8/2/4/6 navigation works ✓
- [x] Navigation Phase 2: NPC names parsed from code (Girl 1, Grandfather 2 etc.) ✓
- [x] Navigation Phase 2: Chests numbered by type and distance ✓
- [x] Navigation Phase 2: Exits show destination code suffix ✓
- [x] Navigation Phase 2: Auto-walk reaches stationary NPCs, player faces them ✓
- [x] Navigation Phase 2: Proximity-lock keeps player next to wandering NPCs ✓
- [x] Navigation Phase 2: NPC nav-list name matches dialogue name ✓ (shop NPCs shown as "Equipment shop (Hahn)" etc.)
- [x] Navigation Phase 2: Auto-run has run animation and footstep sounds ✓

## Completed Tests (Camp Status Sub-screen)

- [x] No false HP/MP announcement on camp root open (old stale bug fixed) ✓
- [x] "Status." announced when opening the status screen ✓
- [x] Character stats announced ✓
- [x] Age announced on page 0 ✓
- [x] Elemental affinities announced (or "No elemental affinities") ✓
- [x] Friendship levels announced with correct character names ✓
- [x] Up/Down virtual cursor navigates individual stat lines ✓
- [x] No stale announcements on camp reopen or character/page switch ✓
- [ ] Favorite food: only displays after food is discovered in-game (untested — needs gameplay progress)

## Pending Tests (Navigation Improvements — 2026-03-08)

- [ ] Field stuck detection: auto-walk into a corner or dead-end, verify it cancels after ~4s with "Path blocked" announcement
- [ ] Field stuck detection: normal auto-walk to NPC/chest still works (no false stuck triggers)
- [ ] Linecast filter: open nav list on a map with walls, check F12 debug log for "linecast blocked" messages
- [ ] Linecast filter: all expected NPCs/chests/exits still appear (no false removals)
- [ ] Floor labels: open nav list on a multi-floor map (e.g. inn), items on other floors show "(above)" or "(below)"
- [ ] Floor labels: items on the same floor have no suffix
- [ ] Regression: auto-walk to NPCs, chests, exits, counter NPCs all still work normally

## Pending Tests (Operations Child Screens) — ALL CONFIRMED WORKING (user, 2026-07-11)

- [x] Operations → Formation: announces formation name + effect on navigation
- [x] Operations → Party Formation: announces character name, level, HP/MP, role, position on navigation
- [x] Operations → Assist Formation (Equip): announces button slot + assigned character/skill
- [x] Operations → Assist Formation (Character picker): announces character names
- [x] Operations → Tactics (character list): announces character + current tactic ✓
- [x] Operations → Tactics (operation picker): announces operation name + description

## Pending Tests (Assault Action Skill Readout) — ✅ CONFIRMED WORKING (user, 2026-07-11)

- [x] Assist Formation character picker: reads skill name, type, cooldown after character
      name (final: NO description — screen parity, user decision; see feature entry)
- [x] Live skill tracking: reassigning Celine's assault skill updates the readout
- [x] "None" row and empty slots: unchanged (no skill summary)

## Dialogue Voice Mode Toggle — TESTED (2026-03-07)

Voice detection fix: replaced broken PlayVoice Harmony hook (native IL2CPP calls
bypass managed stubs) with polling UIConversationSelector.currentVoiceController.IsPlaying().

- [x] F2 toggles dialogue voice mode
- [x] NameOnlyWhenVoiced: voiced cutscene lines announce speaker name only
- [x] NameOnlyWhenVoiced: unvoiced lines read full text
- [x] AlwaysReadFull: all lines read name + text regardless

## Pending Tests (Battle Status Announcements) — ALL TESTED (2026-03-05)

- [x] Enter battle, take damage until ally drops below 50% HP — hear "[Name], health below 50 percent." ✓
- [x] Continue taking damage below 25% — hear "[Name], health critical." ✓
- [x] Ally gets knocked out — hear "[Name], knocked out." ✓
- [x] Ally healed above 50%, then damaged below 50% again — hear warning again (threshold resets on heal) ✓
- [x] Ally gets poisoned or paralyzed — hear "[Name], Poison." or "[Name], Paralyze." ✓
- [x] Same ailment re-applied after wearing off — hear announcement again ✓
- [x] Attack an enemy as the player character — hear "[N] damage." per hit ✓
- [x] Multi-hit combo — damage announcements queue without interrupting each other ✓
- [x] Open mod settings (F4), find "Ally health warnings" — toggle Off, verify no HP warnings in battle ✓
- [x] Toggle "Ally status ailments" Off — verify no ailment announcements ✓
- [x] Toggle "Player damage dealt" Off — verify no damage numbers announced ✓
- [x] All three settings persist in settings.json after game restart ✓

## Pending Tests (Mod Settings Menu) — MOSTLY TESTED (2026-03-05)

- [x] F4 opens menu, hear "Mod settings menu. Save sound: On. Item 1 of 10." ✓ (10 items, not 7 — 3 battle settings added)
- [x] Up/Down arrow keys navigate items, hear label, value, and position ✓ (tested via gamepad)
- [x] Left/Right on toggle item flips On/Off ✓
- [x] Left/Right on volume item changes by 10% (0% to 100%) ✓
- [x] Left/Right on dialogue mode cycles Full text / Name only when voiced ✓
- [x] Escape or F4 again closes menu, hear "Settings saved. Menu closed." ✓ (tested via gamepad B button)
- [x] Gamepad: L1+L3 opens menu ✓
- [x] Gamepad: D-pad Up/Down navigates, D-pad Left/Right changes values ✓
- [x] Gamepad: Circle/B closes menu ✓
- [x] Settings persist after closing and reopening menu ✓
- [x] Settings persist in settings.json after game restart ✓
- [x] Nav overlay does NOT activate while mod menu is open ✓ (nav opened before menu, menu took over)
- [x] Game input is blocked while mod menu is open (no character movement, no other menus) ✓ (fixed: SuppressAllGameInput flag on GameInputManager hooks)

## Known Issues / Future Work

- **Bug: Enemy proximity sound ignores mod settings** — FIXED & CONFIRMED (2026-03-05):
  Fix: added enabled check + per-frame volume sync. Changed sound to Enemynearby.wav. Tested working.

- **Bug: Game crashes when player uses a battle skill** — FIXED (2026-03-05):
  DoDamage hook had `ref DamageResult` parameter — DamageResult is an IL2CPP value type
  (`sealed class : Il2CppSystem.ValueType`) which corrupted Harmony's trampoline marshaling.
  Fix: replaced with DoCollisionReceiveAction hook (CallerCount 2, no ref value types).
  Attacker obtained via `attackCollision.OwnerCharacter` instead of direct parameter.
  **Rule: NEVER hook IL2CPP methods with `ref` value type parameters (extends Il2CppSystem.ValueType).**

- **Bug: Battle skill menu triggers stale announcement on next camp open** — FIXED:
  All sub-screen gates now preserve their `_xxxWasActive` and `_xxxLastIndex` state when
  the root menu cursor moves away, preventing stale re-activation announcements.
  Same fix also resolved stale item announcements on shop open and camp menu scrolling.

- **Bug: Equip screen missing category name when slot is empty** — FIXED:
  Slot list now reads category before item name (e.g. "Weapon: Swift sword").
  Empty slots read "Category: None". Root cause was _equipItemListActive using
  activeInHierarchy (always true) — slot polling never ran. Fixed by using
  UICampEquipSelector.currentState (State.EquipType vs State.Item) instead.

- **Camp status detection** — FIXED: Both activeInHierarchy and root-menu-hidden detection
  failed (root menu selector also stays activeInHierarchy=true in sub-screens). Now fully
  hook-driven via UICampStatusSelector.UpdatePresenter (fires on open + character tab change).

- **Bug: Auto-walk arrival interrupts tutorial/notification speech** — FIXED:
  AnnounceArrival() checks if something was spoken in the last 0.5s via
  ScreenReader.GetRecentMessage(). If so, combines arrival first + interrupted
  message second into one announcement so the user hears both.

- **Bug: L1 nav blocked after camp menu** — FIXED: Camp closure detection used
  gameObject.activeInHierarchy which stays true after camp closes. Changed to
  WindowComponent.IsOpened property which properly reflects open/closed state.

- **Bug: Nav menu opened during camp menu via L1** — FIXED: IsOpened returns false
  during the camp window's opening animation (~36ms), causing IsCampOpen to be
  cleared immediately. Added 1-second grace period after Open postfix fires.
  Also added IsCampOpen gate in Main.ProcessGamepad (L1 press) and
  IsFieldFree check in NavigationHandler.ToggleNavList (keyboard NumPad 5).

- **NPC functional role + name combining** — shop/inn/guild NPCs now shown as e.g.
  "Equipment shop (Hahn)". Needs more in-game testing as more NPCs are encountered.

- **Counter NPC NavMesh fix** — FIXED: Functional NPCs (shops, inns, guilds, collectors,
  facilities) behind counters were filtered out by the NavMesh reachability check because
  no walkable path exists through the counter. Now these NPC types skip the reachability
  filter. Auto-walk uses partial NavMesh path to walk the player to the counter, then
  announces arrival. Player faces the NPC and can press action to interact.

- **Map exit names** — FIXED: Now resolved from game data at runtime via
  ParameterManager.GetFieldParameter(fieldmapID).FieldmapNameID → TextManager.GetMessage().
  Buildings show real names instead of codes like "22A". Results cached per session.

- **Gamepad nav menu** — IMPLEMENTED AND TESTED. L1 hold-to-open with D-pad navigation.
  See Key Bindings (Mod) section above for full control scheme.

- **Auto-walk exit compass direction** — IMPLEMENTED AND TESTED (2026-03-07): When auto-walking
  to an exit-type target (Exits, Stairs, Doors, Warp Points), the arrival message now includes a
  camera-relative compass direction so the player knows which way to walk to pass through the exit.
  E.g. "Arrived at Building entrance to Arlia. Exit is to the North East." Directions are computed
  relative to the camera orientation (North = stick forward/up), not world axes.

- **World map navigation** — OVERHAUL IN PROGRESS (2026-03-22):
  - **Architecture (completed 2026-03-21):**
    - NavigationHandler.Worldmap.cs fully separated from field map logic (no shared Update code)
    - Movement: stick injection via GetPlayerControlStick postfix (GetLeftStick doesn't work on world map — native pipeline)
    - GetPlayerControlStick CallerCount(0) but Harmony patches still intercept native calls (proven pattern)
  - **WorldmapPathfinder.cs — REWRITTEN (2026-03-22), NEEDS TESTING:**
    - **Two-layer walkability system:**
      - Layer 1: FieldManager.CanMove(x, y) — game's baked 1m walkability grid (terrain, ocean, cliffs)
      - Layer 2: Physics.OverlapSphere on layers 22/23 — Col_Obstacle colliders projected onto grid
    - Both layers combined give complete obstacle knowledge at 1m resolution
    - Binary heap A* priority queue — handles 200K+ cell grids efficiently
    - Grid: 1m cells (Stride=1), 300-cell padding, max 800x800 dimension
    - Snap-to-walkable: 30 cell radius (locations like Krosse City sit on non-walkable cells)
    - Stuck detection: 2s interval, diagnostic logging of colliders at stuck position
  - **Key discoveries (2026-03-22):**
    - FieldManager.CanMove(x, y) — game's own walkability grid at 1m resolution!
      Uses WorldGridData.alightFlag. CallerCount(3), safe to call.
    - GetWorldGridDataGridPosition(ref Vector3) — world-to-grid conversion
    - GetWorldGridDataPosition(int x, int y) — grid-to-world conversion
    - IsExistWorldGridData() — checks if grid is loaded
    - Game grid cell size is exactly 1.0m in both X and Z
    - CanMove tracks terrain/ocean/cliffs but NOT Col_Obstacle colliders
    - Col_Obstacle (layers 22/23) DO block player on world map (unlike field maps)
    - Col_Obstacle colliders are NOT stored in any game data structure — only exist as live Unity physics objects
    - CalcHeight with different layer masks shows NO difference (obstacles invisible to all CalcHeight variants)
    - CalcHeight with ref tag returns "Untagged" or "Rock" — not useful for obstacle detection
    - WorldGridData fields: encountIDList, footstepType, continentID, survivalAreaID, alightFlag, fishingWaterPlaceID, locationID (NO obstacle data)
    - Previous CalcHeight-only approach failed because it couldn't see Col_Obstacle physical barriers
    - Previous OverlapSphere-only approach (without CanMove) blocked too many cells (5193 obstacles)
    - Combined approach (CanMove + OverlapSphere) is the correct architecture
  - **Other fixes completed (2026-03-21):**
    - IsFieldFree grace period: tolerates 10 frames of EventManager.IsRunning flicker at terrain transitions
    - CheckFloorChange: uses FieldManager.IsWorldmap() directly instead of _isWorldmap flag
    - _autoWalkDifferentFloor: distance guard prevents premature arrival on stairs (field maps)
    - Arrival radii: chests 1.3m, enemies 1.8m, locations 10m fallback via TryEnterWorldmapLocation
  - **Next steps (testing needed):**
    - Test Salva → Arlia (short distance, previously worked)
    - Test Salva → Krosse City (long distance, previously failed)
    - If Col_Obstacle blocking is too aggressive again (no path), may need to reduce padding or
      use ClosestPoint checks instead of bounding box projection for obstacle marking
    - If path found but character still gets stuck, investigate if specific Col_Obstacles don't
      actually block movement (some may be passable like on field maps)
    - Future: Psynard (flying mount) support
    - Future: use pathfinder for nav list reachability filtering

- **Floor change announcements** — IMPLEMENTED AND TESTED (2026-03-07):
  - Polls player Y position each frame in CheckFloorChange()
  - Announces "Went upstairs." / "Went downstairs." when Y changes by 2+ units
  - 1.5 second cooldown prevents rapid-fire on long staircases
  - Resets on map change to avoid false triggers between areas
  - Auto-walk now accepts partial NavMesh paths for targets on different floors
    (Y difference > 2 units) instead of saying "Cannot reach"
  - Floor-aware arrival logic (2026-03-08): arrival proximity check skipped for
    different-floor targets (prevents false "arrived" when directly above/below).
    At partial path end, announces "Target is above/below you — look for stairs"
    instead of running endlessly. Tested — NavMesh sometimes finds full path
    including stairs (works perfectly), fix is safety net for partial paths.
  - Dynamic floor re-evaluation (2026-03-08): _autoWalkDifferentFloor is now cleared
    each frame if player reaches the same floor as target — prevents infinite walk
    when player goes upstairs to reach NPC but proximity check stayed disabled.
  - Floor-aware NavMesh sampling (2026-03-08): SampleNavMeshFloorAware() tries tight
    radius (1.0) first to stay on correct floor, then falls back to full radius (5.0).
    Y-override removed (2026-03-08): previously overrode sampled Y back to original
    when floor difference exceeded threshold, but this created positions off the NavMesh
    surface causing PathInvalid. Now uses sampled NavMesh position as-is and trusts
    CalculatePath to determine connectivity. Fixes Krosse Castle exit and Overworld
    town gate being falsely filtered as unreachable. ✓ TESTED
  - NOTE: Krosse Guild exit shows PathPartial (genuinely disconnected NavMesh) —
    may become accessible later in story progression. Monitor on revisit.

- **World map fast travel menu** — IMPLEMENTED AND TESTED (2026-03-07):
  - WorldMapHandler.cs: polling-based (same pattern as shop/camp — native-only navigation)
  - Detects UIWorldMapWindow via FindObjectOfType, polls IsOpened for open/close
  - Three-level hierarchy: Location (cities/dungeons with tabs) → Sub-areas → Fast travel points
  - Point selector uses two data types: UIWorldMapLocationListItemData (sub-areas) and UIWorldMapLocationListItemFastTravelData (destinations) — both handled via dual TryCast
  - Unavailable items announced with suffix. Tab changes (City/Dungeon) announced.

- **Bug: First item not announced in camp sub-screen lists** — FIXED (2026-03-07):
  Harmony hooks fire during game's Update (before MelonLoader OnLateUpdate), but the polling
  flags gating them were only set in OnLateUpdate — always one frame too late. Fixed by
  replacing stale flags with live game state reads. Applied to: equip items, formation,
  skills, battle skills (leveling + setting), tactics operations. Equip confirmed working.

- **Bug: Double periods in equip item names and other game text** — FIXED (2026-03-07):
  Game text fields already end with periods. Manual `.Append(". ")` created "Swift sword.. "
  Fixed by using AppendSentence() helper across all hooks that handle raw game text.

- **Bug: Enhance menu shows wrong data when switching between CombatPoint/BattleSkillPoint** — FIXED:
  When navigating between CombatPoint and BattleSkillPoint within the Enhance sub-menu, both passed
  the same IsEnhanceBattleSkillMenu() gate, so _battleSkillWasActive stayed true and inner selectors
  were never re-cached. Combat skills showed missing level/BP on first visit; battle skills showed
  the last combat skill's BP cost. Fix: track _lastBattleSkillMenuItem and re-cache when it changes.

- **Private action notification** — IMPLEMENTED AND TESTED (2026-03-07):
  - PrivateActionHandler.cs: polls ParameterManager.GetLocalityParameter(FieldmapID).IsPrivateAction
  - Plays PrivateAction.wav + screen reader "Private action available. Press Square." once per town visit
  - Volume slider in mod settings menu (0% = off, default 70%)
  - Game has NO native audio cue for PA availability — purely visual icon only

- **Dialogue choice menus** — IMPLEMENTED (2026-03-08), PENDING TEST:
  - DialogueChoiceHandler.cs: announces Yes/No and multi-choice menus during NPC conversations
  - Polling-based activation (finds UIConversationWindow.selectChoiceSelector, detects presenter visibility)
  - Hooks on ShowSelectChoiceMessage capture title text when available (bonus — not relied upon)
  - Index polling for navigation (native-only cursor movement, same pattern as camp menus)
  - Inn Yes/No uses ShowSelectChoiceDirectMessage (native-only call chain) — hook alone missed it
  - Loc keys: dialogue_choice_open_with_title, dialogue_choice_open, dialogue_choice_item

- **Database sub-menu accessibility** — IMPLEMENTED AND TESTED (2026-03-08):
  - CampMenuHandler.Database.cs: partial class with all 6 Database sub-screen handlers
  - Tutorial: browse with name/New/position, locked says "Locked", confirm reads title+description
  - Enemy Picture Book: browse with name/position, locked says "Unknown enemy", confirm reads full stats (HP/EXP/Fol/drops/habitat/boss)
  - Item Picture Book: browse with name/position, locked says "Unknown item", confirm reads name+description
  - Fish Picture Book: browse with name/position, locked says "Unknown fish", confirm reads full details (rare/crown/shadow/habitat/caught/length)
  - Location Picture Book: browse with name/position, locked says "Undiscovered", confirm reads name+discovered by+description
  - Player Data: virtual cursor (no native list selector) — Up/Down steps through 24 stats across 3 categories (Battle Data, Collection Data, Other Data), no wrapping, no position indicator
  - All gates use specific root menu item names (Tutorial, EnemyList, ItemPictureBook, FishPictureBook, Location, PlayerData)
  - Stale-seed pattern prevents spurious announcements on camp open

## Code Cleanup (2026-03-01)

Cleanup branch `claude-mod-cleanup` merged to master. Key changes:

- **File splitting:** CampMenuHandler split into 7 partial files (core, BattleSkill, Equip, Formation, Items, Party, Status). NavigationHandler split into 4 (core, AutoWalk, Build, Patches).
- **New shared utilities:** `TextUtil.cs` (StripTags consolidation), `FieldState.cs` (IsFieldFree consolidation)
- **Config slider bug fixed:** gauges now read `currentIndex` instead of animated `value.text` — values are correct immediately
- **Silent catches fixed:** 6 bare `catch {}` blocks replaced with proper logging
- **Dead code removed:** unused hook registration, dead localization key, commented-out hotkey code
- **Helpers extracted:** `SortAndFilterUnreachable()` (replaced 8 copies), `SuppressNavInput()`, `AppendSkillInfo()`
- **Hardcoded strings moved to Loc.Get():** GameOverHandler retry/title, CampMenuHandler.Party position/status
- **OnSceneChanged added to CampMenuHandler:** prevents stale IsCampOpen if scene changes while camp is open
- **IsFieldFree now checks ShopHandler.IsShopOpen** in NavigationHandler (was missing before)
- **DIAG logs removed:** ~30 unconditional MelonLogger.Msg("DIAG:...") lines removed from ConfigMenuHandler
- **Deferred (low priority):** UpdateXxx polling pattern helper, StripControllerPrefix consolidation across NotificationHandler/GamepadMenuHandler

## Code Cleanup (2026-03-04)

Stale-open check helper consolidation. Key changes:

- **New shared utility:** `SubScreenState.cs` — consolidates _wasActive/_suppressHeading/_lastIndex pattern into reusable class with CheckEntry(), SeedOnOpen(), SuppressNextHeading(), Reset() methods
- **9 sub-screens refactored:** Items, Equip, Formation, Skill, BattleSkillSetting, Party Formation, Assist Formation, Tactics — each replaced 2-3 repeated fields with a single SubScreenState instance
- **Open postfix simplified:** 7 identical try-catch stale-suppress blocks replaced with StaleSuppressIfActive() helper; Equip and BattleSkillSetting blocks expanded to seed child selector indices
- **Bug fixed: camp close announced root menu item** — _menuSelector now nulled on window close (prevented stale "Item, 1 of 10" announcement)
- **Bug fixed: sub-screen content announced on root menu highlight** — Equip slot list and BattleSkillSetting slot list indices now seeded in Open postfix (prevented spurious child announcements when just highlighting root item)
- **Not changed:** BattleSkill main handler (hook-driven), Status (hook-driven), ShopHandler, BattleMenuHandler, GameOverHandler

## Architecture Decisions

- (none yet)

## Key Bindings (Mod)

REWORKED 2026-08-30 — single source of truth is `ModKeys.cs`; full user-facing
reference in `docs/mod-bindings.md`. NumPad removed entirely (numpadless
keyboards); gamepad modifier moved L1 → L2 (L1 = game's pickpocket).

### Keyboard
- F1: Help. F2: Dialogue voice mode. F3: Read Fol. F4: Mod settings menu. F12: Debug (runs binding dump on enable)
- Minus / Equals: Previous / next nav category (modeless — no open/close; auto-builds/refreshes the background list)
- Left / Right bracket: Previous / next nav item
- Backslash: Auto-walk to selected item / cancel active walk
- Battle pause open: Minus / Equals = tier down/up, brackets = character prev/next (same keys, different context)
- H (camp menu open): Read story hint (fallback Quote if dump shows clash)
- P (Quick Recovery overlay): Read party status (fallback Semicolon)
- F5–F11: Debug-only diagnostics (unchanged)

### Gamepad (modifier = L2)
- Hold L2: Open navigation overlay (field only, not in menus/battle)
- D-pad Up/Down (while L2 held): Switch category
- D-pad Left/Right (while L2 held): Navigate previous/next item
- Left stick up (while L2 held): Auto-walk to highlighted item
- Release L2: Close navigation overlay
- L2 press during auto-walk: Cancel auto-walk and reopen overlay
- L2+L3: Toggle mod settings menu
- L2+R3: Read current Fol
- L3 (camp menu open): Read story hint
- L3 (Quick Recovery overlay): Read party status
- Battle pause open: L1 = tier up, R1 = tier down (free during pause; unchanged)
- While L2 held, game actions bound to L2 are suppressed via a dynamic set built
  from live bindings (RebuildModifierSuppressSet in NavigationHandler.Patches.cs)

## Architecture Notes

- Runtime: net6, Unity 2021.3.22f1, IL2CPP, 64-bit
- Game uses Unity NEW Input System — use Keyboard.current[Key.Fx].wasPressedThisFrame (NOT Input.GetKeyDown)
- Game singletons all use ClassName.Instance pattern (SingletonMonoBehaviour)
- Game code namespace: Il2CppGame — must add `using Il2CppGame;` to access game classes
- Required csproj references for IL2CPP: Il2Cppmscorlib.dll + Il2CppInterop.Runtime.dll

## Notes for Next Session

### Skill development fix awaiting test (2026-03-19)
- Fix deployed. User reported: specialty SP costs showed "1" always, some skills showed max when not.
- Log confirmed: Scouting cost 20 SP (125→105) but displayed "SP: 125 / 1".
- Fix uses `UICommon.CalcNeedSpecialSkillForLevelUp()` for fresh specialty costs.
- Knowledge skill max verified against `ConstSkillParameter.levelupSp.Count`.
- See "Skill Development Screen Fix" section above for full test checklist.
- If test fails: check MelonLoader log for errors in `SkillInfoPresenter_Set_Postfix`.
  The `CalcNeedSpecialSkillForLevelUp` call (CallerCount 2) should be safe from managed code.

### Audio clip ready for integration
- **File:** `E:\StarOcean\audio_cue.wav` — 10-second clip (PCM WAV, 44100 Hz, 16-bit mono, ~861 KB)
- **Source:** YouTube clip trimmed from 5s to 15s
- **User has a specific use in mind** — to be implemented in a future session

### Navigation improvements (2026-03-08, late session)
- **Architecture review:** Thoroughly analyzed NavMesh pathfinding, game's AIPathFinder A*,
  NavMeshAgent, and OnMove() alternatives. Conclusion: current approach (NavMesh.CalculatePath
  for field maps, game A* for world map) is optimal. No rewrite needed.
- **Field map stuck detection:** Added 2-second interval check (FieldStuckMinMove=0.5 units).
  Two-strike system: first stuck → recalculate path; still stuck → cancel + announce
  "Path blocked to [target]. Auto-walk stopped." PENDING TEST
- **Physics.Linecast POI filtering:** Secondary filter after NavMesh reachability. Fires
  linecast at eye height, removes items blocked by non-trigger colliders (solid walls).
  Counter NPCs skip this check. Errors default to keeping item. PENDING TEST
- **Floor labels in nav list:** Items with Y difference > FloorChangeThreshold (2.0 units)
  get "(above)" or "(below)" appended to their label. Applied to all categories. PENDING TEST

### Current work (2026-03-08)
- NavMesh reachability fix: removed Y-override from SampleNavMeshFloorAware. The override
  created positions off the NavMesh surface causing PathInvalid, which falsely filtered exits
  like Krosse Castle gate (trigger Y=8.8, NavMesh Y=6.6). Now uses sampled NavMesh position
  as-is and trusts CalculatePath connectivity check. ✓ TESTED
- Krosse Guild exit: PathPartial (genuinely disconnected NavMesh), likely story-gated. Monitor.
- Auto-walk multi-floor NavMesh fix: floor-aware sampling (SampleNavMeshFloorAware) prevents
  NavMesh.SamplePosition from snapping to wrong floor in multi-story buildings (inn Tourist bug).
  Tight radius (1.0) tried first, falls back to full (5.0). ✓ TESTED
- Auto-walk dynamic floor re-evaluation: _autoWalkDifferentFloor cleared each frame once player
  reaches same Y level as target. Prevents infinite walk after going upstairs. ✓ TESTED
- DialogueChoiceHandler: rewritten to polling-based activation (was hook-only, hooks don't fire
  for native-only call chains like inn ShowSelectChoiceDirectMessage). Now detects presenter
  visibility via UIConversationWindow.selectChoiceSelector. PENDING TEST for inn Yes/No.
- Auto-walk field exit fix: auto-walk uses transform.position which bypasses Unity trigger
  colliders, so FieldMapjumpCollision (building doors, gates) never fired. Added TryEnterFieldExit()
  — calls ChangeFieldmap() directly on the nearest exit trigger, same approach as world map entry.
  Now announces "Entering [building]" instead of stopping outside. ✓ TESTED

### Current work (2026-03-07)
- R2 ally switching in battle: BattleTargetHandler now announces controlled ally on R2 press ✓ TESTED
  - Polls controlPlayerIndex + ControlPlayerChangeMode state (6) for first-press detection
  - Announces: name, HP (exact), MP (exact), active buffs/debuffs
  - Index silently seeded at battle start to avoid unwanted announcement
- Equipment Wizard handler: new EquipWizardHandler.cs — polls UISystemWindow.IsShowingEquipWizard,
  announces heading + description + equipment comparison + Yes/No/Reject All menu. Pending test.
- First-item fix: all camp menu hooks now use live game state reads instead of stale polling flags.
  Equip confirmed working. Formation, skills, battle skills, tactics pending test.
- Double-period fix: AppendSentence() applied to all raw game text in hook string builders.
  Equip confirmed. Other menus pending test.
- FieldState.IsFieldFree() hardened: added PauseManager.IsPause + EventManager.IsRunning checks
  - Dialogues, cutscenes, notifications, tutorials now block nav menu from opening
  - Auto-walk cancels immediately when any of these trigger mid-walk
  - All handlers using IsFieldFree() benefit (navigation + enemy proximity)
- Navigation distance label changed from "units" to "meters" (Unity 1 unit = 1 meter)

### Current work (2026-03-05)
- BattleStatusHandler: battle status announcements ✓
  - New file: BattleStatusHandler.cs (~310 lines)
  - Hook: BattleCharacter.DoCollisionReceiveAction (CallerCount 2, prefix+postfix) — HP tracking + damage dealt
  - Hook: CharacterParameter.SetBuffDebuffState (CallerCount 19, postfix) — status ailment detection
  - CRASH FIX: DoDamage had ref DamageResult (IL2CPP ValueType) that crashed Harmony trampolines; replaced with DoCollisionReceiveAction + attackCollision.OwnerCharacter for attacker
  - Ally HP below 50%, below 25%, knocked out — queued announcements, downward transitions only
  - Ally negative status ailments (poison, paralyze, petrify, confusion, silence, faint, death, stop, swallowed, controlled)
  - Player-controlled character damage dealt — announces damage amount per hit
  - All announcements use SayQueued (non-interrupting queue)
  - 3 new ModSettings toggles: AllyHealthWarningEnabled, AllyStatusAilmentEnabled, PlayerDamageDealtEnabled
  - 3 new mod menu items added to ModMenuHandler
  - 5 new Loc strings + 3 menu label strings
- ModMenuHandler: screen-reader-driven mod settings menu ✓
  - New file: ModMenuHandler.cs (~250 lines)
  - Keyboard: F4 to open/close, arrow keys to navigate/change, Escape to close
  - Gamepad: L1+L3 to open/close, D-pad to navigate/change, Circle to close
  - 10 settings: 3 sound toggles, 3 volume sliders (10% steps), dialogue voice mode, 3 battle announcement toggles
  - All input blocked while menu open (keyboard + gamepad)
  - Auto-saves to settings.json on close
  - Loc keys added, help text updated

### Previous work (2026-03-21)
- Super Specialty menu accessibility (CampMenuHandler.SuperSpecialty.cs) ✓ TESTED
  - New file: CampMenuHandler.SuperSpecialty.cs (~300 lines, partial class)
  - Context A: IC tab 2 ("Super Special Skills") — polls currentIndex, reads skillName/skillDescription
    from UISpecialSkillInformationPresenter GameText fields, reads conditions from
    superSpecialSkillLearningPresenter sub-presenter
  - Context B: Enhance → Skill → R2 (Skill Learning) — completely separate menu system
    using UICampSkillLearningSelector (on UICampSkillSelector.learningSelector)
    Polls currentDataList items (UISkillLearningListItemData: skillName, level),
    reads info from UISkillLearningInformationPresenter
  - Both contexts share AppendLearningConditions() for condition1/condition2 text
  - Loc keys: ss_screen, ss_not_learned, ss_requires, ss_position
- BattleResultHandler: bonus announcements (chain, Training, Open Eyes) confirmed working

### Previous work (2026-03-04)
- SubScreenState helper: stale-open check consolidation ✓
  - New file: SubScreenState.cs
  - 9 sub-screens refactored to use helper
  - Two bugs found and fixed during refactor (camp close + root highlight)
- BattlePauseHandler: all bugs fixed and tested ✓
  - Ally name: ParameterManager chain (charaNameID → TextManager) — shows "Claude"
  - HP/MP: direct CharacterParameter reads — shows real values
  - Gamepad tier cycling: moved from D-pad (conflicts with game's native character cycling
    on ALL directions) to L1/R1 shoulder buttons
  - Status conditions tier confirmed working (Stun on enemy)

### Previous work (2026-03-03)
- BattleMenuHandler: battle command menu (Triangle) fully implemented and tested
  - New file: BattleMenuHandler.cs (~1000 lines)
  - Phases: root menu, items, spells, target selection, tactics/strategy
- BattlePauseHandler: initial implementation with tiered info system
  - New file: BattlePauseHandler.cs (~500 lines)

### Previous work (2026-03-02)
- BattleResultHandler: learned skills now announce with description (UICommon.CreateBattleSkillInformationData)
- BattleResultHandler: bonus announcements added (chain, Training, Open Eyes) ✓ TESTED (2026-03-21)
- Old Loc key `battle_result_learned_skills` replaced with `battle_result_learned_skill` (name + desc)
  and `battle_result_learned_skill_noDesc` (name only fallback)

### Previous session (2026-03-01)
- BattleTargetHandler: L2 target cycling announces enemy name, HP%, shield%, leader, buffs/debuffs ✓ TESTED
- SaveNotificationHandler: save sound cue on manual/auto save ✓ TESTED
- ModSettings: JSON persistence for sound toggle/volume settings
- AudioCuePlayer: refactored to file-based WAV (dodge + save sounds from disk)
- TextUtil: shared ParseCharaNameID (was duplicated in NavigationHandler)
- Combat skill enhance: fixed level display (was 0/0), reordered to Name/Level/BP/Desc/Upgrade ✓ TESTED

### Battle skill / combat skill menu separation (2026-03-01)
- **Root battle skills** (Camp → BattleSkill): NEW detailed tactical readout
  - Format: Name. MP. Type. Target. Element. Range. Effect. Description. Level.
  - Target type resolved from ParameterManager.GetBattleSkillParameter(battleSkillID)
  - ✓ TESTED — root battle skills reading correctly
- **Enhance battle skills** (Camp → Enhance → BattleSkillPoint): upgrade-focused readout
  - Format: Name. MP. Level. SP balance/cost. Effect. Description. Upgrade: bonuses.
  - ✓ TESTED — working, user confirmed "rest works fine"
- **Enhance combat skills** (Camp → Enhance → CombatPoint): upgrade-focused readout
  - Format: Name. Level X of Y. BP balance/cost. Description. Upgrade: effect.
  - ✓ TESTED — combat skill level now read from UICampCombatSkillListItemData.skillLevel
    (UIBattleSkillInformationData.skillLevel is always 0 for combat skills)
  - Max level derived from ConstCombatSkillParameter.levelupBp.Count via ParameterManager
  - effectDescription used as upgrade label ("Upgrade: Effect chance up")
  - Duplicate text suppressed (e.g. Body Control where effect == description)
  - Combat skills have no MP cost (naturally skipped)
- **Code separation**: IsBattleSkillRelatedMenu() split into IsRootBattleSkillMenu() + IsEnhanceBattleSkillMenu()
- **Assignment screen**: unchanged, still uses AppendSkillInfo() (root only)
- Files changed: CampMenuHandler.BattleSkill.cs (rewritten), Loc.cs (new strings), CampMenuHandler.cs (RuntimeHelpers)

### Battle target lessons learned
- ShowSelectedTargetEnemy (CallerCount 3) does NOT fire for L2 target switching — likely for skill targeting
- SetControlPlayerTarget (CallerCount 7) is the correct hook for L2 target changes
- CharacterParameter.CharacterName is empty for battle enemies — use ConstEnemyParameter.charaNameID fallback
- BattleManager.stateMachine.currentState == 5 detects TargetChangeMode (for single-enemy re-reads)
- Spectacles is the ONLY see-through mechanism; no Analyze spell exists
- Elemental resistances only shown in pause menu (not during active combat)

### Previous test results (still valid)
- All nav, camp, shop, battle result, skills, save, status features working as documented above

### Key lesson learned
- Camp menu root selector activeInHierarchy stays true even when sub-screens are open.
  This means activeInHierarchy-based detection fails for ALL camp sub-screens.
  Hook-driven detection (used for status) is the reliable alternative when polling fails.

### Camp menu architecture (critical for sub-screen work)
- Root: UICampMenuSelector (menuSelector field on UICampWindow) — DONE
- Sub-screens are separate selector classes, all fields on UICampWindow:
  - itemSelector (UICampItemSelector)
  - statusSelector (UICampStatusSelector)
  - equipSelector (likely UICampEquipSelector)
  - battleSkillSelector, operationSelector, skillSelector, formationSelector
- Each sub-screen needs its own polling loop or hook
- Pattern: access the selector via UICampWindow field, poll currentIndex
- Item names: UICampMenuItemData.menuItem is UIDefine.CampMenuItem enum (toString gives
  e.g. "Status", "Item", "Equip", "BattleSkill", "Formation") — consider adding friendly
  Loc entries (e.g. "BattleSkill" → "Battle Skills") in a follow-up

### CRITICAL: Camp menu patching lessons learned
- Camp navigation is driven entirely from native C++ — NO Harmony hook fires for navigation
  (tested: UpdatePresenter, OnMoveCursor, OnUp, OnDown, Show, UICanSelectedListItemPresenterBase.OnSelected — all failed)
- Methods with CallerCount(0) called only from native code are NOT interceptable via Harmony
- Polling currentIndex from Main.UpdateHandlers() is the correct approach for this menu
- GetComponentInChildren<T>() fails for camp selectors — use the named fields on UICampWindow
- UICampCommandSelector is NOT the root menu — it may be unused in the demo or is a sub-selector

### Animation system notes (for future reference)
- Game uses FieldBillboardObject.PlayMoveAnimation(FieldAnimationKind) as the animation trigger
- CharacterAnimationAccessor.LateUpdate() (MonoBehaviour) resets animation to "Unique" each frame when no input
- MelonLoader OnLateUpdate fires BEFORE game MonoBehaviour LateUpdates — cannot override there
- Solution: Harmony prefix on PlayMoveAnimation blocks non-Run calls on the player during approach
- Player run speed: GetMoveSpeed(true) = 6.5 units/second

### Next feature candidates
- Navigation: Enemies — DONE ✓ (parsed names, TextManager doesn't resolve on field)
- Navigation: Events — DONE ✓ (tested)
- Navigation: Save points — DONE ✓
- Navigation: Stairs — DONE (pending dungeon test)
- Navigation: Doors (stone only) — DONE (pending dungeon test)
- Navigation: Warp Points (panels, circles, platforms) — DONE (pending dungeon test)
- Navigation: Flavor chat triggers (FieldFlavorChatCollision) — party banter spots
- Operations child screens: Party Formation ✓, Formation ✓, Assist Formation ✓, Tactics ✓ (all confirmed 2026-07-11)
- Camp sub-screen: skill learning (UICampSkillLearningSelector — complex, deferred)
- Battle pause menu handler (detailed enemy info: element resistances, buffs, HP when spectacled)
- Battle status announcements (player HP/MP during combat)

### Notes
- Build command: `dotnet build SO2RAccess.csproj` (auto-copies to Mods folder)
- Map exit names now resolved from game data automatically (ConstFieldParameter + TextManager)
