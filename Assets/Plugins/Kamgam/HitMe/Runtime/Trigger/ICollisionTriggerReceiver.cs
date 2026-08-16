using UnityEngine;

namespace Kamgam.HitMe
{
    public interface ICollisionTriggerReceiver
    {
        void TriggerCollision2D(CollisionTrigger trigger, Collider2D other);
        void TriggerCollision3D(CollisionTrigger trigger, Collider other);
    }
}