using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>
    /// 방장 이탈로 방이 폭파(삭제)됐을 때, 튕겨나간 팀원을 UI_NEW 세션 목록 화면으로 되돌리고
    /// 그 위에 안내 팝업을 띄운다. Lobby 씬이 아직 로드되지 않았으면 sceneLoaded 시점으로 예약한다.
    /// </summary>
    public static class UiNewRoomClosedNotice
    {
        private const string BackgroundPath = "UI_NEW/03_팝업 화면들/방 폭파 안내 팝업/팀원-방폭파 안내 배경";
        private const string YesButtonPath = "UI_NEW/03_팝업 화면들/방장 방 나갈때 경고 팝업/예 버튼";
        private const string CanvasName = "@UiNewRoomClosedNotice";

        private static bool s_Pending;

        /// <summary>세션 목록 화면 도착 후 팝업을 띄운다. 다른 씬이면 Lobby 씬 로드 시점으로 예약.</summary>
        public static void ShowOnRoomList()
        {
            if (SceneManager.GetActiveScene().name == SceneNames.Lobby)
            {
                Present();
                return;
            }

            if (s_Pending)
                return;

            s_Pending = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneNames.Lobby)
                return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_Pending = false;
            Present();
        }

        private static void Present()
        {
            // 방이 사라졌으니 대기방 화면이 아니라 세션 목록으로 되돌리고 목록을 새로 받는다.
            var state = Object.FindFirstObjectByType<UiNewSessionState>(FindObjectsInactive.Include);
            if (state != null)
                state.Clear();

            var router = Object.FindFirstObjectByType<UiNewScreenRouter>(FindObjectsInactive.Include);
            if (router != null)
                router.Show(UiNewScreen.RoomList);

            var catalog = Object.FindFirstObjectByType<UiNewSessionCatalogController>(FindObjectsInactive.Include);
            if (catalog != null)
                catalog.Refresh();

            Build();
        }

        private static void Build()
        {
            Canvas canvas = JobsnailUiKit.EnsureOverlayCanvas(CanvasName, 700);
            if (canvas.transform.childCount > 0)
                return;   // 이미 떠 있음

            Image dim = JobsnailUiKit.Box("DimBlocker", canvas.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.52f));
            dim.raycastTarget = true;

            RectTransform frame = JobsnailUiKit.Rect("NoticeFrame", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(405f, 343f));
            var frameImage = frame.gameObject.AddComponent<Image>();

            // 스프라이트를 못 읽어도 흰 네모가 뜨지 않도록 문구 팝업으로 대체한다.
            Sprite background = JobsnailUiKit.Sprite(BackgroundPath);
            if (background != null)
            {
                frameImage.sprite = background;
                frameImage.preserveAspect = true;
            }
            else
            {
                frameImage.color = new Color(0.16f, 0.12f, 0.09f, 0.94f);
                JobsnailUiKit.Label("Message", frame, "방장이 나가서 방이 사라졌어요.", 26,
                    Color.white, TextAlignmentOptions.Center, new Vector2(0f, 20f), new Vector2(340f, 120f));
            }

            // 배경에 그려진 예 버튼 자리 위에 실제 버튼 스프라이트를 얹는다(경고 팝업과 동일 규격).
            JobsnailUiKit.Button("YesButton", frame, JobsnailUiKit.Sprite(YesButtonPath),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(151f, 49f),
                () => Close(canvas), "확인");

            // 배경에 그려진 우상단 × 위 투명 히트박스.
            Button close = JobsnailUiKit.Button("CloseButton", frame, null,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(171.5f, 144.3f), new Vector2(44f, 44f),
                () => Close(canvas));
            close.image.color = Color.clear;
        }

        private static void Close(Canvas canvas)
        {
            if (canvas != null)
                Object.Destroy(canvas.gameObject);
        }
    }
}
