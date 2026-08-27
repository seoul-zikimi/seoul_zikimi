using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        public static PlayerInputHandler Local { get; private set; }

        private PlayerControls m_Controls;
        private InputAction m_Move, m_Sprint, m_Jump, m_Interact, m_Process, m_Revert;
        private InputAction m_RotateHeld, m_Throw, m_ToggleOrder, m_CameraRotate, m_CameraZoom;
        private InputAction m_EmoteWheel;
        private readonly InputAction[] m_Emotes = new InputAction[10];
        private bool m_JumpQueued;
        private bool m_ScaffoldQueued;
        private float m_LastSpaceTime = -10f;
        private const float kDoubleTapWindow = 0.3f;

        private Vector2 m_MobileMove, m_MobileLook, m_MobilePointer;
        private float m_MobileZoom;
        private bool m_MobileSprint, m_MobileProcess, m_MobileRevert, m_MobileThrow, m_HasMobilePointer;
        private int m_InteractFrame = -1, m_RotateFrame = -1, m_OrderFrame = -1;
        private int m_ProcessPressedFrame = -1, m_ProcessReleasedFrame = -1;
        private int m_RevertPressedFrame = -1, m_RevertReleasedFrame = -1;
        private int m_ThrowPressedFrame = -1, m_ThrowReleasedFrame = -1;

        public Vector2 MoveInput => GameplayInputBlocker.Blocked ? Vector2.zero
            : Vector2.ClampMagnitude((m_Move?.ReadValue<Vector2>() ?? Vector2.zero) + m_MobileMove, 1f);
        public bool IsSprinting => !GameplayInputBlocker.Blocked && ((m_Sprint?.IsPressed() ?? false) || m_MobileSprint);
        public Vector2 PointerPosition => m_HasMobilePointer
            ? m_MobilePointer
            : Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public bool InteractPressedThisFrame => !GameplayInputBlocker.Blocked && ((m_Interact?.WasPressedThisFrame() ?? false) || m_InteractFrame == Time.frameCount);
        public bool RotateHeldPressedThisFrame => !GameplayInputBlocker.Blocked && ((m_RotateHeld?.WasPressedThisFrame() ?? false) || m_RotateFrame == Time.frameCount);
        public bool ProcessPressedThisFrame => !GameplayInputBlocker.Blocked && ((m_Process?.WasPressedThisFrame() ?? false) || m_ProcessPressedFrame == Time.frameCount);
        public bool ProcessReleasedThisFrame => !GameplayInputBlocker.Blocked && ((m_Process?.WasReleasedThisFrame() ?? false) || m_ProcessReleasedFrame == Time.frameCount);
        public bool ProcessIsPressed => !GameplayInputBlocker.Blocked && ((m_Process?.IsPressed() ?? false) || m_MobileProcess);
        public bool RevertPressedThisFrame => !GameplayInputBlocker.Blocked && ((m_Revert?.WasPressedThisFrame() ?? false) || m_RevertPressedFrame == Time.frameCount);
        public bool RevertReleasedThisFrame => !GameplayInputBlocker.Blocked && ((m_Revert?.WasReleasedThisFrame() ?? false) || m_RevertReleasedFrame == Time.frameCount);
        public bool RevertIsPressed => !GameplayInputBlocker.Blocked && ((m_Revert?.IsPressed() ?? false) || m_MobileRevert);
        public bool ThrowPressedThisFrame => !GameplayInputBlocker.Blocked && ((m_Throw?.WasPressedThisFrame() ?? false) || m_ThrowPressedFrame == Time.frameCount);
        public bool ThrowReleasedThisFrame => !GameplayInputBlocker.Blocked && ((m_Throw?.WasReleasedThisFrame() ?? false) || m_ThrowReleasedFrame == Time.frameCount);
        public bool ThrowIsPressed => !GameplayInputBlocker.Blocked && ((m_Throw?.IsPressed() ?? false) || m_MobileThrow);
        public bool ToolActionAvailable => GetComponent<PlayerCarry>()?.IsHoldingTool == true;
        public bool ProcessActionAvailable => GetComponent<PlayerCarry>()?.CanProcessTarget == true;
        public bool RevertActionAvailable => GetComponent<PlayerCarry>()?.CanRevertTarget == true;
        public bool ThrowActionAvailable => GetComponent<PlayerCarry>()?.IsHolding == true;
        public InputActionAsset ControlsAsset => m_Controls?.asset;
        // 튜토리얼 진척도 등 읽기 전용 호환 프로퍼티. 실제 카메라는 ConsumeCameraRotate로 모바일 delta를 1회 소비한다.
        public Vector2 CameraRotate => GameplayInputBlocker.Blocked ? Vector2.zero
            : (m_CameraRotate?.ReadValue<Vector2>() ?? Vector2.zero) + m_MobileLook;

        /// <summary>이번에 점프 눌림이 있었으면 true 반환 후 소비(FixedUpdate에서 1회 처리).</summary>
        public bool ConsumeJump()
        {
            if (!m_JumpQueued) return false;
            m_JumpQueued = false;
            return true;
        }

        /// <summary>Space 더블탭(빠른 두 번째 탭)이 있었으면 true 반환 후 소비 — 비계 설치용.</summary>
        public bool ConsumeScaffold()
        {
            if (!m_ScaffoldQueued) return false;
            m_ScaffoldQueued = false;
            return true;
        }

        // 기본 Space/패드 South/모바일 Jump 모두 같은 경로. 첫 탭=점프, 빠른 두 번째 탭=비계.
        private void Update()
        {
            if (GameplayInputBlocker.Blocked) return;
            if (m_Jump?.WasPressedThisFrame() == true) QueueJumpTap();
        }

        private void QueueJumpTap()
        {
            if (Time.time - m_LastSpaceTime <= kDoubleTapWindow)
            {
                m_ScaffoldQueued = true;
                m_LastSpaceTime = -10f;   // 리셋: 연속 탭이 또 더블로 오인되지 않게
            }
            else
            {
                m_JumpQueued = true;
                m_LastSpaceTime = Time.time;
            }
        }

        public Vector2 ConsumeCameraRotate()
        {
            if (GameplayInputBlocker.Blocked) { m_MobileLook = Vector2.zero; return Vector2.zero; }
            Vector2 result = (m_CameraRotate?.ReadValue<Vector2>() ?? Vector2.zero) + m_MobileLook;
            m_MobileLook = Vector2.zero;
            return result;
        }

        public float ConsumeCameraZoom()
        {
            if (GameplayInputBlocker.Blocked) { m_MobileZoom = 0f; return 0f; }
            float result = (m_CameraZoom?.ReadValue<float>() ?? 0f) + m_MobileZoom;
            m_MobileZoom = 0f;
            return result;
        }

        public bool ConsumeToggleOrder() => !GameplayInputBlocker.Blocked && ((m_ToggleOrder?.WasPressedThisFrame() ?? false) || m_OrderFrame == Time.frameCount);
        // 키 설정 팝업 등 입력 차단 중엔 이모트도 막는다(리바인딩 대기 중 F1~F10·T가 그대로 발동하던 문제).
        public bool EmoteWheelPressedThisFrame => !GameplayInputBlocker.Blocked && (m_EmoteWheel?.WasPressedThisFrame() ?? false);
        public bool EmoteWheelReleasedThisFrame => m_EmoteWheel?.WasReleasedThisFrame() ?? false;

        public int ConsumeEmoteIndex()
        {
            if (GameplayInputBlocker.Blocked) return -1;
            for (int i = 0; i < m_Emotes.Length; i++)
                if (m_Emotes[i]?.WasPressedThisFrame() == true) return i;
            return -1;
        }

        // 모바일 UI/터치 어댑터 입력 주입. UI 오브젝트 생성 없이 호출 가능한 순수 기능 포트.
        public void SetMobileMove(Vector2 value) => m_MobileMove = Vector2.ClampMagnitude(value, 1f);
        public void SetMobileSprint(bool value) => m_MobileSprint = value;
        public void SetMobilePointer(Vector2 value) { m_MobilePointer = value; m_HasMobilePointer = true; }
        public void AddMobileCameraDrag(Vector2 delta) => m_MobileLook += delta;
        public void AddMobileZoom(float delta) => m_MobileZoom += delta;
        public void PressMobileInteract() => m_InteractFrame = Time.frameCount;
        public void PressMobileRotateHeld() => m_RotateFrame = Time.frameCount;
        public void PressMobileToggleOrder() => m_OrderFrame = Time.frameCount;
        public void PressMobileJump() { if (!GameplayInputBlocker.Blocked) QueueJumpTap(); }
        public void PressMobileScaffold() { if (!GameplayInputBlocker.Blocked) m_ScaffoldQueued = true; }
        public void SetMobileProcess(bool value) => SetMobileButton(ref m_MobileProcess, value, ref m_ProcessPressedFrame, ref m_ProcessReleasedFrame);
        public void SetMobileRevert(bool value) => SetMobileButton(ref m_MobileRevert, value, ref m_RevertPressedFrame, ref m_RevertReleasedFrame);
        public void SetMobileThrow(bool value) => SetMobileButton(ref m_MobileThrow, value, ref m_ThrowPressedFrame, ref m_ThrowReleasedFrame);

        private static void SetMobileButton(ref bool state, bool value, ref int pressedFrame, ref int releasedFrame)
        {
            if (state == value) return;
            state = value;
            if (value) pressedFrame = Time.frameCount; else releasedFrame = Time.frameCount;
        }

        public void ReleaseMobileInputs()
        {
            m_MobileMove = m_MobileLook = Vector2.zero;
            m_MobileZoom = 0f;
            m_MobileSprint = false;
            SetMobileProcess(false);
            SetMobileRevert(false);
            SetMobileThrow(false);
            m_HasMobilePointer = false;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            Local = this;
            m_Controls = GameplayInputBindings.CreateControls();
            CacheActions();
            GameplayInputBindings.OverridesChanged += ReloadBindingOverrides;
            m_Controls.Enable();
        }

        private void CacheActions()
        {
            m_Move = m_Controls.asset.FindAction(GameplayInputBindings.Move, true);
            m_Sprint = m_Controls.asset.FindAction(GameplayInputBindings.Sprint, true);
            m_Jump = m_Controls.asset.FindAction(GameplayInputBindings.Jump, true);
            m_Interact = m_Controls.asset.FindAction(GameplayInputBindings.Interact, true);
            m_Process = m_Controls.asset.FindAction(GameplayInputBindings.Process, true);
            m_Revert = m_Controls.asset.FindAction(GameplayInputBindings.Revert, true);
            m_RotateHeld = m_Controls.asset.FindAction(GameplayInputBindings.RotateHeld, true);
            m_Throw = m_Controls.asset.FindAction(GameplayInputBindings.Throw, true);
            m_ToggleOrder = m_Controls.asset.FindAction(GameplayInputBindings.ToggleOrder, true);
            m_EmoteWheel = m_Controls.asset.FindAction(GameplayInputBindings.EmoteWheel, true);
            for (int i = 0; i < m_Emotes.Length; i++)
                m_Emotes[i] = m_Controls.asset.FindAction($"Player/Emote{i + 1}", true);
            m_CameraRotate = m_Controls.asset.FindAction(GameplayInputBindings.CameraRotate, true);
            m_CameraZoom = m_Controls.asset.FindAction(GameplayInputBindings.CameraZoom, true);
        }

        private void ReloadBindingOverrides()
        {
            if (m_Controls == null) return;
            m_Controls.Disable();
            GameplayInputBindings.ApplySavedOverrides(m_Controls.asset);
            m_Controls.Enable();
        }

        public override void OnNetworkDespawn()
        {
            GameplayInputBindings.OverridesChanged -= ReloadBindingOverrides;
            m_Controls?.Disable();
            m_Controls?.Dispose();
            m_Controls=null;
            if (Local == this) Local = null;
            ReleaseMobileInputs();
        }
    }
}
