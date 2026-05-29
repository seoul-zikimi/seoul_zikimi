/// <summary>
/// SFX 종류 enum. SoundManager.PlaySFX(SFXType)로 호출.
/// 호출부는 AudioClip을 직접 들고 다닐 필요 없음.
///
/// ── 현재 연결 가능 ────────────────────────────────────────
/// PlayerFootstep : PlayerDustTrail.Update() — isMoving 감지 + 0.35초 쿨다운
/// PlayerBounce   : PlayerBounce.OnCollisionEnter() — 충돌 처리 후 1줄 추가
///
/// ── 예측 (해당 시스템 구현 후 enum 추가 + SoundLibrary.asset 클립 연결) ──
/// BlockPickUp       — PlayerBuildingHandler.TryPickUp()
/// BlockPlace        — PlaceBuildingCommand.Execute()
/// BlockFixed        — GridManager.TryAdvanceProcess() (Fixed 상태 전환 시)
/// BlockFinished     — GridManager.TryAdvanceProcess() (Finished 상태 전환 시)
/// BlockCollapse     — CollapseManager [ClientRpc]
/// BlockCollapseChain— CollapseManager [ClientRpc] (연쇄 붕괴)
/// TimerWarning      — 타이머 매니저 (30초 경고, 1회)
/// TimerTick         — 타이머 매니저 (마지막 10초 틱)
/// TimerEnd          — 타이머 매니저 (타이머 0)
/// ButtonClick       — 각 HUD / Popup
/// GameStart         — 건축 시작 연출
/// VoteOpen          — 건축 종료 투표 열림
/// VoteAgree         — 투표 동의
/// ScoreReveal       — 점수 공개 연출
/// </summary>
public enum SFXType
{
    PlayerFootstep,
    PlayerBounce,
}
