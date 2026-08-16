using UnityEngine;

namespace Kamgam.HitMe
{
    public class BallisticDemo3D : BallisticProjectileSource
    {
        [Header("Spawn")]
        public int Projectiles = 9999;
        public float Delay = 0.2f;
        public bool UseAlignmentLerp = true;

        [Header("Line Renderer")]
        [Min(0.01f)]
        public float LineSegmentsPerUnit = 1f;
        public GameObject LineRendererPrefab;
        
        [Tooltip("The min flight time of projectiles after which the path should be drawn. Useful to start the path a little distance away from the source.")]
        [Min(0f)]
        public float PathDrawMinTime = 0f;
        
        [Tooltip("If set to <= 0f then this is set automatically to draw the path from source to target." +
                 "\nHowever, for 'DirectionAndSpeed' mode there is not target so you should set it to something like 2 seconds. Nice side-effect: it will grow with speed.")]
        [Min(0f)]
        public float PathDrawMaxTime = 0f;

        float _countDown = 0f;
        int _numOfProjectiles = 0;
        
        public void Start()
        {
            _countDown = Delay;
        }

        protected ProjectileLineRenderer _renderer;

        public void Update()
        {
            var source = BallisticProjectileConfig.ResolveSource(Config, SourceOverride);
            var target = BallisticProjectileConfig.ResolveTarget(Config, TargetOverride);

            // Draw path to target
            _renderer = BallisticProjectile.DrawPath(
                out bool possible,
                source,
                target,
                Config, LineSegmentsPerUnit,
                renderer: _renderer, prefab: LineRendererPrefab, UsePrediction,
                minTime: PathDrawMinTime,
                maxTime: Mathf.Approximately(PathDrawMaxTime, 0f) ? float.PositiveInfinity : PathDrawMaxTime
            );
            
            _countDown -= Time.deltaTime;

            if (!possible)
            {
#if UNITY_EDITOR
                Debug.Log("Can't reach target!");
#endif
                return;
            }

            // Spawn
            if (_countDown <= 0f && _numOfProjectiles < Projectiles)
            {
                _countDown = Delay;
                _numOfProjectiles++;

                var projectile = Spawn(startActive: true);

                // Cancel if impossible to hit.
                if (projectile == null)
                    return;
                
                // Register event callbacks
                projectile.OnStart += (p) => Debug.Log("start " + p.name);
                projectile.OnUpdate += (p) => Debug.Log("update " + p.name + ", t: " + p.Time);
                projectile.OnEnd += (p) => Debug.Log("end " + p.name + ", v: " + p.GetVelocity() + ", t: " + p.Time);
                projectile.OnCollision3D += (p, collision, trigger, collider) => Debug.Log("Collision with " + (collision != null ? collision.gameObject : collider.gameObject));

                // Acivate the projectile
                projectile.SetActive(true);

                // Prepare and add the ProjectileAlignmentLerp
                if (UseAlignmentLerp)
                {
                    projectile.gameObject.TryGetComponent<AlignWithVelocity>(out var align);
                    if (align == null)
                    {
                        align = projectile.gameObject.AddComponent<AlignWithVelocity>();
                        // Set start velocity
                        projectile.Evaluate(0f, out var velocity);
                        align.InitializeVelocity(velocity);
                    }
                    align.StopOnCollision = true;
                    align.Apply = false;

                    projectile.gameObject.TryGetComponent<LookAt>(out var lookAt);
                    if (lookAt == null)
                        lookAt = projectile.gameObject.AddComponent<LookAt>();
                    lookAt.Target = target;
                    lookAt.StopOnCollision = true;
                    lookAt.Apply = false;

                    var lerp = projectile.gameObject.AddComponent<AlignLookLerp>();
                    if (Config.CalculationMethod == BallisticCalculation.Time)
                    {
                        lerp.StartTime = Config.Duration * 0.3f;
                        lerp.Duration = Config.Duration * 0.6f;
                    }
                    else
                    {
                        lerp.StartTime = 0.5f;
                        lerp.Duration = 1.0f;
                    }
                }
            }
        }
    }
}