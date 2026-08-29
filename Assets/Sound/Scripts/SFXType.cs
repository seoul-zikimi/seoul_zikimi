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
    UIClick,
    PickUpObject,
    LandObject,
    ThrowObject,
    FallObjectWhileThrowing,
    Hammering,
    Painting,
    BumpPlayers,
    Dash,
    Jump,
    GameOver,
    // ── 경복궁(08/28) — 클립: Assets/Sound/Clips/Gyeongbokgung, 연결: Tools ▸ Sound ▸ 경복궁 사운드 연결 ──
    FireIgnite,    // 발화 순간(화마 급강하)
    FireBurning,   // 타는 중 루프 — SoundLibrary가 아니라 Resources/Sfx/FireBurning을 화염 그룹 AudioSource가 직접 재생
    WaterFill,     // 드므에서 물 뜨기
    WaterPour,     // 양동이 물 붓기(진화 완료)
    HolyChime,     // 사방신 낙하·안착·봉인(신성한 소리)
}
