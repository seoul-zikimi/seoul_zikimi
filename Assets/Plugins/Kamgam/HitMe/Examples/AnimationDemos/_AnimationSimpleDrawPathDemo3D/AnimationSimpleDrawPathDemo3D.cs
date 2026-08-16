using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationSimpleDrawPathDemo3D : MonoBehaviour
    {
        public Transform Source;
        public Transform Target;

        public GameObject ProjectilePrefab;
        public GameObject PathLineRendererPrefab;
        public AnimationProjectileConfig Config;

        protected ProjectileLineRenderer _cachedPathRenderer;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            // Draw the path using a "ProjectileLineRenderer".
            // NOTICE: This will create a NEW renderer object every time you call it. Below is an example which reuses the renderer.
            var renderer = AnimationProjectile.DrawPath<ProjectileLineRenderer>(Source, Target, Config, prefab: PathLineRendererPrefab);

            // Animate the projectile.
            AnimationProjectile.Spawn(ProjectilePrefab, Source, Target, Config);
        }

        public void Update()
        {
            // This does the same as the code above, except that it reuses the renderer.

            // Notice how _cachedPathRenderer is an input and an output. At the very first time the input _cachedPathRenderer will be null
            // but every time after that it will reuse the _cachedPathRenderer for rendering.
            // The prefab is only used at the very first time to create the renderer.
            _cachedPathRenderer = AnimationProjectile.DrawPath(Source, Target, Config, renderer: _cachedPathRenderer, prefab: PathLineRendererPrefab);

            // Try moving the the target around while in play mode and you will see that you actually have two line renderers in the scene.
            // One created in Start() which does not follow the target and one being created and reused in Update().
        }
    }
}