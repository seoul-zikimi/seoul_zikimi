#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationPhyscisDisabled = "HitMe_EventAnimationPhysicsEnabledDisabled";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event Physics Disabled")]
    [RenamedFrom("AnimationProjectileEventPhysicsDisabled")]
    public class AnimationProjectilePhysicsDisabledEvent : ProjectileEventNodeBase<AnimationProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnAnimationPhyscisDisabled;
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnDisablePhysics += onDisablePhysics;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnDisablePhysics -= onDisablePhysics;
        }

        public void onDisablePhysics(AnimationProjectile projectile)
        {
            triggerEvent(projectile);
        }
    }
}
#endif