#if KAMGAM_VISUAL_SCRIPTING
using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationCollision2D = "HitMe_EventAnimationCollision2D";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event Collision2D")]
    [RenamedFrom("AnimationProjectileEventCollision2D")]
    public class AnimationProjectileCollision2DEvent : ProjectileEventNodeBase<AnimationProjectile, Collision2D, Collider2D>
    {
        [AllowsNull]
        public ValueOutput collisionOut;

        [AllowsNull]
        public ValueOutput colliderOut;

        [AllowsNull]
        public ValueOutput collisionTriggerOut;

        protected override string getHookName()
        {
            return EventNames.OnAnimationCollision2D;
        }

        protected override void definition()
        {
            collisionOut = ValueOutput<Collision2D>("Collision2D");
            collisionTriggerOut = ValueOutput<CollisionTrigger>("CollisionTrigger");
            colliderOut = ValueOutput<Collider2D>("Collider2D");
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnCollision2D += onCollision2D;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnCollision2D -= onCollision2D;
        }

        private void onCollision2D(AnimationProjectile projectile, Collision2D collision, CollisionTrigger trigger, Collider2D collider)
        {
            triggerEvent(projectile, collision, trigger, collider);
        }

        protected override void assignArguments(Flow flow, AnimationProjectile projectile, Collision2D collision, CollisionTrigger trigger, Collider2D collider)
        {
            flow.SetValue(collisionOut, collision);
            flow.SetValue(collisionTriggerOut, trigger);
            flow.SetValue(colliderOut, collider);
        }
    }
}
#endif