using UnityEditor;
using UnityEngine;

/// <summary>
/// .glb 프리팹의 피벗을 "바닥면 XZ min-corner"로 자동 정렬하는 에디터 툴.
/// Autotiles3D는 피벗 위치에 타일을 놓으므로, 피벗=min-corner여야 anchor와 일치한다.
/// 사용법: Hierarchy에서 Wrapper 오브젝트 선택 → Grid Setup/피벗 자동 정렬
/// </summary>
public static class PivotAligner
{
    [MenuItem("Grid Setup/피벗 자동 정렬 (선택된 오브젝트)")]
    static void AlignPivot()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("피벗 정렬", "Hierarchy에서 Wrapper 오브젝트를 먼저 선택하세요.", "확인");
            return;
        }

        // 자식 Renderer들의 합산 바운드 계산
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("피벗 정렬", "선택한 오브젝트에 Renderer가 없습니다.\n자식에 .glb 모델이 있는지 확인하세요.", "확인");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        // 목표: 바운드 바닥면 XZ min-corner가 root의 (0,0,0)에 오도록
        // Autotiles3D는 피벗 위치 = InternalPosition이므로 피벗=min-corner여야 anchor와 일치
        Vector3 offset = new Vector3(
            root.transform.position.x - bounds.min.x,          // XZ min-corner 맞춤
            root.transform.position.y - bounds.min.y,          // 바닥면 Y=0 맞춤
            root.transform.position.z - bounds.min.z
        );

        Undo.RecordObjects(new Object[] { root.transform }, "피벗 자동 정렬");
        foreach (Transform child in root.transform)
        {
            Undo.RecordObject(child, "피벗 자동 정렬");
            child.position += offset;
        }

        // 정렬 후 바운드 재계산 → 권장 Footprint 출력
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        int fx = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x));
        int fy = Mathf.Max(1, Mathf.RoundToInt(bounds.size.y));
        int fz = Mathf.Max(1, Mathf.RoundToInt(bounds.size.z));

        Debug.Log($"[PivotAligner] '{root.name}' 정렬 완료!\n" +
                  $"모델 크기: {bounds.size.x:F2} x {bounds.size.y:F2} x {bounds.size.z:F2} (월드 유닛)\n" +
                  $"▶ MaterialDef Footprint 권장값: X={fx}, Y={fy}, Z={fz}");
    }

    [MenuItem("Grid Setup/피벗 자동 정렬 (선택된 오브젝트)", true)]
    static bool AlignPivotValidate() => Selection.activeGameObject != null;
}
