using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Player;
using GridSystem;

/// <summary>
/// '양동이'(화마 진화 도구 — 경복궁) 외형 3종을 VARCO glb로 배선하는 에디터 메뉴.
/// Hammer/PaintCan 배선과 같은 흐름이지만, VARCO glb는 크기가 제각각이라
/// 최장변 1m·중심 피벗으로 정규화한 래퍼 프리팹(_Fit)을 만들어 그걸 참조한다:
///   1) PlayerCarry.m_BucketModel        (PlayerUnit 프리팹) — 손에 든 물 찬 양동이
///   2) PlayerCarry.m_BucketEmptyModel   (PlayerUnit 프리팹) — 손에 든 빈 양동이(물 유무 모델 스왑)
///   3) MaterialDropField.m_BucketModel  (활성 씬 GridManager) — 바닥에 버린 양동이(항상 빈 상태)
///   4) BucketStation 프리팹의 Workstation.m_StationModel — 도구함(양동이 무더기)
/// 없는 glb는 건너뛴다(부분 적용 가능). glb를 다시 뽑으면 이 메뉴만 재실행(멱등 — _Fit 덮어쓰기).
/// </summary>
public static class BucketModelWiring
{
    const string k_ModelDir     = "Assets/Prefabs/Map/3_Gyeongbokgung/Models";
    const string k_GlbFull      = k_ModelDir + "/경복궁_물양동이.glb";
    const string k_GlbEmpty     = k_ModelDir + "/경복궁_빈양동이.glb";
    const string k_GlbStack     = k_ModelDir + "/경복궁_양동이무더기.glb";
    // _Fit은 Resources에 둔다 — 씬/프리팹 배선이 비어 있어도 런타임이 Resources.Load로 폴백(씬 저장 누락 사고 방지)
    const string k_FitFull      = "Assets/Resources/Tools/BucketModel_Fit.prefab";
    const string k_FitEmpty     = "Assets/Resources/Tools/BucketEmpty_Fit.prefab";
    const string k_FitStack     = "Assets/Resources/Tools/BucketStack_Fit.prefab";
    const string k_PrefabPath   = "Assets/Player/Prefabs/PlayerUnit.prefab";
    const string k_StationPath  = "Assets/Resources/SystemObjects/BucketStation.prefab";
    static readonly string[] k_OldFits =   // 예전 위치 정리(중복 혼동 방지)
    {
        "Assets/Player/Prefabs/BucketModel_Fit.prefab",
        "Assets/Player/Prefabs/BucketEmpty_Fit.prefab",
        "Assets/Player/Prefabs/BucketStack_Fit.prefab",
    };

    [MenuItem("Grid Setup/Apply Bucket Model (Held + Dropped + Station)")]
    static void Apply()
    {
        System.IO.Directory.CreateDirectory("Assets/Resources/Tools");
        foreach (var old in k_OldFits)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(old) != null) AssetDatabase.DeleteAsset(old);

        var fitFull  = BuildFit(k_GlbFull, k_FitFull, "BucketModel_Fit");
        var fitEmpty = BuildFit(k_GlbEmpty, k_FitEmpty, "BucketEmpty_Fit");
        var fitStack = BuildFit(k_GlbStack, k_FitStack, "BucketStack_Fit");
        if (fitFull == null && fitEmpty == null && fitStack == null)
        { Debug.LogError("[BucketWiring] 배선할 glb가 하나도 없음 — Models 폴더 확인."); return; }

        // 1)·2) 손에 든 양동이(찬/빈) — PlayerUnit 프리팹의 PlayerCarry
        if (AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath) == null)
            Debug.LogWarning($"[BucketWiring] 프리팹 없음: {k_PrefabPath} — 손 양동이 배선 생략.");
        else
            using (var scope = new PrefabUtility.EditPrefabContentsScope(k_PrefabPath))
            {
                var carry = scope.prefabContentsRoot.GetComponent<PlayerCarry>();
                if (carry == null) Debug.LogWarning("[BucketWiring] PlayerUnit 프리팹에 PlayerCarry 없음.");
                else
                {
                    var so = new SerializedObject(carry);
                    if (fitFull != null)  so.FindProperty("m_BucketModel").objectReferenceValue = fitFull;
                    if (fitEmpty != null) so.FindProperty("m_BucketEmptyModel").objectReferenceValue = fitEmpty;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[BucketWiring] PlayerCarry ← 찬:{fitFull != null} 빈:{fitEmpty != null} (손 양동이).");
                }
            }

        // 3) 바닥에 버린 양동이 — 항상 빈 상태로 떨어지므로 빈 모델(없으면 찬 모델) 배선. GameScene 열려 있어야 함.
        var dropModel = fitEmpty != null ? fitEmpty : fitFull;
        var field = Object.FindFirstObjectByType<MaterialDropField>();
        if (field == null)
            Debug.LogWarning("[BucketWiring] 활성 씬에 MaterialDropField 없음 — GameScene을 열고 다시 실행하세요(바닥 양동이 배선).");
        else if (dropModel != null)
        {
            var so = new SerializedObject(field);
            so.FindProperty("m_BucketModel").objectReferenceValue = dropModel;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(field);
            EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
            Debug.Log($"[BucketWiring] MaterialDropField.m_BucketModel ← {dropModel.name} (바닥 양동이). 씬 저장(Ctrl+S) 필요.");
        }

        // 4) 도구함(양동이 무더기) — BucketStation 프리팹의 Workstation
        if (fitStack != null)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_StationPath) == null)
                Debug.LogWarning($"[BucketWiring] 프리팹 없음: {k_StationPath} — 'Tools ▸ Map ▸ 시스템 오브젝트 프리팹 만들기' 먼저 실행.");
            else
                using (var scope = new PrefabUtility.EditPrefabContentsScope(k_StationPath))
                {
                    var ws = scope.prefabContentsRoot.GetComponent<Workstation>();
                    if (ws == null) Debug.LogWarning("[BucketWiring] BucketStation 프리팹에 Workstation 없음.");
                    else
                    {
                        var so = new SerializedObject(ws);
                        so.FindProperty("m_StationModel").objectReferenceValue = fitStack;
                        so.FindProperty("m_StationModelScale").floatValue = 1.3f;   // 도구함 존재감(망치·페인트함과 비슷한 체급)
                        so.FindProperty("m_StationModelEuler").vector3Value = Vector3.zero;   // _Fit은 정방향 정규화라 회전 보정 불필요
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log("[BucketWiring] BucketStation.Workstation.m_StationModel ← BucketStack_Fit (도구함).");
                    }
                }
        }

        Debug.Log("[BucketWiring] 완료.");
    }

    // glb를 최장변 1m·중심 피벗으로 정규화한 래퍼 프리팹 생성(멱등 덮어쓰기). glb 없으면 null(건너뜀).
    // PlayerCarry가 루트에 m_ToolModelScale(0.4)을 곱하므로 Hammer.glb(≈1m 규격)와 체감 크기가 맞는다.
    static GameObject BuildFit(string glbPath, string fitPath, string rootName)
    {
        var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
        if (glb == null) { Debug.Log($"[BucketWiring] glb 없음(건너뜀): {glbPath}"); return null; }

        var root = new GameObject(rootName);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(glb, root.transform);

        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
        {
            Object.DestroyImmediate(root);
            Debug.LogError($"[BucketWiring] glb에 렌더러 없음 — 임포트 상태 확인: {glbPath}");
            return null;
        }
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 1e-4f) inst.transform.localScale = Vector3.one / longest;

        b = rends[0].bounds;   // 스케일 반영된 바운즈로 중심 재정렬
        foreach (var r in rends) b.Encapsulate(r.bounds);
        inst.transform.localPosition = -b.center;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, fitPath);
        Object.DestroyImmediate(root);
        if (prefab == null) Debug.LogError($"[BucketWiring] _Fit 저장 실패: {fitPath}");
        return prefab;
    }
}
