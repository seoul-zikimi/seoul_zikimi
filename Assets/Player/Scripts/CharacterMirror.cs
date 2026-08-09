using UnityEngine;

/// <summary>
/// 캐리어(달팽이) Animator의 현재 상태를 같은 이름의 자기 상태로 미러링.
/// 두 컨트롤러의 상태 이름이 같아야 한다(Idle/Walk/Run/Jump/Throw/Hammer/Climb).
/// 파라미터·트리거 복제 없이 상태 해시만 따라가므로 리그가 달라도 동작.
/// </summary>
public class CharacterMirror : MonoBehaviour
{
    private Animator m_Carrier;
    private Animator m_Self;
    private int m_LastState;

    public void Init(Animator carrier, Animator self)
    {
        m_Carrier = carrier;
        m_Self = self;
        m_LastState = 0;
    }

    private void LateUpdate()
    {
        if (m_Carrier == null || m_Self == null || !m_Carrier.isActiveAndEnabled) return;

        var st = m_Carrier.IsInTransition(0)
            ? m_Carrier.GetNextAnimatorStateInfo(0)
            : m_Carrier.GetCurrentAnimatorStateInfo(0);
        if (st.shortNameHash == m_LastState) return;
        if (!m_Self.HasState(0, st.shortNameHash)) return;   // 내 컨트롤러에 없는 상태는 무시(마지막 상태 유지)
        m_LastState = st.shortNameHash;
        m_Self.CrossFade(st.shortNameHash, 0.1f, 0);

        // 믹사모 사다리 클립은 정면이 카메라를 봐서 벽을 등지게 됨 — 오를 땐 모델을 180° 돌린다
        transform.localRotation = st.shortNameHash == kClimb
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.identity;
    }

    private static readonly int kClimb = Animator.StringToHash("Climb");
}
