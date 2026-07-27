using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationWithPhysicsDemo3D : AnimationProjectileSource
    {
        [Header("Spawn")]
        public int Projectiles = 9999;
        public float Delay = 0.2f;

        float _countDown = 0f;
        int _numOfProjectiles = 0;

        public void Start()
        {
            _countDown = Delay;
        }

        protected ProjectileLineRenderer _renderer;

        public void Update()
        {
            var source = AnimationProjectileConfig.ResolveSource(Config, SourceOverride);
            var target = AnimationProjectileConfig.ResolveTarget(Config, TargetOverride);

            // Spawn
            _countDown -= Time.deltaTime;
            if (_countDown <= 0f && _numOfProjectiles < Projectiles)
            {
                _countDown = Delay;
                _numOfProjectiles++;

                var projectile = Spawn(startActive: false);

                // Acivate the projectile
                projectile.SetActive(true);
            }                      
        }
    }
}