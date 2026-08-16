#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    [IncludeInSettings(include: false)]
    [UnitTitle("Animation Projectile Source")]
    [UnitCategory("HitMe")]
    [TypeIcon(typeof(Animation))]
    public class AnimationProjectileSourceNode : Unit
    {
        public ControlInput spawn;
        public ControlOutput spawned;

        public ValueInput Prefab;
        public ValueInput StartActive;
        public ValueInput Source;
        public ValueInput Target;
        public ValueInput Alignment;
        public ValueInput UsePrediction;
        public ValueInput TimeScale;

        public ValueInput OverridePrefabConfig;
        public ValueInput ProjectileConfig;

        public ValueInput AddDestruction;
        public ValueInput DestructionConfig;

        [DoNotSerialize]
        public ValueOutput projectile { get; private set; }

        protected AnimationProjectile _lastCreatedProjectile;

        protected override void Definition()
        {
            spawn = ControlInput(nameof(spawn), Spawn);
            spawned = ControlOutput(nameof(spawned));

            projectile = ValueOutput<AnimationProjectile>("Projectile", (flow) => { return _lastCreatedProjectile != null && _lastCreatedProjectile.gameObject != null ? _lastCreatedProjectile : null; });

            Prefab = ValueInput<GameObject>("Prefab", null);
            StartActive = ValueInput<bool>("Start Active", true);
            Source = ValueInput<GameObject>("Source", null);
            Target = ValueInput<GameObject>("Target", null);
            Alignment = ValueInput<ProjectileAlignment>("Alignment", ProjectileAlignment.WithAnimationCurve);
            UsePrediction = ValueInput<bool>("Use Prediction", false);
            TimeScale = ValueInput<float>("Time Scale", 1f);

            OverridePrefabConfig = ValueInput<bool>("Override Prefab Config", true);
            ProjectileConfig = ValueInput<AnimationProjectileConfigAsset>("Projectile Config Asset", null);

            AddDestruction = ValueInput<bool>("Add Destruction", true);
            DestructionConfig = ValueInput<DestructionConfigAsset>("Destruction Config Asset", null);

            Succession(spawn, spawned);
        }

        private ControlOutput Spawn(Flow flow)
        {
            var prefab = flow.GetValue<GameObject>(Prefab);
            var spawnPoint = flow.GetValue<GameObject>(Source);
            var target = flow.GetValue<GameObject>(Target);
            var configAsset = flow.GetValue<AnimationProjectileConfigAsset>(ProjectileConfig);
            var destructionAsset = flow.GetValue<DestructionConfigAsset>(DestructionConfig);

            if (prefab == null || spawnPoint == null || target == null || configAsset == null || destructionAsset == null)
            {
                throw new System.Exception(nameof(AnimationProjectileSourceNode) + ": Values missing. Please provide all needed values and objets.");
            }

            _lastCreatedProjectile = AnimationProjectileSource.Spawn(
                prefab,
                flow.GetValue<bool>(StartActive),
                spawnPoint.transform,
                target.transform,
                flow.GetValue<ProjectileAlignment>(Alignment),
                flow.GetValue<bool>(UsePrediction),
                flow.GetValue<float>(TimeScale),
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