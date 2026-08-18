using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// DDP 기믹 ③ LED 장미 발판 —
    /// DDP 어울림광장 옆 'LED 장미정원'(장미 25,550송이)에서 따온 기믹.
    /// 발판(Spot_RosePad0~N)이 주기적으로 피고 진다. 피어 있는 동안만 밟을 수 있어
    /// 어울림광장(하층)과 잔디지붕 데크(상층 건축장)를 잇는 공중 지름길이 된다.
    /// 지면 발밑이 꺼지므로 개화 타이밍을 보고 건너야 한다.
    ///
    /// 개화 주기는 '라운드 시작 시각 + 발판 순번 위상'만으로 결정되는 완전 결정론이라
    /// 서버는 기준 시각(epoch) 하나만 복제한다 — 발판마다 상태를 복제할 필요가 없다.
    /// 콜라이더 On/Off는 각 클라 로컬(물리는 원래 각자 로컬).
    /// GameLoopManager가 런타임 부착, 맵 카드에 DdpGimmickConfig가 없으면 스스로 잠잔다.
    /// </summary>
    public class LedRoseNetwork : DdpGimmickBase
    {
        // 개화 사이클의 기준 시각(서버 시각). 라운드 재시작 시 갱신 → 전 클라가 같은 박자로 다시 맞춘다.
        private readonly NetworkVariable<float> m_Epoch =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>이 맵에서 장미 발판이 켜져 있으면 인스턴스. 아니면 null.</summary>
        public static LedRoseNetwork Instance { get; private set; }

        private readonly List<Vector3> m_Pads = new();
        private readonly List<GameObject> m_PadObjects = new();

        private float Period => Mathf.Max(0.5f, Config.RoseBloomSeconds + Config.RoseFadeSeconds);

        protected override void OnGimmickSpawn()
        {
            Instance = this;
            if (IsServer) m_Epoch.Value = Now;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            DestroyVisuals();
        }

        /// <summary>재시작용(서버): 개화 박자를 처음부터 다시.</summary>
        public void ServerReset()
        {
            if (!IsServer || !Active) return;
            m_Epoch.Value = Now;
        }

        /// <summary>발판 i의 사이클 내 경과 시간(0 ~ Period).</summary>
        private float CycleTime(int i)
        {
            float t = Now - m_Epoch.Value + i * Config.RosePadPhaseOffset;
            float p = Period;
            return Mathf.Repeat(t, p);
        }

        /// <summary>발판 i가 지금 피어 있는가(밟을 수 있는가).</summary>
        public bool IsBloomed(int i) => CycleTime(i) < Config.RoseBloomSeconds;

        private void Update()
        {
            if (!Active || !IsSpawned) return;
            if (!RefreshMarkers(DdpSpots.RosePadPrefix, m_Pads, 1)) return;

            EnsurePadObjects();

            for (int i = 0; i < m_PadObjects.Count; i++)
            {
                var pad = m_PadObjects[i];
                if (pad == null) continue;

                float cyc = CycleTime(i);
                bool bloomed = cyc < Config.RoseBloomSeconds;
                // 지기 직전 경고 구간 — 깜빡여서 "곧 꺼진다"를 알린다.
                bool warning = bloomed && cyc > Config.RoseBloomSeconds - Config.RoseWarnSeconds;

                // 콜라이더: 피어 있을 때만 밟을 수 있다. 발밑에서 사라지면 그대로 떨어진다.
                var col = pad.GetComponent<Collider>();
                if (col != null && col.enabled != bloomed) col.enabled = bloomed;

                var rend = pad.GetComponentInChildren<Renderer>();
                if (rend == null) continue;

                if (!bloomed)
                {
                    // 져 있는 동안: 자리만 희미하게 남겨 어디로 건널지 보이게(밟히진 않음)
                    Tint(pad, new Color(0.35f, 0.22f, 0.32f, 1f) * 0.5f);
                    pad.transform.localScale = new Vector3(Config.RosePadSize, 0.06f, Config.RosePadSize);
                    continue;
                }

                float glow = warning
                    ? 0.45f + Mathf.Abs(Mathf.Sin(Time.time * 14f)) * 0.55f   // 급하게 깜빡
                    : 0.75f + Mathf.Abs(Mathf.Sin(Time.time * 2.2f + i)) * 0.25f;

                Tint(pad, kRose * glow);
                // 활짝 필수록 살짝 두툼해진다(피어난 느낌)
                float open = Mathf.Clamp01(cyc / 0.4f);
                pad.transform.localScale = new Vector3(
                    Config.RosePadSize * (0.6f + 0.4f * open), 0.18f, Config.RosePadSize * (0.6f + 0.4f * open));
            }
        }

        private static readonly Color kRose = new Color(0.98f, 0.42f, 0.68f);

        // 발판 실체: Resources/Ddp/RosePad 프리팹(VARCO 모델 자리)이 있으면 그걸, 없으면 납작한 큐브.
        private void EnsurePadObjects()
        {
            if (m_PadObjects.Count == m_Pads.Count) return;

            DestroyVisuals();
            var prefab = Resources.Load<GameObject>("Ddp/RosePad");

            for (int i = 0; i < m_Pads.Count; i++)
            {
                GameObject pad;
                if (prefab != null)
                {
                    pad = Instantiate(prefab);
                    if (pad.GetComponentInChildren<Collider>() == null)
                        pad.AddComponent<BoxCollider>();
                }
                else
                {
                    pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                }
                pad.name = $"~DdpRosePad{i}";
                pad.transform.position = m_Pads[i];
                pad.transform.localScale = new Vector3(Config.RosePadSize, 0.18f, Config.RosePadSize);
                m_PadObjects.Add(pad);
            }
        }

        private void DestroyVisuals()
        {
            foreach (var p in m_PadObjects) if (p != null) Destroy(p);
            m_PadObjects.Clear();
        }

        private static Material s_Mat;
        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponentInChildren<Renderer>();
            if (r == null) return;
            if (s_Mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                s_Mat = sh != null ? new Material(sh) { hideFlags = HideFlags.HideAndDontSave } : null;
            }
            if (s_Mat != null) r.sharedMaterial = s_Mat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            mpb.SetColor(Shader.PropertyToID("_EmissionColor"), c * 1.5f);
            r.SetPropertyBlock(mpb);
        }
    }
}
