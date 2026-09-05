using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 트레일러(플레이 영상) 촬영 모드 규약 — 2026-09-04 기획.
///
///  · 방 제목을 <see cref="RoomKeyword"/>로 만들면 정원 5명(배우 4 + 관전 1). UI에는 <see cref="DisplayRoomName"/>으로 보인다.
///  · 닉네임이 <see cref="SpectatorNick"/>인 참가자는 '관전자' — 몸·이름표가 모든 클라에서 숨겨지고, 대기방 슬롯·준비·인원·팀·동의
///    계산에서 빠지며, 자기 화면엔 HUD 대신 자유 시점 카메라(TrailerCamera)가 뜬다. 관전자는 방을 만드는 게 아니라 '들어간다'.
///  · 관전자 표식은 접속 승인 페이로드(SessionPasswordGate)로 서버에 먼저 알린다 — 슬롯 배정(OnClientConnected)이 닉네임 제출보다
///    먼저 오기 때문. 서버는 <see cref="IsSpectator(ulong)"/>로 clientId를 판정한다.
///  · 관전 기능은 에디터·개발 빌드 클라에서만 켜진다(릴리즈에서 닉네임만 바꿔 투명 인간이 되는 악용 방지). 서버 쪽 처리 코드는
///    빌드 종류와 무관하게 있어야 한다 — 촬영 클라(맥 에디터)가 릴리즈 빌드 방에 들어갈 수 있게.
/// </summary>
public static class TrailerMode
{
    public const string RoomKeyword = "!플레이영상!";
    public const string DisplayRoomName = "건축 잘하는 소라게 구해요";
    public const string SpectatorNick = "!관전!";
    public const int TrailerRoomCapacity = 5;

    /// <summary>접속 승인 페이로드에 실리는 관전자 표식(비밀번호 해시 뒤에 줄바꿈으로 붙는다).</summary>
    public const string PayloadFlag = "spectator";

    public static bool IsTrailerRoom(string sessionName)
        => !string.IsNullOrEmpty(sessionName) && sessionName.Trim() == RoomKeyword;

    /// <summary>UI에 보여줄 방 이름 — 촬영방 키워드는 평범한 제목으로 바꿔 보인다.</summary>
    public static string DisplayName(string sessionName)
        => IsTrailerRoom(sessionName) ? DisplayRoomName : sessionName;

    public static bool IsSpectatorNick(string nickname)
        => !string.IsNullOrEmpty(nickname) && nickname.Trim() == SpectatorNick;

    /// <summary>이 클라가 관전자로 들어가는가 — 닉네임이 !관전! 이고 에디터/개발 빌드일 때만.</summary>
    public static bool LocalIsSpectator
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return IsSpectatorNick(PlayerPrefs.GetString("PlayerNickname", ""));
#else
            return false;
#endif
        }
    }

    /// <summary>트레일러 카메라가 이름표를 숨기고 있는가(PlayerUnit.UpdateNametag가 읽음).</summary>
    public static bool HideNametags { get; set; }

    // ── 서버측 관전자 명단(clientId) — 접속 승인에서 채우고 끊기면 지운다 ──
    private static readonly HashSet<ulong> s_Spectators = new();

    public static void MarkSpectator(ulong clientId) => s_Spectators.Add(clientId);
    public static void UnmarkSpectator(ulong clientId) => s_Spectators.Remove(clientId);
    public static void ClearSpectators() => s_Spectators.Clear();
    public static bool IsSpectator(ulong clientId) => s_Spectators.Contains(clientId);
    public static int SpectatorCount => s_Spectators.Count;

    /// <summary>ids 중 관전자 수.</summary>
    public static int CountIn(IReadOnlyList<ulong> ids)
    {
        if (s_Spectators.Count == 0 || ids == null) return 0;
        int n = 0;
        for (int i = 0; i < ids.Count; i++) if (s_Spectators.Contains(ids[i])) n++;
        return n;
    }

    /// <summary>관전자를 뺀 접속자 목록. 관전자가 없으면 원본을 그대로 돌려준다(할당 없음).</summary>
    public static IReadOnlyList<ulong> FilterPlayers(IReadOnlyList<ulong> ids, List<ulong> buffer)
    {
        if (ids == null) return buffer;
        if (s_Spectators.Count == 0) return ids;
        buffer.Clear();
        for (int i = 0; i < ids.Count; i++)
            if (!s_Spectators.Contains(ids[i])) buffer.Add(ids[i]);
        return buffer;
    }
}
