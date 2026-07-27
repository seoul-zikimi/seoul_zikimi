#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

using UnityEngine;

namespace Kamgam.HitMe
{
    [AddComponentMenu("Hit Me/Ballistic Projectile")]
#if KAMGAM_VISUAL_SCRIPTING
    [IncludeInSettings(include: false)]
#endif
    public partial class BallisticProjectile : MonoBehaviour, IProjectile
    {
        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public BallisticProjectileConfigAsset ConfigAsset = null;

        [System.NonSerialized]
        protected BallisticProjectileConfigAsset _configAssetInstance = null;
        
        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        protected BallisticProjectileConfig _config = null;

        /// <summary>
        /// Returns a (cached) copy of the config of the ConfigAsset if ConfigAsset is not null.<br />
        /// Otherwise it returns the Config set on the projectile.
        /// </summary>
        public BallisticProjectileConfig Config
        {
            get
            {
                // _configWasSet is needed because this object is serialized in the editor inspector
                // and thus _config will always have the default values. It will never be null if set in the inspector.
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

        /// <summary>
        /// The age of the projectile since spawning in seconds.
        /// </summary>
        public float Time { get; private set; } = 0f;

        protected bool _destroyed = false;
        protected float _duration = 0f;
        // It is considered complete after Time > _duration or after the first collision.
        protected bool _complete = false;
        protected bool _isInitialized = false;

        protected Rigidbody _rigidbody;
        public Rigidbody Rigidbody
        {
            get
            {
                if (_rigidbody == null)
                {
                    gameObject.TryGetComponent(out _rigidbody);
                }
                return _rigidbody;
            }
        }

        protected Rigidbody2D _rigidbody2D;
        public Rigidbody2D Rigidbody2D
        {
            get
            {
                if (_rigidbody2D == null)
                {
                    gameObject.TryGetComponent(out _rigidbody2D);
                }
                return _rigidbody2D;
            }
        }

        /// <summary>
        /// Used to reset the cache for config values.
        /// </summary>
        protected void clearConfigCache()
        {
            _configAssetInstance = null;
        }

        public void SetActive(bool value)
        {
            if (gameObject.activeSelf != value)
                gameObject.SetActive(value);

            if(value)
            {
                initialize();
            }
        }

        protected void initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            if(_startVelocity.HasValue)
                Config.ApplyStartVelocity(_startVelocity.Value, gameObject, Config.GetSourcePosWithOffset(), _compensateSimulation);

            triggerStartEvent();
            triggerUpdateEvent();
        }

        public void Awake()
        {
            ProjectileRegistry.Instance.RegisterProjectile(this);
        }

        public void Update()
        {
            if (_destroyed)
                return;

            if (_complete)
                return;

            // Notice: Time is only needed for alignment and events. The "animation" is
            // done automatically by the physics engine after adding the initial start velocity.
            Time += UnityEngine.Time.deltaTime;

            if (Time > _duration)
            {
                _complete = true;
                triggerUpdateEvent();
                triggerEndEvent();
            }
            else
            {
                triggerUpdateEvent();
            }
        }

        public void FixedUpdate()
        {
            if (Config.OverrideGravity)
            {
                Rigidbody.useGravity = false;
                Rigidbody.AddForce(Config.GetGravity());
            }
        }

        public void Abort()
        {
            if (_complete)
                return;

            _complete = true;

            triggerEndEvent();
        }

        public Vector3 GetVelocity()
        {
            if (Time == 0f && _startVelocity.HasValue)
                return _startVelocity.Value;

            if (Config.Dimensions == PhysicsDimensions.Physics3D)
            {
                // 3D
                gameObject.TryGetComponent<Rigidbody>(out var rb);
                if (rb != null)
                {
                    return rb.GetVelocity();
                }
            }
            else
            {
                // 2D
                gameObject.TryGetComponent<Rigidbody2D>(out var rb);
                if (rb != null)
                {
#if UNITY_6000_0_OR_NEWER
                    return rb.linearVelocity;
#else
                    return rb.velocity;
#endif
                }
            }

            return Vector3.zero;
        }

        public Vector2 GetVelocity2D()
        {
            if (Time == 0f && _startVelocity.HasValue)
                return _startVelocity.Value;

            if (Config.Dimensions == PhysicsDimensions.Physics2D)
            {
                gameObject.TryGetComponent<Rigidbody2D>(out var rb);
                if (rb != null)
                {
#if UNITY_6000_0_OR_NEWER
                    return rb.linearVelocity;
#else
                    return rb.velocity;
#endif
                }
            }

            return Vector2.zero;
        }

        public Vector3 Evaluate(float timeInSec)
        {
            var startVelocity = GetStartVelocity();
            if (!startVelocity.HasValue)
            {
                return Config.SourcePosition + Config.GetSourceOffsetInWorldSpace();
            }
            else
            {
                return BallisticUtils.CalcPositionByStartVelocity(startVelocity.Value, timeInSec, Config.GetGravity());
            }
        }

        public Vector3 Evaluate(float timeInSec, out Vector3 velocity)
        {
            var startVelocity = GetStartVelocity();
            if (!startVelocity.HasValue)
            {
                velocity = Vector3.zero;
                return Config.GetSourcePosWithOffset();
            }
            else
            {
                return BallisticUtils.CalcPositionByStartVelocity(out velocity, startVelocity.Value, timeInSec, Config.GetGravity(), Config.Dimensions);
            }
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
            _duration = 0f;
            _complete = false;
            _destroyed = false;
            _isInitialized = false;
        }

        public void OnDestroy()
        {
            _destroyed = true;
            ProjectileRegistry.Instance.UnregisterProjectile(this);
        }
    }
}