using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 비주얼 스타일 — "민짜 3D 폴리곤 세상 같다" 해결 원클릭. 두 가지를 한 번에 깐다:
    ///
    /// ① 전맵 카툰 외곽선 — PC/Mobile 렌더러에 FullScreenPassRendererFeature(Hidden/SeoulToonEdge)를
    ///    추가한다. 깊이 기반 잉크 라인이 블록·건물 윤곽에 붙어 만화 그림 톤이 된다(원경은 페이드).
    ///    세기·색·거리는 Assets/Map/Materials/Mat_ToonEdge 머티리얼에서 조절.
    ///
    /// ② 맵별 무드 그레이딩 — 낮 맵 6종 배경 프리팹 루트에 MapMoodGrade를 붙이고 프리셋을 굽는다.
    ///    (DDP는 밤 시스템 MapNightAmbience 담당이라 제외.)
    ///    재실행 시 프리셋으로 리셋 — 확정 수치는 아래 kMoods 표를 고칠 것.
    ///
    /// 실행: Tools ▸ Map ▸ ★ 비주얼 스타일(카툰 외곽선+맵 무드) 적용
    /// 해제: Tools ▸ Map ▸ 비주얼 스타일 해제
    /// </summary>
    public static class VisualStyleTool
    {
        private const string kFeatureName = "SeoulToonEdge";
        private const string kEdgeMatPath = "Assets/Map/Materials/Mat_ToonEdge.mat";
        private static readonly string[] kRendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        private struct Mood
        {
            public string Path;
            public float Sat, Con, Temp, Tint;
            public Color Filter;
        }
        // 주간 볼륨 기본(채도 15·대비 8) 기준의 절대값 프리셋.
        // 1차(09/02) 피드백 "하늘이 칙칙해졌다" → 원인: 대비↑ + 한색 색온도가 파스텔 하늘을 죽임.
        // 2차: 대비는 기본(8)보다 낮춰 하늘을 밝게 틔우고, 색온도는 전부 중립~따뜻, 채도로 화사함을 낸다.
        private static readonly Mood[] kMoods =
        {
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_Tutorial.prefab",      Sat = 20f, Con = 5f, Temp = 4f, Tint = 0f, Filter = Color.white },                                // 화사한 입문
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_GwangTongGyo.prefab",  Sat = 22f, Con = 5f, Temp = 2f, Tint = 0f, Filter = Color.white },                                // 맑은 청계천 오후
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_NamsanTower.prefab",   Sat = 18f, Con = 7f, Temp = 0f, Tint = 0f, Filter = Color.white },                                // 산 위 맑은 공기
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_VersusField.prefab",   Sat = 22f, Con = 5f, Temp = 4f, Tint = 0f, Filter = Color.white },                                // 잔디 경기장 쨍하게
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_LotteWorld.prefab",    Sat = 28f, Con = 4f, Temp = 6f, Tint = 2f, Filter = new Color(1.00f, 0.99f, 0.97f) },             // 놀이공원 캔디 팝
            new Mood { Path = "Assets/Resources/MapPrefabs/MapBg_Gyeongbokgung.prefab", Sat = 18f, Con = 5f, Temp = 9f, Tint = 2f, Filter = new Color(1.00f, 0.985f, 0.955f) },           // 늦은 오후 고궁 골드
        };

        [MenuItem("Tools/Map/★ 비주얼 스타일(카툰 외곽선+맵 무드) 적용")]
        public static void Apply()
        {
            int edges = ApplyToonEdge();
            int moods = ApplyMoods();
            AssetDatabase.SaveAssets();
            Debug.Log($"[비주얼스타일] 완료 ✔ 카툰 외곽선 렌더러 {edges}개(현재 비활성) · 맵 무드 {moods}개. " +
                      "맵 색감은 각 MapBg 프리팹의 MapMoodGrade에서 조절. (남산 City##는 삭제 확정 — MapTouchupAutoSetup v2)");
        }

        // (남산 City## 파사드 리스킨은 폐기 — 사용자 확정 "차라리 제거": MapTouchupAutoSetup v2가 삭제한다)

        [MenuItem("Tools/Map/비주얼 스타일 해제")]
        public static void Remove()
        {
            int edges = 0, moods = 0;
            foreach (var path in kRendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) continue;
                for (int i = data.rendererFeatures.Count - 1; i >= 0; i--)
                {
                    var f = data.rendererFeatures[i];
                    if (f == null || f.name != kFeatureName) continue;
                    var so = new SerializedObject(data);
                    var list = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    list.DeleteArrayElementAtIndex(i);
                    if (map != null && i < map.arraySize) map.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    Object.DestroyImmediate(f, true);
                    EditorUtility.SetDirty(data);
                    edges++;
                }
            }
            foreach (var m in kMoods)
            {
                var root = PrefabUtility.LoadPrefabContents(m.Path);
                try
                {
                    var grade = root.GetComponent<MapMoodGrade>();
                    if (grade == null) continue;
                    Object.DestroyImmediate(grade);
                    PrefabUtility.SaveAsPrefabAsset(root, m.Path);
                    moods++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[비주얼스타일] 해제 ✔ 외곽선 {edges}개 · 무드 {moods}개 제거");
        }

        // ───────────────────── ① 카툰 외곽선(풀스크린 패스) ─────────────────────
        private static int ApplyToonEdge()
        {
            var shader = Shader.Find("Hidden/SeoulToonEdge");
            if (shader == null) { Debug.LogError("[비주얼스타일] Hidden/SeoulToonEdge 셰이더를 못 찾음 — Assets/Map/Shaders 임포트 확인"); return 0; }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(kEdgeMatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, kEdgeMatPath);
            }
            else if (mat.shader != shader) mat.shader = shader;

            int applied = 0;
            foreach (var path in kRendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) { Debug.LogWarning($"[비주얼스타일] 렌더러 없음: {path}"); continue; }

                var existing = data.rendererFeatures.FirstOrDefault(f => f != null && f.name == kFeatureName);
                if (existing != null)
                {   // 이미 있음 — 머티리얼 참조만 보정
                    var eso = new SerializedObject(existing);
                    var pm = eso.FindProperty("passMaterial");
                    if (pm != null && pm.objectReferenceValue != mat) { pm.objectReferenceValue = mat; eso.ApplyModifiedProperties(); EditorUtility.SetDirty(data); }
                    applied++;
                    continue;
                }

                // FullScreenPassRendererFeature는 URP 버전에 따라 네임스페이스가 달라 리플렉션으로 잡는다
                var type = System.Type.GetType("FullScreenPassRendererFeature, Unity.RenderPipelines.Universal.Runtime")
                        ?? System.Type.GetType("UnityEngine.Rendering.Universal.FullScreenPassRendererFeature, Unity.RenderPipelines.Universal.Runtime");
                if (type == null) { Debug.LogError("[비주얼스타일] FullScreenPassRendererFeature 타입을 못 찾음(URP 버전 확인)"); return applied; }

                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = kFeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);

                var fso = new SerializedObject(feature);
                Set(fso, "passMaterial", p => p.objectReferenceValue = mat);
                Set(fso, "injectionPoint", p => p.intValue = (int)RenderPassEvent.BeforeRenderingPostProcessing);
                Set(fso, "requirements", p => p.intValue = (int)ScriptableRenderPassInput.Depth);
                Set(fso, "fetchColorBuffer", p => p.boolValue = true);
                Set(fso, "bindDepthStencilAttachment", p => p.boolValue = false);
                fso.ApplyModifiedProperties();

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                applied++;
            }
            return applied;
        }

        private static void Set(SerializedObject so, string prop, System.Action<SerializedProperty> write)
        {
            var p = so.FindProperty(prop);
            if (p != null) write(p);
        }

        // ───────────────────── 맵 터치업 자동 실행기 ─────────────────────
        // 프리팹에 이미 저장된 잔재를 지우는 일회성 정리 — 내용을 고치면 kTouchupVersion을 올린다.
        // (광통교 자동 실행기와 같은 패턴: delayCall + 플레이 종료 재시도 — 타이밍 스킵 사고 방지)
        [InitializeOnLoad]
        public static class MapTouchupAutoSetup
        {
            private const int kVersion = 2;   // 2: 남산 회색 박스빌딩(City##) 전부 제거("차라리 제거하자" 09/03) / 1: 경복궁 그레이박스 산 제거
            private const string kKey = "Map.TouchupVersion";

            static MapTouchupAutoSetup()
            {
                EditorApplication.delayCall += TryRun;
                EditorApplication.playModeStateChanged += s =>
                { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryRun; };
            }

            private static void TryRun()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorPrefs.GetInt(kKey, 0) >= kVersion) return;

                // ① 경복궁: 그레이박스 산 제거(멱등 — 없으면 무시)
                RemoveByName("Assets/Resources/MapPrefabs/MapBg_Gyeongbokgung.prefab",
                             t => t.name == "Mountain_1" || t.name == "Mountain_2", "경복궁 그레이박스 산");
                // ② 남산: 단색 회색 박스빌딩 City## 전부 제거 — 파사드 리스킨 대신 삭제로 확정(09/03)
                RemoveByName("Assets/Resources/MapPrefabs/MapBg_NamsanTower.prefab",
                             t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^City\d+$"), "남산 회색 박스빌딩");

                EditorPrefs.SetInt(kKey, kVersion);
            }

            private static void RemoveByName(string path, System.Func<Transform, bool> match, string label)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var doomed = root.GetComponentsInChildren<Transform>(true)
                                     .Where(t => t != root.transform && match(t))
                                     .Select(t => t.gameObject).ToList();
                    foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);
                    if (doomed.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[맵터치업] {label} 제거 {doomed.Count}개 ✔");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        // ───────────────────── ② 맵별 무드 프리셋 ─────────────────────
        private static int ApplyMoods()
        {
            int applied = 0;
            foreach (var m in kMoods)
            {
                if (!System.IO.File.Exists(m.Path)) { Debug.LogWarning($"[비주얼스타일] 프리팹 없음: {m.Path}"); continue; }
                var root = PrefabUtility.LoadPrefabContents(m.Path);
                try
                {
                    var grade = root.GetComponent<MapMoodGrade>();
                    if (grade == null) grade = root.AddComponent<MapMoodGrade>();
                    grade.Saturation = m.Sat;
                    grade.Contrast = m.Con;
                    grade.Temperature = m.Temp;
                    grade.Tint = m.Tint;
                    grade.ColorFilter = m.Filter;
                    PrefabUtility.SaveAsPrefabAsset(root, m.Path);
                    applied++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return applied;
        }
    }
}
