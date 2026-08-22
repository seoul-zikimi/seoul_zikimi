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
