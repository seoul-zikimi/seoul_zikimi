using UnityEngine;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody), typeof(PlayerMovement), typeof(PlayerBounce))]
    [UnityEngine.RequireComponent(typeof(PlayerDustTrail))]
    public class PlayerUnit : MonoBehaviour, IPlayerProduct
    {
        private PlayerMovement m_Movement;
        private PlayerBounce   m_Bounce;
        private PlayerDustTrail m_DustTrail;

        public string ProductName { get; set; }

        public void Initialize(PlayerConfigSO config)
        {
            ProductName = "Player_" + GetInstanceID();
            gameObject.name = ProductName;
            gameObject.tag = "Player";

            m_Movement = GetComponent<PlayerMovement>();
            m_Movement.Init(config);
            m_Bounce = GetComponent<PlayerBounce>();
            m_Bounce.Init(config);

            Rigidbody rb = GetComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezePositionY
                             | RigidbodyConstraints.FreezeRotationX
                             | RigidbodyConstraints.FreezeRotationY
                             | RigidbodyConstraints.FreezeRotationZ;

            m_DustTrail = GetComponent<PlayerDustTrail>();
            m_DustTrail.Init(config, rb);
        }
    }
}