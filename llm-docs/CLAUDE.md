# llm-docs — Reference Index

This directory holds reference material generated for AI assistants working on the
**SO2RAccess** mod. It complements (does not replace) the hand-maintained docs in `docs/`.
Read only the file relevant to your task — these are large; don't load them all up front.

## Contents

- **`game-model.md`** — Conceptual model of *Star Ocean: The Second Story R*: genre and
  spatial model, control scheme, battle system, out-of-battle systems (camp menu, Item
  Creation, Private Actions, shops, guild, etc.), a full enumeration of game screens, and
  stat/currency mechanics. Read this to understand *how the game works* before touching a
  feature you're unfamiliar with. Lines flagged `(unverified)` are from training knowledge
  not independently confirmed online.

- **`api-index.md`** — A *finder's index* of the decompiled game source
  (`decompiled/Assembly-CSharp/Il2CppGame/`, ~2,701 files). Tells you where classes live,
  the naming conventions to grep for (`*Manager`, `UI*Selector`, `Const*Parameter`,
  `Field*`, `Battle*`), and groups the important classes by concern. Ends with a
  "Gaps vs docs/game-api.md" section listing source areas not yet documented. Use this to
  locate a real class/method before writing code (CLAUDE.md rule: never guess names).

## How this relates to other docs

- `docs/game-api.md` — the hand-curated, mod-tested API reference (singletons, key bindings,
  confirmed hook points). This is the authoritative "what works" doc; `api-index.md` points
  you at everything else in the source that isn't in it yet.
- `docs/*.md` — reusable accessibility-modding patterns and Unity/MelonLoader/Tolk technique
  guides.
- `project_status.md` — live project tracking and the running log of discovered game facts.
