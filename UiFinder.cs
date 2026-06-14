using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Helper for the "lazily find a field UI overlay, then verify it's really
    /// showing" pattern used by polling handlers. The game's field overlays keep
    /// activeInHierarchy == true even when hidden, so callers supply an
    /// <c>isActive</c> predicate (typically activeInHierarchy AND a populated data
    /// list). FindObjectOfType is throttled to once per second while the cache is
    /// empty to avoid per-frame scene scans.
    /// </summary>
    internal static class UiFinder
    {
        /// <summary>
        /// Returns true when <paramref name="cache"/> refers to an active overlay.
        /// If the cache is empty, attempts a throttled FindObjectOfType&lt;T&gt;
        /// (at most once per <paramref name="nextFindTime"/> window). A cached but
        /// inactive overlay is kept (not re-found) — matching the existing handler
        /// behaviour. On a predicate exception the cache is cleared.
        /// </summary>
        public static bool TryGetActiveOverlay<T>(
            ref T cache, ref float nextFindTime, Func<T, bool> isActive)
            where T : UnityEngine.Object
        {
            if (cache != null)
            {
                try { return isActive(cache); }
                catch { cache = null; return false; }
            }

            if (Time.time < nextFindTime) return false;
            nextFindTime = Time.time + 1f;

            try
            {
                cache = UnityEngine.Object.FindObjectOfType<T>();
                if (cache != null)
                {
                    try { return isActive(cache); }
                    catch { cache = null; return false; }
                }
            }
            catch { /* scene scan unavailable this frame */ }

            return false;
        }
    }
}
