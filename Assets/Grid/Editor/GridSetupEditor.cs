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
