using UnityEngine;
using Unity.Netcode;

namespace Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        private PlayerControls m_Controls;
        
        public Vector2 MoveInput { get; private set; }
        public Vector2 CameraRotate { get;private set; }
        public float CameraZoom { get; private set; }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }
            
            m_Controls = new PlayerControls();
            m_Controls.Player.Move.performed += ctx => MoveInput = ctx.ReadValue<Vector2>();
            m_Controls.Player.Move.canceled += ctx => MoveInput = Vector2.zero;
            m_Controls.Camera.Rotate.performed += ctx => CameraRotate = ctx.ReadValue<Vector2>();
            m_Controls.Camera.Rotate.canceled += ctx => CameraRotate = Vector2.zero;
            m_Controls.Camera.Zoom.performed += ctx => CameraZoom = ctx.ReadValue<float>();
            m_Controls.Camera.Zoom.canceled += ctx => CameraZoom = 0f;
            m_Controls.Enable();
        }

        public override void OnNetworkDespawn()
        {
            m_Controls?.Disable();
            m_Controls=null;
        }
    }
}
