#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

namespace SeoulZikimi.MapTools
{
    /// <summary>
    /// 맵 메시(고폴리 환경 모델) 감축 툴.
    /// · 원클릭: 씬에서 Assets/Map 아래 메시를 자동으로 모두 찾아 목표 삼각형으로 감축.
    /// · 원본(.glb/.fbx)은 안 건드리고, 감축본을 Assets/Map/_Simplified 에 새 에셋으로 저장 후
    ///   씬 인스턴스의 sharedMesh 를 인플레이스 교체(Undo 가능).
    /// · 같은 메시 공유 인스턴스는 한 번만 굽고 재사용(재실행 시 디스크 캐시도 재사용).
    /// </summary>
    public static class MapMeshDecimator
    {
        const string k_OutDir = "Assets/Map/_Simplified";
        const string k_MapPrefix = "Assets/Map/";   // 이 경로 아래에서 임포트된 메시만 대상
        const int k_DefaultTarget = 12000;           // 메시당 목표 삼각형(원클릭 기본값)

        // ── 원클릭: 씬 전체 맵 자동 감축 ─────────────────────────────────────
        [MenuItem("Tools/Map/★ 맵 메시 감축 (원클릭)", priority = 0)]
        static void DecimateMapOneClick()
        {
            var filters = FindMapFiltersInScene();
            if (filters.Count == 0)
            {
                EditorUtility.DisplayDialog("맵 메시 감축",
                    "씬에서 Assets/Map 메시를 못 찾았습니다.\nGameScene을 열었는지, 맵이 프리팹이면 프리팹 모드인지 확인하세요.", "확인");
                return;
            }
            Decimate(filters, k_DefaultTarget);
        }

        // ── 선택한 것만 감축(고급) ───────────────────────────────────────────
        [MenuItem("Tools/Map/선택한 오브젝트만 감축", priority = 1)]
        static void DecimateSelection()
        {
            var filters = new List<MeshFilter>();
            foreach (var go in Selection.gameObjects)
                filters.AddRange(go.GetComponentsInChildren<MeshFilter>(true));
            if (filters.Count == 0)
            {
                EditorUtility.DisplayDialog("맵 메시 감축", "선택 항목에 MeshFilter가 없습니다.", "확인");
                return;
            }
            Decimate(filters, k_DefaultTarget);
        }

        [MenuItem("Tools/Map/선택한 오브젝트만 감축", validate = true)]
        static bool DecimateSelection_Validate() => Selection.gameObjects.Length > 0;

        // ── 씬에서 Assets/Map 출처 메시를 가진 MeshFilter 전부 수집 ───────────
        static List<MeshFilter> FindMapFiltersInScene()
        {
            var all = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var hit = new List<MeshFilter>();
            foreach (var f in all)
            {
                if (f.sharedMesh == null) continue;
                string p = AssetDatabase.GetAssetPath(f.sharedMesh);
                if (!string.IsNullOrEmpty(p) && p.StartsWith(k_MapPrefix, System.StringComparison.OrdinalIgnoreCase))
                    hit.Add(f);
            }
            return hit;
        }

        // ── 실제 감축 엔진 ───────────────────────────────────────────────────
        static void Decimate(List<MeshFilter> filters, int target)
        {
            EnsureReadable(filters);   // QEM이 정점/인덱스를 읽어야 하므로 Read/Write 보장
            EnsureFolder(k_OutDir);

            var map = new Dictionary<Mesh, Mesh>();   // 원본 → 결과
            var triCache = new Dictionary<Mesh, int>();
            int simplified = 0, skipped = 0, failed = 0;
            long beforeTris = 0, afterTris = 0;

            int TrisOf(Mesh m)
            {
                if (m == null) return 0;
                if (triCache.TryGetValue(m, out var t)) return t;
                t = m.isReadable ? m.triangles.Length / 3 : 0;
                return triCache[m] = t;
            }

            try
            {
                for (int i = 0; i < filters.Count; i++)
                {
                    var filter = filters[i];
                    var src = filter.sharedMesh;
                    if (src == null) continue;

                    EditorUtility.DisplayProgressBar("맵 메시 감축",
                        $"{src.name} ({i + 1}/{filters.Count})", (float)i / filters.Count);

                    Mesh result;
                    if (!map.TryGetValue(src, out result))
                    {
                        int srcTris = TrisOf(src);
                        if (srcTris == 0)
                        {
                            Debug.LogWarning($"[Decimator] 읽기 불가/빈 메시 건너뜀: {src.name}");
                            result = map[src] = src;
                        }
                        else if (srcTris <= target)
                        {
                            skipped++;
                            result = map[src] = src;
                        }
                        else
                        {
                            string outPath = OutPath(src, target);
                            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(outPath);
                            if (existing != null) { simplified++; result = map[src] = existing; }
                            else
                            {
                                try
                                {
                                    var ms = new MeshSimplifier();
                                    ms.PreserveBorderEdges = true;        // 끝단 무너짐 방지
                                    ms.PreserveUVSeamEdges = true;        // 텍스처 솔기 보존
                                    ms.PreserveSurfaceCurvature = true;   // 곡면 실루엣 유지
                                    ms.Initialize(src);
                                    ms.SimplifyMesh(Mathf.Clamp01((float)target / srcTris));
                                    var dst = ms.ToMesh();
                                    dst.name = src.name + "_d" + target;
                                    dst.RecalculateBounds();
                                    AssetDatabase.CreateAsset(dst, outPath);
                                    simplified++;
                                    result = map[src] = dst;
                                }
                                catch (System.Exception e)
                                {
                                    Debug.LogError($"[Decimator] 감축 실패 {src.name}: {e.Message}");
                                    failed++;
                                    result = map[src] = src;
                                }
                            }
                        }
                    }

                    if (result != src) Assign(filter, result);
                    beforeTris += TrisOf(src);
                    afterTris += TrisOf(result);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            float pct = beforeTris > 0 ? 100f * afterTris / beforeTris : 0f;
            Debug.Log($"[Decimator] 완료 — 감축 {simplified} · 건너뜀(이미 가벼움) {skipped} · 실패 {failed}\n" +
                      $"삼각형: {beforeTris:N0} → {afterTris:N0}  ({pct:0.#}%). 씬 저장(Ctrl+S) 필요.");
            EditorUtility.DisplayDialog("맵 메시 감축 완료",
                $"감축 {simplified} / 건너뜀 {skipped} / 실패 {failed}\n" +
                $"삼각형 {beforeTris:N0} → {afterTris:N0}  ({pct:0.#}%)\n\n" +
                $"· 되돌리기: Ctrl+Z\n· 씬 저장: Ctrl+S 잊지 마세요.", "확인");
        }

        static void Assign(MeshFilter f, Mesh m)
        {
            Undo.RecordObject(f, "Decimate Mesh");
            f.sharedMesh = m;
            EditorUtility.SetDirty(f);
        }

        // 읽기 불가(.glb/.fbx 기본값) 모델은 Read/Write 켜고 재임포트
        static void EnsureReadable(List<MeshFilter> filters)
        {
            var paths = new HashSet<string>();
            foreach (var f in filters)
            {
                var m = f.sharedMesh;
                if (m == null || m.isReadable) continue;
                string p = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
            }
            foreach (var p in paths)
                if (AssetImporter.GetAtPath(p) is ModelImporter imp && !imp.isReadable)
                {
                    imp.isReadable = true;
                    imp.SaveAndReimport();
                }
        }

        static string OutPath(Mesh src, int target)
        {
            string srcPath = AssetDatabase.GetAssetPath(src);
            string hash = string.IsNullOrEmpty(srcPath) ? "proc" : Hash8(srcPath + "/" + src.name);
            return $"{k_OutDir}/{Sanitize(src.name)}__{hash}_d{target}.asset";
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static string Hash8(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
