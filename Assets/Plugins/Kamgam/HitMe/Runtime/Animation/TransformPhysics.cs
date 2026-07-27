using UnityEngine;

namespace Kamgam.HitMe
{
    public class TransformPhysics : MonoBehaviour
    {
        public static TransformPhysics GetOrCreate(Transform target)
        {
            return GetOrCreate(target.gameObject);
        }

        public static TransformPhysics GetOrCreate(GameObject target)
        {
            if (target == null)
                return null;

            var obj = target.GetComponent<TransformPhysics>();
            if (obj == null)
                obj = target.AddComponent<TransformPhysics>();

            return obj;
        }

        public static void DestroyIfExistsOn(Transform target)
        {
            if (target == null)
                return;

            Destroy(target.gameObject);
        }

        public static void DestroyIfExistsOn(GameObject target)
        {
            if (target == null)
                return;

            var obj = target.GetComponent<TransformPhysics>();
            if (obj != null)
                GameObject.Destroy(obj);
        }

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

        protected bool _setPositionExecutedInFixedUpdate;
        protected bool _setPosition;
        protected Vector3 _position;

        protected bool _setVelocityExecutedInFixedUpdate;
        protected bool _setVelocity;
        protected Vector3 _velocity;

        public void SetPosition(Vector3 position)
        {
            _setPosition = true;
            _setPositionExecutedInFixedUpdate = false;
            _position = position;
        }

        public Vector3 GetPosition()
        {
            return _position;
        }

        public void SetVelocity(Vector3 velocity)
        {
            _setVelocity = true;
            _setVelocityExecutedInFixedUpdate = false;
            _velocity = velocity;
        }

        public Vector3 GetVelocity()
        {
            return _velocity;
        }

        /// <summary>
        /// Called from MonoBehaviour.FixedUpdate() - but only if needed.
        /// </summary>
        public void FixedUpdate()
        {
            // We set the position and velocity in every fixed update until the next frame was called.
            // We have to set the position every fixed update or else the object would start to simulate (fall if gravity is enabled).
            //
            // If the values would have only been set in Update then we could have called it
            // from there and be done. But we also need to take into account that the user may
            // call it from OnCollision and that might be in the middle of the multiple fixed
            // updates (within the current frame).
            // See: https://docs.unity3d.com/Manual/ExecutionOrder.html

            if (_setPosition)
            {
                _setPositionExecutedInFixedUpdate = true;
                Rigidbody.MovePosition(_position);
            }

            if (_setVelocity)
            {
                _setVelocityExecutedInFixedUpdate = true;
                Rigidbody.AddForce(_velocity - Rigidbody.GetVelocity(), ForceMode.VelocityChange);
            }
        }


        /// <summary>
        /// Called from MonoBehaviour.Update().
        /// </summary>
        protected void Update()
        {
            // Stop updating the position only if it has been set at least once in FixesUpdate.
            if (_setPositionExecutedInFixedUpdate)
                _setPosition = false;

            // Stop updating the velocity only if it has been set at least once in FixesUpdate.
            if (_setVelocityExecutedInFixedUpdate)
                _setVelocity = false;
        }
    }
}