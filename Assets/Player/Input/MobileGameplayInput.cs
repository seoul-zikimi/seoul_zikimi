using UnityEngine;

namespace Player
{
    /// <summary>
    /// 모바일 UI가 나중에 연결할 공개 입력 포트. 이 클래스는 UI/프리팹을 생성하지 않는다.
    /// EventTrigger, 새 Input System On-Screen Control, 커스텀 UI 어느 쪽에서도 아래 메서드를 호출할 수 있다.
    ///
    /// TODO(기획 이미지 UI 연결):
    /// - 좌하단 조이스틱: 드래그 중 SetMove, 포인터 업/취소 시 SetMove(Vector2.zero)
    /// - 우측 점프: PressJump
    /// - 우측 던지기: 포인터 다운 SetThrowPressed(true), 업/취소 false
    /// - 우측 공정: ProcessActionAvailable일 때만 표시하고 다운/업을 SetProcessPressed에 전달
    /// - 공정취소: ProcessCancelAvailable일 때만 표시하고 SetRevertPressed에 다운/업 전달
    /// - 하단 휴대폰: ToggleOrder
    /// - 우상단 감정표현 드롭다운: 선택 index를 TriggerEmote(index)에 전달
    /// - 화면 월드 탭: TapWorld(screenPosition), 카메라 드래그/핀치: AddCameraDrag/AddPinchZoom
    /// </summary>
    public static class MobileGameplayInput
    {
        private static PlayerInputHandler Input => PlayerInputHandler.Local;

        /// <summary>실제 조이스틱 프리팹이 표시되는 동안 보이지 않는 임시 이동 영역을 끈다.</summary>
        public static bool HasVisibleMoveControl { get; set; }

        /// <summary>주문 폰 등 전체화면 UI가 열린 동안 월드 터치(이동·탭·카메라)를 잠근다 — MobileControlsHUD가 세팅.</summary>
        public static bool WorldInputLocked { get; set; }

        public static bool Available => Input != null;
        public static bool ToolActionAvailable => Input != null && Input.ToolActionAvailable;
        public static bool ProcessActionAvailable => Input != null && Input.ProcessActionAvailable;
        public static bool ProcessCancelAvailable => Input != null && Input.RevertActionAvailable;
        public static bool ThrowAvailable => Input != null && Input.ThrowActionAvailable;

        public static void SetMove(Vector2 normalized) => Input?.SetMobileMove(normalized);
        public static void SetSprint(bool pressed) => Input?.SetMobileSprint(pressed);
        public static void SetPointerPosition(Vector2 screenPosition) => Input?.SetMobilePointer(screenPosition);
        public static void AddCameraDrag(Vector2 screenDelta) => Input?.AddMobileCameraDrag(screenDelta);
        public static void AddPinchZoom(float screenDelta) => Input?.AddMobileZoom(screenDelta * 4f);

        public static void TapWorld(Vector2 screenPosition)
        {
            Input?.SetMobilePointer(screenPosition);
            Input?.PressMobileInteract();
        }

        public static void PressJump() => Input?.PressMobileJump();
        public static void PressScaffold() => Input?.PressMobileScaffold();
        public static void PressRotateHeld() => Input?.PressMobileRotateHeld();
        public static void ToggleOrder() => Input?.PressMobileToggleOrder();
        public static void TriggerEmote(int index) => PlayerEmote.Local?.TriggerEmote(index);
        public static void SetProcessPressed(bool pressed) => Input?.SetMobileProcess(pressed);
        public static void SetRevertPressed(bool pressed) => Input?.SetMobileRevert(pressed);
        public static void SetThrowPressed(bool pressed) => Input?.SetMobileThrow(pressed);
        public static void ReleaseAll() => Input?.ReleaseMobileInputs();
    }
}
