#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationUpdate = "HitMe_EventAnimationUpdate";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event Update")]
    public class AnimationProjectileEventUpdate : ProjectileEventNodeBase<AnimationProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnAnimationUpdate;
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnUpdate += onUpdate;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnUpdate -= onUpdate;
        }

        public void onUpdate(AnimationProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif