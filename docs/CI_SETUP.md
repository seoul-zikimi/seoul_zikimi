# CI 설정 가이드 (1회만 하면 됨)

`.github/workflows/unity-ci.yml`이 하는 일:
- **main 대상 푸시·PR마다**: EditMode 테스트 114+건 자동 실행(맵 배선 계약 테스트 포함 — DDP류 회귀를 머지 전에 잡음). 결과가 PR 체크로 표시된다.
- **수동(Actions 탭 ▸ Unity CI ▸ Run workflow)**: Android 개발용 APK 스모크 빌드.
- iOS 빌드는 CI에서 안 한다(macOS 러너 분당 10배 + 서명 복잡) — 기존 로컬 파이프라인 유지.

## 1. Unity 라이선스 시크릿 등록 (필수 — 이거 전엔 CI가 라이선스 실패로 빨갛게 뜸)

Unity가 CI 머신에서 에디터를 돌리려면 라이선스 활성화가 필요하다. Personal 라이선스 기준:

1. GitHub 저장소 ▸ **Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret**
2. 다음 3개 등록:
   - `UNITY_EMAIL` — Unity 계정 이메일
   - `UNITY_PASSWORD` — Unity 계정 비밀번호 (2FA 켜져 있으면 GameCI 문서의 TOTP 절차 참고)
   - `UNITY_LICENSE` — 라이선스 파일(.ulf) **내용 전체**. 얻는 법:
     1. 로컬 머신에서: `Unity Hub ▸ 설정(톱니) ▸ Licenses`로 활성화된 Personal 라이선스가 있는 상태에서
        macOS 기준 `/Library/Application Support/Unity/Unity_lic.ulf` 파일을 연다
     2. 그 XML 내용 전체를 복사해 시크릿 값으로 붙여넣기
     3. 파일이 없으면 GameCI 활성화 가이드( https://game.ci/docs/github/activation )의
        수동 활성화(.alf → license.unity3d.com → .ulf) 절차를 따른다

⚠ 계정 비밀번호가 부담스러우면 CI 전용 Unity 계정을 하나 파서 쓰는 걸 권장(팀 공용).

## 2. 첫 실행 확인

1. 이 브랜치가 main에 머지되면(또는 main에 워크플로 파일이 있으면) 자동 활성화.
2. Actions 탭에서 `Unity CI` 실행 확인. **첫 실행은 느리다**(에디터 이미지 다운로드 + Library 임포트, 20~40분).
   이후는 Library 캐시로 8~15분 수준.
3. 테스트가 하나라도 실패하면 PR에 빨간 체크 — 로그의 `EditMode 테스트 결과`에서 어떤 테스트인지 보인다.

## 3. 알아둘 것 / 한계

- **비용**: private 저장소 무료 플랜은 월 2,000분. 테스트 잡(~10분)은 넉넉하지만 Android 빌드(30~60분)는
  수동 트리거만 해둔 이유. 필요하면 태그 푸시 때만 빌드하도록 바꾸면 된다.
- **저장소 4.4GiB**: 얕은 체크아웃으로 버티고 있지만, 장기적으로는 대용량 바이너리(glb·mp3·png)를
  **git-lfs로 이관**하는 게 클론·CI·팀원 모두에게 이롭다(별도 작업 — 전 팀원 협의 필요, 히스토리 재작성).
- **Library 캐시 상한**: GitHub 캐시는 저장소당 10GB. Library가 그보다 커지면 캐시가 밀려나
  가끔 느린 실행이 생긴다(고장 아님).
- 에디터 버전은 `ProjectSettings/ProjectVersion.txt`에서 자동 감지 — Unity 업그레이드 시 CI는 손댈 것 없음.
- PlayMode/네트워크(NGO) 테스트는 아직 없음(ARCHITECTURE.md §6) — 생기면 워크플로에 `testMode: PlayMode` 잡 추가.
