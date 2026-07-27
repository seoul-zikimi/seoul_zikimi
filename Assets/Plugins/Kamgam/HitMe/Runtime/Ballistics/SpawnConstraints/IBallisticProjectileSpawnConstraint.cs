#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
    public interface IBallisticProjectileSpawnConstraint
    {
        /// <summary>
        /// Returns true if the projectile should be allowed to spawn and false otherwise.
        /// </summary>
        /// <returns></returns>
        public bool Allow(Vector3 startVelocity, float angle2D, BallisticProjectileConfig config, Transform source, Transform target, Vector3? sourcePos, Vector3? targetPos, IMovementPredictor predictor);
    }
}