using UnityEngine;

namespace Player
{
    /// <summary>
    /// 추후 모바일 버튼/조이스틱의 UnityEvent에 바로 연결할 얇은 MonoBehaviour 어댑터.
    /// 현재는 어떤 오브젝트에도 자동 부착하지 않으며 UI도 만들지 않는다.
    /// TODO: 모바일 Canvas를 만들 때 입력 루트 한 곳에만 이 컴포넌트를 붙인다.
    /// 같은 버튼에 Bridge와 별도 EventTrigger가 동시에 입력을 보내면 홀드 입력이 중복될 수 있다.
    /// </summary>
    public sealed class MobileGameplayInputBridge : MonoBehaviour
    {
        public void SetMove(Vector2 value) => MobileGameplayInput.SetMove(value);
        public void StopMove() => MobileGameplayInput.SetMove(Vector2.zero);
        public void SetSprint(bool pressed) => MobileGameplayInput.SetSprint(pressed);
        public void Jump() => MobileGameplayInput.PressJump();
        public void Scaffold() => MobileGameplayInput.PressScaffold();
        public void RotateHeldObject() => MobileGameplayInput.PressRotateHeld();
        public void ToggleOrder() => MobileGameplayInput.ToggleOrder();
        public void TriggerEmote(int index) => MobileGameplayInput.TriggerEmote(index);
        public void ProcessDown() => MobileGameplayInput.SetProcessPressed(true);
        public void ProcessUp() => MobileGameplayInput.SetProcessPressed(false);
        public void RevertDown() => MobileGameplayInput.SetRevertPressed(true);
        public void RevertUp() => MobileGameplayInput.SetRevertPressed(false);
        public void ThrowDown() => MobileGameplayInput.SetThrowPressed(true);
        public void ThrowUp() => MobileGameplayInput.SetThrowPressed(false);
        public void TapWorld(Vector2 screenPosition) => MobileGameplayInput.TapWorld(screenPosition);
        public void DragCamera(Vector2 screenDelta) => MobileGameplayInput.AddCameraDrag(screenDelta);
        public void PinchZoom(float screenDelta) => MobileGameplayInput.AddPinchZoom(screenDelta);

        private void OnDisable() => MobileGameplayInput.ReleaseAll();
    }
}
