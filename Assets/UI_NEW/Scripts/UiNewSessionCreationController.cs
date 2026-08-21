using System;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionCreationController : MonoBehaviour
    {
        [SerializeField] private RoomCreationPanel panel;
        [SerializeField] private CreateSession createSession;
        [SerializeField] private UiNewSessionState sessionState;
        [SerializeField] private LobbyPanel lobby;

        private CreateRoomRequest request;

        private void Awake()
        {
            panel.CreateRequested += OnCreateRequested;
            createSession.SessionCreated += OnSessionCreated;
            createSession.CreateSessionFailed += OnCreateFailed;
        }

        private void OnDestroy()
        {
            if (panel != null)
                panel.CreateRequested -= OnCreateRequested;
            if (createSession != null)
            {
                createSession.SessionCreated -= OnSessionCreated;
                createSession.CreateSessionFailed -= OnCreateFailed;
            }
        }

        private void OnCreateRequested(CreateRoomRequest value)
        {
            request = value;
            GridSystem.GameLoopManager.HostSelectedMap = value.MapIndex;
            switch (value.ModeIndex)
            {
                case 1:
                    GridSystem.GameLoopManager.HostSelectedMode = 1;
                    GridSystem.GameLoopManager.HostVersusUsesItems = true;
                    break;
                case 2:
                    GridSystem.GameLoopManager.HostSelectedMode = 1;
                    GridSystem.GameLoopManager.HostVersusUsesItems = false;
                    break;
                case 3:
                    GridSystem.GameLoopManager.HostSelectedMode = 2;
                    break;
                default:
                    GridSystem.GameLoopManager.HostSelectedMode = 0;
                    break;
            }
            GridSystem.GameLoopManager.HostWeatherEnabled = value.WeatherEnabled;
            GridSystem.GameLoopManager.HostSeasonSelectionMode = SeoulZikimi.Weather.SeasonSelectionMode.Random;
            GridSystem.GameLoopManager.HostFixedSeason = SeoulZikimi.Weather.Season.Spring;
            createSession.RequestCreateSession(value.RoomName, value.Visibility == RoomVisibility.Private, value.Password);
        }

        private async void OnSessionCreated(ISession session)
        {
            sessionState.Set(session);
            lobby.SetRoomName(session.Name);
            JobsnailSessionManager.Instance.RegisterActiveSession(session);

            if (session.IsHost)
            {
                try
                {
                    IHostSession host = session.AsHost();
                    host.SetProperty("MapIndex", new SessionProperty(request.MapIndex.ToString()));
                    host.SetProperty("ModeIndex", new SessionProperty(GridSystem.GameLoopManager.HostSelectedMode.ToString()));
                    host.SetProperty("Weather", new SessionProperty(request.WeatherEnabled ? "1" : "0"));
                    host.SetProperty("SeasonSelection", new SessionProperty("Random"));
                    host.SetProperty("FixedSeason", new SessionProperty("Spring"));
                    host.SetProperty("State", new SessionProperty("Lobby"));
                    await host.SavePropertiesAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UI_NEW] 방 메타데이터 저장 실패: {ex.Message}");
                }
            }

            UiNewSessionService.StartNetwork(session);
            panel.CompleteCreation();
        }

        private static void OnCreateFailed(string message) =>
            Debug.LogError($"[UI_NEW] 방 생성 실패: {message}");
    }
}
