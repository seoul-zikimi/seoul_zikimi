GameplayFramework 네트워크 연동 가이드

1. 기본 원칙

게임 상태 변경은 서버에서만 수행한다.

클라이언트 UI는 RPC 요청만 전송한다.

팀, 타이머, 종료 동의, 아이템, 점수 결과는 서버 결과를 복제한다.

현재 협동 모드는 기존 GameLoopManager가 계속 담당한다.

새 GameplayFlowController는 2대2 또는 새 모드를 구현할 때 사용한다.

2. 현재 협동 모드

현재 협동 게임은 CurrentCoopGameplayAdapter까지 연결되어 있다.

지원 기능:



건축 종료 동의 → 기존 GameLoopManager.RequestToggleConsent()

게임 나가기 → 기존 GameLoopManager.RequestLeaveToLobby()

타이머·인원·동의·완성도 조회 → GetCurrentStatus()

따라서 기존 협동 모드는 새 GameplayFlowController로 교체하지 않는다.

3. 팀 배정

ITeamAssignmentGateway

void RequestTeamAssignment(

GameModeDefinition mode,

IReadOnlyCollection<string> playerIds);

사용 시점:



서버가 2대2 게임을 시작한다.

GameplayFlowController.StartSession(TeamVersus, playerIds)를 호출한다.

TeamAssignmentRequested 이벤트가 발생한다.

4명을 2명씩 나눈다.

TeamRoster를 만들어 아래 함수를 호출한다.

flow.ConfirmTeamAssignment(teamRoster);

예시:



var roster = new TeamRoster(

new Dictionary<string, string>

{

["client-0"] = "A",

["client-1"] = "A",

["client-2"] = "B",

["client-3"] = "B"

});



flow.ConfirmTeamAssignment(roster);

playerId는 Netcode ClientId나 Unity Player ID 중 하나를 정해 전체 시스템에서 일관되게 사용해야 한다.

4. 로컬 플레이어와 팀 조회

ILocalGameplayIdentity

string PlayerId { get; }

string TeamId { get; }

사용 시점:



건축 종료 동의 요청

아이템 사용 요청

게임 나가기

아군/상대 팀 판정

클라이언트가 임의로 TeamId를 보내더라도 서버의 TeamRoster로 다시 검증해야 한다.

5. 상대 팀 조회

IOpponentTeamResolver

string GetOpponentTeamId(string sourceTeamId);

사용 시점:



지진

날씨 공격

안개

이동·공정 디버프

주문 해킹

2대2에서는 일반적으로 A → B, B → A를 반환한다. 최종 판단은 반드시 서버 팀 정보로 한다.

6. 게임 시작 흐름

서버 호출 순서:



flow.StartSession(mode, connectedPlayerIds);



// 2대2일 경우

flow.ConfirmTeamAssignment(teamRoster);



// 건축물 선택 완료

flow.ConfirmBuildingSelection(buildingId, buildingTimeLimitSeconds);



// 맵과 정답 로딩 완료

flow.StartBuilding();

서버 게임 루프:



flow.Tick(deltaTime);

타임어택: 건축물별 제한시간 사용

2대2: 항상 420초

자유 모드: 제한시간 없음

Tick은 서버에서만 호출한다.

7. 건축 종료 및 항복

클라이언트가 종료 버튼을 누르면 서버 RPC를 보낸다.

서버에서:



FinishConsentState state =

flow.ToggleBuildFinishConsent(playerId);

처리 결과:



협동: 남아 있는 모든 플레이어가 동의하면 채점

2대2: 같은 팀의 남은 팀원 전원이 동의하면 해당 팀 항복

한 명만 남은 팀: 혼자 동의해도 처리 가능

UI 복제용 데이터:



state.ConsentCount

state.RequiredCount

state.GroupId

state.IsResolved

클라이언트가 직접 동의 수를 계산하지 않고 서버 결과를 표시해야 한다.

8. 플레이어 이탈

ILeaveGameGateway

void LeaveWithoutScoring();

사용 시점:



클라이언트가 ‘게임 나가기’를 누른다.

서버에 이탈을 통지한다.

서버에서 호출한다.

flow.NotifyPlayerLeft(playerId);

세션과 Netcode 연결을 정리한다.

로비로 이동한다.

마지막 플레이어까지 나가면 게임은 무효화되며 채점과 보상을 호출하지 않는다.

9. 아이템 스폰

ICompetitiveItemSpawnGateway

string Spawn(CompetitiveItemSpawnRequest request);

void Despawn(string itemInstanceId, ItemDespawnReason reason);

서버가 구현해야 할 내용:



맵의 유효한 랜덤 위치 선정

NetworkObject 생성

고유 아이템 인스턴스 ID 반환

모든 클라이언트에 스폰 복제

사용 또는 60초 만료 시 despawn

request.Reason:



TimedWorldSpawn: 30초 주기 월드 스폰

CompletionMilestone: 특정 팀의 완성도 10% 보상

request.BeneficiaryTeamId가 있으면 해당 팀 보상 아이템이다.

서버 루프:



itemSpawnDirector.Tick(deltaTime);

팀 완성도가 변경되면:



itemSpawnDirector.ReportCompletion(teamId, completionPercent);

아이템 사용이 확정되면:



itemSpawnDirector.NotifyConsumed(itemInstanceId);

완성도가 내려갔다가 복구돼도 이미 지급한 10% 보상은 다시 지급되지 않는다.

10. 아이템 사용

서버 처리 순서:



플레이어가 실제로 해당 아이템을 소유했는지 확인

아직 만료되지 않았는지 확인

플레이어의 팀을 서버에서 확인

사용 가능한 상태인지 확인

아래 함수 호출

itemUseService.Use(

itemKind,

sourcePlayerId,

sourceTeamId);

인벤토리 또는 월드 아이템 제거

적용 결과 복제

IHeldCompetitiveItemGateway

bool TryGetHeldItem(out CompetitiveItemKind kind);

void ConsumeHeldItem();

현재 대포는 CompetitiveItemKind에 포함되지 않는다.

11. 아이템 효과 인터페이스

다음 인터페이스들은 서버 권위 구현이 필요하다.



IUnfixedConstructionTarget

void CollapseAllUnfixed(string teamId);

지진 사용 시 호출한다.



대상 팀 영역의 미고정 재료 제거

기존 붕괴 규칙으로 위쪽 재료 연쇄 붕괴

결과를 모든 클라이언트에 복제

ITemporaryTeamWeatherTarget

void ApplyTemporaryWeather(

string teamId,

WeatherKind weather,

float durationSeconds);

날씨 아이템 사용 시 호출한다.



대상 팀 진영에만 적용

기존 날씨가 있다면 새 날씨로 교체

지속시간을 다시 60초로 초기화

ITeamFogTarget

void ApplyFog(string teamId, float durationSeconds);

대상 팀 클라이언트에만 5초간 안개를 표시한다.



ITeamMovementModifierTarget

void ApplyMovementSpeedMultiplier(

string teamId,

float multiplier,

float durationSeconds);

디버프: 0.7

버프: 1.3

지속시간: 15초

ITeamProcessModifierTarget

void ApplyProcessSpeedMultiplier(

string teamId,

float multiplier,

float durationSeconds);

PlayerCarry의 공정 진행 속도에 적용한다.



ITeamOrderLockTarget

void LockNewOrders(

string teamId,

float durationSeconds);

5초 동안 서버가 해당 팀의 새 주문 요청을 거부한다.



ITeamWeatherImmunityTarget

void ApplyWeatherImmunity(

string teamId,

float durationSeconds);

30초 동안 해당 팀의 날씨성 미끄러짐과 붕괴 효과를 무시한다.

12. 팀별 채점

ITeamCompletionScoreGateway

TeamCompletionScore GetCompletionScore(string teamId);

2대2 종료 시 각 팀의 건축 영역을 따로 채점한다.

현재 GridNetwork.Score는 맵 전체 점수만 제공하므로 다음 구현이 필요하다.



팀 A 건축 영역 필터링

팀 B 건축 영역 필터링

각 영역의 완성도 계산

서버 점수 복제

두 점수를 얻은 뒤:



VersusMatchResult result = resultResolver.Resolve(

endContext,

teamAScore,

teamBScore);

판정:



완성도가 높은 팀 승리

같으면 무승부

항복이면 완성도와 무관하게 항복 팀 패배

13. 보상

IGameplayRewardGateway

void GrantRewards(GameEndContext endContext);

호출 조건:



채점이 정상 완료됨

무효화된 게임이 아님

자유 모드가 아님

보상 처리 완료 후:



flow.CompleteReward();

채점 완료 시에는 먼저:



flow.CompleteScoring();

14. 네트워크 개발자가 구현하지 않아도 되는 것

다음은 Framework 내부에 이미 구현되어 있다.



모드별 기본 규칙

2대2 7분 타이머 판정

협동 종료 동의 정책

팀 단위 항복 동의 정책

아이템 확률 선택

30초 아이템 스폰 주기

완성도 10% 보상 중복 방지

60초 아이템 만료 계산

아이템별 효과 선택

완성도 승패 및 무승부 판정

네트워크 개발자는 이 로직을 다시 작성하지 않고 서버 RPC, NetworkVariable/NetworkList, NetworkObject와 위 인터페이스를 연결하면 된다.