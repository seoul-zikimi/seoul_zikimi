using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        private PlayerControls m_Controls;
        private bool m_JumpQueued;
        
        public Vector2 MoveInput    { get; private set; }
        public Vector2 CameraRotate { get; private set; }
        public float   CameraZoom   { get; private set; }
        public bool    IsSprinting  { get; private set; }

        /// <summary>이번에 점프 눌림이 있었으면 true 반환 후 소비(FixedUpdate에서 1회 처리).</summary>
        public bool ConsumeJump()
        {
            if (!m_JumpQueued) return false;
            m_JumpQueued = false;
            return true;
        }

        // Space는 InputActions에 없어 직접 읽음(이 컴포넌트는 owner에서만 enabled).
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) m_JumpQueued = true;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            m_Controls = new PlayerControls();
            m_Controls.Player.Move.performed    += ctx => MoveInput = ctx.ReadValue<Vector2>();
            m_Controls.Player.Move.canceled     += ctx => MoveInput = Vector2.zero;
            m_Controls.Player.Sprint.performed  += ctx => IsSprinting = true;
            m_Controls.Player.Sprint.canceled   += ctx => IsSprinting = false;
            m_Controls.Camera.Rotate.performed  += ctx => CameraRotate = ctx.ReadValue<Vector2>();
            m_Controls.Camera.Rotate.canceled   += ctx => CameraRotate = Vector2.zero;
            m_Controls.Camera.Zoom.performed    += ctx => CameraZoom = ctx.ReadValue<float>();
            m_Controls.Camera.Zoom.canceled     += ctx => CameraZoom = 0f;
            m_Controls.Enable();
        }

        public override void OnNetworkDespawn()
        {
            m_Controls?.Disable();
            m_Controls=null;
        }
    }
}
