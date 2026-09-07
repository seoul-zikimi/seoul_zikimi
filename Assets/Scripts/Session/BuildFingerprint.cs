using UnityEngine;

/// <summary>
/// 빌드 지문 — 넷코드 구조에 영향을 주는 소스(스크립트·씬·NetworkObject 프리팹)의 해시.
///
/// 왜 필요한가: Netcode for GameObjects는 서버와 클라의 코드가 같다는 걸 검사하지 않는다.
/// NetworkVariable/NetworkList 목록이 하나라도 다르면 에러 없이 옆 칸 값을 읽어 버리고,
/// 실제 사고로 "방장은 광통교인데 팀원은 튜토리얼 맵"이 났다(2026-09-07, PC 빌드가 촬영용 로컬 수정본이었음).
/// 그래서 접속 승인 단계에서 이 지문을 비교해 다른 빌드끼리는 아예 붙지 못하게 한다(SessionPasswordGate).
///
/// 값의 출처: 빌드 시 BuildFingerprintWriter(에디터 훅)가 Resources/BuildFingerprint.txt로 구워 넣고,
/// 에디터 플레이 모드는 같은 계산을 즉석에서 한다 — 에디터 호스트 + 기기 클라 조합도 정확히 비교된다.
/// </summary>
public static class BuildFingerprint
{
    public const string ResourceName = "BuildFingerprint";
    public const string Unknown = "unknown";

    private static string s_Cached;

#if UNITY_EDITOR
    /// <summary>에디터 전용: BuildFingerprintWriter가 [InitializeOnLoadMethod]에서 꽂아 준다(런타임 어셈블리가 에디터 코드를 못 부르므로).</summary>
    public static System.Func<string> EditorComputeHook;
#endif

    /// <summary>이 실행본의 지문. 빌드는 Resources에 구워진 값, 에디터는 즉석 계산. 못 구하면 "unknown".</summary>
    public static string Current
    {
        get
        {
            if (!string.IsNullOrEmpty(s_Cached))
                return s_Cached;
#if UNITY_EDITOR
            if (EditorComputeHook != null)
            {
                try { s_Cached = EditorComputeHook(); }
                catch (System.Exception ex) { Debug.LogWarning($"[BuildFingerprint] 에디터 지문 계산 실패: {ex.Message}"); }
                if (!string.IsNullOrEmpty(s_Cached))
                    return s_Cached;
            }
#endif
            var asset = Resources.Load<TextAsset>(ResourceName);
            s_Cached = asset != null && !string.IsNullOrWhiteSpace(asset.text) ? asset.text.Trim() : Unknown;
            if (s_Cached == Unknown)
                Debug.LogWarning("[BuildFingerprint] Resources/BuildFingerprint.txt가 없습니다 — 빌드 전처리(BuildFingerprintWriter)가 안 돌았습니다.");
            return s_Cached;
        }
    }
}
