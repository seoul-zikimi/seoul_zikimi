using UnityEngine;

namespace Kamgam.HitMe
{
    public class TransformPhysics2D : MonoBehaviour
    {
        public static TransformPhysics2D GetOrCreate(Transform target)
        {
            return GetOrCreate(target.gameObject);
        }

        public static TransformPhysics2D GetOrCreate(GameObject target)
        {
            if (target == null)
                return null;

            var obj = target.GetComponent<TransformPhysics2D>();
            if (obj == null)
                obj = target.AddComponent<TransformPhysics2D>();

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

            var obj = target.GetComponent<TransformPhysics2D>();
            if (obj != null)
                GameObject.Destroy(obj);
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

        protected bool _setPositionExecutedInFixedUpdate;
        protected bool _setPosition;
        protected Vector2 _position;

        protected bool _setVelocityExecutedInFixedUpdate;
        protected bool _setVelocity;
        protected Vector2 _velocity;

        public void SetPosition(Vector2 position)
        {
            _setPosition = true;
            _setPositionExecutedInFixedUpdate = false;
            _position = position;
        }

        public Vector2 GetPosition()
        {
            return _position;
        }

        public void SetVelocity(Vector2 velocity)
        {
            _setVelocity = true;
            _setVelocityExecutedInFixedUpdate = false;
            _velocity = velocity;
        }

        public Vector2 GetVelocity()
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
            // If the velocity would have only been set in Update then we could have called it
            // from there and be done. But we also need to take into account that the user may
            // call it from OnCollision and that might be in the middle of the multiple fixed
            // updates (within the current frame).
            // See: https://docs.unity3d.com/Manual/ExecutionOrder.html

            if (_setPosition)
            {
                _setPositionExecutedInFixedUpdate = true;
                Rigidbody2D.MovePosition(_position);
            }

            if (_setVelocity)
            {
                _setVelocityExecutedInFixedUpdate = true;
#if UNITY_6000_0_OR_NEWER
                Rigidbody2D.linearVelocity = _velocity;
#else
                Rigidbody2D.velocity = _velocity;
#endif
            }
        }


        /// <summary>
        /// Called from MonoBehaviour.Update().
        /// </summary>
        protected void Update()
        {
            // Stop updating the position
            if (_setPositionExecutedInFixedUpdate)
                _setPosition = false;

            // Stop updating the velocity
            if (_setVelocityExecutedInFixedUpdate)
                _setVelocity = false;
        }
    }
}