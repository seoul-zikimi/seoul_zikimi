using UnityEngine;

/// <summary>
/// UI 등장 팝(스케일 0.8 → 오버슛 → 1, easeOutBack). SetActive(true) 될 때마다 재생. unscaled.
/// 패널/HUD 루트에 AddComponent만 하면 됨.
/// </summary>
public class UiPopIn : MonoBehaviour
{
    const float kDur = 0.32f;
    const float kFrom = 0.7f;
    const float kBack = 2.4f;   // 오버슛 강도(easeOutBack c1 — 클수록 크게 튀었다 들어옴)

    float m_T = float.MaxValue;
    Vector3 m_Base;
    bool m_HasBase;

    void OnEnable()
    {
        if (!m_HasBase) { m_Base = transform.localScale; m_HasBase = true; }
        m_T = 0f;
    }

    void Update()
    {
        if (m_T >= kDur) return;
        m_T += Time.unscaledDeltaTime;
        float n = Mathf.Clamp01(m_T / kDur);
        const float c1 = kBack, c3 = c1 + 1f;
        float e = 1f + c3 * Mathf.Pow(n - 1f, 3f) + c1 * Mathf.Pow(n - 1f, 2f);   // easeOutBack(끝에서 정확히 1)
        transform.localScale = m_Base * Mathf.LerpUnclamped(kFrom, 1f, e);
    }
}
