#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
    [AddComponentMenu("Hit Me/Ballistic Projectile Source")]
#if KAMGAM_VISUAL_SCRIPTING
    [IncludeInSettings(include: true)]
#endif
    public class BallisticProjectileSource : MonoBehaviour
    {
        [Header("Spawn")]
        public GameObject Prefab;
        
        [Tooltip("You can use this to override the source on the prefab even if 'OverridePrefabConfig' is disabled.")]
        public Transform SourceOverride;

        [Tooltip("You can use this to override the target on the prefab even if 'OverridePrefabConfig' is disabled.")]
        public Transform TargetOverride;

        public ProjectileAlignment Alignment = ProjectileAlignment.WithVelocity;

        [Tooltip("Predictions are not 100% acurate (especially for fast or erratic moving objects like a character controlled by a player).")]
        public bool UsePrediction = true;

        [Header("Config")]
        [Tooltip("The projectile config on this object will replace the config on the projectile prefab. Each projectile gets a COPY of this config. If the prefab has none yet then it will be added (as a copy).")]
        public bool OverridePrefabConfig = true;

        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        [ShowIf("OverridePrefabConfig", true, ShowIfAttribute.DisablingType.ReadOnly)]
        public BallisticProjectileConfigAsset ConfigAsset = null;
        protected BallisticProjectileConfigAsset _configAssetInstance = null;

        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        [ShowIf("OverridePrefabConfig", true, ShowIfAttribute.DisablingType.ReadOnly)]
        protected BallisticProjectileConfig _config = null;

        public BallisticProjectileConfig Config
        {
            get
            {
                if (ConfigAsset != null)
                {
                    if (_configAssetInstance == null)
                    {
                        _configAssetInstance = ScriptableObject.Instantiate(ConfigAsset);
                    }
                    return _configAssetInstance.Config;
                }
                else
                {
                    return _config;
                }
            }

            set
            {
                _config = value;
            }
        }

        [Header("Destruction")]
        public bool AddDestruction = true;

        [ShowIf("AddDestruction", true, ShowIfAttribute.DisablingType.ReadOnly)]
        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public DestructionConfigAsset DestructionConfigAsset = null;
        protected DestructionConfigAsset _destructionConfigAssetInstance = null;

        [SerializeField]
        [ShowIf("AddDestruction", true, ShowIfAttribute.DisablingType.ReadOnly)]
        [ShowIf("DestructionConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        protected DestructionConfig _destructionConfig = null;

        public DestructionConfig DestructionConfig
        {
            get
            {
                if (DestructionConfigAsset != null)
                {
                    if (_destructionConfigAssetInstance == null)
                    {
                        _destructionConfigAssetInstance = ScriptableObject.Instantiate(DestructionConfigAsset);
                    }
                    return _destructionConfigAssetInstance.Config;
                }
                else
                {
                    // Return the config if no asset was set
                    return _destructionConfig;
                }
            }

            set
            {
                _destructionConfig = value;
            }
        }

        public delegate void OnNewProjectileSpawnedDelegate(BallisticProjectile projectile);
        
        /// <summary>
        /// Delegate you can use to get informed whenever a new projectile is successfully spawned.<br />Handy for vfx or sounds.
        /// </summary>
        public OnNewProjectileSpawnedDelegate OnNewProjectileSpawned;
        
        /// <summary>
        /// Optional spawn constraint
        /// </summary>
        [System.NonSerialized]
        public IBallisticProjectileSpawnConstraint SpawnConstraint = null;

        /// <summary>
        /// Use this to calculate the trajectory without spawning any projectile. Useful for targeting limitation before firing.
        /// </summary>
        /// <param name="startVelocity">Output. The start velocity (can also be interpreted as the start direction).</param>
        /// <param name="angle2D">Output. The start angle2D (yz-plane angle).</param>
        /// <param name="checkConstraints">If enabled then this method will take the constraints into account.</param>
        /// <param name="usePrediction">If enabled the this method will try go get a IMovementPredictor component from the target.</param>
        /// <param name="positionContainsOffset">(Optional) Do the given positions already include the offsets or should they be applied (offsets are taken from the config).</param>
        /// <returns></returns>
        public bool Evaluate(out Vector3 startVelocity, out float angle2D, bool checkConstraints = true, bool usePrediction = true, bool positionContainsOffset = true)
        {
            return Evaluate(
                out startVelocity, out angle2D,
                Config, checkConstraints, usePrediction,
                sourcePos: null, targetPos: null, SourceOverride, TargetOverride, positionContainsOffset);
        }
        
        /// <summary>
        /// Use this to calculate the trajectory without spawning any projectile. Useful for targeting limitation before firing.
        /// </summary>
        /// <param name="startVelocity">Output. The start velocity (can also be interpreted as the start direction).</param>
        /// <param name="angle2D">Output. The start angle2D (yz-plane angle).</param>
        /// <param name="config">The config  to use for the calculation.</param>
        /// <param name="usePrediction">If enabled then this method will try go get a IMovementPredictor component from the target.</param>
        /// <param name="checkConstraints">If enabled then this method will take the constraints into account.</param>
        /// <param name="sourcePos">(Optional) Is used instead of the config source pos if not null.</param>
        /// <param name="targetPos">(Optional) Is used instead of the config target pos if not null.</param>
        /// <param name="sourceOverride">(Optional) Is used instead of the config source if not null.</param>
        /// <param name="targetOverride">(Optional) Is used instead of the config target if not null.</param>
        /// <param name="positionContainsOffset">(Optional) Do the given positions already include the offsets or should they be applied (offsets are taken from the config).</param>
        /// <returns></returns>
        public bool Evaluate(
            out Vector3 startVelocity,
            out float angle2D,
            BallisticProjectileConfig config,
            bool checkConstraints,
            bool usePrediction,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform sourceOverride = null, Transform targetOverride = null, bool positionContainsOffset = true)
        {
            // Resolve source / target transforms
            var resolvedSource = BallisticProjectileConfig.ResolveSource(config, sourceOverride);
            var resolvedTarget = BallisticProjectileConfig.ResolveTarget(config, targetOverride);
            
            IMovementPredictor predictor = null;
            if (UsePrediction && resolvedTarget != null)
            {
                resolvedTarget.gameObject.TryGetComponent<IMovementPredictor>(out predictor);
            }

            // Resolve source / target positions
            config.ApplyToPositions(ref sourcePos, ref targetPos, resolvedSource, resolvedTarget, positionContainsOffset, applyOffsets: true);

            // Calc trajectory
            bool possible = BallisticUtils.CalcStartVelocity(out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, resolvedSource, resolvedTarget, positionContainsOffset);

            // Check constraints if needed.
            if (possible && checkConstraints && SpawnConstraint != null)
            {
                if (!SpawnConstraint.Allow(startVelocity, angle2D, config, resolvedSource, resolvedTarget, sourcePos, targetPos, predictor))
                    return false;
            }
            
            return possible;
        }
        
        /// <summary>
        /// Returns null if the target can not be reached.
        /// </summary>
        /// <param name="startActive"></param>
        /// <returns></returns>
        public virtual BallisticProjectile Spawn(bool startActive = true)
        {
            return Spawn(Prefab, startActive);
        }

        /// <summary>
        /// Returns null if the target can not be reached.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="startActive"></param>
        /// <returns></returns>
        public virtual BallisticProjectile Spawn(GameObject prefab, bool startActive = true)
        {
            
            var projectile = Spawn(
                prefab,
                startActive,
                SourceOverride,
                TargetOverride,
                Alignment,
                UsePrediction,
                OverridePrefabConfig,
                Config,
                AddDestruction,
                DestructionConfig,
                SpawnConstraint
                );

            if (projectile != null)
                OnNewProjectileSpawned?.Invoke(projectile);
            
            return projectile;
        }

        /// <summary>
        /// Returns null if the target can no be reached.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="startActive"></param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="alignment"></param>
        /// <param name="usePrediction"></param>
        /// <param name="overridePrefabConfig"></param>
        /// <param name="config"></param>
        /// <param name="addDestruction"></param>
        /// <param name="destructionConfig"></param>
        /// <param name="spawnConstraint"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BallisticProjectile Spawn(
            GameObject prefab,
            bool startActive,
            Transform source,
            Transform target,
            ProjectileAlignment alignment,
            bool usePrediction,
            bool overridePrefabConfig,
            BallisticProjectileConfig config,
            bool addDestruction,
            DestructionConfig destructionConfig,
            IBallisticProjectileSpawnConstraint spawnConstraint = null
            )
        {
            if (prefab == null)
                throw new System.Exception("No prefab for projectile instantiation given. Prefab parameter is null!");

            var projectile = BallisticProjectile.Spawn(prefab, source, target, overridePrefabConfig ? config : null, compensateSimulation: true, usePrediction, startActive, spawnConstraint);

            // Projectile is null if the target can not be reached with the current configuration.
            if (projectile == null)
                return null;

            // Add or configure destruction
            var destruction = projectile.GetComponent<Destruction>();
            if (destruction == null && addDestruction)
            {
                destruction = projectile.gameObject.AddComponent<Destruction>();
            }
            if (destruction != null)
            {
                destruction.Reset(resetConfig: true);
                destruction.Config = destructionConfig.Copy();
            }

            // Add alignment helpers (if needed)
            if (alignment == ProjectileAlignment.WithVelocity)
            {
                var align = projectile.gameObject.AddComponent<AlignWithVelocity>();
                projectile.Evaluate(0f, out var velocity);
                align.InitializeVelocity(velocity);
                align.StopOnCollision = true;
            }
            else if (alignment == ProjectileAlignment.LookAtTarget)
            {
                var lookAt = projectile.gameObject.AddComponent<LookAt>();
                lookAt.Target = target;
                lookAt.StopOnCollision = true;
            }
            else if (alignment == ProjectileAlignment.WithAnimationCurve)
            {
                Debug.LogError("Animation Velocity alignment is not supported in BallisticProjectile.");
            }

            return projectile;
        }

#if UNITY_EDITOR
        protected GameObject _editorLastPrefab;

        public void OnValidate()
        {
            // Update the Config.Dimensions based on the used prefab.
            if (_editorLastPrefab != Prefab && Prefab != null)
            {
                _editorLastPrefab = Prefab;
                bool is2D = _editorLastPrefab.TryGetComponent<Rigidbody2D>(out _) || _editorLastPrefab.TryGetComponent<Collider2D>(out _);
                if (Config != null)
                {
                    Config.Dimensions = is2D ? PhysicsDimensions.Physics2D : PhysicsDimensions.Physics3D;
                }
            }
        }
#endif
    }
}