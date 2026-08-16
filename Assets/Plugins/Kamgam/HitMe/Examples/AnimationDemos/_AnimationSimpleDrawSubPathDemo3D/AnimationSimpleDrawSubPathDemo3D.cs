using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationSimpleDrawSubPathDemo3D : MonoBehaviour
    {
        public GameObject PathLineRendererPrefab;

        protected ProjectileLineRenderer _cachedPathRenderer;
        protected AnimationProjectile _projectile;

        public AnimationProjectileSource ProjectileSource;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            _projectile = ProjectileSource.Spawn();
        }

        public void Update()
        {
            if (_projectile != null)
            {
                _cachedPathRenderer = AnimationProjectile.DrawPath(
                    ProjectileSource.SourceOverride,
                    ProjectileSource.TargetOverride,
                    ProjectileSource.Config,
                    renderer: _cachedPathRenderer,
                    prefab: PathLineRendererPrefab,
                    maxTime: _projectile.Time // This causes the path to be draw only behind the projectile
                    );
            }
        }
    }
}