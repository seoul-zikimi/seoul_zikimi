using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 비주얼 진단 — 셰이더/렌더러 상태 리포트와 게임 화면 캡처를 파일로 저장한다.
    /// Claude(CLI)가 에디터 화면을 직접 못 보므로, 이 파일 두 개가 '눈' 역할을 한다:
    ///   Library/VisualDiagReport.txt  — 셰이더 컴파일 에러 여부·렌더러 피처 상태·씬 세팅
    ///   Library/VisualDiag.png        — 게임 화면(플레이 중 실행해야 캡처됨)
    /// 실행: Tools ▸ Map ▸ ★ 비주얼 진단(캡처+리포트) — 가급적 플레이 중에.
    /// </summary>
    public static class VisualDiagTool
    {
        private const string kReportPath = "Library/VisualDiagReport.txt";
        private const string kShotPath = "Library/VisualDiag.png";

        // 자동 실행 — 사용자가 메뉴를 찾을 필요 없이, 플레이 시작 6초 뒤(맵 로드 후) 리포트+캡처가 저절로 저장된다.
        [InitializeOnLoadMethod]
        private static void AutoHook() => EditorApplication.playModeStateChanged += OnPlayMode;

        private static double s_CaptureAt = -1;

        private static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            s_CaptureAt = -1;   // GameScene(맵) 진입을 기다렸다가 +4초 뒤 캡처 — 6초 고정은 타이틀 화면을 찍었다(09/03)
            EditorApplication.update += TickCapture;
        }

        private static void TickCapture()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= TickCapture; s_CaptureAt = -1; return; }
            if (s_CaptureAt < 0)
            {
                if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("GameScene")) return;
                // 씬만으론 로딩 팁 화면이 찍힌다(09/03) — 맵 배경 프리팹이 실제로 뜬 뒤 +3초
                var bg = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.parent == null && t.name.StartsWith("~MapBackground"));
                if (bg == null) return;
                s_CaptureAt = EditorApplication.timeSinceStartup + 3.0;
                return;
            }
            if (EditorApplication.timeSinceStartup < s_CaptureAt) return;
            EditorApplication.update -= TickCapture;
            s_CaptureAt = -1;
            Run();
        }

        [MenuItem("Tools/Map/★ 비주얼 진단(캡처+리포트)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[진단] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · 플레이 중: {EditorApplication.isPlaying}");
            sb.AppendLine($"품질 레벨: {QualitySettings.GetQualityLevel()} ({QualitySettings.names[QualitySettings.GetQualityLevel()]})");
            sb.AppendLine($"활성 RP 에셋: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "없음(빌트인?)")}");

            // 셰이더 컴파일 상태
            foreach (var name in new[] { "Hidden/SeoulToonEdge", "Universal Render Pipeline/Toon", "Hidden/PickupOutline" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { sb.AppendLine($"셰이더 '{name}': ❌ 못 찾음"); continue; }
                bool err = ShaderUtil.ShaderHasError(sh);
                sb.AppendLine($"셰이더 '{name}': {(err ? "❌ 컴파일 에러 있음" : "✔ 에러 없음")} (isSupported={sh.isSupported})");
                if (err)
                    foreach (var m in ShaderUtil.GetShaderMessages(sh))
                        sb.AppendLine($"    · [{m.severity}] {m.message} (line {m.line})");
            }

            // 렌더러 피처 상태
            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) { sb.AppendLine($"렌더러 없음: {path}"); continue; }
                sb.AppendLine($"렌더러 {data.name}:");
                foreach (var f in data.rendererFeatures)
                    sb.AppendLine($"    · {(f == null ? "(null!)" : $"{f.name} active={f.isActive} type={f.GetType().Name}")}");
            }

            // 씬/카메라
            var cam = Camera.main;
            if (cam != null)
            {
                var add = cam.GetUniversalAdditionalCameraData();
                sb.AppendLine($"메인 카메라 '{cam.name}': 포프={add.renderPostProcessing}, AA={add.antialiasing}, 렌더러 인덱스={GetRendererIndex(add)}");
            }
            else sb.AppendLine("메인 카메라 없음(플레이 전이면 정상일 수 있음)");
            sb.AppendLine($"안개: {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogStartDistance}~{RenderSettings.fogEndDistance} 색{RenderSettings.fogColor}");

            // 부유물 스캔 — "공중에 떠 있는 덤불" 정체 규명용: 지면보다 한참 위에 떠 있는 소형 렌더러를 이름째 나열
            if (EditorApplication.isPlaying)
            {
                int listed = 0;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    var b = r.bounds;
                    if (b.min.y < 8f || b.min.y > 80f) continue;                       // 지면 근처/초고공 제외
                    if (Mathf.Max(b.size.x, b.size.z) > 8f || b.size.y > 8f) continue; // 빌딩 몸통 제외(소형만)
                    string path = r.transform.name;
                    for (var t2 = r.transform.parent; t2 != null; t2 = t2.parent) path = t2.name + "/" + path;
                    sb.AppendLine($"[부유?] y {b.min.y:F1}~{b.max.y:F1} · {path}");
                    if (++listed >= 25) { sb.AppendLine("[부유?] …25개에서 컷"); break; }
                }
            }

            System.IO.File.WriteAllText(kReportPath, sb.ToString());
            Debug.Log($"[진단] 리포트 저장 ✔ {kReportPath}\n{sb}");

            // 화면 캡처 — 플레이 중에만 게임 화면이 찍힌다
            if (EditorApplication.isPlaying)
            {
                ScreenCapture.CaptureScreenshot(kShotPath);
                Debug.Log($"[진단] 게임 화면 캡처 ✔ {kShotPath} (다음 프레임에 저장됨)");
            }
            else Debug.LogWarning("[진단] 플레이 중이 아니라 화면 캡처 생략 — 플레이 상태에서 다시 실행하면 PNG도 저장됨");
        }

        private static string GetRendererIndex(UniversalAdditionalCameraData add)
        {
            try { return new SerializedObject(add).FindProperty("m_RendererIndex")?.intValue.ToString() ?? "?"; }
            catch { return "?"; }
        }
    }
}
