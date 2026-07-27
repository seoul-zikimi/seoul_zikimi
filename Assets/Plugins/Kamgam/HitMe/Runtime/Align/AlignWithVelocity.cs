using System;
using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Aligns the view direction with the velocity.<br /><br />
    /// NOTICE: In the very first frame after instantiation the direction will not be set yet. Use InitializeVelocity(velocity) to set the initial look direction.<br /><br />
    /// The calculated rotation is stored in "Result" and the "HasResult" flag is set to true once that has been done.<br /><br />
    /// You can use this information if Apply is set to false to read the rotation value and use elsewhere.
    /// </summary>
    [AddComponentMenu("Hit Me/Align/Align With Velocity")]
    public class AlignWithVelocity : MonoBehaviour
    {
        [Tooltip("The target that should be aligned. Leave null to use the object itself.")]
        public Transform Target;
        public bool Active = true;
        public bool StopOnCollision = false;
        public bool Apply = true;
        public Vector3 WorldUpAxis = Vector3.up;

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

        public enum VelocitySourceType
        {
            Rigidbody = 0, Rigidbody2D = 1, Transform = 2
        }

        protected VelocitySourceType _velocitySource = VelocitySourceType.Transform;

        protected Vector3? _lastPos;
        protected Vector3 _transformVelocity = Vector3.zero;
        protected Vector3 Velocity3D
        {
            get
            {
                switch (_velocitySource)
                {
                    case VelocitySourceType.Rigidbody:
                        return Rigidbody.GetVelocity();
                        
                    case VelocitySourceType.Rigidbody2D:
#if UNITY_6000_0_OR_NEWER
                        return Rigidbody2D.linearVelocity;
#else
                        return Rigidbody2D.velocity;
#endif
                    case VelocitySourceType.Transform:
                    default:
                        return _transformVelocity;
                }
            }
        }

        protected Vector2 Velocity2D
        {
            get
            {
                return Velocity3D;
            }
        }

        protected float Speed
        {
            get
            {
                return Velocity3D.magnitude;
            }
        }

        public void InitializeVelocity(Vector3 velocity, bool refreshSource = true)
        {
            if (refreshSource)
                RefreshVelocitySource();

            _transformVelocity = velocity;
            
            if(Apply)
            {
                Result = Target.transform.rotation;
                Result.SetLookRotation(velocity, WorldUpAxis);
                setRotation(Result);
            }
        }

        public void Start()
        {
            RefreshVelocitySource();

            if (DetectForwardAxisAtStart)
                DetectForwardAxis();
        }

        public void RefreshVelocitySource()
        {
            if (Target == null)
                Target = transform;

            if (Rigidbody != null && !Rigidbody.isKinematic)
            {
                _velocitySource = VelocitySourceType.Rigidbody;
            }
            else if (Rigidbody2D != null
#if UNITY_6000_5_OR_NEWER
                     && Rigidbody2D.bodyType != RigidbodyType2D.Kinematic)
#else
                     && !Rigidbody2D.isKinematic)
#endif
            {
                _velocitySource = VelocitySourceType.Rigidbody2D;
            }
            else
            {
                _velocitySource = VelocitySourceType.Transform;
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

            // Update transform velocity vector
            var pos = transform.position;
            if (_lastPos.HasValue)
            {
                _transformVelocity = (pos - _lastPos.Value) / Time.deltaTime;
            }
            _lastPos = pos;

            updateRotation();
        }

        protected void updateRotation()
        {
            // Align to Velocity vector
            if (_lastPos.HasValue && Velocity3D.sqrMagnitude > 0.001f)
            {
                if (Target == null)
                    Target = transform;

                Result.SetLookRotation(Velocity3D, WorldUpAxis);
                HasResult = true;

                // In 2D mode, rotate around the up axis by 90 to make the y-axis the new forward axis.
                if (ForwardAxisY)
                {
                    Result = Quaternion.AngleAxis(-90f, Result * Vector3.up) * Result;
                }

                if (Apply)
                {
                    // Formerly: Target.LookAt(pos + Velocity3D, WorldUpAxis);
                    setRotation(Result);
                }
            }
        }

        protected void setRotation(Quaternion rotation)
        {
            switch (_velocitySource)
            {
                case VelocitySourceType.Rigidbody:
                    Rigidbody.MoveRotation(rotation);
                    break;
                case VelocitySourceType.Rigidbody2D:
                    Rigidbody2D.MoveRotation(rotation);
                    break;
                case VelocitySourceType.Transform:
                default:
                    break;
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
            _transformVelocity = Vector3.zero;
            _lastPos = null;

            if (Target == null)
            {
                Target = this.transform;
            }

            Active = true;
        }
    }
}