using UnityEngine;

namespace Kamgam.HitMe
{
    [CreateAssetMenu(fileName = "AnimationProjectileConfig", menuName = "Hit Me/AnimationProjectileConfig", order = 401)]
    public class AnimationProjectileConfigAsset : ScriptableObject
    {
        public AnimationProjectileConfig Config;
    }
}