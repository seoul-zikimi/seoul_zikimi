using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Autotiles3D;
using GridSystem;

/// <summary>
/// (A) 오서링 부트스트랩 — Autotiles3D 맵 제작 환경을 한 번에 세팅한다.
///  · MaterialCatalog의 재료마다 Autotiles3D 타일 자동 생성(이름=MaterialDef 이름 → 익스포터 자동 매칭).
///  · 씬에 Autotiles3D 그리드+레이어를 런타임 GridSize에 맞춰(8x4x8, Unit 1, identity) 보장.
/// 디자이너는 TileGroup/그리드를 손으로 만들 필요 없이, 칠하고 Export만 하면 된다.
/// 타일 1개 = 블록 1개(앵커=칠한 칸, min-corner). 멀티칸 재료는 '한 번' 칠하면 익스포터가
/// footprint 전체로 펼쳐 채운다(겹치게 칠하지 말 것). 프리팹도 footprint 크기라 칠할 때 블록 전체가 보인다.
/// </summary>
public static class Autotiles3DBootstrap
{
    const string k_PrefabDir = "Assets/Grid/Prefabs/Tiles";
    const string k_ResourcesDir = "Assets/ThirdParty/Autotiles3D/Resources";
    const string k_GroupPath = k_ResourcesDir + "/GridTiles.asset";

    // ── 한방 세팅: AnswerAuthoring 같은 씬을 열고 실행 ─────────────────────
    [MenuItem("Grid Setup/★ Setup Autotiles3D Authoring (Active Scene)")]
    static void SetupAuthoring()
    {
        var catalog = FindCatalog();
        if (catalog == null)
        {
            Debug.LogError("[Grid] MaterialCatalog가 없습니다 — 먼저 'Create Sample Catalog + Answer'.");
            return;
        }

        var group = BuildTiles(catalog);

        // 그리드 크기 = 런타임 GridSize에 맞춤(없으면 기본 8x4x8). Autotiles3D: Width=XZ(정사각), Height=Y.
        var mgr = Object.FindFirstObjectByType<GridManager>();
        Vector3Int gs = mgr != null ? mgr.GridSize : new Vector3Int(8, 4, 8);
        int width = Mathf.Max(1, Mathf.Max(gs.x, gs.z));
        int height = Mathf.Max(1, gs.y);

        var grid = Object.FindFirstObjectByType<Autotiles3D_Grid>();
        if (grid == null)
            grid = new GameObject("Autotiles3D Grid").AddComponent<Autotiles3D_Grid>();

        grid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        grid.transform.localScale = Vector3.one;          // 좌표 정합: identity + Unit 1
        grid.GridSize = LevelSize.Finite;
        grid.Width = width;
        grid.Height = height;
        grid.Unit = 1f;
        grid.gameObject.name = $"Autotiles3D Grid ({width}x{height}x{width})";

        // 레이어 보장(+ GridTiles 미리 선택)
        grid.TileLayers.RemoveAll(l => l == null);
        var layer = grid.TileLayers.Count > 0 ? grid.TileLayers[0] : null;
        if (layer == null)
        {
            var layerGO = new GameObject("Answer Layer");
            layerGO.transform.SetParent(grid.transform, false);
            layer = layerGO.AddComponent<Autotiles3D_TileLayer>();
            layer.LayerName = "Answer";
            grid.TileLayers.Add(layer);
        }
        layer.Group = group;

        EditorUtility.SetDirty(grid);
        EditorUtility.SetDirty(layer);
        Selection.activeGameObject = layer.gameObject;   // 레이어 선택 → 바로 페인트 가능
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[Grid] ★ Autotiles3D 오서링 세팅 완료 (그리드 {width}x{height}x{width}, Unit 1).\n" +
                  "1) (선택된) 'Answer Layer'에서 씬을 좌클릭 — 타일 1개 = 블록 1개(멀티칸은 한 번만 칠하면 통째로). Ctrl+휠 회전, Alt+휠 층 이동\n" +
                  "2) 'Grid Setup/Export Answer from Autotiles3D' → ExportedAnswer.asset\n" +
                  "3) 런타임 씬 GridManager의 Answers 리스트에 ExportedAnswer 지정.");
    }

    // ── 타일만 생성 ──────────────────────────────────────────────────────
    [MenuItem("Grid Setup/Create Autotiles3D Tiles From Catalog")]
    static void CreateTiles()
    {
        var catalog = FindCatalog();
        if (catalog == null)
        {
            Debug.LogError("[Grid] MaterialCatalog가 없습니다 — 먼저 'Create Sample Catalog + Answer'.");
            return;
        }
        var group = BuildTiles(catalog);
        Debug.Log($"[Grid] ✅ Autotiles3D 타일 {group.Tiles.Count}개 생성 → {k_GroupPath}");
    }

    static MaterialCatalog FindCatalog()
    {
        var mgr = Object.FindFirstObjectByType<GridManager>();
        if (mgr != null && mgr.Catalog != null) return mgr.Catalog;
        return AssetDatabase.LoadAssetAtPath<MaterialCatalog>("Assets/Grid/Data/MaterialCatalog.asset");
    }

    static Autotiles3D_TileGroup BuildTiles(MaterialCatalog catalog)
    {
        EnsureFolder(k_PrefabDir);
        EnsureFolder(k_ResourcesDir);

        var group = AssetDatabase.LoadAssetAtPath<Autotiles3D_TileGroup>(k_GroupPath);
        if (group == null)
        {
            group = ScriptableObject.CreateInstance<Autotiles3D_TileGroup>();
            AssetDatabase.CreateAsset(group, k_GroupPath);
        }
        group.Tiles.Clear();   // 재실행 시 갱신(idempotent)

        foreach (var def in catalog.Materials)
        {
            if (def == null) continue;
            var prefab = def.Prefab != null ? def.Prefab : MakePlaceholderPrefab(def);
            var tile = new Autotiles3D_Tile(def.name);   // 이름 = MaterialDef 이름 → 익스포터 자동 매칭
            tile.Default = prefab;
            group.Tiles.Add(tile);
        }

        group.UpdateTilesWithGroupName();
        group.ConstructMapping();
        EditorUtility.SetDirty(group);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return group;
    }

    static GameObject MakePlaceholderPrefab(MaterialDef def)
    {
        string path = $"{k_PrefabDir}/Tile_{def.name}.prefab";

        // 피벗 = 셀 min-corner(규약). 큐브를 footprint 전체 크기로 둬, 칠하면 '블록 한 개'가 통째로 보이게.
        var fp = def.Footprint;
        float fx = Mathf.Max(1, fp.x), fy = Mathf.Max(1, fp.y), fz = Mathf.Max(1, fp.z);
        var root = new GameObject($"Tile_{def.name}");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = new Vector3(fx * 0.5f, fy * 0.5f, fz * 0.5f);
        cube.transform.localScale = new Vector3(fx, fy, fz) * 0.98f;
        cube.GetComponent<Renderer>().sharedMaterial = GetOrCreateMat(ColorForMask(def.RequiredMask));

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);   // 매번 덮어써 footprint 변경 반영
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material GetOrCreateMat(Color c)
    {
        string id = $"{Mathf.RoundToInt(c.r * 9)}{Mathf.RoundToInt(c.g * 9)}{Mathf.RoundToInt(c.b * 9)}";
        string path = $"{k_PrefabDir}/TileMat_{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh) { color = c };
        m.SetColor("_BaseColor", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    static Color ColorForMask(int mask)
    {
        if ((mask & (int)ProcessType.Painted) != 0) return new Color(0.30f, 0.85f, 0.40f);
        if ((mask & (int)ProcessType.Fixed) != 0)   return new Color(0.35f, 0.60f, 1.00f);
        return new Color(0.72f, 0.72f, 0.72f);
    }
}
