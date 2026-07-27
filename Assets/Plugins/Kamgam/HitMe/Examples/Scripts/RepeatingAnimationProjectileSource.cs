using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    [RequireComponent(typeof(AnimationProjectileSource))]
    public class RepeatingAnimationProjectileSource : MonoBehaviour
    {
        protected AnimationProjectileSource _projectileSource;
        public AnimationProjectileSource ProjectileSource
        {
            get
            {
                if (_projectileSource == null)
                {
                    _projectileSource = this.GetComponent<AnimationProjectileSource>();
                }
                return _projectileSource;
            }
        }

        [Header("Spawn")]
        public float InitialDelay = 1f;
        public float Delay = 0.2f;
        public int MaxProjectiles = 9999;

        protected int _projectiles = 0;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(InitialDelay);

            while (_projectiles < MaxProjectiles)
            {
                ProjectileSource.Spawn();
                _projectiles++;
                yield return new WaitForSeconds(Delay); // you should cache this in a real application.
            }

        }
    }
}
