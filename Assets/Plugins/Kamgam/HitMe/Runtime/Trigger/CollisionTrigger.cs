using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Usage:<br />
    /// 1) Add this to a CHILD of your projectile prefab.<br />
    /// 2) Add a collider to this object and set isTrigger to true.
    /// 3) Resize the trigger to be slightly bigger than the collider
    ///    of your projectile (it should be hit first).
    /// 4) Done. Whenever another object hits this trigger it will cause a collision
    ///    in the animated projectile.
    /// </summary>
    [AddComponentMenu("Hit Me/Collision Trigger")]
    public class CollisionTrigger : MonoBehaviour
    {
        protected ICollisionTriggerReceiver _receiver;
        public ICollisionTriggerReceiver Receiver
        {
            get
            {
                if (_receiver == null)
                {
                    _receiver = this.GetComponentInParent<ICollisionTriggerReceiver>(includeInactive: true);
                }
                return _receiver;
            }
        }

        public void OnTriggerEnter(Collider other)
        {
            Receiver?.TriggerCollision3D(this, other);
        }

        public void OnTriggerEnter2D(Collider2D other)
        {
            Receiver?.TriggerCollision2D(this, other);
        }

#if UNITY_EDITOR
        public void Reset()
        {
            // Warn about wrong use.
            var projectile = GetComponent<IProjectile>();
            var rigidbody = GetComponent<Rigidbody>();
            var rigidbody2D = GetComponent<Rigidbody2D>();
            if (projectile != null || rigidbody != null || rigidbody2D != null)
            {
                Debug.LogError(
                    "Hit Me: The CollisionTrigger needs to go on a CHILD of the projectile, NOT the projectile itself. Usage:\n" +
                    " 1) Add this to a CHILD of your projectile prefab (create one if needed).\n" +
                    " 2) Add a collider to this object and set isTrigger to true.\n" +
                    " 3) Resize the trigger to be slightly bigger than the collider" +
                    "    of your projectile (it should be hit first).\n" +
                    " 4) Done. Whenever another object is hit this trigger it will cause a collision" +
                    "    in the animated projectile.\n"
                    );
            }

            // Convenience for the user (add a reasonable collider).
            
            // 3D
            var rigidbodyInParent = GetComponentInParent<Rigidbody>(includeInactive: true);
            if (rigidbodyInParent != null)
            {
                var collider = gameObject.GetComponent<SphereCollider>();
                if (collider == null)
                    collider = gameObject.AddComponent<SphereCollider>();
                collider.isTrigger = true;

                if (transform.parent != null)
                {
                    var sphereColliderInParent = transform.parent.GetComponentInParent<SphereCollider>();
                    if (sphereColliderInParent != null)
                    {
                        collider.center = sphereColliderInParent.center;
                        collider.radius = sphereColliderInParent.radius + Mathf.Min(sphereColliderInParent.radius * 2, 2f);
                    }
                }
            }
            
            // 2D
            var rigidbody2DInParent = GetComponentInParent<Rigidbody2D>(includeInactive: true);
            if (rigidbody2DInParent != null)
            {
                var collider2D = gameObject.GetComponent<CircleCollider2D>();
                if (collider2D == null)
                    collider2D = gameObject.AddComponent<CircleCollider2D>();
                collider2D.isTrigger = true;

                if (transform.parent != null)
                {
                    var circleCollider2DInParent = transform.parent.GetComponentInParent<CircleCollider2D>();
                    if (circleCollider2DInParent != null)
                    {
                        collider2D.offset = circleCollider2DInParent.offset;
                        collider2D.radius = circleCollider2DInParent.radius + Mathf.Min(circleCollider2DInParent.radius * 2, 2f);
                    }
                }
            }
        }
#endif
    }
}