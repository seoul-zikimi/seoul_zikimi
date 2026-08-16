#if KAMGAM_VISUAL_SCRIPTING
using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnBallisticCollision3D = "HitMe_EventBallisticCollision3D";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Ballistic Projectile Collision3D Event")]
    public class BallisticProjectileCollision3DEvent : ProjectileEventNodeBase<BallisticProjectile, Collision, Collider>
    {
        protected override string getHookName()
        {
            return EventNames.OnBallisticCollision3D;
        }

        protected override void registerEvent(BallisticProjectile projectile)
        {
            projectile.OnCollision3D += onCollision3D;
        }

        protected override void unregisterEvent(BallisticProjectile projectile)
        {
            projectile.OnCollision3D -= onCollision3D;
        }

        protected void onCollision3D(BallisticProjectile projectile, Collision collision, CollisionTrigger trigger, Collider collider)
        {
            triggerEvent(projectile, collision, trigger, collider);
        }
    }
}
#endif