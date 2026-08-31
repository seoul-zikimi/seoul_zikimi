# 서울지키미 아키텍처 개요

새 스크립트를 만들기 전에 이 문서를 먼저 읽으세요. 어디에 두고, 누구와 어떻게 통신해야 하는지가 전부 여기 있습니다.
(성능 작업 이력은 PERF_TODO.md, 맵 저작은 Assets/Grid/AUTHORING_GUIDE.md 등 각 가이드 참고)

## 1. 어셈블리 지도 — 의존은 아래 방향으로만

```
Core                    ← 기반 인프라(Singleton<T>, IAssetProvider 등). 아무도 참조 안 함
  ↑
SeoulZikimi.Gameplay    ← 게임 모드·팀·경쟁 아이템·조기종료 '계약'(인터페이스 22종)과 순수 로직
  ↑          ↖
SeoulZikimi.Weather     ← 사계절·날씨·낮밤(인터페이스 9종, 계약 기반)
  ↑
GridSystem              ← 그리드·배치·채점·맵·기믹·넷코드(NGO). 게임의 중추
  ↑
Assembly-CSharp         ← Player, UI, Sound, Network 로비 등 asmdef 없는 전부
```

**규칙:**
- 위 화살표 역방향 참조 금지. 순환 참조는 현재 0건 — 유지할 것.
- 하위 어셈블리(GridSystem)가 상위(Assembly-CSharp)의 것을 써야 하면 **직접 참조 불가**. 허용된 우회는 두 가지뿐:
  - **리플렉션 브릿지**: `GridSoundBridge`(GridSystem → SoundManager). 새 브릿지를 늘리기 전에 정말 필요한지 재고.
  - **정적 창구**: `GridSystem.LocalPlayerHands`처럼 하위 어셈블리에 상태 슬롯을 두고 상위가 채워 넣는 방식.
- 새 시스템이 계약(인터페이스)을 가질 수 있으면 GameplayFramework/Weather 스타일로: 인터페이스 + 운영 구현 + 테스트용 Fake.
- **Grid도 계약 보유(2026-08-30)**: `GridInterfaces.cs` — `IGridState`(읽기+CellsChanged) / `IGridRequests`(클라 요청) / `IGridServerOps`(서버 권위) / `IPickupField`(픽업 필드). 새 소비자는 필드를 인터페이스 타입으로 선언(찾을 땐 구체 타입으로 GetComponent). 기존 소비자는 손대는 김에 점진 전환 — 일괄 치환 금지.

## 2. 통신 규약 — 서로를 찾는 방법 (우선순위 순)

1. **직렬화 참조**(SerializeField) — 같은 프리팹/씬 안에서는 이것.
2. **이벤트** — 발화자→다수 통지는 `event System.Action`(예: `GridNetwork.CellsChanged`, `MaterialDepot.Spawned`). 구독 해제를 OnNetworkDespawn/OnDisable에서 반드시.
3. **정적 Instance** — 씬에 정확히 1개인 시스템(`MapCatalog.Instance`, `WaterGateNetwork.Instance` 등, 현재 14종). `OnNetworkSpawn`에서 세팅했다면 스폰 전 접근이 null임을 항상 가정.
4. **Find류(FindFirstObjectByType 등)** — **초기화 1회 + null 캐시 패턴만 허용**:
   ```csharp
   if (m_Net == null) m_Net = FindFirstObjectByType<GridNetwork>();   // OK: 찾을 때까지만
   ```
   **Update/틱마다 캐시 없이 Find 금지.** `GameObject.Find("이름")`은 런타임 스폰 마커 추적(FindSpot 계열)에만, 반드시 시간 게이트와 함께.

## 3. 데이터 주도 규약 (ScriptableObject)

- 맵 1개 = `MapDef` 에셋(배경·정답·기믹 설정·브금). 맵 추가는 씬 추가가 아니라 **에셋+생성 툴**.
- 튜닝 값은 코드 상수 대신 Config SO(`DdpGimmickConfig` 등) — 기획자가 에셋에서 만짐.
- **⚠ 지연 로드 계약(iOS 생명줄)**: MapDef의 배경/완성체 프리팹은 반드시 `Assets/Resources/MapPrefabs/`에.
  에디터는 직접 참조로 돌지만 **빌드는 Resources 경로 문자열로만 로드**한다(로비 메모리 보호 — EXC_RESOURCE 이력).
  맵 생성 툴(DdpMapTool 등)의 출력 경로도 여기여야 한다. 위반은 `MapDefLazyLoadContractTests`가 잡는다.
- 새 에셋 커밋 시 **.meta 필수 동반**(GUID = 모든 참조의 신원. meta 누락 = 머신마다 참조 파손).

## 4. 넷코드 규약 (NGO 2.11)

- 서버 권위: 상태 변경은 서버만(`IsServer` 가드). 클라는 Rpc로 요청.
- 복제는 **전이 시점만**(NetworkVariable/NetworkList) — 매 틱 위치 복제 대신 페이즈+시작시각을 복제하고 각 클라가 결정론 보간(케이블카·물길 방식).
- NetworkList 이벤트 함정: `Clear`는 Value가 비고, `Full`은 별도 경로 — 미지 타입은 전체 Reconcile 폴백(MaterialDropField.OnChanged 참고).
- 맵 기믹은 `~GimmickBase` 템플릿(Namsan/Ddp/Gyeongbokgung/Lotte): GameLoopManager가 런타임 부착, MapDef에 Config 없으면 스스로 잠잔다. 새 맵 기믹은 이 틀을 따를 것.

## 5. 코드 컨벤션

- 네이밍: 인스턴스 `m_`, 정적 `s_`, 상수 `k`. 파일당 공개 클래스 1개.
- 주석은 **왜**를 쓴다 — 함정·사고 이력·제약을 남길 것("무엇"은 코드가 말한다). 이 프로젝트 주석의 존재 이유는 다음 사람이 같은 함정에 안 빠지게 하는 것.
- 공개 API에는 `/// <summary>` — 호출 가능 조건(서버 전용? 스폰 후?)을 명시.
- 핫패스(Update/틱)에서: LINQ·박싱·문자열 조립·`new` 컬렉션 금지, 버퍼 멤버 승격+NonAlloc(PERF_TODO 이력 참고).

## 6. 테스트 규약

- EditMode 테스트(현재 112케이스): **순수 로직**(좌표·풋프린트·붕괴·채점)과 **계약**(재료 프리팹 규격, MapDef 지연 로드 배선)을 커버.
- 새 순수 계산 함수는 `public static` + 테스트 동반(CarPosAt/ClampToFloorWorld 스타일 — "순수 계산 — 테스트 대상" 주석).
- 에셋 배선 실수가 런타임에야 터지는 유형이면 **계약 테스트로 승격**해 재발을 막을 것.
- 한계(알려진 공백): NGO 네트워크·입력 통합 테스트 없음 — 멀티 QA는 Multiplayer Play Mode 수동. PlayMode 테스트 도입은 별도 과제.

## 7. 새 스크립트 체크리스트

- [ ] 어느 어셈블리 소관인가? (역방향 참조가 필요해지면 설계를 다시)
- [ ] 이미 있는 시스템으로 되는가? (Singleton 14종·이벤트·창구·기믹 베이스 목록 먼저 확인)
- [ ] 통신은 §2 우선순위를 따랐는가? (Update 속 비캐시 Find 없음)
- [ ] 튜닝 값은 SO로 뺐는가? 에셋을 만들면 Resources 계약(§3)에 걸리는가?
- [ ] 구독 해제·정리(OnDisable/OnNetworkDespawn)를 짝 맞췄는가?
- [ ] 순수 로직이면 테스트를 붙였는가? 공개 API에 summary를 달았는가?
