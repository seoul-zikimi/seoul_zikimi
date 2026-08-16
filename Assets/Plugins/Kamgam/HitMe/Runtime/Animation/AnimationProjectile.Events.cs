using UnityEngine;
using UnityEngine.Events;

namespace Kamgam.HitMe
{
    public partial class AnimationProjectile
    {
        // PHYSICS

        /// <summary>
        /// Called before the animation starts.<br />
        /// Use this to disable any custom physics objects on the projectile.<br />
        /// If SupportPhysics is off then this will never execute.
        /// </summary>
        public event System.Action<AnimationProjectile> OnDisablePhysics;

        /// <summary>
        /// Called before the animation starts.<br />
        /// Use this to disable any custom physics objects on the projectile.<br />
        /// If SupportPhysics is off then this will never execute.
        /// </summary>
        public UnityEvent<AnimationProjectile> OnDisablePhysicsEvent;

        /// <summary>
        /// Called after the animation has ended (or a collision was detected if StopOnCollision is enabled).<br />
        /// Use this to disable any custom physics objects on the projectile.<br />
        /// If SupportPhysics is off then this will never execute.
        /// </summary>
        public event System.Action<AnimationProjectile> OnEnablePhysics;

        /// <summary>
        /// Called after the animation has ended (or a collision was detected if StopOnCollision is enabled).<br />
        /// Use this to disable any custom physics objects on the projectile.<br />
        /// If SupportPhysics is off then this will never execute.
        /// </summary>
        public UnityEvent<AnimationProjectile> OnEnablePhysicsEvent;

        protected void triggerDisablePhysicsEvent()
        {
            OnDisablePhysics?.Invoke(this);
            OnDisablePhysicsEvent?.Invoke(this);
        }

        protected void triggerEnablePhysicsEvent()
        {
            OnEnablePhysics?.Invoke(this);
            OnEnablePhysicsEvent?.Invoke(this);
        }

        // LIFECYCLE
        public event System.Action<AnimationProjectile> OnStart;
        public UnityEvent<AnimationProjectile> OnStartEvent;

        public event System.Action<AnimationProjectile> OnUpdate;
        public UnityEvent<AnimationProjectile> OnUpdateEvent;

        public event System.Action<AnimationProjectile> OnEnd;
        public UnityEvent<AnimationProjectile> OnEndEvent;

        protected void triggerStartEvent()
        {
            OnStart?.Invoke(this);
            OnStartEvent?.Invoke(this);
        }

        protected void triggerUpdateEvent()
        {
            OnUpdate?.Invoke(this);
            OnUpdateEvent?.Invoke(this);
        }

        protected void triggerEndEvent()
        {
            OnEnd?.Invoke(this);
            OnEndEvent?.Invoke(this);
        }


        // COLLISIONS
        public int _triggeredCollisions = 0;

        public event System.Action<AnimationProjectile, Collision2D, CollisionTrigger, Collider2D> OnCollision2D;
        public UnityEvent<AnimationProjectile, Collision2D, CollisionTrigger, Collider2D> OnCollision2DEvent;

        public event System.Action<AnimationProjectile, Collision, CollisionTrigger, Collider> OnCollision3D;
        public UnityEvent<AnimationProjectile, Collision, CollisionTrigger, Collider> OnCollision3DEvent;

        protected void triggerCollisionEvent2D(Collision2D collision)
        {
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_animationComplete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision2D?.Invoke(this, collision, null, null);
            OnCollision2DEvent?.Invoke(this, collision, null, null);
        }

        protected void triggerCollisionEvent2D(CollisionTrigger trigger, Collider2D collider)
        {
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_animationComplete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision2D?.Invoke(this, null, trigger, collider);
            OnCollision2DEvent?.Invoke(this, null, trigger, collider);
        }

        protected void triggerCollisionEvent3D(Collision collision)
        {
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_animationComplete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision3D?.Invoke(this, collision, null, null);
            OnCollision3DEvent?.Invoke(this, collision, null, null);
        }

        protected void triggerCollisionEvent3D(CollisionTrigger trigger, Collider collider)
        {
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_animationComplete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision3D?.Invoke(this, null, trigger, collider);
            OnCollision3DEvent?.Invoke(this, null, trigger, collider);
        }

        public void ResetCollisionCounter()
        {
            _triggeredCollisions = 0;
        }
    }
}