using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 에미션 색을 팔레트 사이로 천천히 순환시킨다 — DDP 미디어 파사드(LED 스트립)용.
    /// 대상 렌더러의 머티리얼 '인스턴스'에만 쓴다(공유 에셋 오염 방지 — 에디터에서 색이 눌어붙으면 안 된다).
    /// _EMISSION 키워드는 에셋(DdpMapTool.EnsureEmissiveMaterial)에서 이미 켜져 있어야 한다 —
    /// 여기서는 색만 바꾼다(런타임 키워드 켜기는 빌드에서 변형이 잘려 있을 수 있다).
    /// </summary>
    public class EmissionCycler : MonoBehaviour
    {
        [Tooltip("색을 순환시킬 렌더러들. 인덱스마다 위상이 어긋나 물결처럼 흐른다.")]
        public Renderer[] Targets;

        [Tooltip("순환 팔레트 — 마지막에서 처음으로 되감아 이어진다.")]
        public Color[] Palette =
        {
            new Color(0.25f, 0.85f, 1.00f),   // 시안
            new Color(0.55f, 0.40f, 1.00f),   // 보라
            new Color(1.00f, 0.30f, 0.75f),   // 마젠타
            new Color(0.25f, 0.55f, 1.00f),   // 파랑
        };

        [Tooltip("팔레트 한 바퀴 도는 시간(초)")]
        public float CycleSeconds = 16f;

        [Tooltip("에미션 밝기 배수(블룸 문턱 1.1을 넘겨야 빛나 보인다)")]
        public float Intensity = 3.0f;

        private static readonly int kEmission = Shader.PropertyToID("_EmissionColor");
        private Material[] m_Mats;   // 대상별 인스턴스(첫 머티리얼만 — 스트립은 단일 머티리얼)

        private void Start()
        {
            if (Targets == null) return;
            m_Mats = new Material[Targets.Length];
            for (int i = 0; i < Targets.Length; i++)
                if (Targets[i] != null) m_Mats[i] = Targets[i].material;   // 인스턴스화
        }

        private void Update()
        {
            if (m_Mats == null || Palette == null || Palette.Length == 0 || CycleSeconds <= 0f) return;
            for (int i = 0; i < m_Mats.Length; i++)
            {
                var mat = m_Mats[i];
                if (mat == null || !mat.HasProperty(kEmission)) continue;
                // 대상마다 반 스텝씩 위상을 밀어 두 줄이 서로 다른 색으로 흐른다
                float t = (Time.time / CycleSeconds + i * 0.5f / Palette.Length) % 1f;
                float f = t * Palette.Length;
                int a = Mathf.FloorToInt(f) % Palette.Length;
                int b = (a + 1) % Palette.Length;
                var c = Color.Lerp(Palette[a], Palette[b], f - Mathf.Floor(f)) * Intensity;
                c.a = 1f;
                mat.SetColor(kEmission, c);
            }
        }
    }
}
