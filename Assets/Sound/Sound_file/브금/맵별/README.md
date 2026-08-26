# 맵별 배경음악 (BGM)

이 폴더에 **맵 전용 mp3**를 넣고, 맵 카드(MapDef)의 `Bgm` 칸으로 드래그하면 그 맵에서만 다른 곡이 나옵니다.

## 넣는 법 (3단계)

1. **mp3를 이 폴더에 복사**
   파일명 규칙: `BGM_<맵영문이름>_<페이즈>.mp3`
   ```
   BGM_Ddp_Building.mp3        ← 건축 중
   BGM_Ddp_Urgent.mp3          ← 남은 60초 긴박
   BGM_Ddp_Result.mp3          ← 결과 화면
   ```

2. **인스펙터에서 Import 설정** (mp3 클릭 → Inspector)
   | 항목 | 값 | 이유 |
   |---|---|---|
   | Load Type | `Streaming` | BGM은 길어서 통째로 메모리에 올리면 낭비 |
   | Compression Format | `Vorbis` | |
   | Quality | 70 정도 | |
   | Preload Audio Data | 체크 해제 | |
   | Force To Mono | 해제(스테레오 유지) | |

3. **맵 카드에 꽂기**
   `Assets/Map/Maps/Map_XXX.asset` 선택 → Inspector 의 **Bgm** 항목:
   - `Building` — 건축 중 BGM
   - `Urgent` — 남은 60초 긴박 BGM
   - `Result` — 결과 화면 BGM

## 비워두면?

**비운 칸은 기존 공용 BGM이 그대로 나옵니다.** (`Assets/Sound/Data/SoundLibrary.asset` 의 `bgmEntries`)

즉 세 칸을 다 비우면 지금과 완전히 동일하게 동작하고, `Building` 칸에만 곡을 꽂으면
"건축 중에만 이 맵 전용 곡, 긴박·결과는 공용 곡" 이 됩니다. 원하는 칸만 채우면 됩니다.

## 동작 구조

```
GameLoopManager (페이즈 전환)
  └─ GridSoundBridge.SetPhaseForMap("Building", mapIndex)
       ├─ MapCatalog.Get(mapIndex).Bgm.Building 이 있으면 → SoundManager.PlayBGM(clip)   ← 맵 전용
       └─ 없으면                                          → SoundManager.SetPhase(phase) ← 공용 폴백
```

- 전환은 기존과 똑같이 **DOTween 1초 crossfade** (`SoundManager._bgmFadeDuration`)
- 같은 곡이 이미 재생 중이면 무시되므로 맵을 다시 골라도 곡이 끊기지 않습니다
- 볼륨은 기존 AudioMixer `BGMVolume` 슬라이더가 그대로 적용됩니다

## 참고: 현재 공용 BGM

`Assets/Sound/Data/SoundLibrary.asset`

| GamePhase | 파일 |
|---|---|
| Lobby | `브금/Hanok Legends_Mod.mp3` |
| Building | `브금/BGM_InGame.mp3` |
| BuildingUrgent | `브금/BGM_InGameUrgent.mp3` |
| Result | `브금/BGM_InGameUrgent.mp3` (긴박곡 재사용 — 결과 전용 곡이 생기면 교체 권장) |

`브금/BGM_Lobby.mp3` 와 `브금/탈락자들/` 5곡은 현재 어디에서도 참조되지 않는 미사용 파일입니다.
맵 전용 BGM 후보로 바로 써도 됩니다.
