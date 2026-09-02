using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 밤 맵에서 '지은 블록'과 지정한 배경 오브젝트를 자체 발광시킨다(DDP 미디어 파사드 느낌).
    ///
    /// 대상 렌더러의 머티리얼 인스턴스에 '베이스 색 × 틴트 × 세기'를 에미션으로 켠다 —
    /// 은색 패널은 은백색으로, 유리는 파랗게, LED 장미는 분홍으로 각자 제 색대로 빛난다.
    /// (SRP Batcher가 MaterialPropertyBlock을 무시하는 프로젝트라 인스턴스 머티리얼을 쓴다 —
    ///  블록 수십 개 수준이라 인스턴스 비용은 무시 가능.)
    ///
    /// URP Lit(_EmissionColor + _EMISSION 키워드)과 glTFast(emissiveFactor — 키워드 없음) 둘 다 지원.
    /// _EMISSION 셰이더 변형은 배경 프리팹의 가로등 머티리얼(에셋에서 켜 둠)이 빌드에 실어 나른다.
    ///
    /// 블록은 GridNetwork가 "~GridVisuals" 아래에 수시로 만들었다 없애므로(공정·붕괴·완성체 교체)
    /// 가벼운 폴링으로 '새 렌더러만' 처리한다. 정답 고스트(~AnswerGhost)는 딴 루트라 안 건드린다.
    /// </summary>
    public class NightBuildGlow : MonoBehaviour
    {
        [Tooltip("이 이름의 씬 오브젝트 아래(지은 블록들)를 감시한다. 비우면 감시 안 함.")]
        public string WatchRootName = "~GridVisuals";

        [Tooltip("추가로 발광시킬 배경 오브젝트(LED 장미밭 등). 프리팹 내부 참조.")]
        public Transform[] ExtraTargets;

        [Tooltip("에미션 = 베이스 색 × 틴트 × 세기. 틴트로 전체 색온도를 잡는다.")]
        public Color Tint = new Color(0.80f, 0.88f, 1.00f);
        public float Intensity = 1.25f;

        [Tooltip("새 렌더러 탐색 주기(초). 블록이 놓인 뒤 최대 이만큼 늦게 켜진다.")]
        public float PollInterval = 0.5f;

        [Tooltip("대상 머티리얼을 URP Lit 에미션 인스턴스로 강제 교체(베이스 텍스처·색 승계). " +
                 "glTFast 임포트 머티리얼은 에미션 0으로 구워지면 셰이더에서 에미션 분기가 아예 빠져 " +
                 "런타임에 emissiveFactor를 줘도 안 빛난다 — LED 장미가 그 케이스(09/01).")]
        public bool ForceLitEmissive = false;

        [Tooltip("반짝임 세기(0=고정 발광). 0.6이면 기준 밝기의 ±60%를 오간다 — LED 장미용.")]
        public float TwinkleAmount = 0f;
        [Tooltip("반짝임 속도(사이클/초 감각). 대상마다 위상이 달라 밭 전체가 별밭처럼 반짝인다.")]
        public float TwinkleSpeed = 2.2f;

        private readonly HashSet<Renderer> m_Seen = new();
        // 반짝임용: 발광을 켠 머티리얼 인스턴스와 (셰이더별) 에미션 프로퍼티·기준값·위상
        private readonly List<Material> m_TwinkleMats = new();
        private readonly List<int> m_TwinkleProp = new();
        private readonly List<Color> m_TwinkleBase = new();
        private readonly List<float> m_TwinklePhase = new();
        private Transform m_WatchRoot;
        private float m_NextPoll;

        private void OnEnable() { m_NextPoll = 0f; }   // 리스폰 시 즉시 1회

        private void Update()
        {
            if (Time.time >= m_NextPoll)
            {
                m_NextPoll = Time.time + PollInterval;

                if (m_WatchRoot == null && !string.IsNullOrEmpty(WatchRootName))
                {
                    var go = GameObject.Find(WatchRootName);
                    if (go != null) m_WatchRoot = go.transform;
                }

                if (m_WatchRoot != null) ApplyUnder(m_WatchRoot);
                if (ExtraTargets != null)
                    foreach (var t in ExtraTargets)
                        if (t != null) ApplyUnder(t);
            }

            // 반짝임 — 매 프레임, 인스턴스 머티리얼의 에미션만 흔든다(키워드는 이미 켜져 있음)
            if (TwinkleAmount > 0f)
            {
                float t = Time.time * TwinkleSpeed;
                for (int i = 0; i < m_TwinkleMats.Count; i++)
                {
                    var mat = m_TwinkleMats[i];
                    if (mat == null) continue;
                    float k = 1f + TwinkleAmount * Mathf.Sin(t + m_TwinklePhase[i]);
                    mat.SetColor(m_TwinkleProp[i], m_TwinkleBase[i] * Mathf.Max(0.05f, k));
                }
            }
        }

        private void RegisterTwinkle(Renderer r, Material mat, int propId, Color baseEmission)
        {
            if (TwinkleAmount <= 0f) return;
            m_TwinkleMats.Add(mat);
            m_TwinkleProp.Add(propId);
            m_TwinkleBase.Add(baseEmission);
            // 위치 기반 위상 — 결정론(리스폰해도 같은 별밭)이면서 송이마다 제각각
            var p = r.transform.position;
            m_TwinklePhase.Add((p.x * 12.9898f + p.z * 78.233f) % (Mathf.PI * 2f));
        }

        private void ApplyUnder(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (m_Seen.Contains(r)) continue;
                m_Seen.Add(r);
                // ⚠ .materials — 이 렌더러 전용 인스턴스를 만든다(공유 에셋을 더럽히지 않게).
                var mats = r.materials;
                bool replaced = false;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var mat = mats[mi];
                    if (mat == null) continue;
                    Color baseCol =
                        mat.HasProperty("_BaseColor")      ? mat.GetColor("_BaseColor") :
                        mat.HasProperty("baseColorFactor") ? mat.GetColor("baseColorFactor") :
                        Color.white;
                    var emission = baseCol * Tint * Intensity;
                    emission.a = 1f;

                    if (ForceLitEmissive)
                    {
                        // glTFast의 에미션-없는 변형 문제를 우회 — 베이스 텍스처·색을 승계한
                        // URP Lit 에미션 인스턴스로 통째 교체(장미 수십 송이 수준이라 비용 무시 가능).
                        var sh = Shader.Find("Universal Render Pipeline/Lit");
                        if (sh != null)
                        {
                            var lit = new Material(sh);
                            var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                                    : mat.HasProperty("baseColorTexture") ? mat.GetTexture("baseColorTexture") : null;
                            if (tex != null) lit.SetTexture("_BaseMap", tex);
                            lit.SetColor("_BaseColor", baseCol);
                            lit.EnableKeyword("_EMISSION");
                            lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                            lit.SetColor("_EmissionColor", emission);
                            mats[mi] = lit;
                            replaced = true;
                            RegisterTwinkle(r, lit, Shader.PropertyToID("_EmissionColor"), emission);
                            continue;
                        }
                    }

                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        mat.SetColor("_EmissionColor", emission);
                        RegisterTwinkle(r, mat, Shader.PropertyToID("_EmissionColor"), emission);
                    }
                    else if (mat.HasProperty("emissiveFactor"))   // glTFast 임포트 머티리얼
                    {
                        mat.SetColor("emissiveFactor", emission);
                        RegisterTwinkle(r, mat, Shader.PropertyToID("emissiveFactor"), emission);
                    }
                }
                if (replaced) r.materials = mats;
            }
        }
    }
}
