#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
#if KAMGAM_VISUAL_SCRIPTING
    // Why? See: https://forum.unity.com/threads/unable-to-provide-a-default-for-getvalue-on-object-valueinput.1140022/#post-9138727
    [Unity.VisualScripting.Inspectable]
#endif
    [System.Serializable]
    public class BallisticProjectileSpawnConstraintAngle2D : IBallisticProjectileSpawnConstraint
    {
        public float MinAngle2D = 0f;
        public float MaxAngle2D = 90f;

        /// <summary>
        /// Returns true if the projectile should be allowed to spawn and false otherwise.<br />
        /// It does the check based on the min and max angle 2D.
        /// </summary>
        /// <returns></returns>
        public bool Allow(Vector3 startVelocity, float angle2D, BallisticProjectileConfig config, Transform source,
            Transform target, Vector3? sourcePos, Vector3? targetPos, IMovementPredictor predictor)
        {
            return angle2D >= MinAngle2D && angle2D <= MaxAngle2D;
        }
    }
}