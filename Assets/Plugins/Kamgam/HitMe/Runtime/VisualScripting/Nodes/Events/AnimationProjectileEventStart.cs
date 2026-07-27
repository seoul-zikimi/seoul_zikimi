#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationStart = "HitMe_EventAnimationStart";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event Start")]
    public class AnimationProjectileEventStart : ProjectileEventNodeBase<AnimationProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnAnimationStart;
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnStart += onStart;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnStart -= onStart;
        }

        public void onStart(AnimationProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif