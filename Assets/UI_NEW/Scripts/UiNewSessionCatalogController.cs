using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionCatalogController : MonoBehaviour
    {
        [SerializeField] private RoomListPanel roomList;
        [SerializeField] private int maxRooms = 6;

        private void Awake() => roomList.RefreshRequested += Refresh;
        private void Start() => Refresh();
        private void OnDestroy()
        {
            if (roomList != null)
                roomList.RefreshRequested -= Refresh;
        }

        public async void Refresh()
        {
            // UGS 일시 실패(레이트리밋 429, 순간 네트워크 오류)는 한 번 쉬었다 재시도 —
            // 첫 실패에 바로 빈 목록을 보여주면 "방이 다 사라졌다"로 오해한다.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await RefreshAsync();
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 0 && this != null)
                    {
                        Debug.LogWarning($"[UI_NEW] 방 목록 조회 실패 — 1.5초 뒤 재시도: {ex.Message}");
                        await Task.Delay(1500);
                        if (this == null) return;   // 재시도 대기 중 화면 파괴
                        continue;
                    }
                    Debug.LogError($"[UI_NEW] 방 목록 조회 실패: {ex.Message}");
                    if (roomList != null)
                        roomList.SetRooms(Array.Empty<UiNewSessionRoom>());
                    return;
                }
            }
        }

        private async Task RefreshAsync()
        {
            await UiNewSessionService.EnsureReadyAsync();
            var result = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
            {
                SortOptions = new List<SortOption> { new(SortOrder.Ascending, SortField.CreationTime) }
            });

            var rooms = new List<UiNewSessionRoom>();
            foreach (ISessionInfo session in result.Sessions)
            {
                if (UiNewSessionService.ReadProperty(session, "State") == "InGame")
                    continue;

                string mapName = MapName(session);
                rooms.Add(new UiNewSessionRoom(
                    session.Id,
                    session.Name,
                    UiNewSessionService.ReadProperty(session, "PasswordHash"),
                    Mathf.Max(0, session.MaxPlayers - session.AvailableSlots),
                    session.MaxPlayers,
                    mapName));
                if (rooms.Count >= maxRooms)
                    break;
            }
            roomList.SetRooms(rooms);
        }

        private static string MapName(ISessionInfo session)
        {
            if (!int.TryParse(UiNewSessionService.ReadProperty(session, "MapIndex"), out int index))
                return string.Empty;
            // '랜덤' 방은 실제 맵이 정해지지 않았다 — Get()이 0번으로 폴백해 엉뚱한 맵 이름이 뜨는 걸 막는다.
            if (index == GridSystem.MapCatalog.RandomMapIndex) return UiNewMapOptions.RandomLabel;
            GridSystem.MapCatalog catalog = GridSystem.MapCatalog.Instance;
            var definition = catalog != null ? catalog.Get(index) : null;
            return definition != null ? definition.DisplayName : string.Empty;
        }
    }
}
