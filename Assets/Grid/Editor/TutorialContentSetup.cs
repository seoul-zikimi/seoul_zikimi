using System.IO;
using GridSystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 "재료(MaterialDef+프리팹)"와 "정답(MapAnswerData)"을 전부 자동으로 만드는 1회성 에디터 툴.
/// GLB 모델을 Footprint 박스에 자동으로 맞춰 프리팹화하고, MaterialDef 필드를 채우고,
/// MaterialCatalog에 등록하고, Answer_Tutorial.asset을 직접 코드로 만들어 Map_Tutorial에 연결까지 한다.
/// AnswerAuthoring 씬에서 손으로 칠할 필요가 없다 — 이미 정해진 좌표를 그대로 굽는 방식.
/// 실행: 메뉴 Jobsnail ▸ Tutorial ▸ Setup Tutorial Content (전체 자동).
/// 다시 실행해도 안전(멱등) — 기존 값을 덮어쓸 뿐 중복 등록하지 않는다.
/// </summary>
public static class TutorialContentSetup
{
    // ── 경로 ──
    private const string kWallGlbPath = "Assets/Map/00_Tutorial/벽.glb";
    private const string kDoorGlbPath = "Assets/Map/00_Tutorial/문이 있는 벽.glb";
    private const string kRoofGlbPath = "Assets/Map/00_Tutorial/지붕.glb";

    private const string kWallMatPath = "Assets/Grid/Data/Mat_TutorialWall.asset";
    private const string kDoorMatPath = "Assets/Grid/Data/Mat_TutorialWallDoor.asset";
    private const string kRoofMatPath = "Assets/Grid/Data/Mat_TutorialRoof.asset";

    private const string kPrefabDir = "Assets/Grid/Prefabs/";
    private const string kCatalogPath = "Assets/Grid/Data/MaterialCatalog.asset";
    private const string kAnswerPath = "Assets/Grid/Data/Answer_Tutorial.asset";
    private const string kMapDefPath = "Assets/Map/Maps/Map_Tutorial.asset";

    // ── 재료 ID ──
    private const int kWallId = 10;
    private const int kDoorId = 11;
    private const int kRoofId = 12;

    // ── 정답 그리드 레이아웃 (4×3×4, 좌/우 X 극단·앞/뒤 Z 극단 — TutorialQuestSequence의 군집 판정과 매칭) ──
    private static readonly Vector3Int kGridSize = new(4, 3, 4);
    private static readonly Vector3Int[] kDoorCells = { new(1, 0, 0), new(2, 0, 0) };   // 뒷벽(문벽, preset)
    private static readonly Vector3Int[] kLeftCells = { new(0, 0, 0), new(0, 0, 1) };   // 왼쪽 벽(X=0 끝)
    private static readonly Vector3Int[] kRightCells = { new(3, 0, 0), new(3, 0, 1) };  // 오른쪽 벽(X=3 끝)
    private static readonly Vector3Int[] kFrontCells = { new(1, 0, 3), new(2, 0, 3) };  // 앞쪽 벽(Z=3 끝)

    [MenuItem("Jobsnail/Tutorial/Setup Tutorial Content (전체 자동)")]
    public static void SetupAll()
    {
        var wallPrefab = WrapModelToFootprint(kWallGlbPath, "Box_TutorialWall", new Vector3Int(2, 1, 1));
        var doorPrefab = WrapModelToFootprint(kDoorGlbPath, "Box_TutorialWallDoor", new Vector3Int(2, 1, 1));
        var roofPrefab = WrapModelToFootprint(kRoofGlbPath, "Box_TutorialRoof", new Vector3Int(4, 1, 4));

        ConfigureMaterial(kWallMatPath, kWallId, new Vector3Int(2, 1, 1), wallPrefab, requireFixed: true, mustBeFixed: true);
        ConfigureMaterial(kDoorMatPath, kDoorId, new Vector3Int(2, 1, 1), doorPrefab, requireFixed: false, mustBeFixed: false);
        ConfigureMaterial(kRoofMatPath, kRoofId, new Vector3Int(4, 1, 4), roofPrefab, requireFixed: false, mustBeFixed: false);

        RegisterInCatalog(kWallMatPath);
        RegisterInCatalog(kDoorMatPath);
        RegisterInCatalog(kRoofMatPath);

        BuildAnswer();
        LinkAnswerToMap();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TutorialContentSetup] 완료: 프리팹 3개 + MaterialDef 3개 + MaterialCatalog 등록 + Answer_Tutorial 생성 + Map_Tutorial 연결");
    }

    // GLB를 인스턴스화해 렌더러 바운즈를 측정하고, 바운즈의 최소 모서리가 (0,0,0)에 오도록,
    // 크기가 정확히 Footprint가 되도록 위치/스케일을 계산해 프리팹으로 저장한다(수동 정렬 불필요).
    private static GameObject WrapModelToFootprint(string glbPath, string prefabName, Vector3Int footprint)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
        if (model == null)
        {
            Debug.LogError($"[TutorialContentSetup] 모델을 찾지 못함: {glbPath}");
            return null;
        }

        var root = new GameObject(prefabName);
        var instance = Object.Instantiate(model, root.transform);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError($"[TutorialContentSetup] {glbPath}에서 Renderer를 찾지 못함(임포트 확인 필요)");
            Object.DestroyImmediate(root);
            return null;
        }

        // root가 월드 원점 + identity이고 instance도 identity 상태라, 지금 시점의 world bounds == root 로컬 기준 크기.
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Vector3 size = bounds.size;
        Vector3 scale = new(
            size.x > 0.0001f ? footprint.x / size.x : 1f,
            size.y > 0.0001f ? footprint.y / size.y : 1f,
            size.z > 0.0001f ? footprint.z / size.z : 1f);

        instance.transform.localScale = scale;
        instance.transform.localPosition = new Vector3(-bounds.min.x * scale.x, -bounds.min.y * scale.y, -bounds.min.z * scale.z);

        string path = kPrefabDir + prefabName + ".prefab";
        Directory.CreateDirectory(kPrefabDir);
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static void ConfigureMaterial(string assetPath, int id, Vector3Int footprint, GameObject prefab, bool requireFixed, bool mustBeFixed)
    {
        var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(assetPath);
        if (def == null)
        {
            Debug.LogError($"[TutorialContentSetup] MaterialDef를 찾지 못함: {assetPath}");
            return;
        }

        var so = new SerializedObject(def);
        so.FindProperty("m_Id").intValue = id;

        var fp = so.FindProperty("m_Footprint");
        fp.FindPropertyRelative("x").intValue = footprint.x;
        fp.FindPropertyRelative("y").intValue = footprint.y;
        fp.FindPropertyRelative("z").intValue = footprint.z;

        so.FindProperty("m_Prefab").objectReferenceValue = prefab;

        var procs = so.FindProperty("m_RequiredProcesses");
        procs.arraySize = requireFixed ? 1 : 0;
        if (requireFixed)
            procs.GetArrayElementAtIndex(0).intValue = (int)ProcessType.Fixed;

        so.FindProperty("m_MustBeFixed").boolValue = mustBeFixed;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(def);
    }

    private static void RegisterInCatalog(string materialAssetPath)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kCatalogPath);
        var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(materialAssetPath);
        if (catalog == null || def == null) return;

        var so = new SerializedObject(catalog);
        var list = so.FindProperty("m_Materials");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == def)
                return;   // 이미 등록됨

        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = def;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    // AnswerAuthoring 씬에서 손으로 칠하는 대신, 정해둔 좌표를 그대로 MapAnswerData에 굽는다.
    private static void BuildAnswer()
    {
        var wallDef = AssetDatabase.LoadAssetAtPath<MaterialDef>(kWallMatPath);
        var doorDef = AssetDatabase.LoadAssetAtPath<MaterialDef>(kDoorMatPath);
        var roofDef = AssetDatabase.LoadAssetAtPath<MaterialDef>(kRoofMatPath);
        if (wallDef == null || doorDef == null || roofDef == null)
        {
            Debug.LogError("[TutorialContentSetup] MaterialDef를 먼저 설정해야 Answer를 만들 수 있습니다.");
            return;
        }

        var cells = new System.Collections.Generic.List<AnswerCell>();
        foreach (var c in kDoorCells) cells.Add(new AnswerCell { cell = c, materialId = doorDef.Id, rotationStep = 0 });
        foreach (var c in kLeftCells) cells.Add(new AnswerCell { cell = c, materialId = wallDef.Id, rotationStep = 1 });
        foreach (var c in kRightCells) cells.Add(new AnswerCell { cell = c, materialId = wallDef.Id, rotationStep = 1 });
        foreach (var c in kFrontCells) cells.Add(new AnswerCell { cell = c, materialId = wallDef.Id, rotationStep = 0 });
        for (int x = 0; x < kGridSize.x; x++)
            for (int z = 0; z < kGridSize.z; z++)
                cells.Add(new AnswerCell { cell = new Vector3Int(x, 1, z), materialId = roofDef.Id, rotationStep = 0 });

        var answer = AssetDatabase.LoadAssetAtPath<MapAnswerData>(kAnswerPath);
        bool isNew = answer == null;
        if (isNew) answer = ScriptableObject.CreateInstance<MapAnswerData>();

        var so = new SerializedObject(answer);
        var gs = so.FindProperty("m_GridSize");
        gs.FindPropertyRelative("x").intValue = kGridSize.x;
        gs.FindPropertyRelative("y").intValue = kGridSize.y;
        gs.FindPropertyRelative("z").intValue = kGridSize.z;

        var cellsProp = so.FindProperty("m_Cells");
        cellsProp.arraySize = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            var elem = cellsProp.GetArrayElementAtIndex(i);
            var cellVec = elem.FindPropertyRelative("cell");
            cellVec.FindPropertyRelative("x").intValue = cells[i].cell.x;
            cellVec.FindPropertyRelative("y").intValue = cells[i].cell.y;
            cellVec.FindPropertyRelative("z").intValue = cells[i].cell.z;
            elem.FindPropertyRelative("materialId").intValue = cells[i].materialId;
            elem.FindPropertyRelative("rotationStep").intValue = cells[i].rotationStep;
        }

        var presetProp = so.FindProperty("m_PresetCells");
        presetProp.arraySize = kDoorCells.Length;
        for (int i = 0; i < kDoorCells.Length; i++)
        {
            var elem = presetProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("x").intValue = kDoorCells[i].x;
            elem.FindPropertyRelative("y").intValue = kDoorCells[i].y;
            elem.FindPropertyRelative("z").intValue = kDoorCells[i].z;
        }

        so.FindProperty("m_TimeLimitSeconds").floatValue = 9999999f;
        so.FindProperty("m_DisplayName").stringValue = "튜토리얼 집";
        so.ApplyModifiedProperties();

        if (isNew)
            AssetDatabase.CreateAsset(answer, kAnswerPath);
        EditorUtility.SetDirty(answer);
    }

    private static void LinkAnswerToMap()
    {
        var mapDef = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath);
        var answer = AssetDatabase.LoadAssetAtPath<MapAnswerData>(kAnswerPath);
        if (mapDef == null || answer == null) return;

        var so = new SerializedObject(mapDef);
        var list = so.FindProperty("m_Answers");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == answer)
                return;   // 이미 연결됨

        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = answer;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(mapDef);
    }
}
