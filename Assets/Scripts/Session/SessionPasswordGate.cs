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

    /// <summary>[클라] 조인 직전에 보낼 비밀번호를 ConnectionData에 싣는다(공개방은 빈 페이로드).
    /// 재접속(ReconnectAsync)은 같은 NetworkConfig를 재사용하므로 자동으로 다시 실린다.</summary>
    public static void SetLocalPassword(string password)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return;
        nm.NetworkConfig.ConnectionData = string.IsNullOrEmpty(password)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(SessionPasswordHash.Of(password));
    }

    /// <summary>NetworkManager가 (재)등장할 때마다 승인 검증을 건다 — JobsnailSessionDisconnectWatcher가 부른다.</summary>
    public static void Configure(NetworkManager nm)
    {
        if (nm == null)
            return;
        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = Approve;
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

        // 공개방 — 전원 통과
        if (string.IsNullOrEmpty(s_ExpectedHash))
        {
            response.Approved = true;
            return;
        }

        string sent = request.Payload != null && request.Payload.Length > 0
            ? Encoding.UTF8.GetString(request.Payload)
            : "";
        bool ok = string.Equals(sent, s_ExpectedHash, StringComparison.OrdinalIgnoreCase);
        response.Approved = ok;
        if (!ok)
        {
            response.Reason = "wrong_password";
            Debug.LogWarning($"[SessionPasswordGate] 비밀번호 불일치 — 접속 거부(clientId={request.ClientNetworkId})");
        }
    }
}
