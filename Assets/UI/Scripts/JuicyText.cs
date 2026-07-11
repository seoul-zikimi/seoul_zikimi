using TMPro;
using UnityEngine;

/// <summary>
/// TMP 글자별 물결·흔들(텍스트 애니메이터). 큰 순간 텍스트(등급·배너·토스트)에만 붙여 예쁘게.
/// 정점(vertex)만 건드려서 rectTransform 스케일/회전(팝·기울임)과 공존. unscaled 시간(일시정지 무관).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class JuicyText : MonoBehaviour
{
    [SerializeField] private float m_Amplitude = 5f;    // 위아래 흔들 픽셀
    [SerializeField] private float m_Frequency = 4f;    // 흔들 속도
    [SerializeField] private float m_PerCharPhase = 0.4f; // 글자별 위상차(물결)
    [SerializeField] private float m_Rotate = 7f;       // 글자 회전 각도

    private TMP_Text m_Text;

    private void Awake() => m_Text = GetComponent<TMP_Text>();

    public void Configure(float amp, float freq, float phase, float rot)
    { m_Amplitude = amp; m_Frequency = freq; m_PerCharPhase = phase; m_Rotate = rot; }

    private void LateUpdate()
    {
        if (m_Text == null) return;
        m_Text.ForceMeshUpdate();
        var info = m_Text.textInfo;
        if (info.characterCount == 0) return;

        float time = Time.unscaledTime * m_Frequency;
        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible) continue;

            var verts = info.meshInfo[ch.materialReferenceIndex].vertices;
            int vi = ch.vertexIndex;
            float t = time + i * m_PerCharPhase;
            float s = Mathf.Sin(t);

            Vector3 offset = new Vector3(0f, s * m_Amplitude, 0f);
            Vector3 center = (verts[vi] + verts[vi + 2]) * 0.5f;          // 글자 중심
            Quaternion rot = Quaternion.Euler(0f, 0f, s * m_Rotate);      // 글자별 살랑 회전
            for (int k = 0; k < 4; k++)
                verts[vi + k] = center + rot * (verts[vi + k] - center) + offset;
        }

        for (int i = 0; i < info.meshInfo.Length; i++)
        {
            info.meshInfo[i].mesh.vertices = info.meshInfo[i].vertices;
            m_Text.UpdateGeometry(info.meshInfo[i].mesh, i);
        }
    }
}
