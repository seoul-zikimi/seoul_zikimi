using UnityEngine;
using UnityEngine.Events;

namespace Kamgam.HitMe
{
    public partial class BallisticProjectile
    {
        // LIFECYCLE
        public event System.Action<BallisticProjectile> OnStart;
        public UnityEvent<BallisticProjectile> OnStartEvent;

        public event System.Action<BallisticProjectile> OnUpdate;
        public UnityEvent<BallisticProjectile> OnUpdateEvent;

        public event System.Action<BallisticProjectile> OnEnd;
        public UnityEvent<BallisticProjectile> OnEndEvent;

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

        public event System.Action<BallisticProjectile, Collision2D, CollisionTrigger, Collider2D> OnCollision2D;
        public UnityEvent<BallisticProjectile, Collision2D, CollisionTrigger, Collider2D> OnCollision2DEvent;

        public event System.Action<BallisticProjectile, Collision, CollisionTrigger, Collider> OnCollision3D;
        public UnityEvent<BallisticProjectile, Collision, CollisionTrigger, Collider> OnCollision3DEvent;

        protected void triggerCollisionEvent2D(Collision2D collision)
        {
            if (    Config.UseCollisionValidation
                &&  Config.CollisionValidation2D != null 
                && !Config.CollisionValidation2D(this, collision, null, null))
                return;
            
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_complete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision2D?.Invoke(this, collision, null, null);
            OnCollision2DEvent?.Invoke(this, collision, null, null);
        }

        protected void triggerCollisionEvent2D(CollisionTrigger trigger, Collider2D collider)
        {
            if (     Config.UseCollisionValidation
                 &&  Config.CollisionValidation2D != null 
                 && !Config.CollisionValidation2D(this, null, trigger, collider))
                return;
            
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_complete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision2D?.Invoke(this, null, trigger, collider);
            OnCollision2DEvent?.Invoke(this, null, trigger, collider);
        }

        protected void triggerCollisionEvent3D(Collision collision)
        {
            if (     Config.UseCollisionValidation
                 &&  Config.CollisionValidation3D != null 
                 && !Config.CollisionValidation3D(this, collision, null, null))
                return;
            
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_complete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision3D?.Invoke(this, collision, null, null);
            OnCollision3DEvent?.Invoke(this, collision, null, null);
        }

        protected void triggerCollisionEvent3D(CollisionTrigger trigger, Collider collider)
        {
            if (     Config.UseCollisionValidation
                 &&  Config.CollisionValidation3D != null 
                 && !Config.CollisionValidation3D(this, null, trigger, collider))
                return;
            
            _triggeredCollisions++;
            if (_triggeredCollisions > Config.TriggerFirstNCollisions)
                return;

            if (_complete && !Config.TriggerCollisionsAfterEnd)
                return;

            OnCollision3D?.Invoke(this, null, trigger, collider);
            OnCollision3DEvent?.Invoke(this, null, trigger, collider);
        }

        public void ResetCollisionCounter()
        {
            _triggeredCollisions = 0;
        }

        public void OnCollisionEnter(Collision collision)
        {
            TriggerCollision(collision3D: collision);
        }

        public void OnCollisionEnter2D(Collision2D collision)
        {
            TriggerCollision(collision2D: collision);
        }

        public void TriggerCollision2D(CollisionTrigger trigger, Collider2D other)
        {
            TriggerCollision(trigger: trigger, collider2D: other);
        }

        public void TriggerCollision3D(CollisionTrigger trigger, Collider other)
        {
            TriggerCollision(trigger: trigger, collider3D: other);
        }

        public void TriggerCollision(Collision collision3D = null, Collision2D collision2D = null, CollisionTrigger trigger = null, Collider collider3D = null, Collider2D collider2D = null)
        {
            if (CanCollide())
            {
                // Trigger events
                if (collision2D != null)
                {
                    triggerCollisionEvent2D(collision2D);
                }
                else if (collision3D != null)
                {
                    triggerCollisionEvent3D(collision3D);
                }
                else if (trigger != null)
                {
                    if (collider2D != null)
                    {
                        triggerCollisionEvent2D(trigger, collider2D);
                    }
                    else
                    {
                        triggerCollisionEvent3D(trigger, collider3D);
                    }
                }

                Abort();
            }
        }

        public bool CanCollide()
        {
            return Time > Config.CollisionMinAge;
        }
    }
}