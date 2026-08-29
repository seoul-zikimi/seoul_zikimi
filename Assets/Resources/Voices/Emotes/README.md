# 감정표현 보이스 넣는 곳

대사 **11종 × 캐릭터 3종 = 33칸**입니다. 캐릭터별로 폴더가 나뉘어 있어요.

```
Voices/Emotes/
├── default/      ← 달팽이 (기본 캐릭터, id = "")
├── char_turtle/  ← 거북이
├── char_crab/    ← 소라게
└── (이 폴더 바로 밑) ← 공용 폴백: 캐릭터 폴더가 비었을 때 대신 나감(선택)
```

**넣는 법**: 각 폴더의 README에 있는 파일 이름 그대로 오디오를 넣으면 끝입니다.
코드 수정도, 인스펙터 드래그도 필요 없습니다. 파일이 없으면 그 칸만 무음 — 에러가 아닙니다.

- [달팽이 11칸](default/README.md)
- [거북이 11칸](char_turtle/README.md)
- [소라게 11칸](char_crab/README.md)

## 대사 11종 (파일 이름 ↔ 대사)

세 폴더 모두 **같은 파일 이름**을 씁니다. 폴더가 캐릭터를 구분해요.

| 파일 이름 (확장자 제외) | 대사 |
|---|---|
| `Voice_Emote_00_HammerBring` | 망치 갖다줘! |
| `Voice_Emote_01_PaintBring` | 페인트 갖다줘! |
| `Voice_Emote_02_HammerNeed` | 망치질 필요해! |
| `Voice_Emote_03_PaintNeed` | 페인트칠 필요해! |
| `Voice_Emote_04_NotFixed` | 고정 안됐어! |
| `Voice_Emote_05_BuildHere` | 여기 좀 지어줘! |
| `Voice_Emote_06_DontCome` | 오지 마! |
| `Voice_Emote_07_GoodJob` | 잘했어! |
| `Voice_Emote_08_WhatDoing` | 뭐해!! |
| `Voice_Emote_09_Nice` | 좋았어! |
| `Voice_Emote_10_Complete` | 완성했어! |

## 형식 주의 ⚠️

- **지원 형식: `.wav` / `.mp3` / `.ogg`** — Unity가 오디오 클립으로 임포트하는 형식.
- **`.mp4`는 안 됩니다** (Unity가 비디오로 취급). mp4로 받았다면 변환:
  ```
  ffmpeg -i 입력.mp4 -vn Voice_Emote_00_HammerBring.mp3
  ```
- 권장: 모노, 1~2초, 앞뒤 무음 잘라내기.

## 동작 방식

- 재생 시 **말한 플레이어의 캐릭터 폴더 > 공용** 순으로 찾습니다(맵별 BGM의 "빈 칸은 공용 폴백"과 같은 방식).
  전부 없으면 무음.
- 캐릭터 id는 `CharacterWearer`가 전원에게 복제하므로, 다른 사람 화면에서도 그 사람 캐릭터의 목소리로 들립니다.
- 대사 목록·파일 이름 매핑의 원본은 [EmoteDefs.cs](../../../Player/Scripts/EmoteDefs.cs) — 대사를 추가하면 이 표와 각 폴더 README도 갱신할 것.
- 재생은 `PlayerEmote.Play()`가 `SoundManager.PlaySFXAt()`(3D, SFX 볼륨 슬라이더 적용)로 처리.

## 캐릭터를 더 추가하면?

`CharacterCatalog.All`에 한 줄 추가한 뒤, **그 id와 같은 이름의 폴더**를 여기 만들면 됩니다.
(폴더 이름 = 캐릭터 id. 빈 id인 달팽이만 예외적으로 `default`.)
