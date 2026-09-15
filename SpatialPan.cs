using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The one camera-relative stereo rule every spatial cue shares (beacons, the
    /// enemy proximity loop): pan is the sideways component of the direction to
    /// the object (-1 left .. +1 right; the mixer applies constant power), rear is
    /// how far behind the camera it is (0 = level with or ahead, 1 = straight
    /// behind). One helper so a cue never pans differently from the others.
    /// </summary>
    public static class SpatialPan
    {
        /// <summary>Objects closer than this (m, flat) sit in the centre: direction is meaningless.</summary>
        private const float CentreDistance = 0.05f;

        /// <summary>Right-hand direction for a flattened camera forward (90° clockwise seen from above).</summary>
        public static Vector3 RightOf(Vector3 camForwardFlat) =>
            new Vector3(camForwardFlat.z, 0f, -camForwardFlat.x);

        /// <summary>Pan and rear amount of <paramref name="targetPos"/> as heard from <paramref name="listenerPos"/>.</summary>
        public static void Compute(Vector3 listenerPos, Vector3 targetPos, Vector3 camForwardFlat,
            out float pan, out float rear)
        {
            Vector3 to = targetPos - listenerPos;
            to.y = 0f;
            float flat = to.magnitude;
            if (flat < CentreDistance)
            {
                pan = 0f;
                rear = 0f;
                return;
            }
            to /= flat;
            pan = Mathf.Clamp(Vector3.Dot(to, RightOf(camForwardFlat)), -1f, 1f);
            rear = Mathf.Clamp01(-Vector3.Dot(to, camForwardFlat));
        }
    }
}
