using System;
using UnityEngine;

public enum GameLanguage { Korean = 0, English = 1 }

/// <summary>
/// 게임 언어 선택 + 인라인 번역 헬퍼. 모든 어셈블리(Core)에서 쓸 수 있다.
///   L.T("한글", "English")  → 현재 언어 문자열.  보간 문자열도 그대로: L.T($"입장 {a}/{b}", $"Joined {a}/{b}")
/// 언어는 PlayerPrefs("GameLanguage")에 저장. 기본값 = 시스템 언어가 한국어면 한국어, 아니면 영어.
/// 전환 시 이미 만들어진 UI는 갱신하지 않는다 — 메인 메뉴 설정에서 바꾸면 씬을 다시 로드한다.
/// static readonly 문자열 배열처럼 한 번만 초기화되는 값은 <see cref="LocCache{T}"/>로 언어별로 캐시할 것.
/// </summary>
public static class L
{
    private const string kPrefKey = "GameLanguage";
    private static GameLanguage? s_Lang;

    public static GameLanguage Lang
    {
        get
        {
            if (s_Lang == null)
            {
                // MonoBehaviour 생성자·필드 이니셜라이저 안에서 불리면(프리팹 역직렬화 중 — 에디터 임포트/도메인 리로드 포함)
                // Unity API(systemLanguage·PlayerPrefs)가 UnityException을 던진다. 실제로 CharacterWearer(= CharacterAbility.Snail)와
                // PlayerInputHandler(= EmoteDefs 정적 초기화)가 그 경로였고, 후자는 TypeInitializationException으로 EmoteDefs를 통째로 죽였다.
                // 그 시점엔 캐시하지 않고 한국어로만 답한다 — 안전한 시점의 다음 호출이 제대로 읽고, LocCache는 언어가 달라지면 스스로 다시 만든다.
                try
                {
                    int fallback = Application.systemLanguage == SystemLanguage.Korean ? 0 : 1;
                    s_Lang = (GameLanguage)PlayerPrefs.GetInt(kPrefKey, fallback);
                }
                catch (UnityException)
                {
                    return GameLanguage.Korean;
                }
            }
            return s_Lang.Value;
        }
        set
        {
            if (s_Lang == value) return;
            s_Lang = value;
            PlayerPrefs.SetInt(kPrefKey, (int)value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    // 플레이 시작 직후(어떤 씬 오브젝트도 만들어지기 전)에 언어를 확정해 둔다 → 이후 생성자에서 불려도 위의 폴백을 타지 않는다.
    // 'Enter Play Mode' 도메인 리로드를 꺼 둔 경우의 정적 값 잔재도 여기서 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResolveAtStartup()
    {
        s_Lang = null;
        _ = Lang;
    }

    /// <summary>현재 언어가 영어인가.</summary>
    public static bool En => Lang == GameLanguage.English;

    /// <summary>언어가 바뀐 직후(저장 후) 호출.</summary>
    public static event Action Changed;

    /// <summary>현재 언어의 문자열. 영어 번역이 비어 있으면 한글을 그대로 쓴다.</summary>
    public static string T(string ko, string en) => En && !string.IsNullOrEmpty(en) ? en : ko;

    /// <summary>언어 이름(설정 UI 표시용).</summary>
    public static string DisplayName(GameLanguage lang) => lang == GameLanguage.English ? "English" : "한국어";
}

/// <summary>
/// 언어별로 한 번만 만들고 재사용하는 값(문자열 배열·정의 테이블 등).
///   static readonly LocCache&lt;string[]&gt; s_Labels = new();
///   static string[] Labels => s_Labels.Get(() => new[] { L.T("가", "A"), L.T("나", "B") });
/// 언어가 바뀌면 다음 Get에서 팩토리를 다시 돌려 새 언어 값을 돌려준다.
/// </summary>
public sealed class LocCache<T>
{
    private T m_Value;
    private GameLanguage m_Lang;
    private bool m_Has;

    public T Get(Func<T> make)
    {
        var lang = L.Lang;
        if (!m_Has || m_Lang != lang)
        {
            m_Value = make();
            m_Lang = lang;
            m_Has = true;
        }
        return m_Value;
    }
}
