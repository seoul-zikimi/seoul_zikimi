/// <summary>
/// 비밀방 비밀번호 문자 정책 — 허용: 한글(완성형+자모)·영문 대소문자·숫자·특수문자 !@#$%^&*()_-+=,./ (길이 제한 없음)
/// 생성/입장 입력창 양쪽에서 같은 규칙으로 걸러 "여기선 되는데 저기선 안 되는" 비번을 없앤다.
///
/// 참고: 인젝션류 위험은 원래 없다 — 비번은 어디에도 평문 저장/전송되지 않고 SHA256 해시로만
/// 다닌다(세션 프로퍼티·접속 승인 페이로드 모두 16진수 해시). 이 정책의 실익은
/// ① 이모지·제어문자·공백 등 플랫폼마다 입력이 달라지는 문자를 차단해 "맞는 비번인데 안 열림" 방지,
/// ② 입력 UX 통일이다. 자모(ㄱ-ㅎ,ㅏ-ㅣ)를 허용하는 이유: IME 조합 중간값을 지우면 한글 타이핑이 깨진다.
/// </summary>
public static class SessionPasswordPolicy
{
    private const string kAllowedSpecials = "!@#$%^&*()_-+=,./";

    /// <summary>입력창 밑 안내 문구 — 허용 문자가 바뀌면 여기 하나만 고치면 된다.</summary>
    public const string HintText = "사용 가능: 한글 · 영문 · 숫자 · " + kAllowedSpecials;

    public static bool IsAllowedChar(char c)
        => (c >= '가' && c <= '힣')      // 완성형 한글
        || (c >= 'ㄱ' && c <= 'ㅎ')      // 호환 자음(조합 중간값 포함)
        || (c >= 'ㅏ' && c <= 'ㅣ')      // 호환 모음
        || (c >= 'A' && c <= 'Z')
        || (c >= 'a' && c <= 'z')
        || (c >= '0' && c <= '9')
        || kAllowedSpecials.IndexOf(c) >= 0;

    /// <summary>허용 밖 문자만 걷어낸 문자열을 돌려준다(입력창 onValueChanged용). 길이는 자르지 않는다.</summary>
    public static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
            if (IsAllowedChar(c))
                sb.Append(c);
        return sb.Length == value.Length ? value : sb.ToString();
    }
}
