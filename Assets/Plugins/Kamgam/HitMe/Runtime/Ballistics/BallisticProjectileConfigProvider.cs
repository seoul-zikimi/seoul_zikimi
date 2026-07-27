using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// A helper component to make managing configs easier.<br />
    /// It handles config assets and config creation.
    /// </summary>
    [AddComponentMenu("Hit Me/Ballistic Projectile Config Provider")]
    public class BallisticProjectileConfigProvider : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("You can reference a config from an asset. If set then this will override the local config.")]
        public BallisticProjectileConfigAsset ConfigAsset = null;
        protected BallisticProjectileConfigAsset _configAssetInstance = null;

        [SerializeField]
        [ShowIf("ConfigAsset", null, ShowIfAttribute.DisablingType.ReadOnly)]
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
    }
}