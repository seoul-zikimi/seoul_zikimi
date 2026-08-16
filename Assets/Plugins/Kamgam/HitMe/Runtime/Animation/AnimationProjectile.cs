#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
    [AddComponentMenu("Hit Me/Animation Projectile")]
#if KAMGAM_VISUAL_SCRIPTING
    [IncludeInSettings(include: false)]
#endif
    public partial class AnimationProjectile : MonoBehaviour, IProjectile
    {
        public static int DistanceApproximationSteps = 6;

        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public AnimationProjectileConfigAsset ConfigAsset = null;
        protected AnimationProjectileConfigAsset _configAssetInstance = null;
        
        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        protected AnimationProjectileConfig _config = null;

        public AnimationProjectileConfig Config
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
                    // Return the config if no asset was set
                    return _config;
                }
            }
            
            set
            {
                _config = value;
            }
        }

        public T GetConfig<T>() where T : class
        {
            return Config as T;
        }

        public float TimeScale = 1f;
        public bool Paused { get; set; }

        /// <summary>
        /// The age of the projectile since spawning in seconds.
        /// </summary>
        public float Time { get; private set; } = 0f;

        protected bool _destroyed = false;
        // Variables to keeping track of the initial state for curve scaling.
        protected float _initialDistance;
        protected Vector3 _initialTiling;
        protected Vector3 _initialScale;
        protected bool _prepared;

        bool useCurveScaling => Config.UseCurves && Config.CurveScaleMode != CurveScaleMode.Stretch;

        public void SetActive(bool value)
        {
            if (gameObject.activeSelf != value)
                gameObject.SetActive(value);

            if (value)
            {
                initializeAnimation();
            }
        }

        public void Awake()
        {
            ProjectileRegistry.Instance.RegisterProjectile(this);
        }

        public void Prepare()
        {
            if (_prepared)
                return;

            _prepared = true;
            
            if (useCurveScaling)
            {
                // If the reference distance is set to < 0f then assume the current distance should be used.
                _initialTiling = new Vector3(Config.CurveTileX, Config.CurveTileY, Config.CurveTileZ);
                _initialScale = new Vector3(Config.CurveScaleX, Config.CurveScaleY, Config.CurveScaleZ);
                _initialDistance = getReferenceDistance();
                applyCurveScaleMode();
            }

            if (Config.UseSpeed)
            {
                Config.Duration = GetDurationBasedOnApproximatedDistanceAndSpeed(DistanceApproximationSteps);
            }
        }

        public void Start()
        {
            Prepare(); // Usually called before Start() but just to make sure it is called every time.
            
            Time = 0f;
            triggerStartEvent();
            UpdateAnimation(Time);
        }

        protected float getReferenceDistance()
        {
            return Config.CurveScaleReferenceDistance >= 0f ? Config.CurveScaleReferenceDistance : GetLinearDistance();
        }

        public void Update()
        {
            if (_destroyed)
                return;

            if (Paused)
                return;

            if (!_animationComplete)
            {
                Time += UnityEngine.Time.deltaTime * TimeScale;

                UpdateAnimation(Time);
            }

            if (Config.FollowTarget)
            {
                if (Config.UseSpeed)
                    Config.Duration = GetDurationBasedOnApproximatedDistanceAndSpeed(DistanceApproximationSteps);
            
                if (useCurveScaling)
                    applyCurveScaleMode();
            }
        }

        protected void applyCurveScaleMode()
        {
            float ratio = updateCurveScaleRatio();

            if (Config.CurveScaleMode == CurveScaleMode.Repeat)
                updateCurveTiling(ratio);
            else if (Config.CurveScaleMode == CurveScaleMode.Scale)
                updateCurveScale(ratio);
        }

        protected float updateCurveScaleRatio()
        {
            var distance = GetLinearDistance();
            var ratio = distance / _initialDistance;

            // Angles are handled in the animation evaluation via Config.CurveScaleRatio.
            Config.CurveScaleRatio = ratio;

            return ratio;
        }

        private void updateCurveTiling(float ratio)
        {
            var tiling = _initialTiling * ratio;
            Config.CurveTileX = tiling.x;
            Config.CurveTileY = tiling.y;
            Config.CurveTileZ = tiling.z;
        }

        private void updateCurveScale(float ratio)
        {
            var scale = _initialScale * ratio;
            Config.CurveScaleX = scale.x;
            Config.CurveScaleY = scale.y;
            Config.CurveScaleZ = scale.z;
        }

        public void Reset(bool resetConfig)
        {
            if (resetConfig)
            {
                ConfigAsset = null;
                _configAssetInstance = null;
                _config = null;
            }

            ResetCollisionCounter();
            Time = 0f;
            _destroyed = false;
            _isInitialized = false;
        }

        public void OnDestroy()
        {
            _destroyed = true;
            ProjectileRegistry.Instance.UnregisterProjectile(this);
        }

        public float GetDurationBasedOnLinearDistanceAndSpeed(AnimationProjectileSource projectileSource = null)
        {
            var distance = Config.GetLinearDistance(projectileSource);
            return distance / Config.EvaluateSpeed(distance);
        }

        public float GetDurationBasedOnApproximatedDistanceAndSpeed(int steps, AnimationProjectileSource projectileSource = null)
        {
            var distance = GetApproximatedDistance(steps, projectileSource);
            return distance / Config.EvaluateSpeed(distance);
        }

        public float GetLinearDistance(AnimationProjectileSource projectileSource = null)
        {
            return Config.GetLinearDistance(projectileSource);
        }

        public float GetApproximatedDistance(int steps, AnimationProjectileSource projectileSource = null)
        {
            if (steps <= 1)
            {
                return Config.GetLinearDistance(projectileSource);
            }

            var source = AnimationProjectileConfig.ResolveSource(Config, projectileSource != null ? projectileSource.SourceOverride : null);
            var target = AnimationProjectileConfig.ResolveTarget(Config, projectileSource != null ? projectileSource.TargetOverride : null);

            float distance = 0f;
            Vector3 pointA = Config.GetSourcePosWithOffset(source);
            Vector3 pointB;
            float tStepSize = Config.Duration / (float)steps;
            for (int i = 1; i < steps; i++)
            {
                float t = i * tStepSize;
                pointB = Evaluate(t);
                distance += (pointA - pointB).magnitude;
                //Debug.DrawLine(pointA, pointB, Color.red, 0.2f);
                pointA = pointB;
            }
            pointB = Config.GetTargetPosWithOffset(target);
            distance += (pointA - pointB).magnitude;
            //Debug.DrawLine(pointA, pointB, Color.red, 0.2f);

            return distance;
        }
    }
}