using System;

/// <summary>
/// 비밀방 비밀번호 정책. 방을 만들 때 세션 프로퍼티 "PasswordHash"에 해시를 저장하고,
/// 입장할 때는 같은 방식으로 해시해 비교한다. UGS 세션 자체에는 비밀번호를 걸지 않으므로
/// (짧은 비밀번호가 UGS 제약에 걸린다) 평문을 JoinSessionOptions.Password로 보내면 안 된다.
/// 해시 계산 자체는 SecurityUtils를 그대로 쓴다.
/// </summary>
public static class SessionPasswordHash
{
    public static string Of(string value) => SecurityUtils.Sha256Hash(value);

    /// <summary>저장된 해시와 입력한 평문 비밀번호가 같은지. 해시가 없으면 항상 false.</summary>
    public static bool Matches(string storedHash, string password)
    {
        return !string.IsNullOrEmpty(storedHash)
               && string.Equals(storedHash, Of(password), StringComparison.OrdinalIgnoreCase);
    }
}
