using System.Security.Cryptography;
using System.Text;

public static class SecurityUtils
{
    /// <summary>
    /// 문자열을 SHA256으로 해시화(암호화)합니다.
    /// </summary>
    public static string Sha256Hash(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        StringBuilder sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2")); // 16진수 문자열로 변환
        }
        return sb.ToString();
    }
}