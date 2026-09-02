using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    internal static class UiNewSessionService
    {
        public static async Task EnsureReadyAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            // 세션 옵션이 WithPlayerName()을 쓰므로 인증 서비스에 이름이 등록돼 있어야 한다.
            // 최초 1회만 채우면 메인화면에서 닉네임을 바꿔도 옛 이름이 남으므로, 매번 저장값과 비교해 갱신한다.
            string saved = SaveService.Nickname?.Trim() ?? "";
            saved = saved.Replace(" ", "");   // UGS 플레이어 이름은 공백 불가
            if (string.IsNullOrEmpty(saved))
            {
                string pid = AuthenticationService.Instance.PlayerId ?? "";
                saved = "Guest" + (pid.Length >= 5 ? pid.Substring(0, 5) : Random.Range(10000, 99999).ToString());
            }

            // UGS는 표시 이름 뒤에 "#1234" 형태의 태그를 붙여 돌려주므로 태그를 뗀 값으로 비교한다.
            string current = AuthenticationService.Instance.PlayerName ?? "";
            int tag = current.IndexOf('#');
            if (tag >= 0)
                current = current.Substring(0, tag);
            if (current != saved)
                await AuthenticationService.Instance.UpdatePlayerNameAsync(saved);
        }

        public static void StartNetwork(ISession session)
        {
            if (session == null)
                return;

            JobsnailSessionManager.Instance.RegisterActiveSession(session);
            LobbyRoomNet.RequiredTotalPlayers = LobbyRoomNet.RoomCapacity;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.LogWarning("[UI_NEW] NetworkManager가 없어 세션 네트워크 시작을 건너뜁니다.");
                return;
            }

            UnityTransport transport = manager.GetComponent<UnityTransport>();
            if (transport != null)
                manager.NetworkConfig.NetworkTransport = transport;
            // SessionSettings.createNetworkSession=true인 세션은 Multiplayer Services가
            // Relay 설정과 StartHost/StartClient를 담당한다. 여기서 다시 시작하면 같은
            // NetworkManager에 중복 시작 요청이 발생해 씬 NetworkObject가 스폰되지 않는다.
        }

        public static string ReadProperty(ISessionInfo session, string key)
        {
            if (session?.Properties != null
                && session.Properties.TryGetValue(key, out SessionProperty property)
                && property != null)
                return property.Value ?? string.Empty;
            return string.Empty;
        }
    }
}
