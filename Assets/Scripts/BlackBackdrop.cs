using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 레터박스 여백을 검정으로. 메뉴·로비는 2D UI 배경(preserveAspect)이라 비율이 다르면 여백이 생기고,
/// 그 여백에 카메라 스카이박스(하늘색)가 비친다 → 카메라를 '검정 단색 클리어'로 바꿔 여백을 검정으로.
/// GameScene(3D+스카이박스)은 FOV로 화면을 꽉 채워 레터박스가 없으므로 건드리지 않는다(하늘 유지).
/// </summary>
public static class BlackBackdrop
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.GameScene) return;   // 3D 씬은 스카이박스 유지

        foreach (var cam in Camera.allCameras)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }
    }
}
