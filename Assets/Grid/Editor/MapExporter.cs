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
}
