using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 비밀방 2차 방어선(서버측 검증).
/// 목록의 PasswordHash 비교는 클라 UX일 뿐이라, 개조 클라이언트는 조인 API를 직접 불러
/// 비밀번호 없이 입장할 수 있다. 그래서 넷코드 접속 승인(ConnectionApproval) 단계에서
/// 호스트가 비밀번호 해시를 다시 검증해 틀리면 연결 자체를 거부한다.
/// 전송 구간은 Relay 기본 DTLS로 이미 암호화 — 여기는 '올바른 클라인 척'하는 접속을 막는 층.
/// UX 정책(팝업에서 해시 비교 후 입장)은 그대로다.
/// </summary>
public static class SessionPasswordGate
{
    private static string s_ExpectedHash;   // [호스트] 이 방의 비밀번호 해시(공개방 = null)

    /// <summary>[호스트] 방 생성 시 기대 비밀번호 등록(공개방은 null/빈값).</summary>
    public static void SetExpectedPassword(string password)
        => s_ExpectedHash = string.IsNullOrEmpty(password) ? null : SessionPasswordHash.Of(password);

    /// <summary>방을 떠날 때 호출 — 다음 방에 이전 방 비밀번호가 남지 않게.</summary>
    public static void Clear() => s_ExpectedHash = null;

    /// <summary>거절 사유(빌드 불일치). 뒤에 "|서버지문"이 붙는다 — 클라가 안내 문구에 양쪽 지문을 보여 준다.</summary>
    public const string BuildMismatchReason = "build_mismatch";

    // 접속 페이로드 형식: "SZ1|<빌드지문>|<비밀번호해시>" (공개방은 해시 빈칸).
    // 빌드지문(BuildFingerprint)이 다른 빌드끼리 붙으면 NetworkVariable 스트림이 어긋나 엉뚱한 값을 읽는다
    // (실사고: 팀원만 튜토리얼 맵). 넷코드가 이걸 검사하지 않으므로 여기서 막는다.
    private const string kPayloadTag = "SZ1";

    /// <summary>[클라] 조인 직전에 보낼 비밀번호를 ConnectionData에 싣는다(공개방은 빈 해시).
    /// 재접속(ReconnectAsync)은 같은 NetworkConfig를 재사용하므로 자동으로 다시 실린다.</summary>
    public static void SetLocalPassword(string password)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;
        nm.NetworkConfig.ConnectionData = BuildPayload(password);
    }

    private static byte[] BuildPayload(string password)
    {
        string hash = string.IsNullOrEmpty(password) ? "" : SessionPasswordHash.Of(password);
        return Encoding.UTF8.GetBytes($"{kPayloadTag}|{BuildFingerprint.Current}|{hash}");
    }

    /// <summary>NetworkManager가 (재)등장할 때마다 승인 검증을 건다 — JobsnailSessionDisconnectWatcher가 부른다.</summary>
    public static void Configure(NetworkManager nm)
    {
        if (nm == null)
            return;
        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = Approve;
        // 어떤 경로로 StartClient가 불려도 빌드지문은 항상 실려 가게 기본 페이로드를 깔아 둔다(비밀번호는 조인 시 덮어씀).
        if (nm.NetworkConfig.ConnectionData == null || nm.NetworkConfig.ConnectionData.Length == 0)
            nm.NetworkConfig.ConnectionData = BuildPayload(null);
    }

    private static void Approve(NetworkManager.ConnectionApprovalRequest request,
                                NetworkManager.ConnectionApprovalResponse response)
    {
        // 기존 스폰 동작 유지: 승인 시 기본 PlayerPrefab 자동 생성(위치는 기존 스폰 로직 몫)
        response.CreatePlayerObject = true;
        response.PlayerPrefabHash = null;

        // 호스트 자신은 항상 통과
        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        string payload = request.Payload != null && request.Payload.Length > 0
            ? Encoding.UTF8.GetString(request.Payload)
            : "";
        ParsePayload(payload, out string clientFingerprint, out string sent);

        // 빌드지문 불일치 — 코드가 다른 빌드는 붙여 봐야 상태 복제가 깨진다. 구버전(태그 없는 페이로드)도 여기서 걸린다.
        string mine = BuildFingerprint.Current;
        if (!string.Equals(clientFingerprint, mine, StringComparison.Ordinal))
        {
            response.Approved = false;
            response.Reason = $"{BuildMismatchReason}|{mine}";
            Debug.LogWarning($"[SessionPasswordGate] 빌드 불일치로 접속 거부(clientId={request.ClientNetworkId}, 클라={clientFingerprint}, 서버={mine}) — 같은 커밋으로 다시 빌드하세요.");
            return;
        }

        // 공개방 — 전원 통과
        if (string.IsNullOrEmpty(s_ExpectedHash))
        {
            response.Approved = true;
            return;
        }

        bool ok = string.Equals(sent, s_ExpectedHash, StringComparison.OrdinalIgnoreCase);
        response.Approved = ok;
        if (!ok)
        {
            response.Reason = "wrong_password";
            Debug.LogWarning($"[SessionPasswordGate] 비밀번호 불일치 — 접속 거부(clientId={request.ClientNetworkId})");
        }
    }

    // "SZ1|지문|해시" 파싱. 태그가 없으면(구버전 빌드) 지문을 빈값으로 돌려 불일치 처리되게 한다.
    private static void ParsePayload(string payload, out string fingerprint, out string passwordHash)
    {
        fingerprint = "";
        passwordHash = "";
        if (string.IsNullOrEmpty(payload))
            return;
        string[] parts = payload.Split('|');
        if (parts.Length < 3 || parts[0] != kPayloadTag)
            return;
        fingerprint = parts[1];
        passwordHash = parts[2];
    }
}
