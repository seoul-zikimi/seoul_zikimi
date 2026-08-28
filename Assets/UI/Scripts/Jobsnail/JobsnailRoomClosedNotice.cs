using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 방장 이탈로 방이 폭파(삭제)됐을 때, 튕겨나간 팀원에게 메인 메뉴 위로 안내 팝업을 띄운다.
/// 씬 전환(BootstrapScene 로드) 이후에 떠야 하므로 sceneLoaded 콜백으로 예약하는 구조.
/// </summary>
public static class JobsnailRoomClosedNotice
{
    private const string BackgroundPath = "UI_NEW/03_팝업 화면들/방 폭파 안내 팝업/팀원-방폭파 안내 배경";
    private const string YesButtonPath = "UI_NEW/03_팝업 화면들/방장 방 나갈때 경고 팝업/예 버튼";

    private static bool s_Pending;

    /// <summary>메인 메뉴 도착 후 팝업을 띄운다. 아직 다른 씬이면 BootstrapScene 로드 시점으로 예약.</summary>
    public static void ShowOnMainMenu()
    {
        if (SceneManager.GetActiveScene().name == SceneNames.BootstrapScene)
        {
            Build();
            return;
        }

        if (s_Pending)
            return;

        s_Pending = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.BootstrapScene)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        s_Pending = false;
        Build();
    }

    private static void Build()
    {
        var canvas = JobsnailUiKit.EnsureOverlayCanvas("@JobsnailRoomClosedNotice", 700);
        if (canvas.transform.childCount > 0)
            return;

        var dim = JobsnailUiKit.Box("DimBlocker", canvas.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.52f));
        dim.raycastTarget = true;

        var frame = JobsnailUiKit.Rect("NoticeFrame", canvas.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(405f, 343f));
        var frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.sprite = JobsnailUiKit.Sprite(BackgroundPath);

        // 배경에 그려진 예 버튼 자리 위에 실제 버튼 스프라이트를 얹는다(경고 팝업과 동일 규격).
        JobsnailUiKit.Button("YesButton", frame, JobsnailUiKit.Sprite(YesButtonPath),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(151f, 49f),
            () => Object.Destroy(canvas.gameObject));

        // 배경에 그려진 우상단 × 위 투명 히트박스.
        var close = JobsnailUiKit.Button("CloseButton", frame, null,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(171.5f, 144.3f), new Vector2(44f, 44f),
            () => Object.Destroy(canvas.gameObject));
        close.image.color = Color.clear;
    }
}
