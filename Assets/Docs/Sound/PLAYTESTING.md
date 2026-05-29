# 사운드 플레이테스트 가이드

> 기획자용 — 개발 환경에서 직접 테스트하는 방법

---

## 테스트 1 — AudioMixer 세팅 (최초 1회)

SoundManager가 동작하려면 에디터에서 AudioMixer를 직접 만들어야 합니다.

### 1-1. AudioMixer 파일 생성

1. **Project 창** → `Assets/Sound/Data/` 폴더에서 우클릭
2. **Create → Audio Mixer** → 이름을 `GameAudioMixer`로 변경
3. 파일 더블클릭 → **Audio Mixer 창** 열기

### 1-2. BGM / SFX 그룹 추가

1. Audio Mixer 창 왼쪽 **Groups 패널**에서 `Master` 선택
2. 하단 **+** 클릭 → 그룹 이름 `BGM` 입력
3. `Master` 다시 선택 → **+** 클릭 → 그룹 이름 `SFX` 입력

### 1-3. 볼륨 파라미터 Expose

**BGM:**
1. Groups 패널에서 `BGM` 클릭
2. Inspector에서 **Volume** 항목 우클릭
3. `"Expose 'Volume (of BGM)' to script"` 클릭
4. Audio Mixer 창 왼쪽 상단 **Exposed Parameters** 클릭
5. 추가된 항목 이름을 정확히 `BGMVolume`으로 변경

**SFX:**
1. Groups 패널에서 `SFX` 클릭
2. Inspector에서 **Volume** 항목 우클릭
3. `"Expose 'Volume (of SFX)' to script"` 클릭
4. **Exposed Parameters**에서 이름을 `SFXVolume`으로 변경

> ⚠️ 파라미터 이름이 정확히 `BGMVolume`, `SFXVolume`이어야 합니다. 오타 시 볼륨 조절이 동작하지 않습니다.

---

## 테스트 2 — SoundLibrary 클립 연결 (최초 1회)

### 2-1. SoundLibrary.asset 생성

1. **Project 창** → `Assets/Sound/Data/` 에서 우클릭
2. **Create → Sound → Library** → 이름을 `SoundLibrary`로 변경

### 2-2. 클립 연결

Inspector에서 아래 클립을 연결합니다.

**SFX Entries:**

| Type | Clips (복수 등록 시 재생 때마다 랜덤) |
|------|--------------------------------------|
| `PlayerFootstep` | `FootstepGravel1.wav`, `FootstepGravel2.wav`, `FootstepGravel3.wav` |
| `PlayerBounce` | `FeelDuckBoom.wav` |

> 경로: `Assets/ThirdParty/Feel/NiceVibrations/HapticSamples/Footsteps/`  
> 경로: `Assets/ThirdParty/Feel/FeelDemos/Duck/Sounds/`

**BGM Entries:**

| Phase | Clip |
|-------|------|
| `Lobby` | `FeelBlobMusic.wav` |
| `Building` | `FeelWheelMusic.wav` |
| `BuildingUrgent` | `FeelSnakeBackgroundDrums.wav` |
| `Result` | `Award1.wav` |

> 경로: `Assets/ThirdParty/Feel/FeelDemos/Blob/Sounds/`  
> 경로: `Assets/ThirdParty/Feel/FeelDemos/Wheel/Sounds/`  
> 경로: `Assets/ThirdParty/Feel/FeelDemos/Snake/Sounds/`  
> 경로: `Assets/ThirdParty/Feel/NiceVibrations/HapticSamples/ApplicationUX/`

---

## 테스트 3 — 데모씬에서 사운드 확인

SoundManagerDemoScene에서 키보드로 모든 기능을 단독 테스트합니다.  
이 테스트는 **혼자** 실행할 수 있습니다.

### 3-1. 데모씬 생성 (최초 1회)

> ⚠️ 테스트 1, 2 완료 후 진행하세요.

1. Unity 상단 메뉴 → **Game > Setup > Create SoundManager Demo Scene**
2. `Assets/Scenes/SoundManagerDemoScene.unity` 생성 확인
3. 씬 더블클릭으로 열기 → **▶ Play**

### 3-2. 조작 방법

| 키 | 동작 |
|----|------|
| `[1]` | BGM: Lobby |
| `[2]` | BGM: Building |
| `[3]` | BGM: BuildingUrgent |
| `[4]` | BGM: Result |
| `[Q]` | SFX: PlayerFootstep |
| `[W]` | SFX: PlayerBounce |
| `[F1]` | BGM 즉시 정지 |
| `[F2]` | BGM fade-out 후 정지 |
| `[F3]` | SFX 전체 정지 |

### 3-3. 확인 항목

- [ ] `[2]` 누름 → BGM 재생됨
- [ ] `[3]` 누름 → 1초에 걸쳐 BGM이 부드럽게 전환됨 (crossfade)
- [ ] `[2]` 연속 두 번 → 두 번째 호출에서 아무 변화 없음 (같은 클립 무시)
- [ ] `[Q]` 빠르게 연타 → 여러 footstep SFX 겹쳐 재생됨
- [ ] `[W]` → PlayerBounce SFX 재생됨
- [ ] `[F1]` → BGM 즉시 끊김
- [ ] `[F2]` → BGM 서서히 사라짐
- [ ] `[F3]` → 재생 중이던 SFX 모두 정지
- [ ] Unity **Profiler** → SFX 연타 중 **GC Alloc 0** 확인

---

## 테스트 4 — 실게임 연결 확인 (유진테스트 씬)

플레이어 이동/충돌 시 사운드가 실제로 나오는지 확인합니다.

### 4-1. BootstrapScene에 SoundManager 배치 (최초 1회)

> 아직 배치 안 했다면:

1. **Project 창** → `Assets/Scenes/BootstrapScene` 열기
2. Hierarchy 빈 곳 우클릭 → **Create Empty** → 이름 `@SoundManager`
3. Inspector → **Add Component** → `SoundManager` 검색 → 선택
4. `_library` 필드에 `SoundLibrary.asset` 연결
5. `_mixer` 필드에 `GameAudioMixer.mixer` 연결

### 4-2. 씬 열기

1. **Project 창** → `Assets/Docs/Player/PlayerTest_YujinScene` 더블클릭
2. **▶ Play**

### 4-3. 확인 항목

- [ ] 캐릭터 이동 중 발소리(footstep)가 일정 간격(0.35초)으로 재생됨
- [ ] 캐릭터가 멈추면 발소리가 사라짐
- [ ] **생성** → **모이기** → 충돌 순간 bounce SFX 재생됨
- [ ] 볼륨 조절 필드에 0~1 값 입력 시 소리 크기가 바뀜

---

## 이펙트 종류 / 수치 변경 방법

클립 교체는 `SoundLibrary.asset`에서 합니다.

1. **Project 창** → `Assets/Sound/Data/SoundLibrary` 클릭
2. **Inspector**에서 원하는 Type의 Clips 배열 교체

> **Tip:** Play 중에 클립을 교체하면 다음 재생부터 바로 반영됩니다.
