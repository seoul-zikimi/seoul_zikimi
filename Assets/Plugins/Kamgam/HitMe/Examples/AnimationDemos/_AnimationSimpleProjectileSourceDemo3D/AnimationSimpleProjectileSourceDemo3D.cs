using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationSimpleProjectileSourceDemo3D : MonoBehaviour
    {
        public AnimationProjectileSource ProjectileSource;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            ProjectileSource.Spawn();
        }
    }
}