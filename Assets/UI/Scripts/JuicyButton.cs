using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼 쫀득: 호버 살짝 확대 → 누르면 눌림 → 떼면 통통 복귀. 아무 uGUI Button에나 부착.
/// JuicyButton.Attach(버튼) 단건 / AttachAll(루트) 일괄. unscaled 시간이라 일시정지 화면에서도 동작.
/// </summary>
public class JuicyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    const float kHover = 1.06f;
    const float kPress = 0.90f;
    const float kSpeed = 16f;     // 수렴 속도(클수록 탱탱)

    float m_Target = 1f, m_Cur = 1f;
    Vector3 m_Base;
    bool m_HasBase;

    void OnEnable()
    {
        if (!m_HasBase) { m_Base = transform.localScale; m_HasBase = true; }
        m_Cur = m_Target = 1f;
        transform.localScale = m_Base;
    }

    public void OnPointerEnter(PointerEventData _) => m_Target = kHover;
    public void OnPointerExit(PointerEventData _)  => m_Target = 1f;
    public void OnPointerDown(PointerEventData _)  => m_Target = kPress;
    public void OnPointerUp(PointerEventData _)    => m_Target = kHover;

    void Update()
    {
        if (Mathf.Approximately(m_Cur, m_Target)) return;
        m_Cur = Mathf.Lerp(m_Cur, m_Target, 1f - Mathf.Exp(-kSpeed * Time.unscaledDeltaTime));
        transform.localScale = m_Base * m_Cur;
    }

    public static void Attach(Component button)
    {
        if (button == null) return;
        if (button.GetComponent<JuicyButton>() == null)
            button.gameObject.AddComponent<JuicyButton>();
    }

    public static void AttachAll(GameObject root)
    {
        if (root == null) return;
        foreach (var b in root.GetComponentsInChildren<Button>(true)) Attach(b);
    }
}
