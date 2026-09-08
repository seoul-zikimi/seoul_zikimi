using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SeoulZikimi.UI.New;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 웹사이트용 UI_NEW 스크린샷(방 리스트 · 방 만들기 · 세션 화면)을 에디트 모드에서 1920x1080으로 렌더한다.
/// 배치 실행: Unity.exe -batchmode -projectPath . -executeMethod WebsiteUiShotTool.Run -quit
/// 출력 폴더는 환경변수 WEBSITE_SHOT_DIR(없으면 프로젝트 루트/tmp/uishots).
/// </summary>
public static class WebsiteUiShotTool
{
    [MenuItem("Tools/Website/UI 스크린샷 3종 저장")]
    public static void Run()
    {
        string outDir = Environment.GetEnvironmentVariable("WEBSITE_SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "uishots");
        Directory.CreateDirectory(outDir);

        EditorSceneManager.OpenScene("Assets/Scenes/UI_NEW_Test.unity", OpenSceneMode.Single);

        var router = UnityEngine.Object.FindFirstObjectByType<UiNewScreenRouter>(FindObjectsInactive.Include);
        if (router == null) throw new Exception("UiNewScreenRouter 없음");
        var canvas = router.GetComponentInParent<Canvas>(true);
        if (canvas == null) canvas = router.GetComponentInChildren<Canvas>(true);
        if (canvas == null) throw new Exception("Canvas 없음");

        // 오버레이 캔버스는 Camera.Render로 안 찍힌다 → 스크린스페이스 카메라로 바꿔 RT에 렌더.
        var camGo = new GameObject("~WebsiteShotCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        cam.orthographicSize = 540f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.cullingMask = ~0;
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        cam.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 100f;

        // 런타임 Awake가 하는 폰트 통일을 흉내낸다.
        try { JobsnailUiKit.ApplyFontPolicy(canvas.transform); } catch (Exception e) { Debug.LogWarning("폰트 정책 실패: " + e.Message); }

        var roomList = UnityEngine.Object.FindFirstObjectByType<RoomListPanel>(FindObjectsInactive.Include);
        var creation = UnityEngine.Object.FindFirstObjectByType<RoomCreationPanel>(FindObjectsInactive.Include);
        var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyPanel>(FindObjectsInactive.Include);

        // 1) 방 리스트
        router.Show(UiNewScreen.RoomList);
        Call(roomList, "Awake"); Call(roomList, "OnEnable");
        roomList?.SetRooms(new List<UiNewSessionRoom>
        {
            new UiNewSessionRoom("s1", "등껍질 삼총사 출근합니다", "", 3, 4, "경복궁 근정전"),
            new UiNewSessionRoom("s2", "광통교 빠르게 돌아요", "", 1, 4, "청계천 광통교"),
            new UiNewSessionRoom("s3", "남산타워 초보 환영", "x", 2, 4, "남산타워"),
            new UiNewSessionRoom("s4", "롯데월드 퍼레이드 구경만", "", 4, 4, "롯데월드"),
            new UiNewSessionRoom("s5", "DDP 야간 공사", "x", 2, 4, "DDP 동대문디자인플라자"),
        });
        Capture(cam, rt, Path.Combine(outDir, "ui_room.png"));

        // 2) 방 만들기(방 리스트 위 팝업)
        router.Show(UiNewScreen.CreateRoom);
        Call(creation, "Awake"); Call(creation, "OnEnable");
        Capture(cam, rt, Path.Combine(outDir, "ui_create.png"));

        // 3) 세션 화면
        router.Show(UiNewScreen.Lobby);
        Call(lobby, "Awake"); Call(lobby, "OnEnable");
        if (lobby != null)
        {
            lobby.SetRoomName("등껍질 삼총사 출근합니다");
            TrySlot(lobby, 0, true, "달팽이반장", true, true, true, 0, false, "", "");
            TrySlot(lobby, 1, true, "거북이", false, false, true, 0, false, "char_turtle", "");
            TrySlot(lobby, 2, true, "소라게", false, false, false, 0, false, "char_crab", "");
            TrySlot(lobby, 3, false, "", false, false, false, 0, false, "", "");
            lobby.SetTeam(0, false, false);
            var thumb = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Map/Maps/Thumb_Gyeongbokgung.png");
            lobby.SetSettings("경복궁 근정전", "타임어택 모드", thumb, true, true);
            lobby.SetBestRecord("완성도 92%");
            lobby.SetPrimaryAction(true, false, false, true);
            lobby.AppendNetworkChat("거북이", "기와는 내가 들게");
            lobby.AppendNetworkChat("소라게", "드므 위치 먼저 확인!");
            lobby.AppendNetworkChat("달팽이반장", "다 준비되면 출근~");
        }
        Capture(cam, rt, Path.Combine(outDir, "ui_session.png"));

        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(camGo);
        Debug.Log("[WebsiteUiShotTool] 저장 완료: " + outDir);
    }

    private static void TrySlot(LobbyPanel lobby, int i, bool occupied, string nick, bool host, bool local, bool ready,
        int team, bool versus, string charId, string outfit)
    {
        try { lobby.SetSlot(i, occupied, nick, host, local, ready, team, versus, charId, outfit); }
        catch (Exception e)
        {
            Debug.LogWarning($"슬롯 {i} 아바타 실패(아바타 없이 진행): {e.Message}");
            var slots = typeof(LobbyPanel).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(lobby) as UiNewLobbySlotView[];
            if (slots != null && i < slots.Length && slots[i] != null)
                slots[i].Apply(occupied, nick, host, local, ready, team, versus, null);
        }
    }

    private static void Call(object target, string method)
    {
        if (target == null) return;
        var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (m == null) return;
        try { m.Invoke(target, null); }
        catch (Exception e) { Debug.LogWarning($"{target.GetType().Name}.{method} 실패(무시): {e.InnerException?.Message ?? e.Message}"); }
    }

    private static void Capture(Camera cam, RenderTexture rt, string path)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuildAll();
        Canvas.ForceUpdateCanvases();
        cam.Render();
        cam.Render();   // 첫 프레임에 폰트 아틀라스가 갱신되는 경우가 있어 한 번 더
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log("[WebsiteUiShotTool] " + path);
    }

    private static void LayoutRebuildAll()
    {
        foreach (var rtf in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            if (rtf.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rtf);
    }
}
