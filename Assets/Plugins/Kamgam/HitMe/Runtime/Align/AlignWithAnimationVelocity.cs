using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Aligns the view direction with the curve of the AnimationProjectile.
    /// </summary>
    [AddComponentMenu("Hit Me/Align/Align With Animation Velocity")]
    [RequireComponent(typeof(AnimationProjectile))]
    public class AlignWithAnimationVelocity : MonoBehaviour
    {
        protected AnimationProjectile _projectile;
        public AnimationProjectile Projectile
        {
            get
            {
                if (_projectile == null)
                {
                    _projectile = this.GetComponent<AnimationProjectile>();
                }
                return _projectile;
            }
        }

        [Tooltip("The target that should be aligned. Leave null to use the object itself.")]
        public Transform Target;

        public bool Active = true;
        public bool StopOnCollision = false;
        public bool StopAfterAnimation = true;
        public bool Apply = true;

        /// <summary>
        /// Use the y-axis as the forward axis. Useful for 2D physics.
        /// </summary>
        [Tooltip("Use the y-axis as the forward axis. Useful for 2D physics.")]
        bool ForwardAxisY = false;
        bool DetectForwardAxisAtStart = true;

        [System.NonSerialized]
        public Quaternion Result;

        [System.NonSerialized]
        public bool HasResult = false;

        protected Rigidbody _rigidbody;
        public Rigidbody Rigidbody
        {
            get
            {
                if (_rigidbody == null)
                {
                    _rigidbody = this.GetComponent<Rigidbody>();
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
                    _rigidbody2D = this.GetComponent<Rigidbody2D>();
                }
                return _rigidbody2D;
            }
        }

        public enum RigidBodyType
        {
            None = 0, Rigidbody = 1, Rigidbody2D = 2
        }

        protected RigidBodyType _rigidbodyType = RigidBodyType.None;

        public void Start()
        {
            RefreshRigidboyType();

            if (DetectForwardAxisAtStart)
                DetectForwardAxis();
        }

        public void RefreshRigidboyType()
        {
            if (Target == null)
                Target = transform;

            if (Rigidbody != null)
            {
                _rigidbodyType = RigidBodyType.Rigidbody;
            }
            else if (Rigidbody2D != null)
            {
                _rigidbodyType = RigidBodyType.Rigidbody2D;
            }
            else
            {
                _rigidbodyType = RigidBodyType.None;
            }
        }

        /// <summary>
        /// If the object has a Rigidbody2D or a Collider2D then set the Y axis as the forward axis.
        /// </summary>
        public void DetectForwardAxis()
        {
            ForwardAxisY = TryGetComponent<Rigidbody2D>(out _) || TryGetComponent<Collider2D>(out _);
        }

        public void LateUpdate()
        {
            if (!Active)
                return;

            if (!StopAfterAnimation || !Projectile.IsAnimationComplete())
                updateRotation();
        }

        protected void updateRotation()
        {
            // Align to Velocity vector
            if (Target == null)
                Target = transform;

            if (Projectile == null)
                return;

            var velocity = Projectile.GetVelocity();
            var upAxis = Projectile.GetUpAxis();
            var rot = Target.transform.rotation;

            if (velocity.sqrMagnitude > 0.01f && upAxis.sqrMagnitude > 0.01f)
            {
                rot.SetLookRotation(velocity, upAxis);
            }

            // In 2D mode, rotate around the up axis by 90 to make the y-axis the new forward axis.
            if (ForwardAxisY)
            {
                rot = Quaternion.AngleAxis(-90f, rot * Vector3.up) * rot;
            }

            Result = rot;
            HasResult = true;

            if (Apply)
            {
                // Keeping those for future debugging needs. They show the up and forward axis.
                //Debug.DrawLine(transform.position, transform.position + velocity, Color.blue);
                //Debug.DrawLine(transform.position, transform.position + upAxis, Color.green);

                setRotation(Result.normalized);
            }
        }

        protected void setRotation(Quaternion rotation)
        {
            if (_rigidbodyType != RigidBodyType.None)
            {
                switch (_rigidbodyType)
                {
                    case RigidBodyType.Rigidbody:
                        Rigidbody.MoveRotation(rotation);
                        break;
                    case RigidBodyType.Rigidbody2D:
                        Rigidbody2D.MoveRotation(rotation);
                        break;
                }
            }

            Target.transform.rotation = rotation;
        }

        public void OnCollisionEnter(Collision collision)
        {
            if (StopOnCollision)
                Active = false;
        }

        public void OnCollisionEnter2D(Collision2D collision)
        {
            if (StopOnCollision)
                Active = false;
        }

        public void Reset()
        {
            if (Target == null)
            {
                Target = this.transform;
            }

            Active = true;
        }
    }
}