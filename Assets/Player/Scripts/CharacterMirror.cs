using UnityEngine;

/// <summary>
/// 캐리어(달팽이) Animator의 현재 상태를 같은 이름의 자기 상태로 미러링.
/// 두 컨트롤러의 상태 이름이 같아야 한다(Idle/Walk/Run/Jump/Throw/Hammer/Climb).
/// 파라미터·트리거 복제 없이 상태 해시만 따라가므로 리그가 달라도 동작.
/// </summary>
public class CharacterMirror : MonoBehaviour
{
    /// <summary>사다리 오를 때 모델 180° 회전 여부 — 클립이 정면을 보는 캐릭터(거북이)만 켠다.</summary>
    public bool ClimbFlip;

    private Animator m_Carrier;
    private Animator m_Self;
    private int m_LastState;
    private Vector3 m_BasePos;       // 부착 시 로컬 위치(비주얼 지면 오프셋)

    public void Init(Animator carrier, Animator self)
    {
        m_Carrier = carrier;
        m_Self = self;
        m_LastState = 0;
        m_BasePos = transform.localPosition;
    }

    private void LateUpdate()
    {
        if (m_Carrier == null || m_Self == null || !m_Carrier.isActiveAndEnabled) return;

        var st = m_Carrier.IsInTransition(0)
            ? m_Carrier.GetNextAnimatorStateInfo(0)
            : m_Carrier.GetCurrentAnimatorStateInfo(0);
        if (st.shortNameHash != m_LastState && m_Self.HasState(0, st.shortNameHash))
        {
            m_LastState = st.shortNameHash;
            m_Self.CrossFade(st.shortNameHash, 0.1f, 0);

            // 믹사모 사다리 클립은 정면이 카메라를 봐서 벽을 등지게 됨 — 오를 땐 모델을 180° 돌린다
            m_Climbing = st.shortNameHash == kClimb;
            transform.localRotation = m_Climbing && ClimbFlip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            if (!m_Climbing) transform.localPosition = m_BasePos;
        }

        // 사다리 중엔 클립이 힙을 옆·위로 밀어 고정 보정으론 안 맞는다 —
        // 매 프레임 렌더 바운즈를 실측해 "바닥 중앙 = 플레이어 루트"로 재중심(닉네임과 정렬).
        if (m_Climbing) RecenterOnParent();
    }

    private bool m_Climbing;

    private void RecenterOnParent()
    {
        var rs = m_Self.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0 || transform.parent == null) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var cur = new Vector3(b.center.x, b.min.y, b.center.z);
        transform.position += transform.parent.position - cur;
    }


    private static readonly int kClimb = Animator.StringToHash("Climb");
}
