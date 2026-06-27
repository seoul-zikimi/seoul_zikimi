using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Player;
using GridSystem;

/// <summary>
/// '페인트통'(페인트 도구) 외형을 PaintCan.glb 모델로 배선하는 일회성 에디터 메뉴.
/// HammerModelWiring과 동일 구조 — glb는 ScriptedImporter라 YAML 직접 참조가 어려워 AssetDatabase로 로드해 두 곳에 할당:
///   1) PlayerCarry.m_PaintCanModel       (PlayerUnit 프리팹) — 손에 든 페인트통
///   2) MaterialDropField.m_PaintCanModel  (활성 씬 GridManager) — 바닥에 버린/던진 페인트통
/// </summary>
public static class PaintCanModelWiring
{
    const string k_GlbPath    = "Assets/Map/00_Basic/PaintCan.glb";
    const string k_PrefabPath = "Assets/Player/Prefabs/PlayerUnit.prefab";

    [MenuItem("Grid Setup/Apply Paint Can Model (Held + Dropped)")]
    static void Apply()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_GlbPath);
        if (model == null) { Debug.LogError($"[PaintCanWiring] 모델 없음: {k_GlbPath}"); return; }

        // 1) 손에 든 페인트통 — PlayerUnit 프리팹의 PlayerCarry
        if (AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath) == null)
            Debug.LogWarning($"[PaintCanWiring] 프리팹 없음: {k_PrefabPath} — 손 페인트통 배선 생략.");
        else
            using (var scope = new PrefabUtility.EditPrefabContentsScope(k_PrefabPath))
            {
                var carry = scope.prefabContentsRoot.GetComponent<PlayerCarry>();
                if (carry == null) Debug.LogWarning("[PaintCanWiring] PlayerUnit 프리팹에 PlayerCarry 없음.");
                else
                {
                    var so = new SerializedObject(carry);
                    so.FindProperty("m_PaintCanModel").objectReferenceValue = model;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[PaintCanWiring] PlayerCarry.m_PaintCanModel ← PaintCan.glb (손 페인트통).");
                }
            }

        // 2) 바닥에 버린/던진 페인트통 — 활성 씬의 MaterialDropField (GameScene 열려 있어야 함)
        var field = Object.FindFirstObjectByType<MaterialDropField>();
        if (field == null)
            Debug.LogWarning("[PaintCanWiring] 활성 씬에 MaterialDropField 없음 — GameScene을 열고 다시 실행하세요(바닥 페인트통 배선).");
        else
        {
            var so = new SerializedObject(field);
            so.FindProperty("m_PaintCanModel").objectReferenceValue = model;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(field);
            EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
            Debug.Log("[PaintCanWiring] MaterialDropField.m_PaintCanModel ← PaintCan.glb (바닥 페인트통). 씬 저장(Ctrl+S) 필요.");
        }

        Debug.Log("[PaintCanWiring] 완료.");
    }
}
