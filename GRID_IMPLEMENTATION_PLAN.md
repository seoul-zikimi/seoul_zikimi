# 그리드 시스템 — 단계별(잘게 쪼갠) 구현 계획

## Context (왜 이 작업인가)

협동 건축 게임("사라진 서울 명소 복구")의 핵심은 **그리드**다. 브리프 §2가 명시하듯 그리드는 성격이 다른 **두 개**가 있고, 이 둘이 **같은 셀 좌표·재료 ID·회전 규약**을 공유해야 채점(종료 시 1:1 비교)이 성립한다:

- **(A) 오서링 그리드** — 디자이너가 **Autotiles3D**(에디터 전용)로 정답 건물 배치 → 자체 익스포터 → `MapAnswerData`(SO).
- **(B) 런타임 그리드** — 플레이어가 플레이 중 재료 배치 + **공정(고정/페인트) 수행/취소** → `RuntimeGrid`(서버 권위, NGO 동기화).

목표: 한꺼번에 구현하지 않고 **아래에서 위로**(데이터 → 순수 로직 → 로컬 씬 → 네트워크) 잘게 쪼개, 각 단계가 **독립적으로 테스트 가능**하게. 최종(G0~G4): **두 플레이어가 협동해 정답을 똑같이 짓고 필요한 공정까지 마치면 서버 채점이 100%**(= A↔B 통합 + 공정 채점 증명).

현 상태: 그리드/재료/맵/공정 코드 **전무**(그린필드). Autotiles3D는 `Assets/ThirdParty/Autotiles3D`. 플레이어/NGO/사운드 컨벤션은 확립됨.

## 검증으로 확정한 정합성 계약 (가장 중요)

- `Autotiles3D_Grid.ToWorldPoint(p) = transform.TransformPoint(p)` — **`Unit` 안 곱함** (`Autotiles3D_Grid.cs:84`). `Unit`은 에디터 핸들 렌더링 전용.
  - **잠금:** 오서링 `Autotiles3D_Grid`를 **identity transform + `Unit=1` + Finite**로. 셀 `(x,y,z)`→world `(x,y,z)`. 런타임 `CellToWorld(c)=Origin(0)+(Vector3)c`도 동일 → **(A)/(B) 좌표 1:1 구조적 정합**.
- 좌표계: Y=수직(`Height`), X/Z=평면(`Width`²). `IsExceedingLevelGrid` 경계 (`Autotiles3D_Grid.cs:49`). 우리도 `Vector3Int`, Y-up.
- Autotiles3D 노드는 **1셀 단위**(멀티칸 타일 타입 없음). **멀티칸 오브젝트 = 앵커 1노드(프리팹 메시가 여러 칸 span)** + 익스포터가 footprint 확장 → 정답 맵은 **셀 단위 저장**.
- `InternalNode`: `InternalPosition:Vector3Int`, `LocalRotation:Quaternion`, `TileID:int`. 열거 `layer.GetAllInternalNodes()` (`Autotiles3D_TileLayer.cs:110-117`).
- Dictionary 직렬화(병렬 `List`+`ISerializationCallbackReceiver`)가 `Autotiles3D_TileLayer.cs:154-180`·`SoundLibrarySO`에 존재 → `MapAnswerData`/`MaterialCatalog`에 차용.

## 잠금된 설계 결정 (사용자 확인 완료)

1. **범위**: G0~G4 끝까지(NGO 서버권위), **공정 시스템(적용/취소/상태저장/채점)을 코어에 포함**.
2. **테스트**: EditMode 유닛 테스트 — 순수 로직(좌표/footprint/배치/**공정**/채점) NUnit 자동검증. **테스트 전용 asmdef 1개** 추가(런타임 어셈블리 불변).
3. **오서링**: **앵커+footprint 확장** — 오브젝트당 타일 1개(앵커=min-corner)를 배치, 익스포터가 `MaterialDef.footprint`로 점유 칸을 **셀 단위** `MapAnswerData`로 확장. 이 확장은 런타임 배치와 **동일한 `EnumerateFootprintCells` 단일 경로** 사용 → A/B 일치 구조적 보장. (정답 데이터 포맷은 여전히 셀 단위 — 1×1×1도 footprint (1,1,1)이라 자연 포함.)
4. **셀 크기**: `Unit=1` 월드 유닛.
5. **앵커**: footprint = min-corner. 런타임 배치에서만 사용.
6. **회전**: `rotationStep` 0~3(0/90/180/270° Y축). 익스포터 `Quaternion→step`, 런타임 `step→Quaternion`.
7. **네트워크**: `RuntimeGrid`=순수 C#(EditMode 테스트). `GridManager`(싱글)·`GridNetwork`(멀티)는 얇은 래퍼. 상태는 `NetworkList<CellEntry>`(서버 write, 늦참 자동복제). 배치/공정 **비주얼은 각 클라가 상태로부터 로컬 재구성**(개별 NetworkObject 스폰 X — 브리프 "상태만 브로드캐스트"와 일치).
8. **공정**: 일부 블록만, **순차**(놓기→고정→페인트), `MaterialDef.requiredProcesses`(순서) 정의. 완료 상태는 **`[Flags] ProcessType` 비트마스크(int)** 로 셀마다 저장(네트워크 친화 + 채점용). **취소 지원**.
9. **공정 취소 패턴**: 스테이지가 선형이므로 **순서 스테이지 진행 + 마지막단계 취소**(State 패턴 구조, `completedMask` 비트연산)로 충분. 다단 undo가 필요해지면 Command 패턴으로 승격.
10. **상호작용 = 도구 방식 확정**: 작업장 도구를 들고 해당 공정 수행(망치=고정, 페인트통=페인트). **그리드측 공정 API(`TryApplyProcess/TryCancelProcess`)는 코어**, 도구 들기/작업장 메커니즘은 **상호작용 레이어(후속)** — G3에선 디버그 입력으로 공정 트리거(배치 디버그 입력과 동일 방식).
11. **채점**: 셀별 (배치 정확 +200) + (요구 공정 완료 +100). 요구 공정은 `MaterialDef`에서 파생(정답 맵엔 미저장). 조기종료 보너스는 게임플레이 레이어(후속).

## 코드 구조 / 컨벤션

- 네임스페이스 `Grid`. 폴더 `Assets/Grid/{Scripts, Scripts/Editor, Data, Prefabs, Scenes, Tests/EditMode}`.
- 런타임→`Assembly-CSharp`, 에디터→`Scripts/Editor`(`Assembly-CSharp-Editor`), 테스트→전용 asmdef.
- 재사용 패턴: SO+`Entry[]`→Dictionary (`SoundLibrarySO`+`SoundManager.BuildMaps`); 매니저 `Core/Scripts/PersistentSingleton.cs`(씬 한정이면 GameScene 배치); NGO 서버권위 입력/복제 `PlayerUnit.cs:22-25,100-106`; 에디터 자동화 `PlayerSetupEditor.cs`; 노드 순회 레퍼런스 `Autotiles3D_TileLayerInspector.cs:150-194`.

---

## Phase G-1 — 하네스 & 스켈레톤 (선결)

- **G-1.1 모듈 스켈레톤.** `Assets/Grid/...` 폴더 + `Grid` 네임스페이스 placeholder. *DoD:* 컴파일.
- **G-1.2 테스트 asmdef.** `Tests/EditMode/Grid.Tests.EditMode.asmdef`(refs `UnityEngine/UnityEditor.TestRunner`, `nunit.framework`; Editor; Test Assemblies on) + 더미 green 테스트. *DoD:* Test Runner에 뜨고 통과. **이후 EditMode DoD 전제.**

## Phase G0 — 데이터 기반 (공정 포함)

- **G0.1 규약.** `GridContract.cs`(`Unit=1`, `Origin=0`, `Height`, `BoundsXZ`, Y-up). *DoD:* 상수 검증 테스트.
- **G0.2 공정 열거 + MaterialDef.** `ProcessType.cs` = `[Flags] enum:int {None=0, Fixed=1, Painted=2, …}`; 정규 순서 배열 정의(Fixed→Painted). `MaterialId.cs`. `MaterialDef.cs : SO [CreateAssetMenu("Grid/MaterialDef")]` — `id, footprint:Vector3Int, prefab, requiredProcesses:List<ProcessType>(순서), requiredMask(파생), mustBeFixed, isBreakable, maxSpawnCount`. *DoD:* 1x1x1(공정無), 기둥 1x1x3(Fixed), 벽 1x3x2(Fixed+Painted) 에셋 생성.
- **G0.3 MaterialCatalog.** `MaterialDef[]` + `[Serializable] TileIdMap{int autotilesTileId; int materialId;}[]`, `OnEnable` dict 재구성. `GetById`, `TileIdToMaterialId`. *DoD:* 조회 정상, 미매핑→sentinel.
- **G0.4 CellState + MapAnswerData.** `CellState`(occupied, materialId, rotationStep, **completedProcessMask:int**, ownerObjectId). `MapAnswerData : SO`(gridSize, `AnswerCell[] cells` 직렬화, `[NonSerialized] Dictionary` 재구성, startPilePosition, timeLimitSeconds, answerImage). `AnswerCell{Vector3Int cell; int materialId; byte rotationStep;}`(공정 미저장). *DoD:* 라운드트립 테스트.
- **G0.5a/b/c Footprint·좌표(순수).** `GridFootprint.RotateXZ`/`EnumerateFootprintCells`(min-corner 재정규화), `GridCoordinates.CellToWorld/WorldToCell`. **`EnumerateFootprintCells`는 익스포터(G1.3)와 런타임 배치(G2.2)가 공유하는 단일 경로** — A/B 점유칸 계산 일원화. *DoD:* 회전 4스텝 명시집합·개수·unique, 좌표 라운드트립.

## Phase G1 — 오서링 + 익스포터 (에디터 전용) → **V0(아키텍처 증명)**

- **G1.1 오서링 씬+그리드.** `Scenes/AnswerAuthoring.unity`, `Autotiles3D_Grid`(identity/`Unit=1`/Finite) + TileGroup(=MaterialDef 프리팹). **오브젝트당 앵커 타일 1개 배치**(멀티칸은 메시가 여러 칸 span; 앵커=min-corner). *DoD:* 씬·블록 보임.
- **G1.2 Tile↔Material 매핑.** 카탈로그 `TileIdMap`. *DoD:* 모든 TileID 해석.
- **G1.3 익스포터 코어.** `Scripts/Editor/MapExporter.cs` `[MenuItem("Grid Setup/Export Answer")]` — 각 앵커 노드 → `TileId→MaterialId→MaterialDef`(footprint 조회) + `step=QuatToStep(LocalRotation)`(`RoundToInt(euler.y/90)&3`) → **`EnumerateFootprintCells(InternalPosition, footprint, step)`로 점유 칸 확장** → 칸마다 `AnswerCell{cell, materialId, rotationStep}` write → `MapAnswerData`. (카탈로그 매핑 G1.2가 선행 — footprint 조회에 필요.) *DoD:* 1×1×1·1x3x2 둘 다 정확한 셀 집합으로 익스포트, 값 일치.
- **G1.4 견고화.** 경계검사, 미매핑 경고(스킵), off-axis/tilt 경고, gridSize/startPile/timeLimit/answerImage. *DoD:* 미매핑 경고+나머지 정상.
- **G1.5 정합성 검증 (V0 게이트).** 셀마다 `CellToWorld≈ToWorldPoint` & 회전 라운드트립(±1°) assert(`Grid Setup/Verify Alignment`). *DoD:* G1.1 씬 통과 → **V0 완료.**

## Phase G2 — 런타임 그리드 로직 (순수 C#)

- **G2.1 컨테이너.** `RuntimeGrid.cs` — `Dictionary<Vector3Int,CellState>` + `GetCell/IsInBounds/IsOccupied`. *DoD:* 빈 그리드 테스트.
- **G2.2 배치/제거.** 공유 `EnumerateFootprintCells`. `CanPlace`(전 셀 in-bounds+empty), `Place`(전 셀 write, 공유 ownerObjectId), `Remove`(전 셀 clear). *DoD:* 1x3x2 step0/1 점유, 겹침/부분OOB 거부, remove 정확, 중복 거부.
- **G2.3 공정 적용/취소(순수).** `TryApplyProcess(anchor, ProcessType)`: 해당 재료의 required이고, 정규 순서상 앞 공정 모두 완료, 미완료일 때만 → `completedProcessMask` set(footprint 전 셀 동기). `TryCancelProcess(anchor, ProcessType)`: 완료됐고 뒤 공정이 남아있지 않을 때만 clear(역순). *DoD:* 테스트 — 페인트가 고정 전 거부, 순차 적용, 취소 역순, 공정無 재료에 적용 거부.
- **G2.4a 채점(배치).** `ScoreAgainst` 배치 일치 +200/셀. *DoD:* 동일→배치만점, 빈 그리드→0.
- **G2.4b 채점(공정).** 배치 일치 셀에서 `(completedMask & requiredMask)==requiredMask`면 +100/셀. *DoD:* 정답배치+공정완료→만점; 고정 누락→공정점 미부여. **점수 가중치/오회전·잉여 셀 정책은 여기서 product 확정** 후 테스트 고정.

## Phase G3 — 로컬(싱글) 배치+공정 → **V1**

- **G3.1 GridManager+기즈모.** GameScene 컴포넌트, RuntimeGrid+config+catalog, `OnDrawGizmos`=`CellToWorld`. *DoD:* identity Autotiles3D 그리드와 기즈모 일치.
- **G3.2 셀 타게팅+하이라이트.** 레이캐스트→`WorldToCell`→하이라이트. *DoD:* 셀 단위 이동, 기즈모 일치.
- **G3.3 디버그 배치 입력(임시).** 숫자키 MaterialDef 선택+클릭→`Place`→`CellToWorld`에 `step→Quaternion` Instantiate, 회전키, 제거키. *DoD:* 1x3x2 배치/회전/제거, footprint 하이라이트, 겹침 거부 피드백.
- **G3.4 디버그 공정 입력(임시) + 비주얼 상태.** 키로 대상 셀에 `TryApplyProcess`/`TryCancelProcess`(나중에 도구 들기로 교체). 셀 비주얼이 상태(놓임/고정/페인트) 반영(색/머티리얼/아이콘). *DoD:* 공정無 블록은 적용 안 됨, 순차 적용/취소가 비주얼에 반영.
- **G3.5 정답 로드+라이브 채점 (V1 게이트).** `MapAnswerData` 로드, 화면 점수(배치+공정), (옵션) 고스트 오버레이. *DoD:* 정답 재구성+공정완료→100% → **V1 완료(싱글 end-to-end).**

## Phase G4 — 네트워크 배치+공정 (NGO 서버권위) → **V2**

- **G4.1 GridNetwork+요청 RPC.** `GridNetwork : NetworkBehaviour`, 서버 권위 `RuntimeGrid`. `[Rpc(SendTo.Server)]` 배치/제거/**공정적용/공정취소** 요청. 서버가 `CanPlace`/`TryApply…`로 검증(비주얼 없음). *DoD:* Multiplayer Play Mode에서 RPC 도달, accept/reject 정확, 치트(OOB/겹침/순서위반) 서버 거부.
- **G4.2 복제 상태+로컬 비주얼.** `CellEntry`(`INetworkSerializable`+`IEquatable`, `{Vector3Int cell; int materialId; byte rotationStep; int completedProcessMask; ulong ownerObjectId;}`). 서버 accept→`NetworkList<CellEntry>` 변경. 모든 클라 `OnListChanged`→로컬 비주얼 생성/갱신/파괴(공정 상태 비주얼 포함). NetworkList는 필드 이니셜라이저(PlayerUnit 패턴). *DoD:* A의 배치+고정이 B/host에 동일 셀/회전/공정상태로 표시.
- **G4.3 제거/취소 + 동일 셀 충돌.** NGO 서버 RPC 순서화 → 먼저 도착 승리. 제거/공정취소 전파. *DoD:* 동시 동일 셀 배치→정확히 1개; 공정취소 전파.
- **G4.4 늦참 전체 동기화.** 배치/공정 후 3번째 클라 입장→`NetworkList` 전체 복제→비주얼 재구성. *DoD:* 늦참자가 완성 그리드+공정상태 정확히 봄.
- **G4.5 서버 채점 (V2 게이트).** 서버 `ScoreAgainst`(배치+공정)→`NetworkVariable<float>`(서버 write/everyone read). *DoD:* 두 클라 협동→정답+공정 완성→모든 화면 100% → **V2 완료(멀티 end-to-end).**

## Phase G5 — 이후(이 계획 밖, 표시만)

- **도구/작업장 상호작용**(망치/페인트통 들기·작업장 배치 — 결정된 도구 방식의 실제 메커니즘; G3 디버그 공정 입력을 대체, 같은 그리드 공정 API 호출).
- **무너짐 규칙 연쇄** — 브리프대로 규칙기반 결정론(물리엔진 X). 단, 기획자 희망 **무빙아웃 "노답중력" 느낌**은 떨어지는 연출/애니메이션(상태 브로드캐스트 후 클라 로컬 연출)로 구현. *物理 도입은 브리프 핵심 제약과 충돌 → 별도 결정 필요(플래그).*
- 오브젝트 풀링(로컬 비주얼), SO 이벤트 채널(BaseEventSO)→MVP UI, 재료 스폰, 게임 루프/타이머/조기종료 보너스.

## 아트/연출 메모 (그리드 좌표 영향 없음)

- **PPU 128 / 로우폴리·카툰** — 텍스처/아트 디렉션. 그리드 좌표(셀=1 월드유닛)·로직 불변. 재료 프리팹이 이 톤을 따름.
- **무빙아웃 "노답중력" 느낌** — 붕괴/낙하 연출 목표. 그리드 배치/공정 코어와 무관, G5 붕괴 레이어에서 연출로.

## 커스텀 블록 추가 (NC VARCO) — 코드 변경 없음

데이터 주도 설계라 디자이너가 새 블록을 **코드 수정 없이** 추가한다. footprint(점유 칸)와 비주얼 메시는 분리(브리프 §3.2) — 어떤 모양이든 정수 footprint를 선언만 하면 됨.

**추가 절차:** ① NC VARCO 메시 → 프리팹화(비주얼+콜라이더) → ② `MaterialDef` 에셋 생성(`prefab`, `footprint`, `requiredProcesses`, `mustBeFixed`, `isBreakable`, `maxSpawnCount`) → ③ Autotiles3D `TileGroup`에 프리팹을 Tile로 등록 + `MaterialCatalog`에 `TileId↔MaterialId` 한 줄 추가. 끝.

**프리팹 규약(필수):** 셀=1유닛에 맞는 스케일; **피벗/원점 = footprint의 min-corner**(멀티칸 메시가 +X/+Y/+Z로 span, 회전·footprint 확장 정합); 플레이어 충돌용 콜라이더(메시 형태에 맞춤); PPU 128 카툰 톤. → `GridSetupEditor`에 "블록 등록" 헬퍼 메뉴를 두어 ②③을 반자동화(PlayerSetupEditor 스타일).

**1×1×1 모듈 큐브 + 통짜 멀티칸 메시(기둥/벽/아치) 모두 지원** — 둘 다 `footprint`만 다를 뿐 동일 경로.

## 생성할 파일 (네임스페이스 `Grid`)

런타임(`Scripts/`): `GridContract, GridCoordinates, GridFootprint, ProcessType, MaterialId, MaterialDef, MaterialCatalog, CellState, MapAnswerData, RuntimeGrid, GridManager, GridNetwork, CellEntry`.
에디터(`Scripts/Editor/`): `MapExporter`(QuatToStep+Verify Alignment), `GridSetupEditor`(스캐폴딩, PlayerSetupEditor 스타일).
테스트(`Tests/EditMode/`): asmdef + `CoordinatesTests, FootprintTests, RotationStepTests, RuntimeGridTests, ProcessTests, ScoreTests, CatalogTests, AlignmentTests`.
에셋: `Data/*.asset`(MaterialDefs, Catalog, MapAnswerData), `Scenes/AnswerAuthoring.unity`, `Prefabs/`.

## 검증 (end-to-end)

- **EditMode 유닛 테스트**: 좌표 라운드트립, footprint 4스텝, 배치/겹침/OOB/remove, **공정 순차 적용/취소**, 채점(배치+공정), 카탈로그, **정합성**(`CellToWorld≈ToWorldPoint`).
- **V0**(G1.5): 오서링 익스포트 후 Verify Alignment 통과 = A↔B 통합 증명.
- **V1**(G3.5): GameScene 싱글로 정답 재구성+공정 완료 → 라이브 채점 100%.
- **V2**(G4.5): Multiplayer Play Mode(2.0.2) host+client 2~3인 협동 → 정답+공정 완성 → 서버 채점 100% 전 화면 동기.
- 각 마이크로 스텝은 DoD 개별 통과 후 진행(테스트 후 단계 진입).

## 남은 product 결정 (그리드 비차단)

- G2.4b 채점 가중치/정책(오회전·잉여 셀 처리, 공정 부분점).
- G5 붕괴의 "노답중력": 연출 vs 실제 물리(브리프 제약과 충돌 — 후속 결정).
- (비차단) 재료 스폰 방식, 도구/작업장 구체 메커니즘, 조기종료 보너스.

## 실행 방식 (점진적 루프)

1. 이 계획을 레포에 **`GRID_IMPLEMENTATION_PLAN.md`**(브리프 옆)로 저장 — 진실 소스.
2. 이후 **페이즈 단위로 반복**: ① 해당 페이즈의 **상세 계획**(파일/클래스 시그니처·DoD·테스트 케이스 구체화) 제시 → ② 그 페이즈 **구현 + 테스트 통과 확인** → ③ 사용자 확인 후 다음 페이즈. 절대 한꺼번에 구현하지 않음.
3. 순서: G-1 → G0 → G1(V0) → G2 → G3(V1) → G4(V2). 각 페이즈 DoD/게이트 통과가 다음 진입 조건.
4. 첫 작업: 레포에 plan 저장 → **G-1 상세계획**부터.
