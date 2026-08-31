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

        private readonly HashSet<Renderer> m_Seen = new();
        private Transform m_WatchRoot;
        private float m_NextPoll;

        private void OnEnable() { m_NextPoll = 0f; }   // 리스폰 시 즉시 1회

        private void Update()
        {
            if (Time.time < m_NextPoll) return;
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

        private void ApplyUnder(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (m_Seen.Contains(r)) continue;
                m_Seen.Add(r);
                // ⚠ .materials — 이 렌더러 전용 인스턴스를 만든다(공유 에셋을 더럽히지 않게).
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;
                    Color baseCol =
                        mat.HasProperty("_BaseColor")      ? mat.GetColor("_BaseColor") :
                        mat.HasProperty("baseColorFactor") ? mat.GetColor("baseColorFactor") :
                        Color.white;
                    var emission = baseCol * Tint * Intensity;
                    emission.a = 1f;

                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        mat.SetColor("_EmissionColor", emission);
                    }
                    else if (mat.HasProperty("emissiveFactor"))   // glTFast 임포트 머티리얼
                    {
                        mat.SetColor("emissiveFactor", emission);
                    }
                }
            }
        }
    }
}
