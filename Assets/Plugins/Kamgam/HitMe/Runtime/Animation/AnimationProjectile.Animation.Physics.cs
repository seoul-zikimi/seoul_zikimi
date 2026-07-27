using System.Collections.Generic;
using UnityEngine;

namespace Kamgam.HitMe
{
    public partial class AnimationProjectile : ICollisionTriggerReceiver
    {
        public List<Collider> CollisionTriggers;
        public List<Collider2D> CollisionTriggers2D;

        protected Rigidbody _rigidbody;
        public Rigidbody Rigidbody
        {
            get
            {
                if (_rigidbody == null)
                {
                    gameObject.TryGetComponent(out _rigidbody);
                }
                return _rigidbody;
            }
        }

        protected Rigidbody2D _rigidbody2D;
        public Rigidbody2D Rigidbody2D
        {
            get
            {
                if (_rigidbody2D == null)
                {
                    gameObject.TryGetComponent(out _rigidbody2D);
                }
                return _rigidbody2D;
            }
        }

        protected TransformPhysics _transformPhysics;
        public TransformPhysics TransformPhysics
        {
            get
            {
                if (_transformPhysics == null)
                {
                    _transformPhysics = TransformPhysics.GetOrCreate(gameObject);
                }
                return _transformPhysics;
            }
        }

        protected TransformPhysics2D _transformPhysics2D;
        public TransformPhysics2D TransformPhysics2D
        {
            get
            {
                if (_transformPhysics2D == null)
                {
                    _transformPhysics2D = TransformPhysics2D.GetOrCreate(gameObject);
                }
                return _transformPhysics2D;
            }
        }

        // Called from MonoBehaviour.Update()
        protected void moveToPositionWithPhysics(Vector3 position)
        {
            if (Config.UseFixedUpdate)
            {
                if (_targetType == TargetType.Rigidbody)
                {
                    TransformPhysics.SetPosition(position);
                }
                else if (_targetType == TargetType.Rigidbody2D)
                {
                    TransformPhysics2D.SetPosition(position);
                }
            }
            else
            {
                if (_targetType == TargetType.Rigidbody)
                {
                    Rigidbody.MovePosition(position);
                }
                else if (_targetType == TargetType.Rigidbody2D)
                {
                    Rigidbody2D.MovePosition(position);
                }
            }

            // Always update the transform
            transform.position = position;
        }

        protected void setVelocityWithPhysics(Vector3 velocity)
        {
            if (Config.UseFixedUpdate)
            {
                if (_targetType == TargetType.Rigidbody)
                {
                    TransformPhysics.SetVelocity(velocity);
                }
                else if (_targetType == TargetType.Rigidbody2D)
                {
                    TransformPhysics2D.SetVelocity(velocity);
                }
            }
            else
            {
                if (_targetType == TargetType.Rigidbody)
                {
                    Rigidbody.AddForce(velocity - Rigidbody.GetVelocity(), ForceMode.VelocityChange);
                }
                else if (_targetType == TargetType.Rigidbody2D)
                {
#if UNITY_6000_0_OR_NEWER
                    Rigidbody2D.linearVelocity = velocity;
#else
                    Rigidbody2D.velocity = velocity;
#endif
                }
            }
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
                else if(collision3D != null)
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

                // Stop animation
                AbortAnimation();
            }
        }

        public bool CanCollide()
        {
            return Config.StopOnCollision && Time > Config.CollisionMinAge;
        }

        protected bool _usedGravity;
        protected bool _isKinematic;
#if UNITY_6000_5_OR_NEWER
        protected RigidbodyType2D _bodyType2D;
#endif
        protected float _usedGravityScale;

        protected void preparePhysicsBeforeAnimation()
        {
            if (!Config.SupportPhysics)
                return;

            // Revert previoius preparations in case this is called twice
            if(_physicsPrepared)
            {
                restorePhysicsAfterAnimation();
            }

            _physicsPrepared = true;

            triggerDisablePhysicsEvent();

            if (Rigidbody != null)
            {
                _usedGravity = Rigidbody.useGravity;
                Rigidbody.useGravity = false;

                _isKinematic = Rigidbody.isKinematic;
                Rigidbody.isKinematic = true;
            }
            else if (Rigidbody2D != null)
            {
                _usedGravityScale = Rigidbody2D.gravityScale;
                Rigidbody2D.gravityScale = 0f;
#if UNITY_6000_5_OR_NEWER
                _bodyType2D = Rigidbody2D.bodyType;
                Rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
#else
                _isKinematic = Rigidbody2D.isKinematic;
                Rigidbody2D.isKinematic = true;
#endif
            }
        }

        protected void restorePhysicsAfterAnimation()
        {
            if (!Config.SupportPhysics)
                return;

            if (Rigidbody != null)
            {
                Rigidbody.useGravity = _usedGravity;
                Rigidbody.isKinematic = _isKinematic;
            }
            if (Rigidbody2D != null)
            {
                Rigidbody2D.gravityScale = _usedGravityScale;
#if UNITY_6000_5_OR_NEWER
                Rigidbody2D.bodyType = _bodyType2D;
#else
                Rigidbody2D.isKinematic = _isKinematic;
#endif
            }
            setVelocityWithPhysics(_velocity); 
            // Debug.Log("End Velocity: " + _velocity + " , magnitude: " + _velocity.magnitude);

            _physicsPrepared = false;

            triggerEnablePhysicsEvent();
        }
    }
}