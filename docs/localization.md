# 다국어(한국어 / English) 구조

설정 화면의 `LANGUAGE` 버튼으로 전환한다. 값은 `PlayerPrefs("GameLanguage")`에 저장되고, 기본값은 시스템 언어가 한국어면 한국어, 아니면 영어다. 전환하면 메인 메뉴 씬을 다시 로드한다(이미 만들어진 UI는 갱신하지 않는다).

## 문자열을 넣는 규칙

| 어디에 있는 문자열인가 | 어떻게 번역하나 |
|---|---|
| C# 코드 리터럴 | `L.T("한글", "English")` 로 감싼다. 보간 문자열도 그대로: `L.T($"입장 {a}/{b}", $"Joined {a}/{b}")` |
| `static readonly string[]` 같은 한 번만 초기화되는 표 | `LocCache<T>`로 언어별 캐시: `static string[] X => s_X.Get(() => new[] { L.T(...), ... });` |
| 프리팹에 박힌 TMP/Text | 프리팹은 손대지 않는다. `Assets/Scripts/Utils/LocTables.cs`의 `Text` 표(한글 → 영어)에 한 줄 추가하면 `LocalizedUI`가 런타임에 교체한다 |
| UI_NEW 이미지(글자가 그림에 박힌 것) | 영어판을 `Assets/Resources/UI_NEW/EN/<ascii>_en.png`에 넣고 `LocTables.SpritePath`에 `한글 스프라이트 이름 → "UI_NEW/EN/<ascii>_en"` 추가. Resources.Load는 한글 경로가 macOS에서 깨질 수 있어 영어판은 ASCII 파일명만 쓴다 |
| 맵 이름(`MapDef`), 정답 이름(`MapAnswerData`) | 인스펙터의 `m_DisplayNameEn`. 화면엔 `LocalizedName`, 세이브/세션 키엔 `DisplayName`(한글 고정) |
| 재료 이름(`MaterialDef`) | 한글은 에셋 파일명 그대로, 영어는 `m_DisplayNameEn`. 화면엔 `LocalizedName` |
| 코디 아이템(`CodiOutfit`) | `DisplayNameEn` 필드, 화면엔 `LocalizedName` |
| 로딩 팁 | `Resources/LoadingTips.csv` 7번째 열 `Text_en`(비면 한글). 엑셀 임포터도 G열을 읽는다 |

## 아직 영어가 아닌 것
- 메인 화면 버튼 이미지 5장(`UI_NEW/00_메인 화면/`): 영어판 품질이 부족해 제외. 만들면 `UI_NEW/EN/`에 넣고 `SpritePath`에 추가하면 된다.
- 구 Jobsnail UI 이미지(`Resources/UI_pngs/`): 로고·정산서·조작법 툴팁·인트로 포스터 등. 텍스트는 코드에서 다 번역됐고 그림만 남았다.
- 감정표현 보이스 33개(`Resources/Voices/Emotes/`): 한국어 음성. 영어 음성을 같은 파일명으로 넣으면 코드 수정 없이 교체된다.

## 주의
- `MapDef.DisplayName`/`MapAnswerData.DisplayName`은 세이브 키(`versus_<이름>`, 최고 기록)라서 언어와 무관하게 한글을 유지한다. 새 표시 코드는 반드시 `LocalizedName`을 쓸 것.
- 튜토리얼 재료는 에셋 이름 `벽`/`문이 있는 벽`/`지붕`으로 찾는다 — 파일명을 바꾸지 말 것.
- 폰트: 서울한강 장체 SDF에 영문 글리프가 들어 있어 별도 폰트 교체는 없다. 이모지는 원래 미지원.
