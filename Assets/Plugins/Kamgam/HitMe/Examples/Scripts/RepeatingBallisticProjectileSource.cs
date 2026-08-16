using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    [RequireComponent(typeof(BallisticProjectileSource))]
    public class RepeatingBallisticProjectileSource : MonoBehaviour
    {
        protected BallisticProjectileSource _projectileSource;
        public BallisticProjectileSource ProjectileSource
        {
            get
            {
                if (_projectileSource == null)
                {
                    _projectileSource = this.GetComponent<BallisticProjectileSource>();
                }
                return _projectileSource;
            }
        }

        [Header("Spawn")]
        public float InitialDelay = 2f;
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
