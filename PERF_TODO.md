# Performance TODO

환경: Unity 6000.3.11f1 / NGO 2.11.2. 기능, 네트워크 권한, 비주얼 결과를 보존한다. 외부 에셋은 수정하지 않는다. 각 항목은 원인 확인 후 수정하고 컴파일/관련 테스트로 검증한다.
감사 이력: 2026-08-28~29 8차원 멀티에이전트 감사 + 발견별 적대적 검증 완료.

## 완료 — 1차 (2026-08-28)

- [x] **P0** `GridNetwork.cs` — 셀 이벤트마다 즉시 재구성(O(N²)) → 더티 플래그로 프레임당 1회.
- [x] **P1** `JobsnailLobbyCharacterStage.cs` — ReadPixels 동기 리드백 → Graphics.CopyTexture. *QA: 대기방 4슬롯.*
- [x] **P1** `PlayerMovement.cs` — 접지·벽 판정 NonAlloc + (frame,fixedTime) 캐시.
- [x] **P1** `MirrorReflection.cs` — 렌더러 재수집을 hierarchyCount 게이트로.
- [x] **P1** `WeatherGroundFx.cs` — 눈 자국 풀링.
- [x] **P2** TimeLeft 0.1초 격자 복제 / FacingYaw 0.5° 격자 / ItemNetwork sqrMagnitude / GameLoopHUD 타이머 게이트 / UiNewLobby 스로틀 / Vefects 데모 Resources 이동(-33.7MiB) / PlayerCarry 조준 NonAlloc.

## 완료 — 2차 (2026-08-29)

- [x] **P0** `GridNetwork.cs:720` — RebuildVisuals를 **owner 단위 스냅샷 diff 증분 재구성**으로 교체: 바뀐 블록만 생성·파괴, 공정 마스크 변경은 색·마커·~Solid 콜라이더만 재평가, 완성체 통짜 전환·늦참은 전체 재구성 폴백, 치트 완성의 owner id 재사용은 시그니처 비교로 감지. *QA: 배치/철거/공정/붕괴/리셋/2vs2/DDP 완성 전환.*
- [x] **P0** `AnswerPreview.cs` — 미니씬 카메라(512² 상시 렌더)를 폰 패널이 실제로 보일 때만 켬(`PanelOpen`, AnswerPanelHUD 접기/펴기 연동) + 정산 중 자동 off.
- [x] **P1** `AnswerPreview.cs:114` — 고스트 재도색을 알파 0.005 양자화+hlId 게이트로(초당 60→~22회), SetActive 상태 캐시, 통짜 맵의 숨은 조각 머티리얼 스킵.
- [x] **P1** `GridFootprint.cs` — 비할당 오버로드(min-corner 해석적 계산) + PlayerCarry 프리뷰·사거리·배치 경로 버퍼 재사용, 앵커=min-corner 불변식으로 min 루프 제거.
- [x] **P1** `GridNetwork.cs:848` — VisualAt을 셀→비주얼 Dictionary O(1) 조회로(완성체 경로 포함).
- [x] **P1** `GridNetwork.cs:34` — 셀 조회 API·서버 아이템 루프 foreach→인덱스 for(박싱 제거).
- [x] **P1** `PlayerSplat.cs` — Grounded()를 PlayerMovement.IsGrounded() 캐시에 위임(+NonAlloc 폴백).
- [x] **P1** `GridSupport.cs` — ExternalSolidAt OverlapBoxNonAlloc.
- [x] **P1** `JuicyText.cs` — 빈 텍스트 조기 탈출(정산 등급 텍스트가 빈 채로 매 프레임 풀 리빌드하던 것).
- [x] **P1** `GameLoopHUD.cs` UpdateResultPanel — 표시값(점수·유물·경과·승패·이름 수) 변화 프레임에만 문자열 조립·스프라이트 Load(점수 늦복제도 값 변화로 자동 갱신).
- [x] **P2** `AnswerPanelHUD.cs` SetCompletion 조기 리턴 / `GameLoopHUD.cs` EndRequestButton 캐시+상태 키, BuffBar 초 게이트 / `PlayerCarry.cs`·`MobileControlsHUD.cs` Scene.name 캐시(+회전 힌트 사전 생성) / `PlayerDustTrail.cs`·`PlayerUnit.cs` 커스텀 트레일 hierarchyCount 캐시+상태 게이트 / `GustNetwork.cs` NonAlloc / `PickupBody.cs` 안착 후 위치 쓰기 생략 / `ItemNetwork.cs` 보유 캐시(더티 플래그)+for 치환 / `CompetitiveItemSpawnDirector.cs` 틱 버퍼 재사용.

## 완료 — 3차 (2026-08-29)

- [x] ~~**P1** `PlayerCarry.cs` — 망치/페인트 CFXR FX 풀링~~ **원복(2026-08-29)**: 2회 시도(① clearBehavior=Disable+루트 Play(true) ② None+전 시스템 개별 Stop(Clear)+Play, PlayerBounce 규약) 모두 실기 QA에서 스파크 미표시 — ①은 재사용부터, ②는 전면. 예외·로그 없음, 정적 분석으로 원인 미확정. Instantiate+Destroy 원상 복구(WaitForSeconds 캐시만 유지). **재도전 시 에디터에서 직접 재생 확인하며 진행할 것.**
- [x] **P1** `GridJuice.cs` — 코드 파티클(FX당 5~20개 CreatePrimitive) Stack 풀(≤64). SetActive 재사용은 Start() 미재실행 → Reinit로 필드·타이머·MPB 명시 리셋, MakeBit의 sharedMaterial 재할당 유지(ItemFx 스파크 가산재질 오염 방지), Pop 시 Unity-null 체크. *QA: 배치/붕괴/아이템 FX.*
- [x] **P1** `CableCarNetwork.cs` — 2초마다 씬 전체 Transform 순회 2회 → 마커·철탑(+렌더러) 캐시 후 위치만 재샘플. 씬 hierarchyCount 합 변화·캐시 파괴·캐시 없음일 때만 풀 재스캔. 경유점·루트 목록 스크래치 멤버 승격. *QA: 남산 주문→배송, 마커 이동 반영.*
- [x] **P1** `WaterGateNetwork.cs`→`MaterialDropField.cs` — 물길 급송(0.2초 픽업당 Value 이벤트)마다 전체 Reconcile 돌던 것을 이벤트 기반으로: Value=해당 픽업만 SetTarget, Add/Insert=해당 비주얼 생성, Remove/RemoveAt=Value 페이로드로 해당 비주얼 파괴(GameObject째), Clear=전부 제거(Value 비어 있음), 미지 타입=풀 Reconcile 폴백. Reconcile 스크래치(HashSet/List) 멤버 승격. kTick 0.2→0.5는 Value 경로가 싸져 불필요해짐. *QA: DDP 물길 재료 급송·킥·줍기·늦참.*
- [x] **P2** `ElevatorNetwork.cs` — GridNetwork에 `CellsChanged` 이벤트 추가(LateUpdate 더티 flush 프레임에 1회 발화) 후 0.5초 폴링에 더티 게이트(스폰 직후 1회는 무조건 판정). *QA: 남산 전망대 완성→개통.*

## 완료 — 4차 (2026-08-29 밤)

- [x] **P1** `GameHudDriver.cs` — 1초 버튼 전수 스윕(FindObjectsByType 씬 전체) → 요청 기반 + 10초 안전망. UIManager의 HUD·팝업·시스템 팝업 인스턴스화 시 RequestJuicySweep() 자동 호출(생성처 대부분은 이미 자체 Attach — 스윕은 빠뜨린 경로 자가 치유용). *QA: 새 팝업·주문 카드 버튼 호버 쫀득 확인.*
- [x] **P2** 미검증 3건 검증·수정: `CameraObstructionFader` SetColor 문자열 → PropertyToID 캐시(페이드 중 매 프레임 경로) / `GridSoundBridge` 호출당 Enum.Parse+object[] 할당 → 값 캐시+인자 버퍼 재사용(효과음 연타 핫패스) / `ItemFx.CannonShot` 발사당 Material 누수 확인 → 공유 재질 1개로. (BuffBar는 2차에 처리됨)
- [x] **P1** `Weather3DVfxRig.cs` — maxParticles 절대 캡(시스템당 1000). 종전 느린 입자 수명 연장 경로가 rate×수명×1.35 재계산으로 대형 맵 눈을 이론상 1.6만 입자까지 키우던 것. 캡 초과분은 방출도 함께 줄이고 입자 크기 √보상(상한 1.8×)으로 체감 밀도 유지. **날씨 기획서(07/24) 확인** — 밀도 요구는 정성적(거세게/조금씩)이라 개수 스펙 없음. *QA: 겨울 눈·여름 태풍 맵 체감 밀도.*
- [x] **P2** `ZoneFogFx.cs` — 안개 구름 다이어트(정상상태 ~113→~56, 크기·알파 보상). 이 연출은 '남이 보는 걸림 표시' 전용(당한 팀 시야 차단은 TeamWeatherFx 카메라 포그 담당)이라 **아이템 게임플레이 효과 불변**. *QA: 2vs2 안개 아이템 시전 — 상대 구역 덮임 체감.*

## 남은 항목 — 코드

- `Assets/UI/Scripts/UIManager.cs:32` (P1·설계) 인게임 HUD 전부 단일 Canvas — 동적 서브트리(타이머·버프바·로딩바)에 중첩 Canvas로 더티 격리. 레이아웃 검증 필요.
- `Assets/Grid/Scripts/PickupBody.cs` (P3) 숨쉬기 스케일을 '~Vis' 자식 래퍼로 옮겨 콜라이더 루트 불변화(스케일 리베이크 제거).
- `Assets/Resources/Voices/Emotes` (P3) 보이스 mp3 33종 loadType DecompressOnLoad→CompressedInMemory 검토 — 전부 로드돼도 ~수 MB라 급하지 않음(지연 로드+캐시 설계는 이미 양호). 신규 브금 4종은 Streaming+프리로드 끔으로 이미 최적.

## 남은 항목 — 프로젝트 설정 (에디터에서 변경 권장)

- `accelerometerFrequency: 60` → 미사용이면 0. / `androidUseSwappy: 0` → 켜기. / `metalAPIValidation: 1` → 프로파일링 시 끄기.
- `il2cppCompilerConfiguration` Release → 스토어 빌드 Master 검토. / `StripUnusedMeshComponents: 0` → 켜기(QA 필요).
- `Maximum Allowed Timestep 0.333` → 0.1 내외 검토. / 밉맵 스트리밍 비활성 → 별도 작업. / `Resources/Fonts` 45MB 정리.
- PPv2 패키지(com.unity.postprocessing) 미사용 잔존 — Feel 호환 컴파일 확인 후 제거 후보.
- ~~Bloom HQ Filtering~~ 기각(빌드 씬 프로파일은 꺼져 있음).
