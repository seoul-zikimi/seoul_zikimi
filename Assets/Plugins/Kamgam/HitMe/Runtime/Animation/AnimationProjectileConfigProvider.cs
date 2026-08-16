using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// A helper component to make managing configs easier.<br />
    /// It handles config assets and config creation.
    /// </summary>
    [AddComponentMenu("Hit Me/Animation Projectile Config Provider")]
    public class AnimationProjectileConfigProvider : MonoBehaviour
    {
        [Header("Config")]
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
                    return _config;
                }
            }

            set
            {
                _config = value;
            }
        }
    }
}