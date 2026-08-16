using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Component to destroy a gameobject based on some criteria (time, collision).
    /// </summary>
    [AddComponentMenu("Hit Me/Destruction")]
    public partial class Destruction : MonoBehaviour
    {
        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public DestructionConfigAsset ConfigAsset = null;
        protected DestructionConfigAsset _configAssetInstance = null;

        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
        protected DestructionConfig _config = null;
        protected bool _configWasSet = false;

        public DestructionConfig Config
        {
            get
            {
                // _configWasSet is needed because this object is serialized in the editor inspector
                // and thus _config will always have the default value (aka it will never be null if set in the inspector).
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

        /// <summary>
        /// The age of the projectile since spawning in seconds.
        /// </summary>
        public float Time { get; private set; } = 0f;

        public float TimeScale = 1f;

        public bool Paused { get; set; }

        protected bool _destroyed = false;

        public void Update()
        {
            if (_destroyed)
                return;

            if (Paused)
                return;

            Time += UnityEngine.Time.deltaTime * TimeScale;

            // Evaluate Time destruction
            if (Config.Mode == DestructionMode.AfterTime && Time > Config.AfterTime)
            {
                DestroyNow();
            }

			// If MaxLifetime is set (!= 0) then destroy if time > MaxLifeTime
            if (!Mathf.Approximately(Config.MaxLifetime, 0f) && Time >= Config.MaxLifetime)
            {
                DestroyNow();
            }
        }

        protected int _collisionCounter = 0;

        public void OnCollisionEnter(Collision collision)
        {
            if (_destroyed)
                return;

            if (Time < Config.CollisionMinAge)
                return;

            _collisionCounter++;
            evaluateImpactDestruction();
        }

        public void OnCollisionEnter2D(Collision2D collision)
        {
            if (_destroyed)
                return;

            if (Time < Config.CollisionMinAge)
                return;

            _collisionCounter++;
            evaluateImpactDestruction();
        }

        public void Reset(bool resetConfig)
        {
            if (resetConfig)
            {
                ConfigAsset = null;
                _configAssetInstance = null;
                _config = null;
                _configWasSet = false;
            }

            Time = 0f;
            _collisionCounter = 0;
            _destroyed = false;
        }

        public void OnDestroy()
        {
            _destroyed = true;
        }

        protected void evaluateImpactDestruction()
        {
            if (Config.Mode == DestructionMode.AtImpact && _collisionCounter >= Config.ImpactThreshold)
            {
                DestroyNow();
            }
            else if (Config.Mode == DestructionMode.AfterImpactDelayed && _collisionCounter >= Config.ImpactThreshold)
            {
                if (Config.AfterImpactDelay <= 0f)
                    DestroyNow();
                else if(_destroyDelayedRoutine == null)
                    DestroyDelayed(Config.AfterImpactDelay);
            }
        }

        protected Coroutine _destroyDelayedRoutine;

        public void DestroyDelayed(float delayInSec, bool realTime = false)
        {
            if (_destroyDelayedRoutine != null)
                StopCoroutine(_destroyDelayedRoutine);

            _destroyDelayedRoutine = StartCoroutine(DestroyDelayedRoutine(delayInSec, realTime));
        }

        public IEnumerator DestroyDelayedRoutine(float delayInSec, bool realTime = false)
        {
            if (realTime)
                yield return new WaitForSecondsRealtime(delayInSec);
            else
                yield return new WaitForSeconds(delayInSec);

            _destroyDelayedRoutine = null;
            DestroyNow();
        }

        public void DestroyNow()
        {
            if (_destroyed)
                return;

            _destroyed = true;

            StopAllCoroutines();

            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}