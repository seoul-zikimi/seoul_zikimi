# UI 플레이테스트 가이드

> 기획자용 — 개발 환경에서 직접 테스트하는 방법

---

## 테스트 1 — UIManager 데모씬에서 기능 확인

UIManagerDemoScene에서 키보드로 모든 UI 레이어를 단독 테스트합니다.  
이 테스트는 **혼자** 실행할 수 있습니다.

### 1-1. 씬 열기

1. **Project 창** → `Assets/Scenes/UIManagerDemoScene` 더블클릭
2. **▶ Play**

### 1-2. 조작 방법

| 키 | 동작 |
|----|------|
| `[H]` | HUD 표시 (`ShowHUDUI<DemoHUD>`) |
| `[G]` | HUD 숨기기 (`HideHUDUI<DemoHUD>`) |
| `[P]` | Popup 열기 (`ShowPopupUI<DemoPopup>`) |
| `[C]` | Popup 닫기 (`ClosePopupUI`) |
| `[S]` | System UI 표시 (`ShowSystemUI<DemoPopup>`) |
| `[X]` | System UI 닫기 (`CloseSystemUI`) |
| `[ESC]` | 모든 Popup 닫기 (`CloseAllPopupUI`) |

### 1-3. 확인 항목

**HUD 레이어:**
- [ ] `[H]` → HUD 화면에 나타남
- [ ] `[H]` 두 번 연속 → 인스턴스 1개만 유지 (두 번째 호출은 SetActive만)
- [ ] `[G]` → HUD 화면에서 사라짐 (인스턴스는 유지)

**Popup 레이어:**
- [ ] `[P]` 세 번 → 팝업 3개가 스택으로 쌓임
- [ ] `[C]` → 가장 위의 팝업 하나만 닫힘 (LIFO)
- [ ] `[ESC]` → 남은 팝업 전부 닫힘

**System 레이어:**
- [ ] `[S]` → System UI가 Popup 위에 뜸 (Sorting Order 100)
- [ ] `[X]` → System UI 닫힘

---

## 테스트 2 — 새 UI 화면 추가 확인

사용법.md Step 1~3 따라 새 HUD 또는 Popup을 만든 후 확인합니다.

### 2-1. 확인 항목

- [ ] 스크립트 클래스명 = 프리팹 파일명 (대소문자 일치)
- [ ] 프리팹 저장 경로가 정확한지 (`Resources/UI/HUD/` 또는 `Resources/UI/Popup/`)
- [ ] `Init()` 내 `Bind<T>()` 호출 후 Console에 `Failed to bind` 없음
- [ ] `Get<T>((int)EnumValue)` 로 컴포넌트 정상 반환됨
- [ ] HUD: `ShowHUDUI<T>()` / `HideHUDUI<T>()` 정상 동작
- [ ] Popup: `ShowPopupUI<T>()` / `ClosePopupUI()` 정상 동작

### 2-2. Bind 실패 시 체크리스트

Console에 `Failed to bind(XXX)` 가 뜨면:
- [ ] 프리팹 안에 해당 이름의 자식 오브젝트 있는지 확인
- [ ] 이름 대소문자가 enum과 완전히 일치하는지 확인
- [ ] 자식 오브젝트에 Bind하려는 컴포넌트가 붙어있는지 확인
