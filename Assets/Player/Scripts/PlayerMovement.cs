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

        // 카메라 forward 기준 이동 (FixedUpdate에서 호출)
        public void Move(Vector2 input, Transform cameraArm, bool isSprinting = false)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraArm.forward, Vector3.up).normalized;
            Vector3 right   = Vector3.ProjectOnPlane(cameraArm.right,   Vector3.up).normalized;
            Vector3 dir     = forward * input.y + right * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            m_Rb.linearVelocity = dir * (isSprinting ? m_Config.SprintSpeed : m_Config.MoveSpeed);
        }
    }
}