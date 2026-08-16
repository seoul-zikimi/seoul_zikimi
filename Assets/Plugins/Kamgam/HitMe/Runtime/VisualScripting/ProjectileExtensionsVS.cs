#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe
{
    [IncludeInSettings(include: true)]
    public static class ProjectileExtensionsVS
    {
        public static AnimationProjectileSource GetAnimationSource(GameObject go)
        {
            AnimationProjectileSource source;
            go.TryGetComponent(out source);
            return source;
        }

        public static AnimationProjectileConfig GetConfig(AnimationProjectile projectile)
        {
            return projectile.Config;
        }

        public static AnimationProjectile SetActive(AnimationProjectile projectile, bool active)
        {
            projectile.SetActive(active);
            return projectile;
        }

        public static AnimationProjectile Pause(AnimationProjectile projectile)
        {
            projectile.Paused = true;
            return projectile;
        }

        public static AnimationProjectile Resume(AnimationProjectile projectile)
        {
            projectile.Paused = false;
            return projectile;
        }

        public static AnimationProjectile Abort(AnimationProjectile projectile)
        {
            projectile.AbortAnimation();
            return projectile;
        }

        public static void Destroy(AnimationProjectile projectile)
        {
            if (projectile.gameObject == null)
                return;

            projectile.AbortAnimation();
            GameObject.Destroy(projectile.gameObject);
        }

        public static Vector3 Evaluate(AnimationProjectile projectile, float time)
        {
            return projectile.Evaluate(time);
        }

        public static float GetTime(AnimationProjectile projectile)
        {
            return projectile.Time;
        }




        public static BallisticProjectileSource GetBallisticSource(GameObject go)
        {
            BallisticProjectileSource source;
            go.TryGetComponent(out source);
            return source;
        }

        public static BallisticProjectileConfig GetConfig(BallisticProjectile projectile)
        {
            return projectile.Config;
        }

        public static BallisticProjectile SetActive(BallisticProjectile projectile, bool active)
        {
            projectile.SetActive(active);
            return projectile;
        }

        public static BallisticProjectile Abort(BallisticProjectile projectile)
        {
            projectile.Abort();
            return projectile;
        }

        public static void Destroy(BallisticProjectile projectile)
        {
            if (projectile.gameObject == null)
                return;

            projectile.Abort();
            GameObject.Destroy(projectile.gameObject);
        }

        public static Vector3 Evaluate(BallisticProjectile projectile, float time)
        {
            return projectile.Evaluate(time);
        }

        public static float GetTime(BallisticProjectile projectile)
        {
            return projectile.Time;
        }
    }
}
#endif