#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Ballistic Projectile Source")]
    [UnitCategory("HitMe")]
    [TypeIcon(typeof(ParticleSystem))]
    public class BallisticProjectileSourceNode : Unit
    {
        public ControlInput spawn;
        public ControlOutput spawned;

        public ValueInput Prefab;
        public ValueInput StartActive;
        public ValueInput Source;
        public ValueInput Target;
        public ValueInput Alignment;
        public ValueInput UsePrediction;

        public ValueInput OverridePrefabConfig;
        public ValueInput ProjectileConfig;

        public ValueInput AddDestruction;
        public ValueInput DestructionConfig;

        public ValueOutput projectile { get; private set; }

        protected BallisticProjectile _lastCreatedProjectile;

        protected override void Definition()
        {
            spawn = ControlInput(nameof(spawn), Spawn);
            spawned = ControlOutput(nameof(spawned));

            projectile = ValueOutput<BallisticProjectile>("Projectile", (flow) => { return _lastCreatedProjectile != null && _lastCreatedProjectile.gameObject != null ? _lastCreatedProjectile : null; });

            Prefab = ValueInput<GameObject>("Prefab", null);
            StartActive = ValueInput<bool>("Start Active", true);
            Source = ValueInput<GameObject>("Source", null);
            Target = ValueInput<GameObject>("Target", null);
            Alignment = ValueInput<ProjectileAlignment>("Alignment", ProjectileAlignment.WithVelocity);
            UsePrediction = ValueInput<bool>("Use Prediction", false);

            OverridePrefabConfig = ValueInput<bool>("Override Prefab Config", true);
            ProjectileConfig = ValueInput<BallisticProjectileConfigAsset>("Projectile Config Asset", null);

            AddDestruction = ValueInput<bool>("Add Destruction", true);
            DestructionConfig = ValueInput<DestructionConfigAsset>("Destruction Config Asset", null);

            Succession(spawn, spawned);
        }

        private ControlOutput Spawn(Flow flow)
        {
            var prefab = flow.GetValue<GameObject>(Prefab);
            var spawnPoint = flow.GetValue<GameObject>(Source);
            var target = flow.GetValue<GameObject>(Target);
            var configAsset = flow.GetValue<BallisticProjectileConfigAsset>(ProjectileConfig);
            var destructionAsset = flow.GetValue<DestructionConfigAsset>(DestructionConfig);

            if (prefab == null || spawnPoint == null || target == null || configAsset == null || destructionAsset == null)
            {
                throw new System.Exception(nameof(BallisticProjectileSourceNode) + ": Values missing. Please provide all needed values and objets.");
            }

            _lastCreatedProjectile = BallisticProjectileSource.Spawn(
                prefab,
                flow.GetValue<bool>(StartActive),
                spawnPoint.transform,
                target.transform,
                flow.GetValue<ProjectileAlignment>(Alignment),
                flow.GetValue<bool>(UsePrediction),
                flow.GetValue<bool>(OverridePrefabConfig),
                configAsset.Config,
                flow.GetValue<bool>(AddDestruction),
                destructionAsset.Config
                );

            return spawned;
        }
    }
}
#endif