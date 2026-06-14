# SubScreenState.cs (129 lines)

Tracks entry/exit state for a camp sub-screen that uses activeInHierarchy polling. Consolidates the repeated _wasActive / _suppressHeading / _lastIndex pattern into a reusable object. Not suitable for hook-driven screens (BattleSkill, Status) or nested child selectors.

IMPORTANT: When stale-seeding in the Open postfix, child selectors (e.g. equip slot list) must ALSO have their own _xxxLastIndex seeded and _xxxWasActive set to true; this class only manages the OUTER selector. Preferred pattern for new child selectors: skip _wasActive tracking entirely, just compare idx == _xxxLastIndex (avoids stale-seed pitfall).

namespace: SO2RAccess (line 3)
usings (non-System / notable only): (none)

## sealed class SubScreenState (line 29)
Reusable state tracker for polled camp sub-screens.

fields/properties (declaration order):
- WasActive : bool (line 31)  — true while selector's gameObject was active last frame; private setter
- SuppressHeading : bool (line 36)  — when true, next activation suppresses heading announcement and preserves pre-seeded LastIndex; cleared by CheckEntry; private setter
- LastIndex : int (line 40)  — last announced cursor index; -1 forces first-item announcement; public setter

methods (declaration order):
- void Reset() (line 46)
  - note: Sets WasActive=false, SuppressHeading=false, LastIndex=-1. Call in Open postfix before stale-seed checks.
- void SeedOnOpen(int currentIndex) (line 57)
  - note: Sets LastIndex=currentIndex and SuppressHeading=true. For sub-screens that poll an outer index (e.g. Items).
- void SuppressNextHeading() (line 67)
  - note: Sets SuppressHeading=true without changing LastIndex. For sub-screens where index is handled by a child selector or Harmony hook (e.g. Formation, Skill, Equip).
- bool CheckEntry(bool isActive, Action announceHeading, string logLabel, Action onHidden = null) (line 90)
  - note: Standard entry/exit gate. Returns false when selector is inactive (calls onHidden on transition) or just-activated this frame (announces heading unless SuppressHeading, then resets SuppressHeading). Returns true to proceed to index polling. Call after root-menu gate check, before index polling.
