using System.IO;
using UnityEditor;
using UnityEngine;
using GridSystem;

/// <summary>
/// 그리드 모듈용 에디터 스캐폴딩. (PlayerSetupEditor 스타일)
/// Assets/Grid/Editor 에 있으므로 Assembly-CSharp-Editor 로 컴파일 →
/// GridSystem(런타임, auto-referenced) + Autotiles3D(에디터) 둘 다 참조 가능.
/// </summary>
public static class GridSetupEditor
{
    const string k_DataDir = "Assets/Grid/Data";

    [MenuItem("Grid Setup/Create Sample Materials")]
    static void CreateSampleMaterials()
    {
        if (!AssetDatabase.IsValidFolder(k_DataDir))
        {
            Directory.CreateDirectory(k_DataDir);
            AssetDatabase.Refresh();
        }

        CreateMaterial("Mat_Floor",  0, new Vector3Int(1, 1, 1), new ProcessType[0],                                mustBeFixed: false);
        CreateMaterial("Mat_Pillar", 1, new Vector3Int(1, 1, 3), new[] { ProcessType.Fixed },                       mustBeFixed: true);
        CreateMaterial("Mat_Wall",   2, new Vector3Int(1, 3, 2), new[] { ProcessType.Fixed, ProcessType.Painted },  mustBeFixed: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GridSetup] 샘플 MaterialDef 3개 생성/갱신 완료: " + k_DataDir);
    }

    static void CreateMaterial(string name, int id, Vector3Int footprint, ProcessType[] required, bool mustBeFixed)
    {
        string path = $"{k_DataDir}/{name}.asset";

        var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(path);
        bool isNew = def == null;
        if (isNew) def = ScriptableObject.CreateInstance<MaterialDef>();

        // private [SerializeField] 필드는 SerializedObject 로 설정
        var so = new SerializedObject(def);
        so.FindProperty("m_Id").intValue = id;
        so.FindProperty("m_Footprint").vector3IntValue = footprint;
        so.FindProperty("m_MustBeFixed").boolValue = mustBeFixed;

        var listProp = so.FindProperty("m_RequiredProcesses");
        listProp.arraySize = required.Length;
        for (int i = 0; i < required.Length; i++)
            listProp.GetArrayElementAtIndex(i).intValue = (int)required[i];  // [Flags] 비트값 저장

        so.ApplyModifiedProperties();

        if (isNew) AssetDatabase.CreateAsset(def, path);
        else       EditorUtility.SetDirty(def);
    }

    // ── 샘플 카탈로그 + 정답 (G3.5) ───────────────────────────────────────
    [MenuItem("Grid Setup/Create Sample Catalog + Answer")]
    static void CreateSampleCatalogAndAnswer()
    {
        var floor  = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Floor.asset");
        var pillar = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Pillar.asset");
        var wall   = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Wall.asset");
        if (floor == null || pillar == null || wall == null)
        {
            Debug.LogError("[GridSetup] 먼저 'Create Sample Materials' 를 실행하세요.");
            return;
        }

        // 1) MaterialCatalog
        var cat = CreateOrLoad<MaterialCatalog>("MaterialCatalog");
        var cso = new SerializedObject(cat);
        var mats = cso.FindProperty("m_Materials");
        mats.arraySize = 3;
        mats.GetArrayElementAtIndex(0).objectReferenceValue = floor;
        mats.GetArrayElementAtIndex(1).objectReferenceValue = pillar;
        mats.GetArrayElementAtIndex(2).objectReferenceValue = wall;
        cso.ApplyModifiedProperties();
        EditorUtility.SetDirty(cat);

        // 2) 샘플 정답: Floor@(0,0,0) + Pillar@(1,0,0). 런타임과 같은 함수로 footprint 펼침.
        var cells = new System.Collections.Generic.List<AnswerCell>();
        AddObject(cells, floor,  new Vector3Int(0, 0, 0), 0);
        AddObject(cells, pillar, new Vector3Int(1, 0, 0), 0);

        var ans = CreateOrLoad<MapAnswerData>("SampleAnswer");
        var aso = new SerializedObject(ans);
        aso.FindProperty("m_GridSize").vector3IntValue = new Vector3Int(8, 4, 8);
        var arr = aso.FindProperty("m_Cells");
        arr.arraySize = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("cell").vector3IntValue = cells[i].cell;
            el.FindPropertyRelative("materialId").intValue = cells[i].materialId;
            el.FindPropertyRelative("rotationStep").intValue = cells[i].rotationStep;
        }
        aso.ApplyModifiedProperties();
        EditorUtility.SetDirty(ans);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GridSetup] MaterialCatalog + SampleAnswer 생성 완료.\n" +
                  "정답 = Floor@(0,0,0) + Pillar@(1,0,0)[고정 필요]. GridManager에 Catalog/Answer를 연결하세요.");
    }

    // ── 네트워크 그리드 씬 세팅 (G4.2) ───────────────────────────────────
    [MenuItem("Grid Setup/Setup Networked Grid In Active Scene")]
    static void SetupNetworkedGrid()
    {
        var floor  = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Floor.asset");
        var pillar = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Pillar.asset");
        var wall   = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Wall.asset");
        var cat    = AssetDatabase.LoadAssetAtPath<MaterialCatalog>($"{k_DataDir}/MaterialCatalog.asset");
        var ans    = AssetDatabase.LoadAssetAtPath<MapAnswerData>($"{k_DataDir}/SampleAnswer.asset");
        if (cat == null || ans == null)
        {
            Debug.LogError("[GridSetup] 먼저 'Create Sample Catalog + Answer' 를 실행하세요.");
            return;
        }

        var go = new GameObject("GridManager");
        go.AddComponent<Unity.Netcode.NetworkObject>();   // NetworkBehaviour 전에 추가
        var mgr = go.AddComponent<GridManager>();
        go.AddComponent<GridNetwork>();
        var dbg = go.AddComponent<GridDebugController>();
        go.AddComponent<AnswerPreview>();
        go.AddComponent<GameLoopManager>();
        go.AddComponent<MaterialDepot>();
        go.AddComponent<MaterialDropField>();

        var mso = new SerializedObject(mgr);
        mso.FindProperty("m_GridSize").vector3IntValue = new Vector3Int(8, 4, 8);
        mso.FindProperty("m_Catalog").objectReferenceValue = cat;
        mso.FindProperty("m_Answer").objectReferenceValue = ans;
        mso.ApplyModifiedProperties();

        var dso = new SerializedObject(dbg);
        var pal = dso.FindProperty("m_Palette");
        pal.arraySize = 3;
        pal.GetArrayElementAtIndex(0).objectReferenceValue = floor;
        pal.GetArrayElementAtIndex(1).objectReferenceValue = pillar;
        pal.GetArrayElementAtIndex(2).objectReferenceValue = wall;
        dso.ApplyModifiedProperties();

        Selection.activeGameObject = go;
        EnsureWorkstations();
        Debug.Log("[GridSetup] 네트워크 그리드 + 작업장 생성/배선 완료. 현재 씬에 배치 — 씬 저장 후 MPPM Host/Client로 테스트.");
    }

    static void EnsureWorkstations()
    {
        if (Object.FindFirstObjectByType<Player.Workstation>() != null) return;
        MakeWorkstation("HammerStation", ProcessType.Fixed,   new Vector3(-2f, 0.5f, 2f));
        MakeWorkstation("PaintStation",  ProcessType.Painted, new Vector3(-2f, 0.5f, 4f));
    }

    static void EnsureGameLoop()
    {
        var grid = Object.FindFirstObjectByType<GridManager>();
        if (grid != null && grid.GetComponent<GameLoopManager>() == null)
            grid.gameObject.AddComponent<GameLoopManager>();
    }

    static void EnsureDepot()
    {
        var grid = Object.FindFirstObjectByType<GridManager>();
        if (grid != null && grid.GetComponent<MaterialDepot>() == null)
            grid.gameObject.AddComponent<MaterialDepot>();
    }

    static void EnsureDropField()
    {
        var grid = Object.FindFirstObjectByType<GridManager>();
        if (grid != null && grid.GetComponent<MaterialDropField>() == null)
            grid.gameObject.AddComponent<MaterialDropField>();
    }

    static void MakeWorkstation(string name, ProcessType tool, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        var ws = go.AddComponent<Player.Workstation>();
        var so = new SerializedObject(ws);
        so.FindProperty("m_Tool").intValue = (int)tool;
        so.ApplyModifiedProperties();
    }

    // ── 한방 세팅: 현재 씬을 멀티 테스트 가능 상태로 ─────────────────────
    [MenuItem("Grid Setup/★ Setup Multiplayer Test (Active Scene)")]
    static void SetupMultiplayerTest()
    {
        // 1) 샘플 에셋 보장
        CreateSampleMaterials();
        CreateSampleCatalogAndAnswer();

        // 2) Main Camera에 CinemachineBrain (플레이어 vcam이 화면을 구동하도록)
        var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam != null && cam.GetComponent<Unity.Cinemachine.CinemachineBrain>() == null)
        {
            cam.gameObject.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            Debug.Log("[GridSetup] Main Camera에 CinemachineBrain 추가.");
        }

        // 3) 네트워크 그리드(없을 때만 생성)
        if (Object.FindFirstObjectByType<GridManager>() == null)
            SetupNetworkedGrid();
        else
            Debug.Log("[GridSetup] 기존 GridManager 발견 — 그리드 생성 생략.");

        EnsureWorkstations();
        EnsureGameLoop();
        EnsureDepot();
        EnsureDropField();

        // 4) PlayerUnit 프리팹에 PlayerCarry + Palette
        EnsurePlayerCarryOnPrefab();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("[GridSetup] ★ 멀티 테스트 세팅 완료. 씬 저장(Ctrl+S) 후, BootstrapScene에서 Play→Host/Client(MPPM).");
    }

    static void EnsurePlayerCarryOnPrefab()
    {
        const string prefabPath = "Assets/Player/Prefabs/PlayerUnit.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogWarning("[GridSetup] PlayerUnit.prefab 없음 — PlayerCarry 배선 생략.");
            return;
        }

        var floor  = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Floor.asset");
        var pillar = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Pillar.asset");
        var wall   = AssetDatabase.LoadAssetAtPath<MaterialDef>($"{k_DataDir}/Mat_Wall.asset");

        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var root = scope.prefabContentsRoot;
            var carry = root.GetComponent<Player.PlayerCarry>();
            if (carry == null) carry = root.AddComponent<Player.PlayerCarry>();

            var so = new SerializedObject(carry);
            var pal = so.FindProperty("m_Palette");
            pal.arraySize = 3;
            pal.GetArrayElementAtIndex(0).objectReferenceValue = floor;
            pal.GetArrayElementAtIndex(1).objectReferenceValue = pillar;
            pal.GetArrayElementAtIndex(2).objectReferenceValue = wall;
            so.ApplyModifiedProperties();
        }
        Debug.Log("[GridSetup] PlayerUnit 프리팹에 PlayerCarry + Palette 배선.");
    }

    // ── 한방 세팅 (카메라+그리드+플레이어) ───────────────────────────────

    static void AddObject(System.Collections.Generic.List<AnswerCell> cells, MaterialDef def, Vector3Int anchor, int rot)
    {
        foreach (var c in GridFootprint.EnumerateFootprintCells(anchor, def.Footprint, rot))
            cells.Add(new AnswerCell { cell = c, materialId = def.Id, rotationStep = (byte)rot });
    }

    static T CreateOrLoad<T>(string name) where T : ScriptableObject
    {
        string path = $"{k_DataDir}/{name}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }
}
