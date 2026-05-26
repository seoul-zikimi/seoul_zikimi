using UnityEngine;

namespace Player
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        private Rigidbody m_Rb;
        private PlayerConfigSO m_Config;

        public void Init(PlayerConfigSO config)
        {
            m_Config = config; m_Rb = GetComponent<Rigidbody>();
        }

        // 인게임에서 실제 이동 입력 처리 (FixedUpdate에서 호출)
        public void Move(Vector2 input)
        {
            Vector3 dir = new Vector3(input.x, 0f, input.y);
            m_Rb.linearVelocity = dir.normalized * m_Config.MoveSpeed;
        }
    }
}