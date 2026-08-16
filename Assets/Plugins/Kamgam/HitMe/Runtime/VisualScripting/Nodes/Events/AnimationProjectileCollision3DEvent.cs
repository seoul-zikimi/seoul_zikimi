#if KAMGAM_VISUAL_SCRIPTING
using Unity.VisualScripting;
using UnityEngine;

namespace Kamgam.HitMe.Nodes
{
    public static partial class EventNames
    {
        public static string OnAnimationCollision3D = "HitMe_EventAnimationCollision3D";
    }

    [IncludeInSettings(include: false)]
    [UnitCategory("Events\\Hit Me")] // Events have to be under the Events category: https://forum.unity.com/threads/c-custom-unitcategory-for-events.1178791/#post-7549207
    [UnitTitle("Animation Projectile Event Collision3D")]
    [RenamedFrom("AnimationProjectileEventCollision3D")]
    public class AnimationProjectileCollision3DEvent : ProjectileEventNodeBase<AnimationProjectile, Collision, Collider>
    {
        [AllowsNull]
        public ValueOutput collisionOut;

        [AllowsNull]
        public ValueOutput colliderOut;

        [AllowsNull]
        public ValueOutput collisionTriggerOut;

        protected override string getHookName()
        {
            return EventNames.OnAnimationCollision3D;
        }

        protected override void definition()
        {
            collisionOut = ValueOutput<Collision>("Collision3D");
            collisionTriggerOut = ValueOutput<CollisionTrigger>("CollisionTrigger");
            colliderOut = ValueOutput<Collider>("Collider3D");
        }

        protected override void registerEvent(AnimationProjectile projectile)
        {
            projectile.OnCollision3D += onCollision3D;
        }

        protected override void unregisterEvent(AnimationProjectile projectile)
        {
            projectile.OnCollision3D -= onCollision3D;
        }

        private void onCollision3D(AnimationProjectile projectile, Collision collision, CollisionTrigger trigger, Collider collider)
        {
            triggerEvent(projectile, collision, trigger, collider);
        }

        protected override void assignArguments(Flow flow, AnimationProjectile projectile, Collision collision, CollisionTrigger trigger, Collider collider)
        {
            flow.SetValue(collisionOut, collision);
            flow.SetValue(collisionTriggerOut, trigger);
            flow.SetValue(colliderOut, collider);
        }
    }
}
#endif