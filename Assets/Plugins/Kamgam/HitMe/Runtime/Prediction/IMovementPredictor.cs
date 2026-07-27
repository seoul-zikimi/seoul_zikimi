using UnityEngine;

namespace Kamgam.HitMe
{
    public interface IMovementPredictor
    {
        /// <summary>
        /// Returns the predicted position in world space.
        /// </summary>
        /// <param name="timeDeltaInSec"></param>
        /// <returns></returns>
        Vector3 PredictPosition(float timeDeltaInSec);
    }
}