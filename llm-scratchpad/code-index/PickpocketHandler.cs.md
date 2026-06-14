# PickpocketHandler.cs (121 lines)

Announces pickpocket menu items on the field. Menu appears when pickpocketing is active and player interacts with an NPC. Navigation is native-only (polling required).
Data source: UIFieldPickPocketSelector.selectChoiceIndex, choiceDataList (UIChoiceData / UIFieldPickPocketChoiceData items: message, rate, itemCount, canDecision).
namespace: SO2RAccess (line 4)
usings (non-System / notable only): Il2CppGame, System.Text

## class PickpocketHandler (line 14)
Polls UIFieldPickPocketSelector each frame; announces heading on open and item name/rate/availability on cursor change.

fields/properties (declaration order):
- _selector : UIFieldPickPocketSelector (line 16)  — cached reference; re-found every 1s if null
- _wasActive : bool (line 17)  — tracks whether menu was active last frame
- _lastIndex : int (line 18)  — last announced cursor index; -1 when reset
- _nextFindTime : float (line 19)  — Time.time threshold for next FindObjectOfType search

methods (declaration order):

- void Update() (line 21)
  - note: finds/verifies _selector (activeInHierarchy + choiceDataList.Count > 0 required); on menu open announces Loc.Get("pickpocket_heading"); on cursor change announces name + rate (strips "%" for TTS) + Loc.Get("ic_unavailable") if canDecision=false + "N of M" position; all in try-catch with debug logging
