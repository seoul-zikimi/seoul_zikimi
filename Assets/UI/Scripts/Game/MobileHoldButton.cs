using Player;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>공정·공정취소·던지기처럼 누르는 동안 유지되는 모바일 액션 버튼.</summary>
public sealed class MobileHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum ActionType { Process, Revert, Throw }
    [SerializeField] private ActionType action;

    public void Configure(ActionType value) => action = value;
    public void OnPointerDown(PointerEventData eventData) => SetPressed(true);
    public void OnPointerUp(PointerEventData eventData) => SetPressed(false);
    public void OnPointerExit(PointerEventData eventData) => SetPressed(false);
    private void OnDisable() => SetPressed(false);

    private void SetPressed(bool pressed)
    {
        switch (action)
        {
            case ActionType.Process: MobileGameplayInput.SetProcessPressed(pressed); break;
            case ActionType.Revert:  MobileGameplayInput.SetRevertPressed(pressed); break;
            case ActionType.Throw:   MobileGameplayInput.SetThrowPressed(pressed); break;
        }
    }
}
