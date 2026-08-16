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
    // 주의: Assets/Grid/Data/MaterialCatalog.asset은 실제로 GameScene에 연결된 카탈로그가 아니다(미사용 잔재).
    // GameScene의 GridManager.m_Catalog가 실제 참조하는 건 이 파일 — 반드시 여기에 등록해야 런타임에 인식된다.
    private const string kCatalogPath = "Assets/Prefabs/Map/1_KwangTongGyo/1_GwangTongGyo_MaterialCatalog.asset";
    private const string kAnswerPath = "Assets/Grid/Data/Answer_Tutorial.asset";
    private const string kMapDefPath = "Assets/Map/Maps/Map_Tutorial.asset";

    // ── 재료 ID ──
    private const int kWallId = 10;
    private const int kDoorId = 11;
    private const int kRoofId = 12;

    // ── 정답 그리드 레이아웃 (2×3×2, 벽 4개가 빈틈없이 붙은 정사각형 + 지붕) ──
    // 사용자가 배경에서 직접 검증한 배치(중심 기준 벽 간격 ~1칸, 지붕 스케일 1.7배)를 그대로 그리드 1칸 단위로 옮김.
    // 벽 Footprint를 1×1×1로 줄여서(기존 2×1×1은 실제 모델 크기보다 훨씬 커서 벽 사이가 벌어져 보였음)
    // TutorialQuestSequence.cs의 상수(kLeftWallCell 등)와 반드시 같은 값으로 유지해야 한다.
    private static readonly Vector3Int kGridSize = new(4, 3, 4);
    private static readonly Vector3Int kDoorCell  = new(0, 0, 0);   // 뒷벽(문벽, preset)
    private static readonly Vector3Int kLeftCell  = new(0, 0, 1);   // 왼쪽 벽
    private static readonly Vector3Int kRightCell = new(1, 0, 0);   // 오른쪽 벽
    private static readonly Vector3Int kFrontCell = new(1, 0, 1);   // 앞쪽 벽
    private static readonly Vector3Int[] kRoofCells =
    {
        new(0, 1, 0), new(1, 1, 0), new(0, 1, 1), new(1, 1, 1),
    };
    private static readonly Vector3Int kWallFootprint = new(1, 1, 1);
    private static readonly Vector3Int kRoofFootprint = new(2, 1, 2);

    // 배경 프리팹(MapBg_Tutorial)에서 사용자가 이미 손으로 맞춰둔 회전/스케일 값 그대로 재사용한다
    // (FrontWall/BackWall 기준 — Footprint에 억지로 늘려 맞추지 않아 원본 모델이 찌그러지지 않는다).
    private static readonly Quaternion kWallModelRotation = new(0.7071068f, 0f, 0f, 0.7071068f);   // 90° X축
    private static readonly Vector3 kWallModelScale = Vector3.one;
    private static readonly Vector3 kDoorModelScale = new(1f, 1f, 1.05f);
    private static readonly Vector3 kRoofModelScale = new(1.7f, 1.7f, 1f);

    [MenuItem("Jobsnail/Tutorial/Setup Tutorial Content (전체 자동)")]
    public static void SetupAll()
    {
        var wallPrefab = WrapModelToFootprint(kWallGlbPath, "Box_TutorialWall", kWallFootprint, kWallModelRotation, kWallModelScale);
        var doorPrefab = WrapModelToFootprint(kDoorGlbPath, "Box_TutorialWallDoor", kWallFootprint, kWallModelRotation, kDoorModelScale);
        var roofPrefab = WrapModelToFootprint(kRoofGlbPath, "Box_TutorialRoof", kRoofFootprint, kWallModelRotation, kRoofModelScale);

        ConfigureMaterial(kWallMatPath, kWallId, kWallFootprint, wallPrefab, requireFixed: true, mustBeFixed: true);
        ConfigureMaterial(kDoorMatPath, kDoorId, kWallFootprint, doorPrefab, requireFixed: false, mustBeFixed: false);
        ConfigureMaterial(kRoofMatPath, kRoofId, kRoofFootprint, roofPrefab, requireFixed: false, mustBeFixed: false);

        RegisterInCatalog(kWallMatPath);
        RegisterInCatalog(kDoorMatPath);
        RegisterInCatalog(kRoofMatPath);

        BuildAnswer();
        LinkAnswerToMap();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TutorialContentSetup] 완료: 프리팹 3개 + MaterialDef 3개 + MaterialCatalog 등록 + Answer_Tutorial 생성 + Map_Tutorial 연결");
    }

    // GLB를 인스턴스화해 지정된 회전/스케일(배경에서 이미 검증된 값)을 그대로 적용하고,
    // Footprint 박스에 억지로 늘리는 대신 XZ 중앙·바닥(Y=0)에만 맞춰 위치를 이동한다.
    // → 모델이 찌그러지지 않고, 배경에 있던 것과 똑같은 비율로 정답/고스트에 보인다.
    private static GameObject WrapModelToFootprint(string glbPath, string prefabName, Vector3Int footprint, Quaternion modelRotation, Vector3 modelScale)
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
        instance.transform.localRotation = modelRotation;
        instance.transform.localScale = modelScale;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError($"[TutorialContentSetup] {glbPath}에서 Renderer를 찾지 못함(임포트 확인 필요)");
            Object.DestroyImmediate(root);
            return null;
        }

        // root가 월드 원점에 있으므로 지금 시점의 world bounds == root 로컬 기준 크기.
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Vector3 targetCenter = new(footprint.x * 0.5f, 0f, footprint.z * 0.5f);
        Vector3 offset = new(targetCenter.x - bounds.center.x, -bounds.min.y, targetCenter.z - bounds.center.z);
        instance.transform.localPosition += offset;

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

        var cells = new System.Collections.Generic.List<AnswerCell>
        {
            new() { cell = kDoorCell,  materialId = doorDef.Id, rotationStep = 0 },
            new() { cell = kLeftCell,  materialId = wallDef.Id, rotationStep = 1 },
            new() { cell = kRightCell, materialId = wallDef.Id, rotationStep = 1 },
            new() { cell = kFrontCell, materialId = wallDef.Id, rotationStep = 0 },
        };
        foreach (var c in kRoofCells)
            cells.Add(new AnswerCell { cell = c, materialId = roofDef.Id, rotationStep = 0 });

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
        presetProp.arraySize = 1;
        var presetElem = presetProp.GetArrayElementAtIndex(0);
        presetElem.FindPropertyRelative("x").intValue = kDoorCell.x;
        presetElem.FindPropertyRelative("y").intValue = kDoorCell.y;
        presetElem.FindPropertyRelative("z").intValue = kDoorCell.z;

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
