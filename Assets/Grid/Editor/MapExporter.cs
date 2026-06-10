using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Autotiles3D;
using GridSystem;

/// <summary>
/// 정답 맵 익스포터 / 정합성 검증 (에디터 전용).
/// Assets/Grid/Editor 에 위치 → Assembly-CSharp-Editor 로 컴파일 →
/// Autotiles3D(Assembly-CSharp) + GridSystem(auto-ref) 둘 다 참조 가능.
/// </summary>
public static class MapExporter
{
    /// <summary>
    /// V0 게이트. 열린 씬의 Autotiles3D_Grid에 배치된 모든 셀에 대해
    /// GridCoordinates.CellToWorld(cell) == grid.ToWorldPoint(cell) 인지 검증한다.
    /// 통과 = (A)오서링 좌표 == (B)런타임 좌표 → 채점 1:1 비교 성립.
    /// </summary>
    [MenuItem("Grid Setup/Verify Alignment")]
    static void VerifyAlignment()
    {
        var grid = Object.FindFirstObjectByType<Autotiles3D_Grid>();
        if (grid == null)
        {
            Debug.LogError("[Grid] 열린 씬에 Autotiles3D_Grid가 없습니다.");
            return;
        }

        const float eps = 1e-3f;
        int w = Mathf.Max(1, grid.Width);
        int h = Mathf.Max(1, grid.Height);
        int count = 0, mismatch = 0;
        var sb = new StringBuilder();

        // 배치 없이도 검증: 그리드 셀 전체를 훑어 CellToWorld == ToWorldPoint 확인.
        // (ToWorldPoint는 셀의 순수 함수 → '놓인 타일'과 '빈 셀'에 동일하게 적용)
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        for (int z = 0; z < w; z++)
        {
            var cell = new Vector3Int(x, y, z);
            Vector3 ours = GridCoordinates.CellToWorld(cell);
            Vector3 auto = grid.ToWorldPoint(cell);
            count++;
            if ((ours - auto).sqrMagnitude > eps * eps)
            {
                mismatch++;
                if (mismatch <= 10)
                    sb.AppendLine($"  cell {cell}: CellToWorld {ours}  vs  ToWorldPoint {auto}");
            }
        }

        if (mismatch == 0)
            Debug.Log($"[Grid] ✅ 정합성 OK — 그리드 {w}x{h}x{w} 전 셀 CellToWorld == ToWorldPoint. (V0 통과)");
        else
            Debug.LogError($"[Grid] ❌ 정합성 실패 {mismatch}/{count}. 그리드 Transform=identity(pos 0/rot 0), Unit Scale=1 확인.\n{sb}");
    }

    /// <summary>
    /// (A) 오서링: 씬에서 Autotiles3D로 칠한 맵 → 정답(MapAnswerData)으로 익스포트.
    /// 칠해진 칸마다 AnswerCell(위치+재료+회전)을 만든다. 재료 매칭은
    ///   ① MaterialCatalog의 TileId→MaterialId 매핑, ② 타일 이름 ≈ MaterialDef 이름(fallback).
    /// 정답은 셀단위 재료 비교라 멀티칸도 칸별로 그대로 들어간다. ExportedAnswer.asset에 저장.
    /// </summary>
    [MenuItem("Grid Setup/Export Answer from Autotiles3D")]
    static void ExportAnswerFromAutotiles3D()
    {
        var grid = Object.FindFirstObjectByType<Autotiles3D_Grid>();
        if (grid == null)
        {
            Debug.LogError("[Grid] 씬에 Autotiles3D_Grid가 없습니다 — Autotiles3D로 맵을 칠한 씬에서 실행하세요.");
            return;
        }
        var mgr = Object.FindFirstObjectByType<GridManager>();
        var catalog = mgr != null ? mgr.Catalog : null;
        if (catalog == null)
        {
            Debug.LogError("[Grid] GridManager + MaterialCatalog가 필요합니다(재료 매칭).");
            return;
        }

        Vector3Int size = mgr.GridSize;
        var cells = new List<AnswerCell>();
        var unmapped = new Dictionary<string, int>();
        int oob = 0, placed = 0;

        foreach (var layer in grid.TileLayers)
        {
            if (layer == null) continue;
            foreach (var node in layer.GetAllInternalNodes())
            {
                placed++;
                int matId = ResolveMaterialId(catalog, node.TileID, node.TileName);
                if (matId == MaterialCatalog.NoMaterial)
                {
                    string k = $"{node.TileGroupName}/{node.TileName} (TileID={node.TileID})";
                    unmapped[k] = unmapped.TryGetValue(k, out var c) ? c + 1 : 1;
                    continue;
                }
                var cell = node.InternalPosition;
                if (cell.x < 0 || cell.x >= size.x || cell.y < 0 || cell.y >= size.y || cell.z < 0 || cell.z >= size.z)
                { oob++; continue; }
                cells.Add(new AnswerCell { cell = cell, materialId = matId, rotationStep = (byte)QuatToStep(node.LocalRotation) });
            }
        }

        if (unmapped.Count > 0)
        {
            var sb = new StringBuilder("[Grid] ⚠ 매핑 안 된 타일(스킵):\n");
            foreach (var kv in unmapped) sb.AppendLine($"  {kv.Key} x{kv.Value}");
            sb.AppendLine("→ 타일 이름을 MaterialDef 이름과 맞추거나, MaterialCatalog의 TileId Map에 추가하세요.");
            Debug.LogWarning(sb.ToString());
        }
        if (oob > 0) Debug.LogWarning($"[Grid] ⚠ 그리드 범위({size}) 밖 타일 {oob}개 스킵.");

        if (cells.Count == 0)
        {
            Debug.LogError($"[Grid] 익스포트할 매핑된 타일이 0개입니다(배치 {placed}). 'Print Autotiles3D Tile IDs'로 타일을 확인하세요.");
            return;
        }

        const string path = "Assets/Grid/Data/ExportedAnswer.asset";
        var asset = AssetDatabase.LoadAssetAtPath<MapAnswerData>(path);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<MapAnswerData>();

        var so = new SerializedObject(asset);
        so.FindProperty("m_GridSize").vector3IntValue = size;
        so.FindProperty("m_TimeLimitSeconds").floatValue = mgr.Answer != null ? mgr.Answer.TimeLimitSeconds : 180f;
        var arr = so.FindProperty("m_Cells");
        arr.arraySize = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("cell").vector3IntValue = cells[i].cell;
            el.FindPropertyRelative("materialId").intValue = cells[i].materialId;
            el.FindPropertyRelative("rotationStep").intValue = cells[i].rotationStep;
        }
        so.ApplyModifiedProperties();

        if (isNew) AssetDatabase.CreateAsset(asset, path);
        else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Grid] ✅ 정답 익스포트 — {cells.Count}칸 → {path}\n" +
                  "GridManager의 Answer 필드에 이 에셋을 지정하면 다음 플레이부터 정답으로 채점됩니다.");
    }

    /// <summary>씬에 칠해진 Autotiles3D 타일들의 (그룹/이름/TileID)를 콘솔에 출력 — 카탈로그 매핑 채울 때 사용.</summary>
    [MenuItem("Grid Setup/Print Autotiles3D Tile IDs")]
    static void PrintTileIds()
    {
        var grid = Object.FindFirstObjectByType<Autotiles3D_Grid>();
        if (grid == null) { Debug.LogError("[Grid] 씬에 Autotiles3D_Grid가 없습니다."); return; }

        var seen = new HashSet<int>();
        var sb = new StringBuilder("[Grid] 배치된 Autotiles3D 타일 종류:\n");
        foreach (var layer in grid.TileLayers)
        {
            if (layer == null) continue;
            foreach (var node in layer.GetAllInternalNodes())
                if (seen.Add(node.TileID))
                    sb.AppendLine($"  {node.TileGroupName}/{node.TileName}  →  TileID = {node.TileID}");
        }
        if (seen.Count == 0) sb.AppendLine("  (칠해진 타일 없음)");
        Debug.Log(sb.ToString());
    }

    // 타일 → materialId 해석: ① 카탈로그 명시 매핑, ② 타일 이름 ≈ MaterialDef 이름(fallback).
    static int ResolveMaterialId(MaterialCatalog catalog, int tileId, string tileName)
    {
        int id = catalog.TileIdToMaterialId(tileId);
        if (id != MaterialCatalog.NoMaterial) return id;
        if (string.IsNullOrEmpty(tileName)) return MaterialCatalog.NoMaterial;

        foreach (var def in catalog.Materials)
        {
            if (def == null) continue;
            string n = def.name;
            if (n.Equals(tileName, System.StringComparison.OrdinalIgnoreCase)) return def.Id;
            if (n.Equals("Mat_" + tileName, System.StringComparison.OrdinalIgnoreCase)) return def.Id;
            if (n.Replace("Mat_", "").Equals(tileName, System.StringComparison.OrdinalIgnoreCase)) return def.Id;
        }
        return MaterialCatalog.NoMaterial;
    }

    // Y축 회전 Quaternion → 0~3 스텝(90°).
    static int QuatToStep(Quaternion q) => Mathf.RoundToInt(q.eulerAngles.y / 90f) & 3;
}
