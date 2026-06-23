using UnityEditor;
using UnityEngine;

/// <summary>
/// WindingWaterAuthoring 의 기획자용 인스펙터.
/// - 물 머티리얼 비어 있으면 Toon Water 자동 지정
/// - 안내 문구 + "다시 그리기" 버튼
/// - Hierarchy 우클릭 ▸ 3D Object ▸ 구불구불 물길 로 바로 생성
/// </summary>
[CustomEditor(typeof(WindingWaterAuthoring))]
public class WindingWaterAuthoringEditor : Editor
{
    const string kWaterMat = "Assets/ThirdParty/Toon Water URP/Toon Water Material 1.mat";

    public override void OnInspectorGUI()
    {
        var t = (WindingWaterAuthoring)target;

        // 물 머티리얼 자동 지정(처음 한 번)
        if (t.waterMaterial == null)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(kWaterMat);
            if (m != null) { t.waterMaterial = m; EditorUtility.SetDirty(t); }
        }

        EditorGUILayout.HelpBox(
            "슬라이더를 움직이면 씬에서 물길이 바로 다시 그려져요.\n" +
            "원하는 모양이 되면 그대로 두면 됩니다. (생성물은 자동 관리)",
            MessageType.Info);

        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("물길 다시 그리기 (Rebuild)", GUILayout.Height(28)))
            t.Rebuild();
    }

    [MenuItem("GameObject/3D Object/구불구불 물길 (Winding Water)", false, 10)]
    static void Create(MenuCommand cmd)
    {
        var go = new GameObject("WindingWater");
        go.AddComponent<WindingWaterAuthoring>();
        GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Winding Water");
        Selection.activeObject = go;
    }
}
