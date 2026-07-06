using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼 쫀득: 호버 살짝 확대 → 누르면 눌림 → 떼면 통통 복귀. 아무 uGUI Button에나 부착.
/// JuicyButton.Attach(버튼) 단건 / AttachAll(루트) 일괄. unscaled 시간이라 일시정지 화면에서도 동작.
/// </summary>
public class JuicyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    const float kHover = 1.08f;
    const float kPress = 0.87f;
    const float kStiffness = 420f;   // 스프링 강성(클수록 빠릿)
    const float kDamping   = 13f;    // 감쇠(작을수록 더 통통 튐)

    float m_Target = 1f, m_Cur = 1f, m_Vel;
    Vector3 m_Base;
    bool m_HasBase;
    Button m_Btn;

    void OnEnable()
    {
        if (!m_HasBase) { m_Base = transform.localScale; m_HasBase = true; }
        if (m_Btn == null) m_Btn = GetComponent<Button>();
        m_Cur = m_Target = 1f; m_Vel = 0f;
        transform.localScale = m_Base;
    }

    bool Usable => m_Btn == null || m_Btn.interactable;   // 비활성 버튼은 반응 안 함

    public void OnPointerEnter(PointerEventData _) { if (Usable) m_Target = kHover; }
    public void OnPointerExit(PointerEventData _)  => m_Target = 1f;
    public void OnPointerDown(PointerEventData _)  { if (Usable) m_Target = kPress; }
    public void OnPointerUp(PointerEventData _)    { if (Usable) { m_Target = kHover; m_Vel += 2.2f; } }   // 떼는 순간 위로 튕김

    void Update()
    {
        if (!Usable && m_Target != 1f) m_Target = 1f;   // 호버 중 비활성화되면 원복
        if (Mathf.Approximately(m_Cur, m_Target) && Mathf.Abs(m_Vel) < 0.001f) return;

        // 감쇠 스프링: 목표를 지나쳤다가 통통 튀며 정착 → 진짜 탱글한 손맛
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);   // 프레임 드랍 시 폭주 방지
        m_Vel += (m_Target - m_Cur) * kStiffness * dt;
        m_Vel *= Mathf.Exp(-kDamping * dt);
        m_Cur += m_Vel * dt;
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
