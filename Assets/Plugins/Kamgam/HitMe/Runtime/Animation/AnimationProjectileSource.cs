#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
    [AddComponentMenu("Hit Me/Animation Projectile Source")]
#if KAMGAM_VISUAL_SCRIPTING
    [IncludeInSettings(include: true)]
#endif
    public class AnimationProjectileSource : MonoBehaviour
    {
        [Header("Spawn")]
        public GameObject Prefab;

        [Tooltip("You can use this to override the source on the prefab even if 'OverridePrefabConfig' is disabled.")]
        public Transform SourceOverride;

        [Tooltip("You can use this to override the target on the prefab even if 'OverridePrefabConfig' is disabled.")]
        public Transform TargetOverride;

        public ProjectileAlignment Alignment = ProjectileAlignment.WithAnimationCurve;
        
        [Tooltip("Predictions are not 100% accurate.\n\n" +
            "If you need to hit the target under all circumstances then please consider turning on 'Follow Target' in the projectile config instead.")]
        public bool UsePrediction = true;

        public float TimeScale = 1f;

        [Header("Animation Config")]
        [Tooltip("The projectile config on this object will replace the config on the projectile prefab. Each projectile gets a COPY of this config. If the prefab has none yet then it will be added (as a copy).")]
        public bool OverridePrefabConfig = true;

        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public AnimationProjectileConfigAsset ConfigAsset = null;
        protected AnimationProjectileConfigAsset _configAssetInstance = null;

        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        protected AnimationProjectileConfig _config = null;
        protected bool _configWasSet = false;

        public AnimationProjectileConfig Config
        {
            get
            {
                // _configWasSet is needed because this object is serialized in the editor inspector
                // and thus _config will always have a default value. It will never be null if set in the inspector.
                if (_config != null && _configWasSet)
                {
                    return _config;
                }
                else if (ConfigAsset != null)
                {
                    if (_configAssetInstance == null)
                    {
                        _configAssetInstance = ScriptableObject.Instantiate(ConfigAsset);
                    }
                    return _configAssetInstance.Config;
                }
                else
                {
                    // Return the config if no asset was set
                    return _config;
                }
            }

            set
            {
                _config = value;
                _configWasSet = true;
            }
        }

        [Header("Destruction")]
        public bool AddDestruction = true;

        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public DestructionConfigAsset DestructionConfigAsset = null;
        protected DestructionConfigAsset _destructionConfigAssetInstance = null;

        [SerializeField]
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

        public virtual AnimationProjectile Spawn(bool startActive = true)
        {
            return Spawn(Prefab, startActive);
        }

        public virtual AnimationProjectile Spawn(GameObject prefab, bool startActive = true)
        {
            return Spawn(
                Prefab, 
                startActive,
                SourceOverride,
                TargetOverride,
                Alignment,
                UsePrediction,
                TimeScale,
                OverridePrefabConfig,
                Config,
                AddDestruction,
                DestructionConfig
            );
        }

        public static AnimationProjectile Spawn(
            GameObject prefab, 
            bool startActive,
            Transform sourceOverride,
            Transform targetOverride,
            ProjectileAlignment alignment,
            bool usePrediction,
            float timeScale,
            bool overridePrefabConfig,
            AnimationProjectileConfig config,
            bool addDestruction,
            DestructionConfig destructionConfig
            )
        {
            if (prefab == null)
                throw new System.Exception("No prefab for projectile instantiation given. Prefab parameter is null!");

            var projectile = AnimationProjectile.Spawn(prefab, sourceOverride, targetOverride, overridePrefabConfig ? config : null, usePrediction, startActive);
            projectile.TimeScale = timeScale;

            // Add or configure destruction
            var destruction = projectile.GetComponent<Destruction>();
            if(destruction == null && addDestruction)
            {
                destruction = projectile.gameObject.AddComponent<Destruction>();
                destruction.Paused = projectile.Paused;
                destruction.TimeScale = projectile.TimeScale;
            }
            if(destruction != null)
            {
                destruction.Reset(resetConfig: true);
                destruction.Config = destructionConfig.Copy();
                destruction.Config.CollisionMinAge = projectile.Config.CollisionMinAge;
            }

            // Add alignment helper (if needed)
            if (alignment == ProjectileAlignment.WithAnimationCurve)
            {
                var align = projectile.gameObject.AddComponent<AlignWithAnimationVelocity>();
                align.StopOnCollision = false;
                align.StopAfterAnimation = true;
            }
            else if (alignment == ProjectileAlignment.WithVelocity)
            {
                var align = projectile.gameObject.AddComponent<AlignWithVelocity>();
                align.StopOnCollision = true;
                // Set start velocity
                projectile.Evaluate(0f, out var velocity);
                align.InitializeVelocity(velocity);
            }
            else if (alignment == ProjectileAlignment.LookAtTarget)
            {
                var lookAt = projectile.gameObject.AddComponent<LookAt>();
                lookAt.Target = targetOverride;
                lookAt.StopOnCollision = true;
            }

            return projectile;
        }
        
#if UNITY_EDITOR
        public void Reset()
        {
            if (SourceOverride == null)
                SourceOverride = this.transform;
        }
#endif
    }
}