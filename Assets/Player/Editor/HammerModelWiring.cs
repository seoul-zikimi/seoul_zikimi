using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Player;
using GridSystem;

/// <summary>
/// '망치'(고정 도구) 외형을 Hammer.glb 모델로 배선하는 일회성 에디터 메뉴.
/// glb는 ScriptedImporter(glTFast) 임포트라 씬/프리팹 YAML 직접 참조가 어렵다 →
/// AssetDatabase로 안전하게 로드해 두 곳에 할당한다:
///   1) PlayerCarry.m_HammerModel       (PlayerUnit 프리팹) — 손에 든 망치
///   2) MaterialDropField.m_HammerModel  (활성 씬 GridManager) — 바닥에 버린/던진 망치
/// </summary>
public static class HammerModelWiring
{
    const string k_GlbPath    = "Assets/Map/00_Basic/Hammer.glb";
    const string k_PrefabPath = "Assets/Player/Prefabs/PlayerUnit.prefab";

    [MenuItem("Grid Setup/Apply Hammer Model (Held + Dropped)")]
    static void Apply()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_GlbPath);
        if (model == null) { Debug.LogError($"[HammerWiring] 모델 없음: {k_GlbPath}"); return; }

        // 1) 손에 든 망치 — PlayerUnit 프리팹의 PlayerCarry
        if (AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath) == null)
            Debug.LogWarning($"[HammerWiring] 프리팹 없음: {k_PrefabPath} — 손 망치 배선 생략.");
        else
            using (var scope = new PrefabUtility.EditPrefabContentsScope(k_PrefabPath))
            {
                var carry = scope.prefabContentsRoot.GetComponent<PlayerCarry>();
                if (carry == null) Debug.LogWarning("[HammerWiring] PlayerUnit 프리팹에 PlayerCarry 없음.");
                else
                {
                    var so = new SerializedObject(carry);
                    so.FindProperty("m_HammerModel").objectReferenceValue = model;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[HammerWiring] PlayerCarry.m_HammerModel ← Hammer.glb (손 망치).");
                }
            }

        // 2) 바닥에 버린/던진 망치 — 활성 씬의 MaterialDropField (GameScene 열려 있어야 함)
        var field = Object.FindFirstObjectByType<MaterialDropField>();
        if (field == null)
            Debug.LogWarning("[HammerWiring] 활성 씬에 MaterialDropField 없음 — GameScene을 열고 다시 실행하세요(바닥 망치 배선).");
        else
        {
            var so = new SerializedObject(field);
            so.FindProperty("m_HammerModel").objectReferenceValue = model;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(field);
            EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
            Debug.Log("[HammerWiring] MaterialDropField.m_HammerModel ← Hammer.glb (바닥 망치). 씬 저장(Ctrl+S) 필요.");
        }

        Debug.Log("[HammerWiring] 완료.");
    }
}
