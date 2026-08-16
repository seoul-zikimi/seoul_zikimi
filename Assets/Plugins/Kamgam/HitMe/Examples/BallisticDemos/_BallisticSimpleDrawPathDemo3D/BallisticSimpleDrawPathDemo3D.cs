using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class BallisticSimpleDrawPathDemo3D : MonoBehaviour
    {
        public Transform Source;
        public Transform Target;

        public GameObject ProjectilePrefab;
        public GameObject PathLineRendererPrefab;
        public BallisticProjectileConfig Config;

        protected ProjectileLineRenderer _cachedPathRenderer;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            // Draw the path using a "ProjectileLineRenderer".
            // NOTICE: This will create a NEW renderer object every time you call it. Below is an example which reuses the renderer.
            bool possible;
            var renderer = BallisticProjectile.DrawPath<ProjectileLineRenderer>(out possible, Source, Target, Config, prefab: PathLineRendererPrefab);
            
            if (!possible)
            {
                Debug.Log("It's not possible to reach the target with the given configuration.");
            }

            // Animate the projectile.
            BallisticProjectile.Spawn(ProjectilePrefab, Source, Target, Config);
        }

        public void Update()
        {
            // This does the same as the code above, except that it reuses the renderer.

            bool possible;
            // Notice how _cachedPathRenderer is an input and an output. At the very first time the input _cachedPathRenderer will be null
            // but every time after that it will reuse the _cachedPathRenderer for rendering.
            // The prefab is only used at the very first time to create the renderer.
            _cachedPathRenderer = BallisticProjectile.DrawPath(out possible, Source, Target, Config, renderer: _cachedPathRenderer, prefab: PathLineRendererPrefab);

            if (!possible)
            {
                Debug.Log("It's not possible to reach the target with the given configuration.");
            }

            // Try moving the the target around while in play mode and you will see that you actually have two line renderers in the scene.
            // One created in Start() which does not follow the target and one being created and reused in Update().
        }
    }
}