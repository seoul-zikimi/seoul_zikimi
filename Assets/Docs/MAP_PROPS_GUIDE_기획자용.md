# 🚧 맵 소품(데코) 가이드 (기획자용)

청계천 광통교 맵을 꾸미는 소품들과 배치 방법 정리예요.
컨셉: **그리드 = 공사 현장** — 안전 라인 두르고, 밖에서 동물들이 구경하는 그림.

> 맵 자체(배경/정답) 추가는 [MAP_ADD_GUIDE_기획자용.md](MAP_ADD_GUIDE_기획자용.md) 참고

---

## 갖고 있는 소품 (Assets/Map/01_GwangTongGyo/Props/)

| 소품 | 파일 | 어디에 놓나 |
|---|---|---|
| 🌳 수양버들 | Prop_WillowTree.glb | 개천가 |
| 🌲 소나무(실사) | Prop_PineTree.glb | 개천가·언덕 |
| 🪑 나무 벤치 | Prop_Bench.glb | 산책로 |
| 🚧 공사 입간판 | Prop_ConstructionSign.glb | 그리드 코너·진입로 |
| 차단봉 | Prop_BarrierPost.glb | 그리드 둘레 (테이프 기둥) |
| 🔶 꼬깔콘 | Prop_TrafficCone.glb | 그리드 둘레·포인트 |
| 🟡 녹십자 바리케이드 | Prop_SafetyBarricade.glb | 진입로 차단 |
| 🐸 구경꾼 개구리 | **Onlooker_Frog.prefab** ← 이걸 쓰세요 | 안전 라인 밖 |
| 🦆 구경꾼 오리 | **Onlooker_Duck.prefab** ← 이걸 쓰세요 | 안전 라인 밖 |
| WARNING 테이프 | (텍스처 — 아래 테이프 툴이 자동 사용) | 차단봉 사이 |

※ 구경꾼은 `Onlooker_이름.prefab`을 놓아야 idle로 살아 움직여요.
(`Prop_Onlooker_..._Idle.fbx`를 직접 놓으면 안 움직여요 — 프리팹은 이미 만들어져 있어요)

---

## 배치 방법

1. Project 창에서 `Assets/Map/Prefabs/MapBg_GwangTongGyo` **더블클릭** (프리팹 모드로 열림)
   ⚠️ 씬에 직접 놓으면 안 돼요! 배경은 프리팹에서만 편집 — 그래야 모든 판에 반영돼요.
2. Props 폴더에서 소품을 Hierarchy로 드래그 → 위치·회전·크기 조절
3. 다 놓았으면 **저장(Ctrl+S)** → 플레이로 확인

### 배치 팁
- 그리드(공사장) 안쪽은 비워두기 — 블록 지을 공간이에요
- 소품이 플레이어 동선을 막으면 안 돼요 (특히 배송 구역 근처)
- 같은 나무도 회전·크기를 조금씩 다르게 하면 자연스러워요

---

## 🟡 WARNING 테이프 두르기 (버튼 한 번)

1. 차단봉(또는 꼬깔콘)들을 먼저 원하는 자리에 배치
2. Hierarchy에서 **이을 순서대로** 봉들을 Ctrl+클릭으로 다중 선택
3. **[메뉴] Tools ▸ Map ▸ Connect Barrier Tape**
   → 선택 순서대로 WARNING 테이프가 자동으로 이어져요
4. 마음에 안 들면 생성된 `~BarrierTape` 오브젝트 지우고 다시 실행

---

## 새 소품이 필요하면

바르코 3D로 뽑아요 (Claude한테 "○○ 만들어줘"라고 하면 됨). 잘 나오게 하는 요령:
- **한 개의 물체만**, 흰 배경, 정면~3/4 뷰
- 폴리곤은 소품 2천~5천, 캐릭터 8천이면 충분 (많으면 게임 느려져요)
- **원기둥에 대각선 무늬는 금물** — 뒷면 텍스처가 깨져요. 민무늬나 가로 링으로
- 움직일 캐릭터는 T포즈로 뽑고 리깅+idle까지 (개구리·오리처럼)
- 새 구경꾼은 idle 애니를 `Prop_이름_Idle.fbx`로 Props 폴더에 넣고
  **Tools ▸ Map ▸ Setup Onlookers** 실행하면 프리팹이 자동으로 생겨요
