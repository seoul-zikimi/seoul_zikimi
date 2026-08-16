using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Makes the object look at Target.
    /// </summary>
    [AddComponentMenu("Hit Me/Align/Look At")]
    public class LookAt : MonoBehaviour
    {
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

        public void Start()
        {
            if (DetectForwardAxisAtStart)
                DetectForwardAxis();
        }

        public void Reset()
        {
            Active = true;
            DetectForwardAxis();
        }

        /// <summary>
        /// If the object has a Rigidbody2D or a Collider2D then set the Y axis as the forward axis.
        /// </summary>
        public void DetectForwardAxis()
        {
            ForwardAxisY = TryGetComponent<Rigidbody2D>(out _) || TryGetComponent<Collider2D>(out _);
        }

        public void Update()
        {
            if (!Active || Target == null)
                return;
            
            Result.SetLookRotation(Target.position - transform.position, WorldUpAxis);
            HasResult = true;

            // In 2D mode, rotate around the up axis by 90 to make the y-axis the new forward axis.
            if (ForwardAxisY)
            {
                Result = Quaternion.AngleAxis(-90f, Result * Vector3.up) * Result;
            }

            if (Apply)
            {
                transform.rotation = Result;
            }
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
    }
}
