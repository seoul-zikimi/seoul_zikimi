using System;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionLifecycleController : MonoBehaviour
    {
        [SerializeField] private HostLeaveWarningPanel warning;
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private UiNewSessionState sessionState;
        [SerializeField] private UiNewSessionCatalogController catalog;

        private bool leaving;

        private void Awake() => warning.ConfirmRequested += ConfirmLeave;

        private async void Start()
        {
            // '방으로 돌아가기'/'로비로 나가기' 둘 다 이 씬으로 오므로 여기서 BGM을 Lobby로 되돌린다.
            SoundManager.Instance?.SetPhase(GamePhase.Lobby);

            // 게임에서 로드된 맵 모델을 로비에선 놓아준다 — 모바일에서 판을 거듭할수록 메모리가 쌓이는 것 방지.
            GridSystem.MapCatalog.Instance?.ReleaseHeavyCaches();
            Resources.UnloadUnusedAssets();

            // GameScene의 '방으로 돌아가기'는 세션과 Netcode를 유지한 채 Lobby 씬만 다시 연다.
            // 씬 로컬 UiNewSessionState는 새로 생성되므로 영속 SessionManager에서 복원해야 한다.
            ISession activeSession = JobsnailSessionManager.Instance.ActiveSession;
            if (activeSession == null)
                return;

            sessionState.Set(activeSession);
            LobbyPanel lobby = FindFirstObjectByType<LobbyPanel>(FindObjectsInactive.Include);
            if (lobby != null)
                lobby.SetRoomName(activeSession.Name);
            router.Show(UiNewScreen.Lobby);

            if (!activeSession.IsHost)
                return;

            try
            {
                IHostSession host = activeSession.AsHost();
                host.SetProperty("State", new SessionProperty("Lobby"));
                await host.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UI_NEW] 복귀한 방 상태 저장 실패: {exception.Message}");
            }
        }
        private void OnDestroy()
        {
            if (warning != null)
                warning.ConfirmRequested -= ConfirmLeave;
        }

        private async void ConfirmLeave()
        {
            if (leaving)
                return;
            leaving = true;
            try
            {
                await JobsnailSessionManager.Instance.LeaveLobbyRoomSecurelyAsync(false);
                sessionState.Clear();
                router.Show(UiNewScreen.RoomList);
                catalog.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI_NEW] 방 퇴장 실패: {ex.Message}");
            }
            finally
            {
                leaving = false;
            }
        }
    }
}
