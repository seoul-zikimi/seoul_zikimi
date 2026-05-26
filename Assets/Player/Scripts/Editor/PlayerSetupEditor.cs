using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Player;
using Player.Test;
using UnityEngine.TextCore.LowLevel;

public static class PlayerSetupEditor
{
    const string k_FontSrc      = @"C:\Windows\Fonts\malgun.ttf";
    const string k_FontDest     = "Assets/Player/Fonts/malgun.ttf";
    const string k_TmpFont      = "Assets/Player/Fonts/MalgunGothic_TMP.asset";
    const string k_PlayerPrefab = "Assets/Player/Prefabs/PlayerUnit.prefab";
    const string k_TestUIPrefab = "Assets/Player/Prefabs/PlayerTestUI.prefab";

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/1. Import Korean Font")]
    static void ImportKoreanFont()
    {
        Directory.CreateDirectory("Assets/Player/Fonts");

        if (!File.Exists(k_FontSrc))
        {
            Debug.LogError("[PlayerSetup] 폰트 없음: " + k_FontSrc);
            return;
        }

        File.Copy(k_FontSrc, k_FontDest, overwrite: true);
        AssetDatabase.Refresh();

        var srcFont = AssetDatabase.LoadAssetAtPath<Font>(k_FontDest);
        var fontAsset = TMP_FontAsset.CreateFontAsset(
            srcFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic);

        AssetDatabase.CreateAsset(fontAsset, k_TmpFont);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerSetup] 한글폰트 완료: " + k_TmpFont);
    }

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/2. Create PlayerUnit Prefab")]
    static void CreatePlayerUnitPrefab()
    {
        Directory.CreateDirectory("Assets/Player/Prefabs");

        var root = new GameObject("PlayerUnit");

        // Rigidbody 먼저 (RequireComponent 중복 방지)
        var rb = root.AddComponent<Rigidbody>();
        rb.mass           = 1f;
        rb.linearDamping  = 1f;
        rb.angularDamping = 0.05f;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = 2f;
        col.radius = 0.5f;
        col.center = new Vector3(0, 1, 0);

        root.AddComponent<PlayerMovement>();
        root.AddComponent<PlayerBounce>();
        root.AddComponent<PlayerUnit>();

        // 시각화용 Cylinder (Collider 제거)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 1, 0);
        body.transform.localScale    = new Vector3(0.9f, 1f, 0.9f);
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());

        PrefabUtility.SaveAsPrefabAsset(root, k_PlayerPrefab);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSetup] PlayerUnit 프리팹 완료: " + k_PlayerPrefab);
    }

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/3. Create TestUI Prefab")]
    static void CreateTestUIPrefab()
    {
        Directory.CreateDirectory("Assets/Player/Prefabs");

        var korFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_TmpFont);
        if (korFont == null)
            Debug.LogWarning("[PlayerSetup] 한글폰트 없음 — Step 1 먼저 실행");

        // Canvas
        var canvasGO = new GameObject("PlayerTestUI");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel (좌상단)
        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGO.transform, false);
        panel.AddComponent<Image>().color = new Color(0, 0, 0, 0.5f);
        var panelRect        = panel.GetComponent<RectTransform>();
        panelRect.anchorMin  = new Vector2(0f, 0.7f);
        panelRect.anchorMax  = new Vector2(0.25f, 1f);
        panelRect.offsetMin  = panelRect.offsetMax = Vector2.zero;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment      = TextAnchor.MiddleCenter;
        layout.spacing             = 8;
        layout.padding             = new RectOffset(10, 10, 10, 10);
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = true;

        // PlayerTestUI 스크립트
        var testUI = canvasGO.AddComponent<PlayerTestUI>();

        // 버튼 + OnClick 연결
        var spawnBtn  = MakeButton(panel, "Button_Spawn",  "생성",   korFont);
        var gatherBtn = MakeButton(panel, "Button_Gather", "모이기",  korFont);
        var clearBtn  = MakeButton(panel, "Button_Clear",  "초기화",  korFont);

        UnityEventTools.AddPersistentListener(spawnBtn.onClick,  testUI.OnSpawnClicked);
        UnityEventTools.AddPersistentListener(gatherBtn.onClick, testUI.OnGatherClicked);
        UnityEventTools.AddPersistentListener(clearBtn.onClick,  testUI.OnClearClicked);

        PrefabUtility.SaveAsPrefabAsset(canvasGO, k_TestUIPrefab);
        Object.DestroyImmediate(canvasGO);
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSetup] TestUI 프리팹 완료: " + k_TestUIPrefab);
    }

    // ─────────────────────────────────────────────
    static Button MakeButton(GameObject parent, string name, string label, TMP_FontAsset font)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
        var btn = go.AddComponent<Button>();

        var textGO   = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;

        var tmp       = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 20;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        if (font != null) tmp.font = font;

        return btn;
    }
}
