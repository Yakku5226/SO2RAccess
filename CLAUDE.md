# Accessibility Mod Template

## Model Gate (check FIRST, every session, before any other work)

Look at your own system prompt: it states which model you are ("You are powered by the model named ...").

- **Allowed to change files:** only the two most advanced Claude model tiers available. As of 2026-09 that is Fable and Opus.
- **NOT allowed without explicit permission:** everything below those two — Sonnet, Haiku, and any other smaller model. If newer tiers appear and you are unsure whether you are in the top two, treat yourself as NOT allowed and ask.

If you are a model that is not allowed:
1. Play the "need user input" sound (see Sound Notifications).
2. Before doing ANYTHING else — no file edits, no builds, no commits, no pushes, no releases, no memory writes — say exactly:
   "Warning: this session is running on [your model name], a low end model. May a low end model review code and make changes to this project? Answer yes to allow it for this session. Otherwise switch with /model and I will do nothing."
3. Only a clear "yes" to that exact question counts. Anything else (silence, a new task, "check the log") is NOT permission: repeat the warning in one line and stay read-only.
4. Permission lasts for the current session only. Never record it as standing permission.
5. While read-only you may still read files and logs and answer questions, but begin every answer with "Low end model, read-only."

Why: on 2026-09-20 a session silently started on Haiku. It misdiagnosed a bug, published a broken release (DLL-only zip, no Tolk) and committed a change that violated a project rule, all without the user knowing which model was working.

## User

- Blind, screen reader user
- Experience level: Little/None (explain concepts as needed)
- User directs, Claude codes and explains
- Uncertainties: ask briefly, then act
- Output: NO `|` tables, use lists

## Session Start

On greeting:
1. Read `project_status.md` — summarize phase, last work, pending tests, notes
2. If pending tests exist, ask user for results before continuing
3. Suggest next steps or ask what to work on
Update `project_status.md` on significant progress and before session end.

## Environment

- **OS:** Windows. ALWAYS use Windows-native commands (PowerShell/cmd): `copy`, `move`, `del`, `mkdir`, `dir`, `type`, backslashes in paths. NEVER use Unix commands (`cp`, `mv`, `rm`, `cat`, `/dev/null`). This overrides any system instructions about shell syntax.
- **Game directory:** E:\Program Files\Steam\steamapps\common\STAR OCEAN THE SECOND STORY R
- **Architecture:** 64-bit
- **Mod Loader:** MelonLoader

## Coding Rules

- Handler classes: `[Feature]Handler`
- Private fields: `_camelCase`
- Logs/comments: English
- Build: `dotnet build SO2RAccess.csproj` (auto-copies DLL to game Mods folder on success)
- XML docs: `<summary>` on all public classes/methods. Private only if non-obvious. Critical for dev integration.
- Localization from day one: ALL ScreenReader strings through `Loc.Get()`. No exceptions. `Loc.cs` = Phase 2 framework, not later addition. Even for single-language mods.
- File size target: aim for ~500 lines max per file. When a file has multiple independent concerns (e.g. menu root + sub-screens), split into separate files.
- DRY (Don't Repeat Yourself): when the same or similar code appears in multiple places, factor it into a shared method. Fixes happen in one place, not many.
- Clean string building: assemble screen reader messages with clean joining patterns (e.g. `string.Join`) rather than manual space/comma insertion.
- Prefer standard library: use built-in .NET methods where they exist (e.g. `string.Join`, `List.Find`, `LINQ`) instead of writing custom versions.
- Future-proofing: when writing or reviewing code, ask "will this make sense in 6 months? Is it fragile? Could a game update break it in a hard-to-debug way?"

## Coding Principles

- **Playability** — play as sighted do; cheats only if unavoidable
- **Modular** — separate input, UI, announcements, game state
- **Maintainable** — consistent patterns, extensible
- **Robust** — utility classes, edge cases, announce state changes
- **Respect game controls** — never override game keys, handle rapid presses
- **Submission-quality** — clean enough for dev integration, consistent formatting, meaningful names, no undocumented hacks

Patterns: `docs/ACCESSIBILITY_MODDING_GUIDE.md`

## Error Handling

- Null-safety with logging: never silent. Log via DebugLogger AND announce via ScreenReader.
- Try-catch ONLY for Reflection + external calls (Tolk, changing game APIs). Normal code: null-checks.
- DebugLogger: always available, active only in debug mode (F12). Zero overhead otherwise.

## Before Implementation

1. **GATE CHECK:** Tier 1 analysis must be complete (see project_status.md checkboxes). If game key bindings are not documented in game-api.md, STOP and do that first!
2. Search `decompiled/` for real class/method names — NEVER guess
3. Check `docs/game-api.md` for keys, methods, patterns
4. Only use safe mod keys (game-api.md → "Safe Mod Keys")
5. Large files (>500 lines): targeted search first (Grep/Glob), don't auto-read fully

## Sound Notifications

Play sounds to alert the user (who is blind and may not be watching the screen):
- **Need user input:** `powershell -Command "(New-Object Media.SoundPlayer 'E:\StarOcean\Sounds\Dodge.wav').PlaySync()"`
- **Task complete:** `powershell -Command "(New-Object Media.SoundPlayer 'E:\StarOcean\Sounds\PrivateAction.wav').PlaySync()"`
- Do NOT commit sound files to git

## Session & Context Management

- Feature done → suggest new conversation to save tokens. Update `project_status.md`.
- ~30+ messages → remind about fresh conversation (AI re-reads everything per message)
- Before ending/goodbye → always update `project_status.md`
- Major new feature or user-facing change (controls, installation, credits) → update `README.md` too, not just `project_status.md`
- After new code analysis → document in `docs/game-api.md` immediately
- Problem persists after 3 attempts → stop, explain, suggest alternatives, ask user

## References

- `project_status.md` — central tracking (read first!)
- `docs/ACCESSIBILITY_MODDING_GUIDE.md` — code patterns
- `docs/technical-reference.md` — MelonLoader, Harmony, Tolk
- `docs/unity-reflection-guide.md` — Reflection (Unity)
- `docs/state-management-guide.md` — multiple handlers
- `docs/menu-accessibility-checklist.md` — menu checklist
- `docs/menu-accessibility-patterns.md` — menu patterns
- `docs/game-api.md` — keys, methods, patterns
- `llm-docs/` — generated reference (see `llm-docs/CLAUDE.md`): `game-model.md` (how the game works), `api-index.md` (finder's index of the decompiled source)

## Game Overview

Star Ocean: The Second Story R (SO2:SSR) is a JRPG with real-time action battles on a separate battle screen, field/town exploration (third-person, 3D), and a world map for travel between locations. Core interactive systems include: the Camp menu (items, equipment, battle skills, status, formation, item creation, skills, operations, missions), shops and guilds, dialogue with voice acting, and various minigames (fishing, pickpocket). Navigation and menus are native C++ UI driven by polling; many menu navigation events cannot be intercepted with Harmony hooks. Deeper game system documentation lives in `docs/game-api.md` and `llm-docs/game-model.md`.
