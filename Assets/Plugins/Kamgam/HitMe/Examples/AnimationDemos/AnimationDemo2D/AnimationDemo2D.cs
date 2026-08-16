using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationDemo2D : AnimationProjectileSource
    {
        [Header("Spawn")]
        public int Projectiles = 9999;
        public float Delay = 0.2f;

        [Header("Line Renderer")]
        [Min(0.01f)]
        public float LineSegmentsPerUnit = 1f;
        public GameObject LineRendererPrefab;

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

            // Draw path to target
            _renderer = AnimationProjectile.DrawPath(
                source,
                target,
                Config, LineSegmentsPerUnit,
                renderer: _renderer, prefab: LineRendererPrefab, UsePrediction
            );

            // Spawn
            _countDown -= Time.deltaTime;
            if (_countDown <= 0f && _numOfProjectiles < Projectiles)
            {
                _countDown = Delay;
                _numOfProjectiles++;

                var projectile = Spawn();

                // Events test
                /*
                var projectile = Spawn(startActive: false);

                // Register event callbacks
                projectile.OnStart += (p) => Debug.Log("start " + p.name);
                projectile.OnUpdate +=(p) => Debug.Log("update " + p.name + ", t: " + p.Time);
                projectile.OnEnd += (p) => Debug.Log("end " + p.name + ", v: " + p.GetVelocity() + ", t: " + p.Time);
                projectile.OnCollision3D += (p, collision, trigger, collider) => Debug.Log("Collision with " + (collision != null ? collision.gameObject : collider.gameObject));
                projectile.OnDisablePhysics += (p) => Debug.Log("Disable physics");
                projectile.OnEnablePhysics += (p) => Debug.Log("Enable physics");

                // Acivate the projectile
                projectile.SetActive(true);
                */
            }                      
        }
    }
}