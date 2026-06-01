using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Player;
using Player.Test;
using UnityEngine.TextCore.LowLevel;

public static class PlayerSetupEditor
{
    const string k_FontSrc      = @"C:\Windows\Fonts\malgun.ttf";
    const string k_FontDest     = "Assets/Player/Fonts/malgun.ttf";
    const string k_TmpFont      = "Assets/Player/Fonts/MalgunGothic_TMP.asset";
    const string k_PlayerPrefab = "Assets/Player/Prefabs/PlayerUnit.prefab";
    const string k_TestUIPrefab     = "Assets/Player/Prefabs/PlayerTestUI.prefab";
    const string k_TestUIPrefabDocs = "Assets/Docs/PlayerTest/PlayerTestUI.prefab";
    const string k_InputActions  = "Assets/Player/Input/PlayerControls.inputactions";
    const string k_PlayerConfig  = "Assets/Player/Data/PlayerConfig.asset";

    const string k_FxHitDYellow  = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR Hit D 3D (Yellow).prefab";
    const string k_FxHitMiscA    = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR3 Hit Misc A.prefab";
    const string k_FxHitMiscSmoke= "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR3 Hit Misc F Smoke.prefab";
    const string k_FxBoing       = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Texts/CFXR _BOING_.prefab";

    const string k_SprintFxBase     = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
    const string k_SprintWindTrails = "Nature/CFXR4 Wind Trails.prefab";
    const string k_SprintLightGlow  = "Light/CFXR3 LightGlow A (Loop).prefab";
    const string k_SprintGlowHDR    = "Impacts/CFXR Impact Glowing HDR (Blue).prefab";
    const string k_SprintAmbient    = "Misc/CFXR3 Ambient Glows.prefab";
    const string k_SprintBouncing   = "Magic Misc/CFXR4 Bouncing Glows Bubble (Blue Purple).prefab";


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

        var srcFont   = AssetDatabase.LoadAssetAtPath<Font>(k_FontDest);
        var fontAsset = TMP_FontAsset.CreateFontAsset(
            srcFont, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048,
            AtlasPopulationMode.Dynamic);

        // 에셋 먼저 저장 (AddObjectToAsset 전 필수)
        AssetDatabase.CreateAsset(fontAsset, k_TmpFont);

        // ASCII로 atlas 워밍업 → 첫 한글 호출 전 초기화 보장
        fontAsset.TryAddCharacters("abcdefghijklmnopqrstuvwxyz ");
        // UI 버튼에 쓸 문자 주입
        fontAsset.TryAddCharacters("생성모이기초화대시달리기");

        // atlas 텍스처를 sub-asset으로 저장
        bool textureAdded = false;
        foreach (var tex in fontAsset.atlasTextures)
        {
            if (tex != null && !AssetDatabase.IsSubAsset(tex))
            {
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
                textureAdded = true;
            }
        }

        // TryAddCharacters 후에도 텍스처가 없으면 빈 텍스처 수동 생성
        // (런타임에 Dynamic이 자동으로 채워줌)
        if (!textureAdded)
        {
            var fallback = new Texture2D(2048, 2048, TextureFormat.Alpha8, false);
            fallback.name = "Atlas";
            AssetDatabase.AddObjectToAsset(fallback, fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
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
        var dustTrail = root.AddComponent<PlayerDustTrail>();
        var smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR Smoke Source 3D.prefab");
        if (smokePrefab != null) dustTrail.SmokePrefab = smokePrefab;
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
        {
            Debug.LogError("[PlayerSetup] 한글폰트 없음 — Step 1 먼저 실행");
            return;
        }

        // Dynamic atlas는 editor에서 자동 populate 안 됨 → 버튼 글자 미리 주입
        korFont.TryAddCharacters("abcdefghijklmnopqrstuvwxyz ");  // 워밍업
        korFont.TryAddCharacters("생성모이기초화대시달리기");
        EditorUtility.SetDirty(korFont);
        AssetDatabase.SaveAssets();

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
        panelRect.anchorMin  = new Vector2(0f, 0.6f);
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
        var spawnBtn        = MakeButton(panel, "Button_Spawn",        "생성",       korFont);
        var gatherBtn       = MakeButton(panel, "Button_Gather",       "모이기",      korFont);
        var sprintGatherBtn = MakeButton(panel, "Button_SprintGather", "대시 모이기", korFont);
        var clearBtn        = MakeButton(panel, "Button_Clear",        "초기화",      korFont);

        UnityEventTools.AddPersistentListener(spawnBtn.onClick,        testUI.OnSpawnClicked);
        UnityEventTools.AddPersistentListener(gatherBtn.onClick,       testUI.OnGatherClicked);
        UnityEventTools.AddPersistentListener(sprintGatherBtn.onClick, testUI.OnSprintGatherClicked);
        UnityEventTools.AddPersistentListener(clearBtn.onClick,        testUI.OnClearClicked);

        // Player/Prefabs 와 Docs/PlayerTest 양쪽에 저장
        PrefabUtility.SaveAsPrefabAsset(canvasGO, k_TestUIPrefab);
        PrefabUtility.SaveAsPrefabAsset(canvasGO, k_TestUIPrefabDocs);
        Object.DestroyImmediate(canvasGO);
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSetup] TestUI 프리팹 완료: " + k_TestUIPrefab);
    }

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/4. Wire Bounce Effects to PlayerConfig")]
    static void WireBounceEffects()
    {
        var config = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(k_PlayerConfig);
        if (config == null)
        {
            Debug.LogError("[PlayerSetup] PlayerConfig.asset 없음: " + k_PlayerConfig);
            return;
        }

        var so = new SerializedObject(config);
        so.FindProperty("BounceEffectHitDYellow")   .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_FxHitDYellow);
        so.FindProperty("BounceEffectHitMiscA")     .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_FxHitMiscA);
        so.FindProperty("BounceEffectHitMiscFSmoke").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_FxHitMiscSmoke);
        so.FindProperty("BounceEffectBoing")        .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_FxBoing);

        so.FindProperty("SprintFxWindTrails") .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_SprintFxBase + k_SprintWindTrails);
        so.FindProperty("SprintFxLightGlow")  .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_SprintFxBase + k_SprintLightGlow);
        so.FindProperty("SprintFxGlowingHDR") .objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_SprintFxBase + k_SprintGlowHDR);
        so.FindProperty("SprintFxAmbientGlows").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_SprintFxBase + k_SprintAmbient);
        so.FindProperty("SprintFxBouncingGlows").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(k_SprintFxBase + k_SprintBouncing);
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerSetup] Bounce + Sprint FX 연결 완료: " + k_PlayerConfig);
    }

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/5. Setup Camera Arm + NGO on PlayerUnit")]
    static void SetupCameraArm()
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlayerPrefab);
        if (prefabAsset == null)
        {
            Debug.LogError("[PlayerSetup] PlayerUnit.prefab 없음 — Step 2 먼저 실행");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(k_PlayerPrefab))
        {
            var root = scope.prefabContentsRoot;

            // ── NGO 컴포넌트 (없는 경우만 추가) ────────────────────
            if (root.GetComponent<NetworkObject>() == null)
                root.AddComponent<NetworkObject>();
            if (root.GetComponent<ClientNetworkTransform>() == null)
                root.AddComponent<ClientNetworkTransform>();
            if (root.GetComponent<PlayerInputHandler>() == null)
                root.AddComponent<PlayerInputHandler>();

            // 기존 CameraArm 있으면 제거 후 재생성
            var existing = root.transform.Find("CameraArm");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            // ── CameraArm (수평 회전 피벗) ──────────────────────
            var armGO = new GameObject("CameraArm");
            armGO.transform.SetParent(root.transform, false);
            armGO.transform.localPosition = new Vector3(0f, 1f, 0f); // 플레이어 허리 높이

            // ── CinemachineCamera ────────────────────────────────
            var camGO = new GameObject("CinemachineCamera");
            camGO.transform.SetParent(armGO.transform, false);

            // 기본 쿼터뷰: 45도 앙각, distance=10
            // y = distance * sin(45°) ≈ 7.07
            // z = -distance * cos(45°) ≈ -7.07
            camGO.transform.localPosition = new Vector3(0f, 7.07f, -7.07f);
            camGO.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            var vcam = camGO.AddComponent<CinemachineCamera>();
            vcam.Priority = 10;

            camGO.AddComponent<PlayerCameraController>();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSetup] CameraArm + NGO 구성 완료: " + k_PlayerPrefab);
    }

    // ─────────────────────────────────────────────
    [MenuItem("Player Setup/6. Generate PlayerControls C# Class")]
    static void GenerateInputActionsClass()
    {
        var importer = AssetImporter.GetAtPath(k_InputActions);
        if (importer == null)
        {
            Debug.LogError("[PlayerSetup] PlayerControls.inputactions 없음: " + k_InputActions);
            return;
        }

        var so = new SerializedObject(importer);
        so.FindProperty("m_GenerateWrapperCode").boolValue    = true;
        so.FindProperty("m_WrapperClassName").stringValue     = "PlayerControls";
        so.FindProperty("m_WrapperCodePath").stringValue      = ""; // 같은 폴더에 생성
        so.FindProperty("m_WrapperCodeNamespace").stringValue = "";
        so.ApplyModifiedProperties();

        AssetDatabase.ImportAsset(k_InputActions, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        Debug.Log("[PlayerSetup] PlayerControls.cs 생성 완료 → Assets/Player/Input/");
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
        tmp.fontSize  = 20;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        if (font != null)
            tmp.font = font;
        tmp.text = label;

        return btn;
    }
}
