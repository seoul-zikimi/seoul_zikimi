using System;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewScreenRouter : MonoBehaviour, IUiNewScreenRouter
    {
        [Serializable]
        private struct ScreenBinding
        {
            public UiNewScreen screen;
            public GameObject root;
        }

        [SerializeField] private ScreenBinding[] screens;
        [SerializeField] private UiNewScreen initialScreen = UiNewScreen.RoomList;

        public UiNewScreen Current { get; private set; }

        private void Awake()
        {
            // UI_NEW_Canvas 전체(비활성 팝업 포함)를 한 번에 SUITE Medium으로 통일한다.
            JobsnailUiKit.ApplyFontPolicy(transform);
            Show(initialScreen);
        }

        public void Show(UiNewScreen screen)
        {
            Current = screen;
            foreach (ScreenBinding binding in screens)
            {
                if (binding.root != null)
                    binding.root.SetActive(ShouldBeActive(binding.screen, screen));
            }
        }

        private static bool ShouldBeActive(UiNewScreen binding, UiNewScreen requested)
        {
            if (binding == requested)
                return true;

            // 팝업은 실제 화면 위에 겹쳐 보인다. 배경 화면을 함께 활성화해 두되,
            // 팝업의 DimBlocker가 아래 UI 입력을 차단한다.
            return binding == UiNewScreen.RoomList
                && (requested == UiNewScreen.CreateRoom || requested == UiNewScreen.Password)
                || binding == UiNewScreen.Lobby && requested == UiNewScreen.HostLeaveWarning;
        }

        public void ShowRoomList() => Show(UiNewScreen.RoomList);
        public void ShowCreateRoom() => Show(UiNewScreen.CreateRoom);
        public void ShowPassword() => Show(UiNewScreen.Password);
        public void ShowLobby() => Show(UiNewScreen.Lobby);
        public void ShowHostLeaveWarning() => Show(UiNewScreen.HostLeaveWarning);
    }
}
