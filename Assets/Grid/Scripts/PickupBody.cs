using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 바닥 재료의 '노답중력' 비주얼 — 서버 권위 목표 위치(target)로 이동(순수 로컬).
    /// 떨굼/킥은 목표로 굴러가고(글라이드+낙하), '던지기'(출발↔목표 수평거리가 멀면)는 포물선으로 날아간다.
    /// 위치 권위는 MaterialDropField(NetworkList)에 있고, 이 컴포넌트는 보이는 모션만 담당.
    /// </summary>
    public class PickupBody : MonoBehaviour
    {
        private Vector3 m_Target;
        private float m_VVel;                       // 수직 속도(낙하)
        private const float kHorizSpeed = 7f;       // 굴러가는 속도
        private const float kGravity = 22f;         // 과장된 중력(떨굼/킥)

        // 포물선 던지기: 떨굼 산포(±0.3)와 안 겹치는 수평거리면 던진 것으로 보고 포물선 궤적.
        // 주의: 발밑 버리기(ServerDrop)의 최악 수평거리는 ~1.131(축당 0.8) — 이 임계값보다 작아 글라이드로 분류된다.
        //       산포(±0.3)나 이 임계값을 바꿀 땐 '버리기 최악거리 < kArcThreshold' 부등식을 깨지 않게 할 것.
        private const float kArcThreshold = 1.2f;   // 출발↔목표 수평거리 이 이상이면 '던지기'
        private const float kArcGravity = 20f;      // 던지기 중력(살짝 떠 보이게)
        private bool m_Arc;
        private Vector3 m_ArcFrom;
        private float m_ArcT, m_ArcDur, m_ArcV0;
        private Vector3 m_Tumble;

        // 마우스 레이캐스트 집기용: 자기 정체 + 소속(어느 MaterialDropField에 RequestGrab 할지)
        public ulong PickupId   { get; private set; }
        public int   MaterialId { get; private set; }
        public int   ToolBit    { get; private set; }
        public MaterialDropField Owner { get; private set; }
        public void SetIdentity(MaterialDropField owner, ulong id, int materialId, int toolBit)
        {
            Owner = owner; PickupId = id; MaterialId = materialId; ToolBit = toolBit;
        }

        /// <summary>
        /// 던지기 포물선 샘플링(조준 미리보기용) — Init/ArcUpdate와 '같은 수식'이라 선 그대로 날아간다.
        /// buffer 길이만큼 점을 채우고 개수를 반환.
        /// </summary>
        public static int SampleArc(Vector3 from, Vector3 to, Vector3[] buffer)
        {
            var flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float h = Mathf.Max(2.0f, flat.magnitude * 0.45f);
            float v0 = Mathf.Sqrt(2f * kArcGravity * h);
            float dy = to.y - from.y;
            float dur = (v0 + Mathf.Sqrt(Mathf.Max(0f, v0 * v0 - 2f * kArcGravity * dy))) / kArcGravity;
            int n = buffer.Length;
            for (int i = 0; i < n; i++)
            {
                float t = dur * i / (n - 1);
                float u = t / dur;
                buffer[i] = new Vector3(
                    Mathf.Lerp(from.x, to.x, u),
                    from.y + v0 * t - 0.5f * kArcGravity * t * t,
                    Mathf.Lerp(from.z, to.z, u));
            }
            return n;
        }

        public void Init(Vector3 from, Vector3 target)
        {
            transform.position = from;
            transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            m_Target = target;
            m_VVel = 0f;
            m_Arc = false;

            var flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
            if (flat.magnitude >= kArcThreshold)   // 던지기 → 포물선
            {
                float h = Mathf.Max(2.0f, flat.magnitude * 0.45f);   // 오버쿡드식: 더 붕 뜨는 로브(정점 높이)
                m_ArcV0 = Mathf.Sqrt(2f * kArcGravity * h);          // 초기 상승 속도(정점에서 v=0)
                float dy = target.y - from.y;
                // y(t)=from.y + v0·t − ½g·t² = target.y 의 하강 해(나중 근) = 총 비행시간
                m_ArcDur = (m_ArcV0 + Mathf.Sqrt(Mathf.Max(0f, m_ArcV0 * m_ArcV0 - 2f * kArcGravity * dy))) / kArcGravity;
                m_ArcFrom = from;
                m_ArcT = 0f;
                m_Arc = true;
                var axis = Vector3.Cross(Vector3.up, flat.normalized);   // 이동 방향 기준 '앞구르기' 축(오버쿡드 느낌)
                m_Tumble = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.right;
            }
        }

        public void SetTarget(Vector3 target) { m_Target = target; m_Arc = false; m_KickT = kKickSquashDur; }   // 킥 → 굴러가기 + 움찔 찌부

        // ── 착지 재바운스(통·통·통 감쇠) ──
        private float m_BounceV;
        private float NextBounce()
        {
            if (m_BounceV < 0.9f) { m_BounceV = 0f; return 0f; }
            float v = m_BounceV;
            m_BounceV *= 0.45f;
            return v;
        }

        // 늦참/비주얼 재생성: 이미 안착한 픽업은 비행 연출 없이 곧장 제자리에(유령 비행 방지).
        public void Snap(Vector3 pos)
        {
            transform.position = pos;
            m_Target = pos;
            m_Arc = false;
            m_VVel = 0f;
        }

        private void Update()
        {
            if (m_Arc) ArcUpdate();
            else { SettleUpdate(); ApplyIdleScale(); }
        }

        // ── 디용: 대기 중 숨쉬기 + 집기 조준 시 두근두근(단일 스케일 쓰기 지점) ──
        private bool m_Targeted;
        private Vector3 m_VisBase = Vector3.one;
        private bool m_VisInit;
        private float m_BreathPhase;

        /// <summary>집기 조준 대상 표시(PlayerCarry가 호출) — 두근두근 펄스.</summary>
        public void SetTargeted(bool on) => m_Targeted = on;

        private const float kKickSquashDur = 0.22f;
        private float m_KickT;   // 킥 움찔 잔여시간

        private void ApplyIdleScale()
        {
            if (!m_VisInit) { m_VisBase = transform.localScale; m_VisInit = true; m_BreathPhase = Random.value * Mathf.PI * 2f; }
            float k = m_Targeted
                ? 1f + Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.06f              // 두근두근(조준)
                : 1f + Mathf.Sin(Time.time * 2f + m_BreathPhase) * 0.018f;       // 숨쉬기(대기)

            if (m_KickT > 0f)   // 차인 순간 움찔 찌부(한 번 눌렸다 복귀)
            {
                m_KickT -= Time.deltaTime;
                k *= 1f - 0.16f * Mathf.Sin((1f - Mathf.Max(0f, m_KickT) / kKickSquashDur) * Mathf.PI);
            }
            transform.localScale = m_VisBase * k;
        }

        // 포물선: 수평은 등속(시간 선형), 수직은 v0·t−½g·t². 끝나면 목표로 스냅 후 글라이드 모드로.
        private void ArcUpdate()
        {
            m_ArcT += Time.deltaTime;
            if (m_ArcT >= m_ArcDur)
            {
                transform.position = m_Target;
                m_Arc = false; m_VVel = 0f;
                m_BounceV = 2.6f;   // 착지 후 통·통·통 재바운스 시동
                GridSoundBridge.PlaySFXAt("FallObjectWhileThrowing", m_Target);
                GridJuice.GroundHit(m_Target, 0.7f);   // 착지 흙 팡(배송·던지기 공용)
                return;
            }
            float u = m_ArcT / m_ArcDur;
            transform.position = new Vector3(
                Mathf.Lerp(m_ArcFrom.x, m_Target.x, u),
                m_ArcFrom.y + m_ArcV0 * m_ArcT - 0.5f * kArcGravity * m_ArcT * m_ArcT,
                Mathf.Lerp(m_ArcFrom.z, m_Target.z, u));
            transform.Rotate(m_Tumble, 520f * Time.deltaTime, Space.World);   // 공중 앞구르기(활발하게)
        }

        // 떨굼/킥: 목표로 굴러 이동 + 중력 낙하 + 구르기 회전(기존 동작 그대로).
        private void SettleUpdate()
        {
            var pos = transform.position;

            var toH = new Vector3(m_Target.x - pos.x, 0f, m_Target.z - pos.z);
            float hd = toH.magnitude;
            Vector3 move = Vector3.zero;
            if (hd > 0.001f)
            {
                float step = Mathf.Min(hd, kHorizSpeed * Time.deltaTime);
                move = toH / hd * step;
                pos += move;
            }

            if (pos.y > m_Target.y + 0.001f)
            {
                m_VVel -= kGravity * Time.deltaTime;
                pos.y += m_VVel * Time.deltaTime;
                if (pos.y < m_Target.y) { pos.y = m_Target.y; m_VVel = NextBounce(); }   // 착지 → 남은 재바운스
            }
            else
            {
                pos.y = m_Target.y;
                if (m_VVel <= 0f) m_VVel = NextBounce();
                if (m_VVel > 0f) pos.y += m_VVel * Time.deltaTime;   // 재바운스 상승 시작
            }

            transform.position = pos;

            if (move.sqrMagnitude > 1e-6f)
            {
                var axis = Vector3.Cross(Vector3.up, move.normalized);
                transform.Rotate(axis, move.magnitude * 220f, Space.World);
            }
        }
    }
}
