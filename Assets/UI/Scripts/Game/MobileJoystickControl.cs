using Player;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>프리팹에 배치된 좌하단 이동 조이스틱. UI 로컬 좌표를 -1..1 이동값으로 변환한다.</summary>
public sealed class MobileJoystickControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform knob;
    [SerializeField] private float radius = 78f;

    public void Configure(RectTransform knobTransform, float movementRadius)
    {
        knob = knobTransform;
        radius = movementRadius;
    }

    public void OnPointerDown(PointerEventData eventData) => Apply(eventData);
    public void OnDrag(PointerEventData eventData) => Apply(eventData);

    public void OnPointerUp(PointerEventData eventData)
    {
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        MobileGameplayInput.SetMove(Vector2.zero);
    }

    private void Apply(PointerEventData eventData)
    {
        var rect = transform as RectTransform;
        if (rect == null || knob == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position,
                eventData.pressEventCamera, out Vector2 local))
            return;

        Vector2 clamped = Vector2.ClampMagnitude(local, radius);
        knob.anchoredPosition = clamped;
        MobileGameplayInput.SetMove(clamped / Mathf.Max(1f, radius));
    }

    private void OnDisable()
    {
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        MobileGameplayInput.SetMove(Vector2.zero);
    }
}
