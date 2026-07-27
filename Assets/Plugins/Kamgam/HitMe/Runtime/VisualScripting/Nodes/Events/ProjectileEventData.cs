#if KAMGAM_VISUAL_SCRIPTING
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public struct ProjectileEventData<TProjectile, TCollision, TCollider>
        where TProjectile : IProjectile
        where TCollision : class
        where TCollider : class
    {
        public TProjectile Projectile;
        public TCollision Collision;
        public CollisionTrigger Trigger;
        public TCollider Collider;

        public ProjectileEventData(TProjectile projectile, TCollision collision, CollisionTrigger trigger, TCollider collider)
        {
            Projectile = projectile;
            Collision = collision;
            Trigger = trigger;
            Collider = collider;
        }
    }
}
#endif