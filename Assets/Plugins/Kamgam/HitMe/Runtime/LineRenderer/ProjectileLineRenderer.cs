using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// A wrapper for the Unity LineRenderer. Sadly we need this because we can not extend the LineRenderer (sealed) class.<br />
    /// You can replace this with your own line renderer if you want.
    /// </summary>
    [AddComponentMenu("Hit Me/Projectile Line Renderer")]
    [RequireComponent(typeof(LineRenderer))]
    public class ProjectileLineRenderer : MonoBehaviour, IProjectileLineRenderer
    {
        protected LineRenderer _renderer;
        public LineRenderer Renderer
        {
            get
            {
                if (_renderer == null)
                {
                    _renderer = this.GetComponent<LineRenderer>();
                }
                return _renderer;
            }
        }

        public int positionCount
        {
            get => Renderer.positionCount;
            set => Renderer.positionCount = value;
        }

        public void SetPosition(int index, Vector3 position)
        {
            Renderer.SetPosition(index, position);
        }
        
        public void SetAlpha(float alpha)
        {
            Gradient gradient = Renderer.colorGradient;
            GradientAlphaKey[] alphaKeys = gradient.alphaKeys;
   
            for (int i = 0; i < alphaKeys.Length; i++)
            {
                alphaKeys[i].alpha = alpha;
            }
   
            gradient.SetKeys(gradient.colorKeys, alphaKeys);
            Renderer.colorGradient = gradient;
        }
    }
}