using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridSystem
{
    /// <summary>
    /// 맵 전용 '무드' 컬러 그레이딩 — 배경 프리팹 루트에 붙여 두면 스폰(OnEnable) 때
    /// 글로벌 볼륨의 채도·대비·색온도·컬러필터를 맵 값으로 바꾸고, 맵 교체·씬 전환(OnDisable) 때 되돌린다.
    /// 롯데월드=캔디 팝, 경복궁=늦은 오후 골드, 남산=크리스프 블루 — 맵마다 다른 공기를 만든다.
    ///
    /// MapNightAmbience(밤 맵 전용)와 같은 원복 패턴: volume.profile(런타임 사본)만 만져
    /// GameVisualProfile 에셋은 더럽히지 않고, static 스냅샷 1벌 + 마지막 적용자만 복원으로
    /// '새 맵 OnEnable → 옛 맵 OnDisable' 순서 문제를 피한다. 밤 맵(DDP)엔 붙이지 않는다.
    ///
    /// 프리셋 일괄 배치: Tools ▸ Map ▸ ★ 비주얼 스타일(외곽선·맵 무드) 적용 — 재실행 시 프리셋으로 리셋되므로
    /// 수치 실험은 플레이 중 인스펙터로 하고, 확정값은 VisualStyleTool의 프리셋 표에 반영할 것.
    /// </summary>
    public class MapMoodGrade : MonoBehaviour
    {
        [Header("컬러 그레이딩 (주간 기본: 채도 15 · 대비 8 · 색온도 0 · 필터 흰색)")]
        public float Saturation = 15f;
        public float Contrast = 8f;
        [Tooltip("색온도(-100 차가움 ~ +100 따뜻함)")]
        public float Temperature = 0f;
        [Tooltip("틴트(-100 초록 ~ +100 자홍)")]
        public float Tint = 0f;
        [Tooltip("컬러 필터 — 살짝만. 강하면 UI까지 물든 것처럼 보인다.")]
        public Color ColorFilter = Color.white;

        // ── 원복용 스냅샷(static 1벌 — MapNightAmbience와 같은 파괴 순서 문제 대응) ──
        private static MapMoodGrade s_Applied;
        private static ColorAdjustments s_CA;
        private static WhiteBalance s_WB;
        private static float s_Sat, s_Con, s_Temp, s_Tint;
        private static Color s_Filter;

        private void OnEnable()
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || v.profile == null) continue;   // profile = 런타임 사본(에셋 오염 방지)
                var p = v.profile;

                if (s_CA == null && p.TryGet(out ColorAdjustments ca))   // 첫 적용 때만 스냅샷
                {
                    s_CA = ca;
                    s_Sat = ca.saturation.value; s_Con = ca.contrast.value; s_Filter = ca.colorFilter.value;
                }
                if (s_CA != null)
                {
                    s_CA.saturation.Override(Saturation);
                    s_CA.contrast.Override(Contrast);
                    s_CA.colorFilter.Override(ColorFilter);
                }

                if (s_WB == null)
                {
                    if (!p.TryGet(out WhiteBalance wb)) wb = p.Add<WhiteBalance>(true);   // 사본에만 추가됨
                    s_WB = wb;
                    s_Temp = wb.temperature.value; s_Tint = wb.tint.value;
                }
                s_WB.temperature.Override(Temperature);
                s_WB.tint.Override(Tint);

                s_Applied = this;
                break;
            }
        }

        private void OnDisable()
        {
            if (s_Applied != this) return;   // 새 맵이 이미 적용했다면 조용히 물러난다
            s_Applied = null;
            if (s_CA != null)
            {
                s_CA.saturation.Override(s_Sat);
                s_CA.contrast.Override(s_Con);
                s_CA.colorFilter.Override(s_Filter);
                s_CA = null;
            }
            if (s_WB != null)
            {
                s_WB.temperature.Override(s_Temp);
                s_WB.tint.Override(s_Tint);
                s_WB = null;
            }
        }
    }
}
