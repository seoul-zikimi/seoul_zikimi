#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Ballistic Projectile Source Proxy")]
    [UnitCategory("HitMe")]
    [TypeIcon(typeof(ParticleSystem))]
    public class BallisticProjectileSourceProxyNode : Unit
    {
        public ControlInput spawn;
        public ControlOutput spawned;
        public ControlOutput impossible;

        public ValueInput BallisticProjectileSourceIn;
        public ValueInput StartActive;

        public ValueOutput Projectile { get; private set; }
        public ValueOutput Config;
        public ValueOutput BallisticProjectileSourceOut;

        /// <summary>
        /// Disable if you want the Source component the be cached.
        /// </summary>
        [Serialize]
        [Inspectable]
        [InspectorExpandTooltip]
        public bool CacheSource { get; set; } = true;

        protected BallisticProjectile _lastCreatedProjectile;
        protected BallisticProjectileSource _cachedSource;

        protected override void Definition()
        {
            spawn = ControlInput(nameof(spawn), Spawn);
            spawned = ControlOutput(nameof(spawned));
            impossible = ControlOutput(nameof(impossible));

            BallisticProjectileSourceIn = ValueInput<GameObject>("Projectile Source", null);
            StartActive = ValueInput<bool>("Start Active", true);

            Projectile = ValueOutput<BallisticProjectile>("Projectile", (flow) => { return _lastCreatedProjectile != null && _lastCreatedProjectile.gameObject != null ? _lastCreatedProjectile : null; });
            Config = ValueOutput<BallisticProjectileConfig>(nameof(Config), (flow) => resolveSource(flow).Config);
            BallisticProjectileSourceOut = ValueOutput<BallisticProjectileSource>("Projectile Source", (flow) => flow.GetValue<BallisticProjectileSource>(BallisticProjectileSourceIn));

            Succession(spawn, spawned);
            Succession(spawn, impossible);
        }

        protected ControlOutput Spawn(Flow flow)
        {
            BallisticProjectileSource source = resolveSource(flow);

            if (source == null)
            {
                throw new System.Exception(nameof(BallisticProjectileSourceProxyNode) + ": No BallisticProjectileSource component found on the 'BallisticProjectileSource' input. Please provide a gameobject with a BallisticProjectileSource component.");
            }

            bool startActive = flow.GetValue<bool>(StartActive);
            _lastCreatedProjectile = source.Spawn(startActive);

            if (_lastCreatedProjectile)
            {
                return spawned;
            }
            else
            {
                return impossible;
            }
        }

        private BallisticProjectileSource resolveSource(Flow flow)
        {
            object obj = flow.GetValue(BallisticProjectileSourceIn);
            if (obj == null)
            {
                throw new System.Exception(nameof(BallisticProjectileSourceProxyNode) + ": The 'BallisticProjectileSource' input is Null. Please provide a gameobject with a BallisticProjectileSource component or a BallisticProjectileSource.");
            }

            BallisticProjectileSource source = null;
            if (CacheSource && _cachedSource != null && _cachedSource.gameObject != null)
            {
                source = _cachedSource;
            }
            else
            {
                GameObject sourceGameObject = obj as GameObject;
                if (sourceGameObject != null)
                {
                    sourceGameObject.TryGetComponent(out _cachedSource);
                }
                else
                {
                    BallisticProjectileSource tmpSource = obj as BallisticProjectileSource;
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