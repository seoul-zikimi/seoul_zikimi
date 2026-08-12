# 감정표현 보이스 넣는 곳

이 폴더에 아래 이름으로 오디오 파일을 넣으면 **해당 대사 발동 시 자동 재생**됩니다.
(파일이 없으면 무음으로 동작 — 에러 아님. 코드 수정 불필요.)

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

- 대사 목록·파일 이름 매핑의 원본은 [EmoteDefs.cs](../../../Player/Scripts/EmoteDefs.cs) — 대사를 추가하면 여기 표도 갱신할 것.
- 재생은 `PlayerEmote.Play()`가 `SoundManager.PlaySFXAt()`(3D, SFX 볼륨 슬라이더 적용)로 처리.
- 다른 플레이어에게도 네트워크 동기화되어 같은 위치에서 들립니다.
