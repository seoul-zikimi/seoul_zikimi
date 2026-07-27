#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationEnd = "HitMe_EventAnimationEnd";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event End")]
    [RenamedFrom("AnimationProjectileEventEnd")]
    public class AnimationProjectileEndEvent : ProjectileEventNodeBase<AnimationProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnAnimationEnd;
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnEnd += onEnd;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnEnd -= onEnd;
        }

        public void onEnd(AnimationProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif