using Unity.Netcode.Components;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// owner(클라) 권위 NetworkAnimator. ClientNetworkTransform과 동일하게 owner가 애니를 구동하고
    /// 서버를 경유해 다른 클라에 복제한다. (기본 NetworkAnimator는 server 권위라 owner 구동과 안 맞음)
    /// Animator 파라미터(Speed/Grounded/Climbing/Processing)는 자동 폴링 동기화,
    /// 트리거(Throw)는 PlayerAnimator가 SetTrigger로 명시 전송. 참고: [[ngo-owner-authoritative-fx-sync]]
    /// </summary>
    [DisallowMultipleComponent]
    public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
