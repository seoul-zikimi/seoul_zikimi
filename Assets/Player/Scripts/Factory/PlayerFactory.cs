using UnityEngine;

namespace Player
{
    public abstract class PlayerFactory : MonoBehaviour
    {
        public abstract IPlayerProduct GetProduct(Vector3 position);
    }
}