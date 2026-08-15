using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 카탈로그의 모든 맵 썸네일을 "완성된 정답 건물" 중심으로 일괄 촬영한다.
    /// 배경 프리팹 위 Spot_GridManager 자리에 정답 구조물(첫 번째 정답)을 조립해 세우고,
    /// 구조물 바운드를 프레이밍한 쿼터뷰로 촬영 → Thumb_<맵이름>.png 저장 + MapDef.m_Thumbnail 자동 연결.
    /// 공터(2vs2 경기장)는 건너뛴다(로비 선택지에도 안 나옴).
    /// </summary>
    public static class MapAnswerThumbnailTool
    {
        private const string kCatalogPath = "Assets/Resources/MapCatalog.asset";
        private static readonly Vector3 kStagePos = new(0f, -5000f, 0f);   // 씬 밖 지하 스테이지
        private static readonly Color kNoPrefabColor = new(0.85f, 0.83f, 0.75f);

        /// <summary>materialId → MaterialDef 조회. 실제 게임과 동일한 결과를 내기 위해
        /// GameScene의 GridManager가 참조하는 MaterialCatalog(= 런타임이 쓰는 그 카탈로그)을 그대로 쓴다.
        /// 프로젝트엔 id가 겹치는 카탈로그가 여럿 있어(구버전 Grid/Data 6종: id2=벽 vs 광통교 카탈로그: id2=상부기둥)
        /// 폴더 추측이나 병합은 엉뚱한 모델을 세운다. 씬에서 못 찾을 때만 재료 수 가장 많은 카탈로그로 폴백.</summary>
        private sealed class MergedMaterialLookup
        {
            private const string kGameScenePath = "Assets/Scenes/GameScene.unity";
            private readonly Dictionary<int, MaterialDef> m_ById = new();

            public MergedMaterialLookup()
            {
                var catalog = FindGameSceneCatalog();
                if (catalog == null)
                {
                    // 폴백: 재료가 가장 많은 카탈로그(= 현행 통합 카탈로그일 가능성이 가장 높음).
                    foreach (var guid in AssetDatabase.FindAssets("t:MaterialCatalog"))
                    {
                        var cat = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                        if (cat != null && (catalog == null || cat.Materials.Count > catalog.Materials.Count)) catalog = cat;
                    }
                    Debug.LogWarning("[MapAnswerThumbnailTool] GameScene의 GridManager 카탈로그를 못 찾아 재료 수 최대 카탈로그로 폴백");
                }
                if (catalog == null) { Debug.LogError("[MapAnswerThumbnailTool] MaterialCatalog을 하나도 못 찾음 — 전부 박스로 대체"); return; }

                foreach (var def in catalog.Materials)
                    if (def != null) m_ById[def.Id] = def;
                Debug.Log($"[MapAnswerThumbnailTool] 재료 카탈로그 = {AssetDatabase.GetAssetPath(catalog)} ({m_ById.Count}종)");
            }

            /// <summary>GameScene.unity의 GridManager 컴포넌트가 들고 있는 m_Catalog 참조를 씬 파일에서 읽는다(씬을 열지 않음).</summary>
            private static MaterialCatalog FindGameSceneCatalog()
            {
                if (!System.IO.File.Exists(kGameScenePath)) return null;
                foreach (var line in System.IO.File.ReadLines(kGameScenePath))
                {
                    int i = line.IndexOf("m_Catalog:", System.StringComparison.Ordinal);
                    if (i < 0) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"guid:\s*([0-9a-f]{32})");
                    if (!m.Success) continue;
                    var cat = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value));
                    if (cat != null) return cat;
                }
                return null;
            }

            public void SelectForMap(MapDef map) { }   // 카탈로그는 게임과 동일하게 전 맵 공통

            public MaterialDef GetById(int id) => m_ById.TryGetValue(id, out var d) ? d : null;
        }

        [MenuItem("Tools/Map/맵 썸네일 일괄 촬영 (완성 건물 중심)")]
        public static void CaptureAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kCatalogPath);
            if (catalog == null) { Debug.LogError($"[MapAnswerThumbnailTool] MapCatalog이 없음: {kCatalogPath}"); return; }
            var materials = new MergedMaterialLookup();

            int done = 0;
            foreach (var def in catalog.Maps)
            {
                if (def == null || def.IsVersusArena) continue;

                materials.SelectForMap(def);
                var sprite = CaptureFor(def, materials);
                if (sprite == null) { Debug.LogWarning($"[MapAnswerThumbnailTool] 촬영 실패: {def.name}"); continue; }

                var so = new SerializedObject(def);
                so.Update();
                so.FindProperty("m_Thumbnail").objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                done++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[MapAnswerThumbnailTool] 완료 ✔ 맵 {done}개 썸네일 갱신(완성 건물 중심)");
        }

        private static Sprite CaptureFor(MapDef def, MergedMaterialLookup materials)
        {
            var answer = def.Answers != null && def.Answers.Count > 0 ? def.Answers[0] : null;
            if (answer == null || answer.Cells.Count == 0)
            {
                Debug.LogWarning($"[MapAnswerThumbnailTool] {def.name}: 정답이 없어 배경만 촬영");
            }

            var stage = new GameObject("~ThumbStage");
            stage.transform.position = kStagePos;
            var tempMats = new List<Material>();
            Camera cam = null;
            RenderTexture rt = null;
            var prevActive = RenderTexture.active;

            try
            {
                // ① 배경(있으면) — Spot_GridManager 마커가 구조물 원점.
                Vector3 origin = kStagePos;
                if (def.BackgroundPrefab != null)
                {
                    var bg = (GameObject)PrefabUtility.InstantiatePrefab(def.BackgroundPrefab);
                    bg.transform.SetParent(stage.transform, false);
                    foreach (var t in bg.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Spot_GridManager") { origin = t.position; break; }
                }

                // ② 정답 구조물 조립(AnswerPreview.GroupAnswer/MakeBlockVisual과 동일 규약, 솔리드).
                Bounds bounds = default;
                bool hasBounds = false;
                if (answer != null && answer.Cells.Count > 0)
                {
                    const float u = 1f;   // GridContract.Unit 규약(셀=1유닛)
                    foreach (var o in GroupAnswer(answer, materials))
                    {
                        Vector3 pos = origin + (Vector3)o.minCell * u;
                        var go = SpawnSolid(o, stage.transform, pos, u, tempMats);
                        var bb = new Bounds(pos + new Vector3(0.5f * o.dims.x, 0.5f * o.dims.y, 0.5f * o.dims.z) * u,
                            new Vector3(o.dims.x, o.dims.y, o.dims.z) * u);
                        if (!hasBounds) { bounds = bb; hasBounds = true; } else bounds.Encapsulate(bb);
                        _ = go;
                    }
                }

                if (!hasBounds)
                {
                    // 정답이 없으면 배경 렌더러 전체로 폴백.
                    foreach (var r in stage.GetComponentsInChildren<Renderer>())
                    {
                        if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds);
                    }
                    if (!hasBounds) return null;
                }

                // ③ 완성 건물을 프레이밍하는 쿼터뷰 카메라.
                var camGO = new GameObject("~ThumbCam");
                camGO.transform.SetParent(stage.transform, false);
                cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 40f;
                float dist = Mathf.Max(4f, bounds.size.magnitude * 0.85f + 2f);
                cam.transform.position = bounds.center + new Vector3(1f, 0.75f, -1f).normalized * dist;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = dist * 6f;

                rt = new RenderTexture(512, 512, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();

                // ④ 저장(MapDef 에셋과 같은 폴더) + 스프라이트 임포트.
                string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(def))?.Replace('\\', '/');
                if (string.IsNullOrEmpty(dir)) dir = "Assets/Map/Maps";
                string pngPath = $"{dir}/Thumb_{def.name.Replace("Map_", "")}.png";
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);

                if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                if (sprite == null)
                {
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(pngPath))
                        if (a is Sprite s) { sprite = s; break; }
                }
                return sprite;
            }
            finally
            {
                if (cam != null) cam.targetTexture = null;
                RenderTexture.active = prevActive;
                if (rt != null) rt.Release();
                foreach (var m in tempMats) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(stage);
            }
        }

        // ── AnswerPreview.GroupAnswer와 동일 규약(펼쳐진 칸 → footprint 오브젝트 재구성) ──

        private struct AnsObject { public MaterialDef def; public int rot; public Vector3Int minCell; public Vector3 dims; }

        private static List<AnsObject> GroupAnswer(MapAnswerData answer, MergedMaterialLookup catalog)
        {
            var objs = new List<AnsObject>();
            var cells = new List<AnswerCell>(answer.Cells);
            cells.Sort((a, c) =>
            {
                if (a.cell.x != c.cell.x) return a.cell.x - c.cell.x;
                if (a.cell.y != c.cell.y) return a.cell.y - c.cell.y;
                return a.cell.z - c.cell.z;
            });

            var claimed = new HashSet<Vector3Int>();
            foreach (var c in cells)
            {
                if (claimed.Contains(c.cell)) continue;
                var def = catalog != null ? catalog.GetById(c.materialId) : null;
                var fp = def != null ? def.Footprint : Vector3Int.one;
                int rot = c.rotationStep;
                var fcells = GridFootprint.EnumerateFootprintCells(c.cell, fp, rot);

                bool ok = true;
                foreach (var fc in fcells)
                    if (claimed.Contains(fc) || !answer.TryGet(fc, out var ac)
                        || ac.materialId != c.materialId || ac.rotationStep != rot)
                    { ok = false; break; }

                Vector3 dims;
                if (ok)
                {
                    foreach (var fc in fcells) claimed.Add(fc);
                    bool swap = ((((rot % 4) + 4) % 4) % 2) == 1;
                    dims = new Vector3(swap ? fp.z : fp.x, fp.y, swap ? fp.x : fp.z);
                }
                else { claimed.Add(c.cell); dims = Vector3.one; }

                objs.Add(new AnsObject { def = def, rot = rot, minCell = c.cell, dims = dims });
            }
            return objs;
        }

        private static GameObject SpawnSolid(AnsObject o, Transform parent, Vector3 pos, float u, List<Material> tempMats)
        {
            GameObject go;
            if (o.def != null && o.def.Prefab != null)
            {
                go = Object.Instantiate(o.def.Prefab, parent);
                GridFootprint.PlaceRotatedPrefab(go, pos, o.def.Footprint, o.rot, u);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, true);
                go.transform.position = pos + Vector3.up * (0.5f * o.dims.y * u);
                go.transform.localScale = new Vector3(o.dims.x, o.dims.y, o.dims.z) * (u * 0.96f);

                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    var m = new Material(shader);
                    m.SetColor("_BaseColor", kNoPrefabColor);
                    tempMats.Add(m);
                    go.GetComponent<Renderer>().sharedMaterial = m;
                }
            }

            foreach (var col in go.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);
            return go;
        }
    }
}
