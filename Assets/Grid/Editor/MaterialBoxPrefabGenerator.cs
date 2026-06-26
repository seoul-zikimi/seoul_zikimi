using UnityEditor;
using UnityEngine;
using GridSystem;

/// <summary>
/// 프리팹 없는 재료(Mat_Floor/Pillar/Wall …)에 footprint 크기의 '박스 프리팹'을 자동 생성·연결.
/// 박스 피벗 = 바닥-중심(자식 큐브 밑면이 루트 Y=0, X/Z 중앙) → GridNetwork.SpawnPrefabVisual /
/// AnswerPreview의 배치식 CellToWorld(minCell)+(dims.x,0,dims.z)*0.5u 과 정확히 정합.
/// 결과: 고스트(반투명)·놓은 블록(솔리드)·들기가 모두 같은 모델로 보인다.
/// </summary>
public static class MaterialBoxPrefabGenerator
{
    const string kPrefabDir = "Assets/Grid/Prefabs";
    const string kMatPath   = kPrefabDir + "/Mat_BlockBox.mat";

    [MenuItem("Grid Setup/Generate Box Prefabs for Materials")]
    static void Generate()
    {
        var boxMat = AssetDatabase.LoadAssetAtPath<Material>(kMatPath);
        if (boxMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogError("[Grid] URP Lit 셰이더를 찾지 못함."); return; }
            boxMat = new Material(sh);
            boxMat.SetColor("_BaseColor", new Color(0.85f, 0.83f, 0.75f));
            AssetDatabase.CreateAsset(boxMat, kMatPath);
        }

        int n = 0;
        n += Make("Assets/Grid/Data/Mat_Floor.asset",  "Box_Floor",  boxMat) ? 1 : 0;
        n += Make("Assets/Grid/Data/Mat_Pillar.asset", "Box_Pillar", boxMat) ? 1 : 0;
        n += Make("Assets/Grid/Data/Mat_Wall.asset",   "Box_Wall",   boxMat) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Grid] ✅ 박스 프리팹 {n}개 생성 + 재료에 연결 완료. 다음 플레이부터 고스트=놓은블록 동일 모델.");
    }

    // 재료 1개 → footprint 크기 박스 프리팹 생성 후 m_Prefab에 연결.
    static bool Make(string defPath, string prefabName, Material mat)
    {
        var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(defPath);
        if (def == null) { Debug.LogError($"[Grid] {defPath} 없음 — 스킵."); return false; }

        var fp = def.Footprint;
        int fx = Mathf.Max(1, fp.x), fy = Mathf.Max(1, fp.y), fz = Mathf.Max(1, fp.z);

        // 루트(피벗=min-corner: 로컬 0,0,0 = 점유칸 최솟값 모서리) + 자식 큐브(footprint 크기, +방향으로 채움)
        // → GridNetwork/AnswerPreview의 배치(CellToWorld(minCell), 오프셋 없음) 및 ~Solid 콜라이더와 정합.
        var root = new GameObject(prefabName);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Box";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale    = new Vector3(fx, fy, fz);
        cube.transform.localPosition = new Vector3(fx * 0.5f, fy * 0.5f, fz * 0.5f);   // min-corner를 루트 0,0,0에 맞춤
        Object.DestroyImmediate(cube.GetComponent<Collider>());          // 배치 시 콜라이더는 어차피 제거됨(~Solid가 담당)
        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;

        string prefabPath = $"{kPrefabDir}/{prefabName}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        if (prefab == null) { Debug.LogError($"[Grid] {prefabPath} 저장 실패."); return false; }

        var so = new SerializedObject(def);
        so.FindProperty("m_Prefab").objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        return true;
    }
}
