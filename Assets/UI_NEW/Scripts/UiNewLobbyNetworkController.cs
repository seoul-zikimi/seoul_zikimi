using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewLobbyNetworkController : MonoBehaviour
    {
        private static readonly string[] ModeNames =
        {
            "타임어택 모드", "대전 모드(아이템전)", "대전 모드", "자유 건축 모드"
        };

        [SerializeField] private LobbyPanel view;
        [SerializeField] private UiNewSessionState sessionState;
        [SerializeField] private LobbyRoomNet lobbyNet;
        private readonly SemaphoreSlim metadataGate = new(1, 1);
        private bool netSubscribed;
        private bool spawnWarningShown;
        private float nextNetPollAt;   // 미바인딩 상태 재탐색 스로틀

        private void Awake()
        {
            view.TextChatRequested += SendChat;
            view.ReadyRequested += ToggleReady;
            view.StartRequested += StartGame;
            view.TeamRequested += SelectTeam;
            view.MapRequested += SelectMap;
            view.MapStepRequested += StepMap;
            view.ModeRequested += SelectMode;
            view.WeatherRequested += ToggleWeather;
        }

        private void OnDestroy()
        {
            UnsubscribeNet();
            if (view == null) return;
            view.TextChatRequested -= SendChat;
            view.ReadyRequested -= ToggleReady;
            view.StartRequested -= StartGame;
            view.TeamRequested -= SelectTeam;
            view.MapRequested -= SelectMap;
            view.MapStepRequested -= StepMap;
            view.ModeRequested -= SelectMode;
            view.WeatherRequested -= ToggleWeather;
        }

        private void Start()
        {
            RebindLobbyNet();
            Refresh();
        }

        private void Update()
        {
            // Lobby -> GameScene -> Lobby 왕복 시 씬에 직렬화된 참조와 Netcode가 실제로
            // 스폰한 in-scene NetworkObject가 달라질 수 있다. null 여부뿐 아니라
            // IsSpawned까지 확인해 실제 네트워크 객체로 다시 바인딩한다.
            // 바인딩된 상태에선 RebindLobbyNet이 조기 리턴이라 매 프레임 싸다. 미바인딩/스폰 대기
            // 상태의 전체 씬 검색(FindObjectsByType 배열 할당)과 스폰 재시도(try/catch)만
            // 0.25초 간격으로 제한한다 — 복구 지연은 메뉴 화면에서 체감 불가.
            if (lobbyNet == null || !lobbyNet.IsSpawned)
            {
                if (Time.unscaledTime < nextNetPollAt) return;
                nextNetPollAt = Time.unscaledTime + 0.25f;
            }
            RebindLobbyNet();

            if (lobbyNet != null && !lobbyNet.IsSpawned
                && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer
                && NetworkManager.Singleton.IsListening)
            {
                // UGS가 이미 열린 테스트 씬에서 NetworkManager를 시작하는 경우 씬 오브젝트
                // 자동 스폰 시점을 지나칠 수 있어, 서버가 정적 NetworkObject를 한 번만 스폰한다.
                try
                {
                    lobbyNet.NetworkObject.Spawn();
                    spawnWarningShown = false;
                }
                catch (Exception exception)
                {
                    if (!spawnWarningShown)
                    {
                        spawnWarningShown = true;
                        Debug.LogWarning($"[UI_NEW] 복귀 로비 네트워크 객체 스폰 대기: {exception.Message}");
                    }
                }
            }
        }

        private void RebindLobbyNet()
        {
            if (lobbyNet != null && lobbyNet.IsSpawned)
            {
                SubscribeNet();
                return;
            }

            LobbyRoomNet best = null;
            LobbyRoomNet[] candidates = FindObjectsByType<LobbyRoomNet>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                LobbyRoomNet candidate = candidates[i];
                if (candidate == null)
                    continue;
                if (candidate.IsSpawned)
                {
                    best = candidate;
                    break;
                }
                if (best == null)
                    best = candidate;
            }

            if (ReferenceEquals(best, lobbyNet))
            {
                SubscribeNet();
                return;
            }

            UnsubscribeNet();
            lobbyNet = best;
            spawnWarningShown = false;
            SubscribeNet();
            Refresh();
        }

        private void SubscribeNet()
        {
            if (lobbyNet == null || netSubscribed) return;
            lobbyNet.StateChanged += Refresh;
            lobbyNet.ChatMessageReceived += view.AppendNetworkChat;
            netSubscribed = true;
        }

        private void UnsubscribeNet()
        {
            if (lobbyNet == null) return;
            lobbyNet.StateChanged -= Refresh;
            lobbyNet.ChatMessageReceived -= view.AppendNetworkChat;
            netSubscribed = false;
        }

        private void Refresh()
        {
            bool spawned = lobbyNet != null && lobbyNet.IsSpawned;
            int mapIndex = spawned ? lobbyNet.SelectedMap : GridSystem.GameLoopManager.HostSelectedMap;
            int modeIndex = spawned ? lobbyNet.SelectedLobbyMode : 0;
            bool versusMode = modeIndex == 1 || modeIndex == 2;
            int count = spawned ? lobbyNet.SlotCount : 0;
            for (int i = 0; i < LobbyRoomNet.RoomCapacity; i++)
            {
                bool occupied = i < count && lobbyNet.IsSlotOccupied(i);
                view.SetSlot(i, occupied, occupied ? lobbyNet.GetSlotName(i) : string.Empty,
                    occupied && lobbyNet.IsSlotHost(i), occupied && lobbyNet.IsSlotLocal(i),
                    occupied && lobbyNet.IsSlotReady(i), occupied ? lobbyNet.GetSlotTeam(i) : 0, versusMode,
                    occupied ? lobbyNet.GetSlotCharacterId(i) : string.Empty,
                    occupied ? lobbyNet.GetSlotOutfitId(i) : string.Empty);
            }

            bool weather = spawned ? lobbyNet.WeatherOn : GridSystem.GameLoopManager.HostWeatherEnabled;
            // '랜덤'은 실제 맵이 아니다 — Get()이 0번 맵으로 폴백하므로 조회 자체를 하지 않는다(엉뚱한 이름·썸네일 방지).
            bool randomMap = mapIndex == GridSystem.MapCatalog.RandomMapIndex;
            GridSystem.MapDef map = (!randomMap && GridSystem.MapCatalog.Instance != null)
                ? GridSystem.MapCatalog.Instance.Get(mapIndex) : null;
            bool isHost = spawned && lobbyNet.IsHost;
            view.SetSettings(randomMap ? UiNewMapOptions.RandomLabel : (map != null ? map.DisplayName : "맵 없음"),
                ModeNames[Mathf.Clamp(modeIndex, 0, ModeNames.Length - 1)], map != null ? map.Thumbnail : null, weather,
                spawned && lobbyNet.CanHostEditSettings);
            bool canChangeTeam = spawned && (!lobbyNet.IsLocallyReady || isHost);
            view.SetTeam(spawned ? lobbyNet.LocalTeam : 0, versusMode, canChangeTeam);
            view.SetBestRecord(BuildRecordText(modeIndex, map, Mathf.Max(1, spawned ? lobbyNet.ConnectedCount : 1)));
            view.SetPrimaryAction(isHost, spawned && lobbyNet.IsLocallyReady,
                spawned && lobbyNet.CanStartGame, spawned);
        }

        private void SendChat(string message) => lobbyNet?.SendChat(message);
        private void ToggleReady() => lobbyNet?.ToggleReadyState();
        private void SelectTeam(int team) => lobbyNet?.SelectLocalTeam(team);

        private void SelectMap(int index) { lobbyNet?.HostSelectMap(index); _ = SaveMetadataAsync(); }

        // 좌우 화살표: 드롭다운과 같은 선택지 순서로 순환(맨 앞 '랜덤' → 공터·튜토리얼 뺀 맵들).
        // 카탈로그 인덱스로 직접 ±1 하면 목록에서 뺀 맵과 '랜덤' 센티널(-1)을 밟는다.
        private readonly List<int> mapStepIndices = new();
        private void StepMap(int step)
        {
            if (lobbyNet == null) return;
            UiNewMapOptions.CollectSelectable(mapStepIndices);
            if (mapStepIndices.Count == 0) return;
            int current = Mathf.Max(0, mapStepIndices.IndexOf(lobbyNet.SelectedMap));
            int next = ((current + step) % mapStepIndices.Count + mapStepIndices.Count) % mapStepIndices.Count;
            SelectMap(mapStepIndices[next]);
        }
        private void SelectMode(int index) { lobbyNet?.HostSelectMode(index); _ = SaveMetadataAsync(); }
        private void ToggleWeather() { lobbyNet?.HostToggleWeather(); _ = SaveMetadataAsync(); }

        private void StartGame()
        {
            // CanStartGame이 최종 조건이다(준비 + 팀 밸런스 + 입장 중인 팀원 없음).
            // 세션 프로퍼티 저장(왕복 통신)에 들어가기 전에 여기서 먼저 막는다.
            if (lobbyNet == null || !lobbyNet.CanStartGame) return;
            _ = MarkInGameAndStartAsync();
        }

        private async Task MarkInGameAndStartAsync()
        {
            // 삭제되었거나 끊어진 UGS 방에서는 Netcode 씬 전환을 시작하지 않는다.
            // 그렇지 않으면 로컬 연결만 남은 채 엉뚱한 맵으로 가거나 전환이 멎는다.
            if (!await SaveMetadataAsync("InGame"))
                return;
            if (lobbyNet == null || !lobbyNet.IsSpawned)
                return;
            lobbyNet.OnStartGameButtonClicked();
        }

        private async Task<bool> SaveMetadataAsync(string state = "Lobby")
        {
            if (lobbyNet == null || !lobbyNet.IsHost) return false;
            ISession session = sessionState != null ? sessionState.ActiveSession : null;
            if (session == null || !session.IsHost) return false;

            await metadataGate.WaitAsync();
            try
            {
                IHostSession host = session.AsHost();
                host.SetProperty("MapIndex", new SessionProperty(lobbyNet.SelectedMap.ToString()));
                host.SetProperty("ModeIndex", new SessionProperty(lobbyNet.SelectedLobbyMode.ToString()));
                host.SetProperty("Weather", new SessionProperty(lobbyNet.WeatherOn ? "1" : "0"));
                host.SetProperty("State", new SessionProperty(state));
                await host.SavePropertiesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UI_NEW] 로비 설정 저장 실패: {ex.Message}");
                return false;
            }
            finally
            {
                metadataGate.Release();
            }
        }

        private static string BuildRecordText(int modeIndex, GridSystem.MapDef map, int players)
        {
            if (map == null || modeIndex == 3)
                return "없음";

            if (modeIndex == 1 || modeIndex == 2)
            {
                SaveService.GetVersus(map.DisplayName, out int wins, out int losses);
                return wins == 0 && losses == 0 ? "없음" : $"{wins}승 {losses}패";
            }

            int bestPercent = -1;
            float bestSeconds = 0f;
            void Consider(string key)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    !SaveService.TryGetBest(key, players, out int percent, out float seconds)) return;
                if (percent > bestPercent || percent == bestPercent && seconds < bestSeconds)
                {
                    bestPercent = percent;
                    bestSeconds = seconds;
                }
            }

            Consider(map.DisplayName);
            if (map.Answers != null)
                foreach (GridSystem.MapAnswerData answer in map.Answers)
                    if (answer != null) Consider(answer.DisplayName);

            if (bestPercent < 0) return "없음";
            int rounded = Mathf.RoundToInt(bestSeconds);
            return $"{bestPercent}% {rounded / 60}분 {rounded % 60}초 ({players}인)";
        }
    }
}
