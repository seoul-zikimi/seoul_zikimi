#if KAMGAM_VISUAL_SCRIPTING
using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnBallisticCollision2D = "HitMe_EventBallisticCollision2D";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Ballistic Projectile Collision2D Event")]
    public class BallisticProjectileCollision2DEvent : ProjectileEventNodeBase<BallisticProjectile, Collision2D, Collider2D>
    {
        protected override string getHookName()
        {
            return EventNames.OnBallisticCollision2D;
        }

        protected override void registerEvent(BallisticProjectile projectile)
        {
            projectile.OnCollision2D += onCollision2D;
        }

        protected override void unregisterEvent(BallisticProjectile projectile)
        {
            projectile.OnCollision2D -= onCollision2D;
        }

        protected void onCollision2D(BallisticProjectile projectile, Collision2D collision, CollisionTrigger trigger, Collider2D collider)
        {
            triggerEvent(projectile, collision, trigger, collider);
        }
    }
}
#endif