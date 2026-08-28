using System;
using UnityEngine;
using UnityEngine.UI;

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
            foreach (ScreenBinding binding in screens)
                if (binding.root != null)
                    AddBackgroundFill(binding.root);
            Show(initialScreen);
        }

        // 화면 아트(1920x1080 통짜 구도)는 16:9가 아니면 여백이 생긴다(캔버스 Expand — 구도는 절대 안 자름).
        // 그 여백을 같은 아트를 화면 전체로 확대(비율 유지·크롭)한 어두운 겹으로 채워 검은 띠 없이 자연스럽게.
        private static void AddBackgroundFill(GameObject screenRoot)
        {
            var bg = screenRoot.transform.Find("Background");
            var bgImage = bg != null ? bg.GetComponent<Image>() : null;
            if (bgImage == null || bgImage.sprite == null)
                return;   // 팝업(딤 배경) 화면은 해당 없음

            var fill = new GameObject("BackgroundFill", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            var rt = (RectTransform)fill.transform;
            rt.SetParent(screenRoot.transform, false);
            rt.SetAsFirstSibling();   // 본 구도·UI 전부의 뒤에 깔린다
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            var img = fill.GetComponent<Image>();
            img.sprite = bgImage.sprite;
            img.raycastTarget = false;
            img.color = new Color(0.55f, 0.55f, 0.55f, 1f);   // 살짝 어둡게 — 본 구도가 또렷이 구분되게

            var fitter = fill.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = 1920f / 1080f;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // 비율 유지한 채 화면을 완전히 덮는 최소 크기
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
