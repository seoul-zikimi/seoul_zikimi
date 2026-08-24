using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// UI가 없어도 기기에서 기본 검증이 가능한 터치 어댑터.
    /// 좌하단 터치=보이지 않는 이동 스틱, 나머지 한 손가락=카메라/탭 상호작용,
    /// 두 손가락=핀치 줌. 향후 실제 UI가 포인터를 차지하면 그 터치는 자동 제외된다.
    ///
    /// TODO(실제 모바일 UI 제작 시):
    /// - 현재 보이지 않는 좌하단 스틱 영역은 임시 기기 테스트용이다. 실제 조이스틱이 SetMove를 호출하면
    ///   이 자동 영역을 끌 수 있도록 serialized/runtime 옵션을 추가한다(이중 이동 입력 방지).
    /// - Screen.safeArea를 기준으로 조이스틱/버튼 영역을 배치해 노치와 홈 인디케이터를 피한다.
    /// - 주문 휴대폰이 전체화면으로 열린 동안에는 월드 탭/카메라 제스처를 잠그는 입력 차단 플래그를 연결한다.
    /// - 실제 iOS/Android 기기에서 touchId와 EventSystem UI 차단, 2손가락 핀치 감도를 최종 튜닝한다.
    /// </summary>
    public sealed class MobileTouchInputDriver : MonoBehaviour
    {
        private const float kJoystickRadius = 110f;
        private const float kTapDistance = 24f;
        private int m_MoveTouch = -1;
        private int m_LookTouch = -1;
        private Vector2 m_MoveOrigin;
        private Vector2 m_LookOrigin;
        private float m_LastPinch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<MobileTouchInputDriver>() != null) return;
            var go = new GameObject("~MobileTouchInputDriver");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileTouchInputDriver>();
        }

        private void Update()
        {
            var screen = Touchscreen.current;
            if (screen == null || PlayerInputHandler.Local == null) return;

            int activeLookCount = 0;
            Vector2 pinchA = default, pinchB = default;
            bool moveAlive = false, lookAlive = false;

            foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed && !touch.press.wasReleasedThisFrame) continue;
                int id = touch.touchId.ReadValue();
                Vector2 pos = touch.position.ReadValue();

                if (touch.press.wasPressedThisFrame && !PointerOverUi(id))
                {
                    if (m_MoveTouch < 0 && pos.x < Screen.width * 0.45f && pos.y < Screen.height * 0.52f)
                    {
                        m_MoveTouch = id;
                        m_MoveOrigin = pos;
                    }
                    else if (m_LookTouch < 0)
                    {
                        m_LookTouch = id;
                        m_LookOrigin = pos;
                    }
                }

                if (id == m_MoveTouch)
                {
                    if (touch.press.isPressed)
                    {
                        moveAlive = true;
                        MobileGameplayInput.SetMove(Vector2.ClampMagnitude((pos - m_MoveOrigin) / kJoystickRadius, 1f));
                    }
                    continue;
                }

                if (touch.press.isPressed && !PointerOverUi(id))
                {
                    if (activeLookCount++ == 0) pinchA = pos; else if (activeLookCount == 2) pinchB = pos;
                }

                if (id == m_LookTouch)
                {
                    if (touch.press.isPressed) lookAlive = true;
                    if (touch.press.wasReleasedThisFrame)
                    {
                        if ((pos - m_LookOrigin).sqrMagnitude <= kTapDistance * kTapDistance)
                            MobileGameplayInput.TapWorld(pos);
                        m_LookTouch = -1;
                    }
                }
            }

            if (!moveAlive && m_MoveTouch >= 0)
            {
                m_MoveTouch = -1;
                MobileGameplayInput.SetMove(Vector2.zero);
            }

            if (activeLookCount >= 2)
            {
                float pinch = Vector2.Distance(pinchA, pinchB);
                if (m_LastPinch > 0f) MobileGameplayInput.AddPinchZoom(pinch - m_LastPinch);
                m_LastPinch = pinch;
            }
            else
            {
                m_LastPinch = 0f;
                if (lookAlive && m_LookTouch >= 0)
                {
                    foreach (var touch in screen.touches)
                        if (touch.touchId.ReadValue() == m_LookTouch && touch.press.isPressed)
                        {
                            MobileGameplayInput.SetPointerPosition(touch.position.ReadValue());
                            MobileGameplayInput.AddCameraDrag(touch.delta.ReadValue());
                            break;
                        }
                }
            }
        }

        private static bool PointerOverUi(int touchId)
            => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touchId);

        private void OnDisable()
        {
            m_MoveTouch = m_LookTouch = -1;
            m_LastPinch = 0f;
            MobileGameplayInput.ReleaseAll();
        }
    }
}
