#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Animation Projectile Source Proxy")]
    [UnitCategory("HitMe")]
    [TypeIcon(typeof(Animation))]
    public class AnimationProjectileSourceProxyNode : Unit
    {
        public ControlInput spawn;
        public ControlOutput spawned;

        public ValueInput AnimationProjectileSourceIn;
        public ValueInput StartActive;

        public ValueOutput Config;
        public ValueOutput Projectile { get; private set; }
        public ValueOutput AnimationProjectileSourceOut;

        /// <summary>
        /// Disable if you want the Source component the be cached.
        /// </summary>
        [Serialize]
        [Inspectable]
        [InspectorExpandTooltip]
        public bool CacheSource { get; set; } = true;

        protected AnimationProjectile _lastCreatedProjectile;
        protected AnimationProjectileSource _cachedSource;

        protected override void Definition()
        {
            spawn = ControlInput(nameof(spawn), Spawn);
            spawned = ControlOutput(nameof(spawned));

            AnimationProjectileSourceIn = ValueInput<GameObject>("Projectile Source", null);
            StartActive = ValueInput<bool>("Start Active", true);

            Projectile = ValueOutput<AnimationProjectile>("Projectile", (flow) => { return _lastCreatedProjectile != null && _lastCreatedProjectile.gameObject != null ? _lastCreatedProjectile : null; });
            Config = ValueOutput<AnimationProjectileConfig>(nameof(Config), (flow) => resolveSource(flow).Config);
            AnimationProjectileSourceOut = ValueOutput<AnimationProjectileSource>("Projectile Source", (flow) => flow.GetValue<AnimationProjectileSource>(AnimationProjectileSourceIn));

            Succession(spawn, spawned);
        }

        protected ControlOutput Spawn(Flow flow)
        {
            AnimationProjectileSource source = resolveSource(flow);

            if (source == null)
            {
                throw new System.Exception(nameof(AnimationProjectileSourceProxyNode) + ": No AnimationProjectileSource component found on the 'AnimationProjectileSource' input. Please provide a gameobject with a AnimationProjectileSource component.");
            }

            bool startActive = flow.GetValue<bool>(StartActive);
            _lastCreatedProjectile = source.Spawn(startActive);

            return spawned;
        }

        private AnimationProjectileSource resolveSource(Flow flow)
        {
            object obj = flow.GetValue(AnimationProjectileSourceIn);
            if (obj == null)
            {
                throw new System.Exception(nameof(AnimationProjectileSourceProxyNode) + ": The 'AnimationProjectileSource' input is Null. Please provide a gameobject with a AnimationProjectileSource component or a AnimationProjectileSource.");
            }

            AnimationProjectileSource source = null;
            if (CacheSource && _cachedSource != null && _cachedSource.gameObject != null)
            {
                source = _cachedSource;
            }
            else
            {
                GameObject sourceGameObject = obj as GameObject;
                if (sourceGameObject != null)
                {
                    sourceGameObject.TryGetComponent(out source);
                }
                else
                {
                    AnimationProjectileSource tmpSource = obj as AnimationProjectileSource;
                    if (tmpSource != null)
                        source = tmpSource;
                }

                if (CacheSource)
                {
                    _cachedSource = source;
                }
            }

            return source;
        }
    }
}
#endif