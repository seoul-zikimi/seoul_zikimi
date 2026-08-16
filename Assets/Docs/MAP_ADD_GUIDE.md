# 맵 추가 가이드

새 맵을 게임에 넣는 방법 정리. "맵"은 두 층위가 있어요:

- **A. 정답 구조물** — 플레이어가 따라 짓는 목표 건물 (`MapAnswerData` 에셋). **대부분 이것만 하면 됨.**
- **B. 환경(배경) 맵** — 광통교처럼 주변 배경 자체를 새로 만드는 것.

---

## A. 정답 구조물 추가 (기본 — 10분)

> 상세 단계는 [Grid/AUTHORING_GUIDE.md](Grid/AUTHORING_GUIDE.md) §4~5 참고. 여기는 요약 + 주의사항.

1. **AnswerAuthoring.unity** 씬 열기 (`Assets/Grid/Scenes`)
2. Autotiles3D로 정답 모양 칠하기 (블록 종류 = 등록된 MaterialDef 팔레트)
3. 메뉴 **Grid Setup → Export Answer from Autotiles3D**
   → `Assets/Grid/Data/ExportedAnswer.asset` 생성됨
4. 생성된 에셋을 **복제 후 이름 변경** (예: `Answer_Cheomseongdae.asset`)
   ⚠️ ExportedAnswer를 그대로 쓰면 다음 익스포트 때 덮어써짐
5. 에셋 인스펙터에서 **Display Name** 입력 (예: "경주 첨성대")
6. **GameScene**의 `GridManager` → **Answers** 목록에 드래그
   - 여러 개면 **라운드마다 서버가 랜덤 선택**
7. 플레이 테스트: TAB 정답 미리보기 + 완성까지 한 판

### ⚠️ Display Name = 저장 기록의 키

맵별 최고기록(Easy Save)이 `best_{DisplayName}_{인원수}p` 키로 저장됨.
- **한 번 배포한 뒤 Display Name을 바꾸면 기존 기록과 분리됨** — 신중히 정하기
- 정답이 다르면 Display Name도 다르게 (같으면 기록이 섞임)

### 블록(재료) 종류가 새로 필요하면

[AUTHORING_GUIDE.md](Grid/AUTHORING_GUIDE.md) §3 — MaterialDef 만들기 + 팔레트 등록 + 프리팹은
`MaterialBoxPrefabGenerator` 사용 (min-corner 피벗 규칙 — 수동으로 만들면 반 칸 어긋남).

---

## B. 환경(배경) 맵 추가 — 씬 안 늘림 (확정 구조)

**결정: 맵마다 씬을 만들지 않는다.** GameScene 하나 유지, 배경만 맵별 프리팹으로 스왑.
(씬 분리는 시스템 오브젝트가 씬마다 복사돼 수정 때마다 전 씬 동기화 지옥 + NGO 씬 전환 코드가 필요.
씬 분리가 정당한 유일한 경우 = 맵별 라이트맵 **베이크**가 꼭 필요할 때인데, 지금은 실시간 라이팅이라 해당 없음.)

### 구조

```
MapDef (에셋, 맵 1개당 1개)      ← Assets/Map/Maps/Map_이름.asset
├─ Display Name  (로비 표시)
├─ Background Prefab (배경 통째)  ← Assets/Map/Prefabs/MapBg_이름.prefab
├─ Answers (이 맵 전용 정답들)    ← 아직 미연결(맵 선택 기능 때 활성화)
└─ Thumbnail (로비 맵 이미지)

MapCatalog (전체 맵 목록)        ← Assets/Resources/MapCatalog.asset (인덱스 = 네트워크 동기화 값)
GameLoopManager.MapIndex          ← 서버가 정하고 전 클라 복제
MapLoader (@MapLoader, GameScene) ← MapIndex 보고 배경 프리팹 스폰
```

### 새 맵 추가 순서

1. 배경 모델을 `Assets/Map/02_새맵이름/`에 임포트
   ⚠️ **폴리곤 확인** — 광통교 GLB 개당 ~50만 트라이앵글로 2fps 사태 전적.
   **Tools ▸ Map ▸ Mesh Decimator**로 감축 (개당 5만 이하 목표)
2. GameScene(또는 빈 임시 씬)에 배경 조립 — 루트 오브젝트 이름 `Background`
3. 루트 선택 → **Tools ▸ Map ▸ Extract Background To Map** 실행
   → 프리팹 저장 + MapDef 생성 + 카탈로그 등록 + 씬에서 배경 제거(런타임 스폰으로 전환)까지 자동
4. 생성된 `Map_이름.asset` 인스펙터에서 Display Name·썸네일 채우기
5. 씬 저장 + 플레이 — MapLoader가 배경을 스폰하면 성공
6. **로비 맵 선택은 구현돼 있음**: 방장이 ◀▶로 선택(맵 2개 이상일 때 표시)
   → `LobbyRoomNet.m_MapIndex`(NetworkVariable)로 방 전원 동기화
   → `GameLoopManager.HostSelectedMap` 경유로 게임 씬에 전달
   → MapDef.Answers가 채워져 있으면 그 맵 전용 정답 세트로 교체(GridManager.SetAnswers)

> 기획자용 요약본: [MAP_ADD_GUIDE_기획자용.md](MAP_ADD_GUIDE_기획자용.md)

### 배경 제작 체크리스트

- [ ] 그리드 볼륨과 겹치지 않게 (건축 공간 확보)
- [ ] 바닥 콜라이더 (낙하 방지) — 지형 솔리드는 블록 지지대로도 인정됨(외부 지지 시스템)
- [ ] 스케일 음수 금지 (BoxCollider 경고 — whiteSTairs 전례)
- [ ] 시야 가림 페이드는 자동 (MapFadeCollider가 콜라이더 자동 추가)
- [ ] 하늘 = FastSky(StylisedSky), 물 = Toon Water — [Docs/Water](Water/) 참고

---

## 완료 체크리스트 (공통)

- [ ] 혼자 한 판 완주 — 배치·공정·채점 정상
- [ ] TAB 정답 미리보기 정상
- [ ] 정산서에 Display Name 표시 확인
- [ ] 정산서 코인·기록 저장 확인 (재시작 후 유지)
- [ ] 멀티(가상 플레이어)로 동기화 확인
- [ ] Stats로 프레임 확인 (배경 추가 시 GPU 병목 주의)
