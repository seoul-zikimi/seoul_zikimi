using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 씬에 복사해 둔 배경(Background 루트)을 '맵'으로 승격:
    /// ① 배경 프리팹 저장 ② MapDef 생성 ③ Resources/MapCatalog 등록 ④ 씬에서 배경 제거 + MapLoader 추가.
    /// 사용: 배경 루트 오브젝트 선택(없으면 이름 "Background" 자동 탐색) → Tools ▸ Map ▸ Extract Background To Map.
    /// </summary>
    public static class MapExtractTool
    {
        private const string kPrefabDir = "Assets/Resources/MapPrefabs";
        private const string kMapDefDir = "Assets/Map/Maps";
        private const string kCatalogPath = "Assets/Resources/MapCatalog.asset";

        [MenuItem("Tools/Map/Extract Background To Map")]
        public static void Extract()
        {
            var root = Selection.activeGameObject;
            if (root == null) root = GameObject.Find("Background");
            if (root == null || !root.scene.IsValid())
            {
                Debug.LogError("[MapExtract] 씬의 배경 루트를 선택하거나 이름을 'Background'로 해주세요.");
                return;
            }

            // 맵 이름은 저장 창에서 직접 정한다(파일명 = MapBg_이름). 기본값: 선택 오브젝트 이름(Background면 씬 기반).
            string defaultName = (root.name != "Background") ? root.name
                : (root.scene.name == "GameScene" ? "GwangTongGyo" : root.scene.name);
            string chosen = EditorUtility.SaveFilePanelInProject(
                "맵 이름 정하기", $"MapBg_{defaultName}", "prefab",
                "새 맵의 이름을 정해주세요. 파일명이 MapBg_이름 형식이면 '이름'이 맵 이름이 됩니다.", kPrefabDir);
            if (string.IsNullOrEmpty(chosen)) return;   // 취소
            string mapName = Path.GetFileNameWithoutExtension(chosen);
            if (mapName.StartsWith("MapBg_")) mapName = mapName.Substring("MapBg_".Length);

            // ① 배경 프리팹
            Directory.CreateDirectory(kPrefabDir);
            string prefabPath = $"{kPrefabDir}/MapBg_{mapName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            if (!ok) { Debug.LogError($"[MapExtract] 프리팹 저장 실패: {prefabPath}"); return; }

            // ② MapDef
            Directory.CreateDirectory(kMapDefDir);
            string defPath = $"{kMapDefDir}/Map_{mapName}.asset";
            var def = AssetDatabase.LoadAssetAtPath<MapDef>(defPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MapDef>();
                AssetDatabase.CreateAsset(def, defPath);
            }
            var so = new SerializedObject(def);
            so.FindProperty("m_BackgroundPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            // ②-b 썸네일 자동 캡처(비어 있을 때만 — 손으로 넣은 이미지는 존중)
            if (so.FindProperty("m_Thumbnail").objectReferenceValue == null)
            {
                var thumb = CaptureThumbnail(prefab, $"{kMapDefDir}/Thumb_{mapName}.png");
                if (thumb != null)
                {
                    so.Update();
                    so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // ③ 카탈로그(Resources) 등록
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(kCatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(kCatalogPath));
                catalog = ScriptableObject.CreateInstance<MapCatalog>();
                AssetDatabase.CreateAsset(catalog, kCatalogPath);
            }
            catalog.EditorAdd(def);
            EditorUtility.SetDirty(catalog);

            // ④ 씬 정리: 배경 제거(런타임엔 MapLoader가 스폰) + MapLoader 보장
            var scene = root.scene;
            Object.DestroyImmediate(root);
            if (Object.FindFirstObjectByType<MapLoader>() == null)
                new GameObject("@MapLoader", typeof(MapLoader));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            Debug.Log($"[MapExtract] 완료 ✔\n프리팹: {prefabPath}\nMapDef: {defPath} (DisplayName·정답·썸네일은 인스펙터에서)\n카탈로그: {kCatalogPath}\n씬 저장(Ctrl+S) 잊지 마세요 — 배경은 이제 런타임 스폰입니다.");
        }

        // 간단 이름 확정(다이얼로그 대신 씬/기본명 사용 — 필요하면 생성된 에셋 이름을 바꾸면 됨)

        /// <summary>선택한 MapDef의 썸네일 재촬영(배경을 바꾼 뒤 갱신용).</summary>
        [MenuItem("Tools/Map/Regenerate Map Thumbnail")]
        public static void RegenerateThumbnail()
        {
            var def = Selection.activeObject as MapDef;
            if (def == null) { Debug.LogError("[MapExtract] MapDef 에셋(Assets/Map/Maps)을 선택하고 실행하세요."); return; }
            var so = new SerializedObject(def);
            var prefab = so.FindProperty("m_BackgroundPrefab").objectReferenceValue as GameObject;
            if (prefab == null) { Debug.LogError("[MapExtract] MapDef에 배경 프리팹이 없습니다."); return; }
            var thumb = CaptureThumbnail(prefab, $"{kMapDefDir}/Thumb_{def.name.Replace("Map_", "")}.png");
            if (thumb == null) return;
            so.FindProperty("m_Thumbnail").objectReferenceValue = thumb;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[MapExtract] 썸네일 갱신 ✔ → {AssetDatabase.GetAssetPath(thumb)}");
        }

        // 배경 프리팹을 임시로 세워놓고 카메라로 찍어 PNG 저장 → Sprite 반환.
        private static Sprite CaptureThumbnail(GameObject prefab, string pngPath)
        {
            GameObject inst = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = new Vector3(0f, -5000f, 0f);   // 씬 내용과 안 겹치게 지하에서 촬영

                // 렌더러 전체 바운즈
                var rends = inst.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return null;
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                camGo = new GameObject("~ThumbCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 40f;
                float dist = b.size.magnitude * 0.75f;
                cam.transform.position = b.center + new Vector3(1f, 0.8f, -1f).normalized * dist;
                cam.transform.LookAt(b.center);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = dist * 4f;

                rt = new RenderTexture(512, 512, 24);
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;   // Multiple로 잡히면 스프라이트 서브에셋이 안 생겨 썸네일 연결이 빈다
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (inst != null) Object.DestroyImmediate(inst);
            }
        }
    }
}
