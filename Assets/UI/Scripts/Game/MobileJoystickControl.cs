using Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>프리팹에 배치된 좌하단 이동 조이스틱. UI 로컬 좌표를 -1..1 이동값으로 변환한다.
/// 배그식 대시: 노브를 가장자리까지 쭉 밀면 달리기(별도 버튼 없음) — 노브가 살구색으로 변해 대시 중임을 알린다.</summary>
public sealed class MobileJoystickControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform knob;
    [SerializeField] private float radius = 78f;

    // 대시 걸림 0.92 / 풀림 0.80 — 히스테리시스로 경계에서 대시가 덜덜거리지 않게.
    private const float kSprintOn = 0.92f;
    private const float kSprintOff = 0.80f;
    private static readonly Color kKnobSprintColor = new(1f, 0.79f, 0.46f, 0.95f);   // JobsnailUiKit.Apricot 톤
    private bool m_Sprinting;
    private Image m_KnobImage;
    private Color m_KnobBaseColor;
    private bool m_KnobColorCached;

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
        SetSprinting(false);
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
        float magnitude = clamped.magnitude / Mathf.Max(1f, radius);
        MobileGameplayInput.SetMove(clamped / Mathf.Max(1f, radius));
        SetSprinting(m_Sprinting ? magnitude >= kSprintOff : magnitude >= kSprintOn);
    }

    private void SetSprinting(bool on)
    {
        if (m_Sprinting == on) return;
        m_Sprinting = on;
        MobileGameplayInput.SetSprint(on);

        if (m_KnobImage == null && knob != null)
        {
            m_KnobImage = knob.GetComponent<Image>();
            if (m_KnobImage != null && !m_KnobColorCached)
            {
                m_KnobBaseColor = m_KnobImage.color;   // 프리팹에서 튜닝한 기본색 보존
                m_KnobColorCached = true;
            }
        }
        if (m_KnobImage != null)
            m_KnobImage.color = on ? kKnobSprintColor : m_KnobBaseColor;
    }

    private void OnDisable()
    {
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        MobileGameplayInput.SetMove(Vector2.zero);
        SetSprinting(false);
    }
}
