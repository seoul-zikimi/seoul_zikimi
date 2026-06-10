# 블록 · 맵 제작 가이드 (기획자용)

이 게임은 **정답 건물**을 보고 똑같이 짓는 협동 게임이다. 기획자가 만들 게 둘:
- **블록(재료)** — 건축에 쓰는 3D 조각 (NC VARCO로 제작).
- **맵(정답)** — "이렇게 지어라"는 목표 배치 → `MapAnswerData` 에셋.

> 💡 용어: **창** = Unity 화면의 패널. **Hierarchy**(씬 안의 오브젝트 목록), **Project**(에셋/파일 목록), **Inspector**(선택한 것의 속성), **Scene**(3D 편집 화면).

---

## 0. 씬이 둘이다 — 안 헷갈리게

| 씬 | 어디 있나 | 무엇을 하나 |
|---|---|---|
| **AnswerAuthoring.unity** (오서링 씬) | Project → `Assets/Grid/Scenes` → 더블클릭 | **맵(정답) 만들기** (Autotiles3D로 칠함) |
| **게임 씬** (플레이 씬) | `Grid Setup → ★ Setup Multiplayer Test`를 돌린 씬 = **GridManager**가 들어있는 씬 | **실제 플레이** + 정답 등록 |

→ **맵은 오서링 씬에서 만들고**, **게임 씬의 GridManager에 등록**해서 쓴다.

---

## 1. 크기 규칙 (PPU 먼저 정리)

- **1 칸(셀) = 1 Unity 월드 유닛.** 그리드는 셀=1유닛 고정.
- 블록 크기는 **footprint(점유 칸 수)** 로 정함: `(1,1,1)`=1칸 큐브, `(1,1,3)`=z로 3칸, `(1,3,2)`=벽 모양.
- **PPU 128 은 "텍스처 아트 디렉션"** 이지 크기 규칙이 아님.
  - 뜻: 텍스처를 **1유닛당 ≈128px** 밀도로 (로우폴리·카툰 톤 통일용).
  - 3D 메시는 PPU 임포트 설정이 아니라 **UV를 그 밀도로** 맞추면 됨. (2D 스프라이트 UI가 있으면 그건 임포트 PPU=128.)
  - **크기·배치·채점은 PPU와 무관** — 오직 footprint(칸 수)로 결정.

---

## 2. 블록(프리팹) 만드는 규약

NC VARCO 메시를 Unity로 가져와 프리팹으로 만들 때 **꼭** 지킬 것:

1. **스케일**: 셀=1유닛에 맞춤. footprint `(fx,fy,fz)` 블록이면 화면상 `fx×fy×fz` 유닛.
2. **피벗/원점 = footprint의 min-corner**(가장 작은 x,y,z 코너). 멀티칸 메시가 **+X/+Y/+Z로 뻗게** → 회전·확장이 맞음.
3. **콜라이더**: 보이는 메시 형태에 맞춰 추가(플레이어 충돌용).
4. **아트**: PPU 128 / 로우폴리 카툰.

---

## 3. 블록 등록 — 3D 모델을 게임이 쓰게 "연결하기" ⭐

블록 하나를 넣으려면 **4단계 체인**으로 연결한다. 각 단계가 다음으로 이어진다:

```
① 3D 모델 → 프리팹           (게임이 띄우고 부딪힐 실제 물건)
        ↓ 이 블록이 "뭔지" 적은 카드를 만든다
② MaterialDef 에셋            (Id·크기·프리팹·공정 = 블록 스펙 카드)
        ↓ 게임이 아는 "블록 목록"에 카드를 넣는다
③ MaterialCatalog             (모든 블록의 마스터 목록)
        ↓ 맵 칠할 때 쓰는 "팔레트"를 갱신한다
④ Autotiles3D 타일            (메뉴 한 번이면 자동)
```

### ① 프리팹 만들기
- **[창: Project]** NC VARCO 3D 모델을 Unity로 가져와 §2 규약대로 **프리팹**으로.
- *왜?* 게임이 화면에 띄우고 충돌 판정할 실제 오브젝트.

### ② MaterialDef = 블록의 "정보 카드" 만들기
- **[창: Project]** 빈 곳 **우클릭 → Create → Grid → MaterialDef** → 이름(예: `Mat_Window`).
- 만든 카드를 클릭 → **[창: Inspector]** 에서 채우기:

  | 칸 | 뜻 | 예 |
  |---|---|---|
  | **Id** | 고유 번호(다른 재료와 겹치면 안 됨) | 3 |
  | **Footprint** | 몇 칸 차지 (x,y,z) | (1,2,1) |
  | **Prefab** | ①에서 만든 프리팹 | Window 프리팹 |
  | **RequiredProcesses** | 필요한 공정(없으면 비움) | Fixed |
  | **MustBeFixed** | 하중부재(기둥/벽)면 체크 → 미고정 시 충격에 무너짐 | ✔ |
  | IsBreakable / MaxSpawnCount | 유리 등 / 스폰 제한(-1=무제한) | |

- *왜?* "이 블록은 3번, 1×2×1, 망치질 필요" 를 게임이 읽는다.

### ③ MaterialCatalog에 카드 넣기
- **[창: Project]** `Assets/Grid/Data/MaterialCatalog` 클릭.
- **[창: Inspector]** 의 **Materials** 리스트에 ②의 MaterialDef를 **드래그**.
- *왜?* 게임은 이 목록으로 "존재하는 블록 전부"를 안다.

### ④ 맵 팔레트(Autotiles3D 타일) 갱신
- **[메뉴] Grid Setup → Create Autotiles3D Tiles From Catalog** 한 번 클릭.
- → 카탈로그의 모든 재료로 타일 자동 생성(타일 이름 = MaterialDef 이름, 프리팹은 ①의 것).
- *왜?* 기획자가 맵 만들 때 이 블록을 골라 칠할 수 있게.

✅ 끝. **코드는 한 줄도 안 건드림** — 전부 에셋 + 메뉴.
(샘플 블록 Floor/Pillar/Wall은 이미 ①~④가 다 돼 있음.)

---

## 4. 맵(정답) 만들기 — Autotiles3D

**[씬: AnswerAuthoring.unity]** (Project → `Assets/Grid/Scenes` → 더블클릭으로 열기)

1. **[메뉴] Grid Setup → ★ Setup Autotiles3D Authoring**
   → 타일·그리드·레이어 자동 세팅. **[창: Hierarchy]** 에 `Answer Layer`가 자동 선택됨.
2. **[창: Inspector]** 에 타일 썸네일(Mat_Floor/Pillar/Wall…)이 뜸 → **하나 클릭**해 고르기.
3. **[창: Scene]** 그리드 위에 마우스 → 미리보기 뜸 → **좌클릭 = 칠하기**, **우클릭 = 지우기**, **Ctrl+휠 = 회전**, **Alt+휠 = 층(Y) 이동**. 정답 모양대로 칸칸이 칠한다.
4. **[메뉴] Grid Setup → Export Answer from Autotiles3D**
   → `Assets/Grid/Data/ExportedAnswer.asset` 생성. **[창: Console]** 에 "익스포트 N칸" 로그.

> 채점은 **셀별 재료 비교** → 멀티칸 재료는 그 칸들을 칠하면 런타임 배치와 자동 일치. 회전은 신경 안 써도 됨(위치로 판정).

---

## 5. 게임에 적용 + 정답 여러 개(랜덤)

**[씬: 게임 씬]** (GridManager가 있는 씬)

1. **[창: Hierarchy]** 에서 **GridManager** 선택.
2. **[창: Inspector]** 의 **Answers** 리스트에 정답 에셋(ExportedAnswer 등)을 **드래그**.
3. **여러 개 넣으면** 게임 시작/재시작마다 **서버가 랜덤으로 하나** 골라 전원 동기화. 1개면 항상 그거.
4. Play → 정답 고스트가 칠한 모양으로 뜨면 성공.

---

## 6. 튜닝/밸런스 값 (코드 상수 — 느낌 조절용)

| 항목 | 위치 | 기본값 |
|---|---|---|
| 닿으면 차이는 거리(노답중력) | `PlayerCarry.kKickRadius` | 0.8 |
| 차이는 거리 | `MaterialDropField.kKickDistance` | 1.6 |
| 굴러가는 속도 / 낙하 중력 | `PickupBody.kHorizSpeed` / `kGravity` | 7 / 22 |
| 줍기·도구 거리 | `PlayerCarry.m_WorkstationRange` | 2.5 |
| 채점 (셀당) | `RuntimeGrid.ScoreAgainst` | 배치 +200 / 공정 +100 |
| 제한시간 | `MapAnswerData.TimeLimitSeconds` (에셋) | 180 |
