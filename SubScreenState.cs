using System;

namespace SO2RAccess
{
    /// <summary>
    /// Tracks entry/exit state for a camp sub-screen that uses activeInHierarchy polling.
    /// Consolidates the repeated _wasActive / _suppressHeading / _lastIndex pattern
    /// into a single reusable object. Not suitable for hook-driven screens (BattleSkill,
    /// Status) or for nested child selectors within a sub-screen.
    ///
    /// IMPORTANT — NESTED CHILD SELECTORS:
    /// This class handles the OUTER sub-screen selector only. If the sub-screen has
    /// nested child selectors with their own manual _xxxWasActive / _xxxLastIndex
    /// tracking (e.g. equip slot list inside the equip sub-screen), those child
    /// trackers are NOT managed by this class. When stale-seeding in the Open postfix:
    ///   1. Call SuppressNextHeading() or SeedOnOpen() on this SubScreenState (outer).
    ///   2. ALSO seed the child's _xxxLastIndex to its current value.
    ///   3. ALSO set the child's _xxxWasActive = true.
    /// If step 3 is missed, the child's first-activation logic will reset its
    /// seeded index to -1, causing a spurious announcement when the root menu
    /// cursor merely highlights the sub-screen's root item (before user confirms).
    /// See CampMenuHandler.cs CampWindow_Open_Postfix equip stale-seed for example.
    ///
    /// PREFERRED PATTERN for new child selectors: skip manual _wasActive tracking
    /// entirely and just compare idx == _xxxLastIndex (see UpdateBattleSkillEquipSlotList).
    /// This avoids the stale-seed pitfall because there's no first-activation reset.
    /// </summary>
    internal sealed class SubScreenState
    {
        /// <summary>True while the selector's gameObject was active last frame.</summary>
        public bool WasActive { get; private set; }

        /// <summary>
        /// When true, the next activation suppresses the heading announcement and
        /// preserves the pre-seeded LastIndex. Cleared automatically by CheckEntry.
        /// </summary>
        public bool SuppressHeading { get; private set; }

        /// <summary>Last announced cursor index. -1 forces first-item announcement.</summary>
        public int LastIndex { get; set; } = -1;

        /// <summary>
        /// Resets all fields to their initial state.
        /// Call in the Open postfix before stale-seed checks.
        /// </summary>
        public void Reset()
        {
            WasActive = false;
            SuppressHeading = false;
            LastIndex = -1;
        }

        /// <summary>
        /// Marks the heading as suppressed and pre-seeds LastIndex with the current
        /// selector index. Use for sub-screens that poll an outer index (e.g. Items).
        /// </summary>
        public void SeedOnOpen(int currentIndex)
        {
            LastIndex = currentIndex;
            SuppressHeading = true;
        }

        /// <summary>
        /// Marks the heading as suppressed without changing LastIndex.
        /// Use for sub-screens where index polling is handled by a child selector
        /// or Harmony hook (e.g. Formation, Skill, Equip).
        /// </summary>
        public void SuppressNextHeading()
        {
            SuppressHeading = true;
        }

        /// <summary>
        /// Standard entry/exit check for a polled sub-screen. Call after the gate
        /// check (root menu item name) and before index polling.
        /// Returns true when the caller should proceed to poll the cursor index.
        /// Returns false when the caller should return (selector inactive or
        /// just-activated this frame).
        /// </summary>
        /// <param name="isActive">gameObject.activeInHierarchy of the selector.</param>
        /// <param name="announceHeading">
        /// Called on genuine first entry to announce the screen heading.
        /// </param>
        /// <param name="logLabel">Short label for DebugLogger messages.</param>
        /// <param name="onHidden">
        /// Optional callback when the selector transitions from active to hidden.
        /// Use to null-out cached sub-selectors.
        /// </param>
        /// <returns>True to continue to index polling; false to return early.</returns>
        public bool CheckEntry(
            bool isActive,
            Action announceHeading,
            string logLabel,
            Action onHidden = null)
        {
            if (!isActive)
            {
                if (WasActive)
                {
                    WasActive = false;
                    onHidden?.Invoke();
                    DebugLogger.LogState($"{logLabel}: selector hidden.");
                }
                return false;
            }

            if (!WasActive)
            {
                WasActive = true;

                if (!SuppressHeading)
                {
                    LastIndex = -1;
                    announceHeading();
                    DebugLogger.LogState($"{logLabel}: selector visible.");
                }
                else
                {
                    SuppressHeading = false;
                    DebugLogger.LogState($"{logLabel}: stale open — heading suppressed.");
                }

                return false;
            }

            return true;
        }
    }
}
