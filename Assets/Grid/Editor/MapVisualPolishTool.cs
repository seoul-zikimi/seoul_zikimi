using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using BlendMode = UnityEngine.Rendering.BlendMode;
using UnityEngine.Rendering.Universal;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 맵 비주얼 정리 — "공중섬 같고 뿌옇다" 해결 원클릭.
    ///
    /// [씬] GameScene · WeatherTest
    ///  · 스카이박스: Feel 데모용 → FastSky 스타일라이즈드(구름·태양, 라이트 방향 따라 낮/저녁 자동)
    ///  · 안개: Exponential(0.019, 30m에서 이미 43% 뿌옇게) → Linear 시작 70m / 끝 260m
    ///          → 플레이 영역 블럭은 선명, 원경만 하늘색으로 녹아 지평선이 됨
    ///  · 앰비언트: 어두운 Flat(0.52) → Trilight(하늘/지평/바닥) — 그림자 쪽 칙칙함 제거
    ///  · 태양광: 약간 따뜻한 색, 1.15
    ///  · 포스트프로세싱: 카메라 켜고 @GlobalVolume에 GameVisualProfile(톤매핑·블룸·비네트·채도)
    ///
    /// [맵 프리팹] MapBg_* 4종 — "~Horizon" 그룹 추가/갱신(멱등)
    ///  · 1km 바닥 원판(맵 최저점 높이) + 맵 밑 스커트(산 몸통 — 뚝 끊김 제거)
    ///  · 원경 실루엣 카드 링 2겹(도심·산 능선) — 텍스처는 코드로 그려 Assets/Map/Horizon/에 PNG로 저장
    ///    → 마음에 안 들면 같은 이름 PNG를 그림(VARCO 등)으로 덮어쓰면 그대로 쓰인다(가로 타일링·투명 배경)
    ///  · Ignore Raycast 레이어 → 시야가림 페이드 콜라이더(MapFadeColliderSetup) 자동 제외, 그림자 X
    ///  · 개별 언덕이 거슬리면 프리팹의 ~Horizon 아래에서 지우거나 옮겨도 됨(재실행 시 새로 깔림)
    ///
    /// 실행: Tools ▸ Map ▸ ★ 비주얼 정리(하늘·안개·원경·포프) 전체 적용
    /// 되돌리기: git checkout(씬·프리팹) — 별도 에셋만 추가되고 기존 오브젝트는 건드리지 않는다.
    /// </summary>
    public static class MapVisualPolishTool
    {
        private const string kMatDir       = "Assets/Map/Materials";
        private const string kSkySrcPath   = "Assets/ThirdParty/FastSky/Materials/StylisedSky.mat";
        private const string kSkyPath      = kMatDir + "/Sky_SeoulStylised.mat";
        private const string kProfilePath  = "Assets/Settings/GameVisualProfile.asset";
        private const string kHorizonName  = "~Horizon";

        private static readonly string[] kScenes =
        {
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/WeatherTest.unity",
        };

        /// <summary>맵별 원경 성격. Grass = 풀밭+나무(교외), City = 도심 옥상 바닥·나무 없음·스커트 없음(시티뷰).</summary>
        private enum GroundKind { Grass, City }
        private struct MapProfile
        {
            public string Path; public GroundKind Ground; public bool Trees; public bool Skirt;
            public string[] RemoveObjects;   // 기획 배경에서 치울 오브젝트 이름(사용자 승인분)
            public float? FloorY;            // 지평선 바닥 높이 강제(없으면 자동 탐지). 바닥 오브젝트를 치운 맵은 필수
            public string[] StretchZ;        // z축으로 1km 늘릴 오브젝트(물길·둔치) — '뚝 끊김' 제거, 강이 지평선까지 도시를 관통
            public float? ChannelXMin, ChannelXMax;   // 바닥 평면에서 비워둘 x 구간(물길). 있으면 바닥을 좌·우 두 장으로 깐다
        }
        private static readonly MapProfile[] kMaps =
        {
            new MapProfile { Path = "Assets/Map/Prefabs/MapBg_Tutorial.prefab",    Ground = GroundKind.Grass, Trees = true,  Skirt = true },
            new MapProfile { Path = "Assets/Map/Prefabs/MapBg_GwangTongGyo.prefab", Ground = GroundKind.City,  Trees = false, Skirt = false,   // 청계천 = 완전 시티뷰
                             FloorY = 5.52f,                                       // 둔치 윗면(5.4)보다 살짝 위에 도시 옥상 텍스처를 덮음(바닥 평면 y = FloorY-0.1 = 5.42). 콜라이더는 그대로
                             StretchZ = new[] { "Cube", "Cube (1)", "Cube (2)" },   // 물길+둔치 40×200 → 1km (08/22 "알아서 커트" 승인)
                             ChannelXMin = -12.6f, ChannelXMax = 13f },            // 물길 구간은 덮지 않음
            new MapProfile { Path = "Assets/Map/Prefabs/MapBg_NamsanTower.prefab",  Ground = GroundKind.City,  Trees = false, Skirt = true,
                             RemoveObjects = new[] { "CityPlain" }, FloorY = -27.4f },                                                                          // 산 위에서 내려다본 도시. 회색 판은 치움(08/22 승인)
            new MapProfile { Path = "Assets/Map/Prefabs/MapBg_VersusField.prefab",  Ground = GroundKind.Grass, Trees = true,  Skirt = true },
        };

        // ── 팔레트(모두 같은 '맑은 오후' 톤으로 묶음 — 안개색 = 지평선 하늘색) ──
        private static readonly Color kFogColor      = new Color(0.89f, 0.93f, 0.97f);   // FastSky 지평선 밝기에 맞춤(어두우면 원경이 회색 덩어리로 뜸)
        private static readonly Color kAmbientSky    = new Color(0.66f, 0.74f, 0.86f);
        private static readonly Color kAmbientEq     = new Color(0.62f, 0.60f, 0.56f);
        private static readonly Color kAmbientGround = new Color(0.34f, 0.31f, 0.28f);
        private static readonly Color kSunColor      = new Color(1.00f, 0.96f, 0.88f);
        private static readonly Color kGroundColor   = new Color(0.56f, 0.68f, 0.46f);   // 연한 풀밭
        // 카드 틴트 — VARCO 컬러 이미지라 거의 흰색. 코드로 그린 흰 실루엣 PNG(폴백)일 때만 색이 여기서 나온다.
        private static readonly Color kCityTint      = new Color(0.80f, 0.86f, 0.96f);   // 배경은 전경보다 흐려야 함
        private static readonly Color kMountainTint  = new Color(0.84f, 0.90f, 1.00f);
        private static readonly Color kTreeTint      = new Color(0.86f, 0.88f, 0.84f);
        private static readonly Color kSkirtColor    = new Color(0.46f, 0.55f, 0.38f);   // 맵 밑 산 몸통(짙은 풀색)
        private const string kHorizonTexDir = "Assets/Map/Horizon";

        private const float kFogStart = 90f;
        private const float kFogEnd   = 320f;

        [MenuItem("Tools/Map/★ 비주얼 정리(하늘·안개·원경·포프) 전체 적용")]
        public static void ApplyAll()
        {
            var sky     = EnsureSkyMaterial();
            var profile = EnsureVolumeProfile();

            foreach (var m in kMaps) ApplyHorizonToPrefab(m);
            foreach (var s in kScenes) ApplySceneSettings(s, sky, profile);
            foreach (var leftover in new[] { "Mat_HorizonHillNear", "Mat_HorizonHillFar", "Mat_HorizonNear", "Mat_HorizonCity", "Mat_HorizonMountain" })   // 1차 버전(언덕 스피어) 잔재 정리
                AssetDatabase.DeleteAsset($"{kMatDir}/{leftover}.mat");

            AssetDatabase.SaveAssets();
            Debug.Log("[비주얼정리] 완료 ✔ 하늘(FastSky)·Linear 안개·Trilight 앰비언트·포프 + 맵 4종 ~Horizon. " +
                      "GameScene 플레이해서 확인 — 톤은 Assets/Settings/GameVisualProfile, 하늘은 Assets/Map/Materials/Sky_SeoulStylised에서 조절.");
        }

        [MenuItem("Tools/Map/비주얼 정리 — 맵 프리팹 ~Horizon만 다시 깔기")]
        public static void ApplyHorizonOnly()
        {
            foreach (var m in kMaps) ApplyHorizonToPrefab(m);
            AssetDatabase.SaveAssets();
            Debug.Log("[비주얼정리] ~Horizon 4종 갱신 ✔");
        }

        // ───────────────────────────── 하늘 ─────────────────────────────
        private static Material EnsureSkyMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kSkyPath);
            if (mat != null)
            {
                SetIf(mat, "_DayBrightness", 0.95f);   // 1.1은 하늘이 하얗게 날아감(포프 노출과 겹침)
                TuneClouds(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            var src = AssetDatabase.LoadAssetAtPath<Material>(kSkySrcPath);
            if (src == null)
            {
                Debug.LogWarning($"[비주얼정리] FastSky 머티리얼이 없음({kSkySrcPath}) — URP Procedural 스카이박스로 대체");
                mat = new Material(Shader.Find("Skybox/Procedural"));
                mat.SetFloat("_AtmosphereThickness", 0.85f);
                mat.SetColor("_SkyTint", new Color(0.55f, 0.72f, 0.95f));
                mat.SetColor("_GroundColor", kFogColor);
                mat.SetFloat("_Exposure", 1.25f);
            }
            else
            {
                mat = new Material(src);
                // 카툰톤: 채도 약간 올리고, 구름은 크고 느리게·조금 적게. 별은 낮엔 안 보이므로 그대로.
                SetIf(mat, "_Saturation", 0.65f);
                SetIf(mat, "_DayBrightness", 0.95f);
                SetIf(mat, "_CloudSpeed", 1.2f);
                TuneClouds(mat);
                SetIf(mat, "_SunSize", 0.15f);
            }
            Directory.CreateDirectory(kMatDir);
            AssetDatabase.CreateAsset(mat, kSkyPath);
            return mat;
        }

        private static void SetIf(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }

        /// <summary>구름: 비틀림(Twirl)이 크면 하늘에 줄무늬 띠가 생긴다 → 낮추고, 뭉게구름답게 크고 부드럽게.</summary>
        private static void TuneClouds(Material mat)
        {
            SetIf(mat, "_CloudTwirl", 0.15f);
            SetIf(mat, "_CloudScale", 0.6f);
            SetIf(mat, "_CloudThickness", 0.4f);
            SetIf(mat, "_CloudSoftness", 0.5f);
            SetIf(mat, "_CloudDensity", 3f);
            SetIf(mat, "_CloudSpeed", 0.8f);
        }

        // ───────────────────────────── 포스트프로세싱 ─────────────────────────────
        private static VolumeProfile EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(kProfilePath);
            if (profile != null)
            {
                if (profile.TryGet<ColorAdjustments>(out var ca)) { ca.postExposure.Override(0f); EditorUtility.SetDirty(ca); }   // 0.15는 바닥/하늘이 회백색으로 떠 보임
                return profile;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, kProfilePath);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0f);
            color.contrast.Override(8f);
            color.saturation.Override(15f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>프로필 에셋의 서브에셋으로 붙여야 저장된다(Add만 하면 재시작 시 사라짐).</summary>
        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ───────────────────────────── 씬 세팅 ─────────────────────────────
        private static void ApplySceneSettings(string scenePath, Material sky, VolumeProfile profile)
        {
            if (!File.Exists(scenePath)) { Debug.LogWarning($"[비주얼정리] 씬 없음: {scenePath}"); return; }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            try
            {
                // 하늘·안개·앰비언트
                RenderSettings.skybox = sky;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = kFogColor;
                RenderSettings.fogStartDistance = kFogStart;
                RenderSettings.fogEndDistance = kFogEnd;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = kAmbientSky;
                RenderSettings.ambientEquatorColor = kAmbientEq;
                RenderSettings.ambientGroundColor = kAmbientGround;
                RenderSettings.ambientIntensity = 1f;

                // 태양
                var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                                .FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null)
                {
                    sun.color = kSunColor;
                    sun.intensity = 1.15f;
                    RenderSettings.sun = sun;
                }

                // 카메라 포프 ON (+ 가벼운 FXAA)
                foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (cam.targetTexture != null) continue;   // 미리보기용 RT 카메라 제외
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data == null) continue;
                    data.renderPostProcessing = true;
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    EditorUtility.SetDirty(cam);
                }

                // 글로벌 Volume — 있으면 프로필만 연결, 없으면 생성
                var volume = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).FirstOrDefault(v => v.isGlobal);
                if (volume == null)
                {
                    var go = new GameObject("@GlobalVolume");
                    volume = go.AddComponent<Volume>();
                    volume.isGlobal = true;
                }
                volume.sharedProfile = profile;
                EditorUtility.SetDirty(volume);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[비주얼정리] 씬 적용 ✔ {Path.GetFileName(scenePath)}");
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        // ───────────────────────────── 맵 프리팹: ~Horizon ─────────────────────────────
        private static void ApplyHorizonToPrefab(MapProfile profile)
        {
            string prefabPath = profile.Path;
            if (!File.Exists(prefabPath)) { Debug.LogWarning($"[비주얼정리] 프리팹 없음: {prefabPath}"); return; }

            // 도시 맵 바닥은 탑뷰 도시 그림(=놀이매트처럼 보임) 대신 차분한 아스팔트 + 입체 빌딩 격자로.
            var groundMat   = profile.Ground == GroundKind.City
                ? EnsureGroundMaterial("Mat_HorizonGroundCity", "Ground_Asphalt", new Color(0.78f, 0.78f, 0.80f))
                : EnsureGroundMaterial("Mat_HorizonGround", "Ground_Grass", new Color(0.66f, 0.74f, 0.58f));
            var skirtMat    = EnsureLitMaterial("Mat_HorizonSkirt", kSkirtColor);
            var cityTex     = EnsureSkylineTexture("Skyline_City", SkylineKind.City);
            var mountainTex = EnsureSkylineTexture("Skyline_Mountain", SkylineKind.Mountain);
            var cityMats     = CardVariants("Mat_HorizonCity",     cityTex,     kCityTint,     2985);
            var mountainMats = CardVariants("Mat_HorizonMountain", mountainTex, kMountainTint, 2980);   // 먼 것부터 그림
            var treeMats    = LoadTreeMaterials();   // Assets/Map/Horizon/Trees/*.png → 알파컷 빌보드(없으면 나무 산포 생략)

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var old = root.transform.Find(kHorizonName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                if (profile.RemoveObjects != null)
                    foreach (var n in profile.RemoveObjects)
                    {
                        var t = root.transform.Find(n);
                        if (t != null) { Object.DestroyImmediate(t.gameObject); Debug.Log($"[비주얼정리] {root.name}: '{n}' 제거"); }
                    }

                // 기준점: ① 바닥 = 수평 면적이 가장 넓은 렌더러(도시 평면/지면) — AABB 최저점은 산 모델 밑면 등에 끌려 내려가서 못 씀
                //         ② 플레이 영역 = Spot_* 마커 분포(없으면 바닥 렌더러) — 원경 링 반경 기준
                if (profile.StretchZ != null)
                    foreach (var n in profile.StretchZ)
                    {
                        var t = root.transform.Find(n);
                        if (t == null) continue;
                        var sc = t.localScale; sc.z = 1000f; t.localScale = sc;
                        Debug.Log($"[비주얼정리] {root.name}: '{n}' z 1km로 연장");
                    }

                GetPlayArea(root, out var playCenter, out float playR, out float playMinY);
                var floor = FindFloor(root, playMinY);
                if (profile.FloorY.HasValue) floor = new Bounds(new Vector3(floor.center.x, profile.FloorY.Value, floor.center.z), new Vector3(floor.size.x, 0.1f, floor.size.z));
                float groundY = floor.min.y - 0.05f;
                var   center = new Vector3(playCenter.x, groundY, playCenter.z);
                // 링은 가깝고 크게 — 45° 카메라에서 맵 가장자리 너머 '빈 바닥'이 보이는 면적을 최소화
                float cityR  = Mathf.Max(playR * 2.5f + 80f, 170f);   // 95m는 너무 가까워 '벽'이 됐음 — 멀리, 낮게
                float mountR = cityR * 1.6f;

                var group = new GameObject(kHorizonName);
                group.transform.SetParent(root.transform, false);
                group.transform.position = Vector3.zero;

                // 1) 지평선 바닥 — 1km 평면. 안개 끝(320m)보다 훨씬 커서 가장자리는 완전히 하늘색으로 녹는다.
                //    물길(Channel)이 있는 맵은 좌·우 두 장으로 깔아 물길을 비운다.
                if (profile.ChannelXMin.HasValue && profile.ChannelXMax.HasValue)
                {
                    float half = 500f;
                    MakeGroundPlane(group.transform, groundMat, "HorizonGroundW", new Vector3(profile.ChannelXMin.Value - half, groundY, center.z), new Vector3(half * 2f, 1f, 1000f));
                    MakeGroundPlane(group.transform, groundMat, "HorizonGroundE", new Vector3(profile.ChannelXMax.Value + half, groundY, center.z), new Vector3(half * 2f, 1f, 1000f));
                }
                else
                {
                    MakeGroundPlane(group.transform, groundMat, "HorizonGround", center, new Vector3(1000f, 1f, 1000f));
                }

                // 2) 스커트 — 맵이 뚝 끊겨 떠 보이지 않게, 가장 넓은 바닥 렌더러(플랫폼) 밑을 찌부 스피어로 메운다.
                //    남산처럼 플랫폼이 바닥 평면보다 높으면 산 몸통이 되고, 평지 맵이면 거의 납작해서 티 안 남.
                //    플랫폼 = 바닥보다 높이 떠 있는 렌더러 중 가장 넓고 플레이 영역 근처인 것(남산: Ground -3, 바닥 CityPlain -27).
                var platform = FindPlatform(root, floor, playCenter, playR);
                float drop = platform.HasValue ? (platform.Value.min.y - 0.3f) - groundY : 0f;
                if (profile.Skirt && platform.HasValue && drop > 0.5f)
                {
                    var pb = platform.Value;
                    var skirt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Prep(skirt, group.transform, "HorizonSkirt", skirtMat);
                    float pw = Mathf.Max(pb.size.x, pb.size.z) * 1.6f;   // 너무 넓히면 케이블카 터미널 등 아랫동네 오브젝트를 묻는다
                    skirt.transform.position = new Vector3(pb.center.x, groundY, pb.center.z);
                    skirt.transform.localScale = new Vector3(pw, drop * 2f, pw);   // 중심이 바닥 → 윗반쪽 = 산
                }

                // 3) 원경 실루엣 카드 링 — 가까운 도심(청회색) + 먼 산 능선(하늘색). 코드로 그린 PNG, 빌보드 아님(겹쳐 세움).
                //    카드 높이는 텍스처 비율에서 자동(카드 1장 = 텍스처 가로 1/3) → 이미지가 안 늘어난다.
                //    카드 높이를 m로 고정(도심 16m·산 28m) — 너비는 이미지 비율대로, 장수는 둘레에 맞춰 자동.
                MakeCardRing(group.transform, cityMats,     "City",  center, cityR,  16f, CardAspect(cityTex), 0);
                MakeCardRing(group.transform, mountainMats, "Mount", center, mountR, 28f, CardAspect(mountainTex), 1);
                // 4) 나무 산포 — 맵 가장자리 바깥 ~ 도심 링 사이에 X자 빌보드 나무를 랜덤으로. (근경 '링'은 벽지처럼 보여 폐기)
                //    남산처럼 떠 있는 맵(스커트 있음)은 아랫동네와 충돌하니 생략.
                if (profile.Trees && treeMats.Count > 0 && drop <= 0.5f)
                    ScatterTrees(group.transform, treeMats, center, playR + 8f, cityR - 10f);

                // 5) 도시 맵: 도로 격자 위 입체 빌딩(창문 텍스처 박스). 맵에서 멀수록 높아져 스카이라인이 생긴다.
                if (profile.Ground == GroundKind.City)
                    ScatterBuildings(root, group.transform, center, groundY, playR + 12f, cityR + 20f, profile);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[비주얼정리] ~Horizon ✔ {Path.GetFileNameWithoutExtension(prefabPath)} (바닥 y={groundY:F1}, 플레이R={playR:F0}, 스커트 {drop:F1}m, 카드 r={cityR:F0}/{mountR:F0})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Plane 프리미티브(10×10m)를 size(m)로 맞춰 깐다. 텍스처 타일링은 머티리얼 기준(1km당)이라 크기에 맞춰 보정.</summary>
        private static void MakeGroundPlane(Transform parent, Material mat, string name, Vector3 center, Vector3 sizeMeters)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Prep(ground, parent, name, mat);
            ground.transform.position = center;
            ground.transform.localScale = new Vector3(sizeMeters.x / 10f, 1f, sizeMeters.z / 10f);
            if (Mathf.Abs(sizeMeters.x - 1000f) > 1f || Mathf.Abs(sizeMeters.z - 1000f) > 1f)
            {   // 1km가 아닌 평면은 타일 밀도가 달라지므로 머티리얼 변형을 따로 둔다(SRP Batcher 때문에 MPB 불가)
                var m = new Material(mat) { name = mat.name + "_" + name };
                var st = mat.GetTextureScale("_BaseMap");
                m.SetTextureScale("_BaseMap", new Vector2(st.x * sizeMeters.x / 1000f, st.y * sizeMeters.z / 1000f));
                string path = $"{kMatDir}/{m.name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null) AssetDatabase.CreateAsset(m, path); else { existing.CopyPropertiesFromMaterial(m); m = existing; }
                ground.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
        }

        /// <summary>둘레에 카드(쿼드)를 25% 겹치게 세워 360° 띠를 만든다. 카드 아랫변 = 바닥. 3종 UV 변형 머티리얼을 돌려 써 반복 티를 줄인다.</summary>
        private static void MakeCardRing(Transform parent, Material[] mats, string prefix, Vector3 center,
                                         float radius, float height, float aspect, int shift)
        {
            float width = height / Mathf.Max(aspect, 0.05f);
            int count = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * radius / (width / 1.25f)));
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f;
                var pos = center + new Vector3(Mathf.Cos(ang) * radius, height * 0.5f, Mathf.Sin(ang) * radius);
                var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Prep(card, parent, $"{prefix}{i + 1}", mats[(i + shift) % mats.Length]);
                card.transform.position = pos;
                card.transform.localScale = new Vector3(width, height, 1f);
                card.transform.rotation = Quaternion.LookRotation(pos - new Vector3(center.x, pos.y, center.z));   // Cull Off라 어느 쪽이든 보임
            }
        }

        /// <summary>같은 텍스처를 가로 1/3씩 다른 구간으로 보여주는 머티리얼 3종(SRP Batcher가 MPB를 무시하므로 머티리얼로 분리).</summary>
        private static Material[] CardVariants(string name, Texture2D tex, Color tint, int queue)
        {
            var arr = new Material[3];
            for (int k = 0; k < 3; k++)
            {
                var m = EnsureCardMaterial($"{name}_{k}", tex, tint, queue);
                if (m != null) { m.SetTextureScale("_BaseMap", new Vector2(1f / 3f, 1f)); m.SetTextureOffset("_BaseMap", new Vector2(k / 3f, 0f)); }
                arr[k] = m;
            }
            return arr;
        }

        /// <summary>카드 1장이 텍스처 가로 1/3을 보여주므로 높이/너비 = (h / (w/3)).</summary>
        private static float CardAspect(Texture2D tex) => tex == null ? 0.4f : tex.height / (tex.width / 3f);

        // ───────────────────────────── 실루엣 텍스처(코드로 그림) ─────────────────────────────
        private enum SkylineKind { City, Mountain }

        /// <summary>가로 타일링되는 실루엣 PNG(흰색 + 알파). 이미 있으면 그대로 둔다 → 손으로 그린 그림으로 교체 가능.</summary>
        private static Texture2D EnsureSkylineTexture(string name, SkylineKind kind)
        {
            Directory.CreateDirectory(kHorizonTexDir);
            string path = $"{kHorizonTexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) { ApplyImportSettings(path); return existing; }

            const int W = 2048, H = 512;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color32[W * H];
            var heights = new float[W];
            var rng = new System.Random(kind == SkylineKind.City ? 11 : 23);

            if (kind == SkylineKind.City)
            {
                // 빌딩: 폭 20~90px, 높이 0.15~0.75, 가끔 고층·안테나. 양끝 높이를 같게 해서 타일링.
                int x = 0;
                while (x < W)
                {
                    int bw = 20 + rng.Next(70);
                    float bh = 0.15f + (float)rng.NextDouble() * 0.6f;
                    if (bh > 0.3f && rng.Next(4) == 0) bh = Mathf.Min(0.95f, bh * 1.25f);
                    for (int i = 0; i < bw && x + i < W; i++) heights[x + i] = bh;
                    if (rng.Next(3) == 0)                                        // 안테나
                        for (int i = -1; i <= 1; i++) { int xi = x + bw / 2 + i; if (xi >= 0 && xi < W) heights[xi] = Mathf.Min(1f, bh + 0.12f); }
                    x += bw + rng.Next(6);
                }
                for (int i = 0; i < 24; i++) heights[i] = heights[W - 24 + i] = heights[24];   // 이음새
            }
            else
            {
                // 산: 사인 3겹 합성(주기가 W의 약수라 자연 타일링) + 잔능선
                for (int i = 0; i < W; i++)
                {
                    float t = i / (float)W * Mathf.PI * 2f;
                    float h = 0.42f + 0.22f * Mathf.Sin(t * 2f + 0.7f) + 0.12f * Mathf.Sin(t * 5f + 2.1f) + 0.06f * Mathf.Sin(t * 11f);
                    h += 0.025f * Mathf.Abs(Mathf.Sin(t * 37f));
                    heights[i] = Mathf.Clamp01(h);
                }
            }

            for (int x = 0; x < W; x++)
            {
                int top = Mathf.RoundToInt(heights[x] * (H - 1));
                for (int y = 0; y < H; y++)
                {
                    bool solid = y <= top;
                    byte v = (byte)(solid && y < top * 0.35f ? 235 : 255);   // 아래쪽 살짝 어둡게(2톤)
                    px[y * W + x] = new Color32(v, v, v, (byte)(solid ? 255 : 0));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            ApplyImportSettings(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>잔디 바닥(URP Lit). Assets/Map/Horizon/Ground_Grass.png가 있으면 그걸, 없으면 코드로 만든 타일 노이즈를 쓴다.</summary>
        private static Material EnsureGroundMaterial(string name, string texName, Color tint)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != sh) mat.shader = sh;

            string texPath = $"{kHorizonTexDir}/{texName}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) tex = texName.Contains("Asphalt")
                ? EnsureNoiseTexture("Ground_AsphaltNoise", new Color(0.50f, 0.50f, 0.53f), new Color(0.56f, 0.56f, 0.58f))
                : EnsureNoiseTexture("Ground_GrassNoise", new Color(0.52f, 0.66f, 0.40f), new Color(0.60f, 0.74f, 0.46f));
            else ApplyTileImportSettings(texPath);

            mat.SetTexture("_BaseMap", tex);
            float tile = 10f;
            mat.SetTextureScale("_BaseMap", new Vector2(1000f / tile, 1000f / tile));   // ※ MaterialPropertyBlock은 SRP Batcher가 무시함
            mat.SetColor("_BaseColor", tex != null ? tint : kGroundColor);   // VARCO 텍스처가 쨍해서 틴트로 눌러줌
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>두 톤이 얼룩지는 타일 노이즈(256²). 잔디/아스팔트 폴백.</summary>
        private static Texture2D EnsureNoiseTexture(string name, Color a, Color b)
        {
            Directory.CreateDirectory(kHorizonTexDir);
            string path = $"{kHorizonTexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N * Mathf.PI * 2f, v = y / (float)N * Mathf.PI * 2f;
                    float n = 0.5f + 0.25f * Mathf.Sin(u * 3f + Mathf.Sin(v * 2f)) + 0.25f * Mathf.Sin(v * 5f + Mathf.Cos(u * 4f) * 1.3f);
                    px[y * N + x] = Color.Lerp(a, b, Mathf.SmoothStep(0f, 1f, n));
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            ApplyTileImportSettings(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>원경 실루엣 PNG 임포트: 가로 Repeat(카드마다 UV 구간을 밀어 씀)·세로 Clamp·알파·밉맵.</summary>
        private static void ApplyImportSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty = imp.textureType != TextureImporterType.Default || !imp.alphaIsTransparency ||
                         imp.wrapModeU != TextureWrapMode.Repeat || imp.wrapModeV != TextureWrapMode.Clamp || !imp.mipmapEnabled ||
                         imp.maxTextureSize < 2048;
            if (!dirty) return;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.wrapModeU = TextureWrapMode.Repeat;
            imp.wrapModeV = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }

        private static void ApplyTileImportSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            // 도시 옥상 텍스처는 격자라 크로스페이드 이음새가 티 남 → Mirror 반복(대칭은 멀리서 티 안 남)
            var wrap = Path.GetFileName(path).StartsWith("Ground_City") ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
            if (imp.wrapMode == wrap && imp.mipmapEnabled && imp.filterMode == FilterMode.Trilinear && imp.maxTextureSize >= 1024) return;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
        }

        // ───────────────────────────── 빌딩 격자(도시 맵) ─────────────────────────────
        private const float kBlock = 26f, kStreet = 6f;   // 블록 26m(도로 6m 포함)

        private static readonly Color[] kFacadeTints =
        {
            new Color(0.93f, 0.90f, 0.84f), new Color(0.86f, 0.89f, 0.94f), new Color(0.90f, 0.86f, 0.82f),
            new Color(0.84f, 0.88f, 0.86f), new Color(0.95f, 0.93f, 0.90f), new Color(0.80f, 0.84f, 0.90f),
        };

        private static void ScatterBuildings(GameObject root, Transform parent, Vector3 center, float groundY,
                                             float rMin, float rMax, MapProfile profile)
        {
            var facadeTex = EnsureFacadeTexture();
            var facades = new Material[kFacadeTints.Length];
            for (int i = 0; i < facades.Length; i++) facades[i] = EnsureFacadeMaterial($"Mat_Bldg_Facade{i}", facadeTex, kFacadeTints[i]);
            var roofMat = EnsureLitMaterial("Mat_Bldg_Roof", new Color(0.52f, 0.53f, 0.56f));

            // 기존 배경 오브젝트 자리(기획자 빌딩·케이블카 터미널 등)는 비운다 — 거대 바닥류는 제외
            var occupied = new System.Collections.Generic.List<Rect>();
            foreach (var r in MeshRenderers(root))
            {
                var b = r.bounds;
                if (b.size.x * b.size.z > 2500f) continue;
                occupied.Add(Rect.MinMaxRect(b.min.x - 2f, b.min.z - 2f, b.max.x + 2f, b.max.z + 2f));
            }

            var grp = new GameObject("Buildings"); grp.transform.SetParent(parent, false);
            var rng = new System.Random(2024);
            var meshLib = LoadMeshLibrary();
            var bldgPrefabs = LoadBuildingPrefabs();   // Assets/Map/Horizon/Buildings/ 의 VARCO 3D(glb/fbx/prefab). 있으면 박스 대신 이걸 배치
            int made = 0;
            int cells = Mathf.CeilToInt(rMax / kBlock) + 1;
            for (int gz = -cells; gz <= cells; gz++)
                for (int gx = -cells; gx <= cells; gx++)
                {
                    // 블록 원점(도로 격자에 정렬) — 플레이 영역 중심 기준
                    float bx0 = center.x + gx * kBlock + kStreet * 0.5f;
                    float bz0 = center.z + gz * kBlock + kStreet * 0.5f;
                    float inner = kBlock - kStreet;
                    // 블록을 2×2 필지로 나눠 필지마다 빌딩 1개(가끔 비움)
                    for (int lz = 0; lz < 2; lz++)
                        for (int lx = 0; lx < 2; lx++)
                        {
                            if (rng.Next(7) == 0) continue;   // 공터
                            float lot = inner * 0.5f;
                            float w = lot * Mathf.Lerp(0.55f, 0.9f, (float)rng.NextDouble());
                            float d = lot * Mathf.Lerp(0.55f, 0.9f, (float)rng.NextDouble());
                            float cx = bx0 + lx * lot + lot * 0.5f + ((float)rng.NextDouble() - 0.5f) * (lot - w) * 0.8f;
                            float cz = bz0 + lz * lot + lot * 0.5f + ((float)rng.NextDouble() - 0.5f) * (lot - d) * 0.8f;

                            float dist = Vector2.Distance(new Vector2(cx, cz), new Vector2(center.x, center.z));
                            if (dist < rMin || dist > rMax) continue;
                            if (profile.ChannelXMin.HasValue && cx + w * 0.5f > profile.ChannelXMin.Value - 3f && cx - w * 0.5f < profile.ChannelXMax.Value + 3f) continue;
                            bool blocked = false;
                            foreach (var o in occupied) if (o.Contains(new Vector2(cx, cz))) { blocked = true; break; }
                            if (blocked) continue;

                            // 높이: 가까우면 낮게(4~9m), 멀수록 높게(최대 ~34m) → 맵 주변은 트이고 멀리 스카이라인
                            float t = Mathf.InverseLerp(rMin, rMax, dist);
                            float hMin = Mathf.Lerp(4f, 9f, t), hMax = Mathf.Lerp(9f, 34f, t);
                            float h = Mathf.Lerp(hMin, hMax, Mathf.Pow((float)rng.NextDouble(), 1.6f));
                            if (rng.Next(12) == 0) h *= 1.6f;   // 가끔 고층

                            GameObject go;
                            if (bldgPrefabs.Count > 0)
                            {   // VARCO 3D 빌딩: 필지 너비에 맞춰 균일 스케일(모델 바닥 원점 가정), 90° 단위 랜덤 회전
                                var src = bldgPrefabs[rng.Next(bldgPrefabs.Count)];
                                go = (GameObject)PrefabUtility.InstantiatePrefab(src, grp.transform);
                                go.name = $"B{++made}";
                                float modelW = MeasureFootprint(src);
                                float sc = modelW > 0.01f ? Mathf.Max(w, d) / modelW : 1f;
                                sc *= Mathf.Lerp(0.9f, 1.15f, (float)rng.NextDouble());
                                sc *= Mathf.Lerp(0.8f, 1.3f, t);   // 멀수록 조금 크게(스카이라인)
                                go.transform.localScale = Vector3.one * sc;
                                go.transform.position = new Vector3(cx, groundY, cz);
                                go.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                                foreach (var r in go.GetComponentsInChildren<Renderer>())
                                { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); }
                                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                            }
                            else
                            {
                                go = new GameObject($"B{++made}");
                                go.transform.SetParent(grp.transform, false);
                                go.transform.position = new Vector3(cx, groundY, cz);
                                var mf = go.AddComponent<MeshFilter>();
                                var mr = go.AddComponent<MeshRenderer>();
                                mf.sharedMesh = GetBoxMesh(meshLib, w, h, d);
                                mr.sharedMaterials = new[] { facades[rng.Next(facades.Length)], roofMat };
                                mr.shadowCastingMode = ShadowCastingMode.Off;
                                mr.receiveShadows = false;
                            }
                            go.layer = LayerMask.NameToLayer("Ignore Raycast");
                            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                        }
                }
            Debug.Log($"[비주얼정리] {root.name}: 빌딩 {made}개 (r {rMin:F0}~{rMax:F0})");
        }

        private const string kMeshLibPath = kHorizonTexDir + "/BldgMeshes.asset";

        /// <summary>Assets/Map/Horizon/Buildings/ 아래 모델(glb·fbx·prefab) 목록. 비어 있으면 박스 빌딩 폴백.</summary>
        private static System.Collections.Generic.List<GameObject> LoadBuildingPrefabs()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            string dir = $"{kHorizonTexDir}/Buildings";
            if (!Directory.Exists(dir)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { dir }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.GetComponentInChildren<Renderer>() != null) list.Add(go);
            }
            return list;
        }

        private static float MeasureFootprint(GameObject prefab)
        {
            var rs = prefab.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Max(b.size.x, b.size.z);
        }

        /// <summary>빌딩 박스 메시 라이브러리(에셋). 치수를 1m 단위로 양자화해 공유 — 메모리 메시는 프리팹 저장 시 사라지므로 에셋 필수.</summary>
        private static System.Collections.Generic.Dictionary<string, Mesh> LoadMeshLibrary()
        {
            var dict = new System.Collections.Generic.Dictionary<string, Mesh>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(kMeshLibPath))
                if (o is Mesh m) dict[m.name] = m;
            return dict;
        }

        private static Mesh GetBoxMesh(System.Collections.Generic.Dictionary<string, Mesh> lib, float w, float h, float d)
        {
            int qw = Mathf.Max(2, Mathf.RoundToInt(w)), qh = Mathf.Max(2, Mathf.RoundToInt(h)), qd = Mathf.Max(2, Mathf.RoundToInt(d));
            string key = $"Bldg_{qw}x{qh}x{qd}";
            if (lib.TryGetValue(key, out var m)) return m;
            m = BuildBox(qw, qh, qd); m.name = key;
            var main = AssetDatabase.LoadMainAssetAtPath(kMeshLibPath);
            if (main == null) { Directory.CreateDirectory(kHorizonTexDir); AssetDatabase.CreateAsset(m, kMeshLibPath); }
            else AssetDatabase.AddObjectToAsset(m, kMeshLibPath);
            lib[key] = m;
            return m;
        }

        /// <summary>바닥 원점 박스. 서브메시 0 = 옆면(창문 텍스처, UV를 m 단위로 타일링), 1 = 지붕.</summary>
        private static Mesh BuildBox(float w, float h, float d)
        {
            const float kWinW = 10f, kWinH = 12f;   // 파사드 텍스처 한 장 = 가로 10m × 세로 12m(창 4×4)
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs   = new System.Collections.Generic.List<Vector2>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var side  = new System.Collections.Generic.List<int>();
            var roof  = new System.Collections.Generic.List<int>();
            float hx = w * 0.5f, hz = d * 0.5f;

            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 dd, Vector3 n, float uLen, float vLen, System.Collections.Generic.List<int> tris, bool tile)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(dd);
                float us = tile ? uLen / kWinW : 1f, vs = tile ? vLen / kWinH : 1f;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(us, 0)); uvs.Add(new Vector2(us, vs)); uvs.Add(new Vector2(0, vs));
                for (int k = 0; k < 4; k++) norms.Add(n);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
            // 옆면 4개(바깥에서 볼 때 반시계)
            Face(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, h, -hz), new Vector3(-hx, h, -hz), Vector3.back,  w, h, side, true);
            Face(new Vector3(hx, 0, hz),  new Vector3(-hx, 0, hz), new Vector3(-hx, h, hz), new Vector3(hx, h, hz),   Vector3.forward, w, h, side, true);
            Face(new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), new Vector3(-hx, h, -hz), new Vector3(-hx, h, hz), Vector3.left,  d, h, side, true);
            Face(new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz),  new Vector3(hx, h, hz),  new Vector3(hx, h, -hz),  Vector3.right, d, h, side, true);
            // 지붕
            Face(new Vector3(-hx, h, -hz), new Vector3(hx, h, -hz), new Vector3(hx, h, hz), new Vector3(-hx, h, hz), Vector3.up, w, d, roof, false);

            var m = new Mesh { name = "Bldg" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetNormals(norms);
            m.subMeshCount = 2;
            m.SetTriangles(side, 0); m.SetTriangles(roof, 1);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>파사드 타일(256²): 창문 4×4. 흰 바탕(틴트로 색 입힘) + 진한 청회색 창, 가끔 불 켜진 창.</summary>
        private static Texture2D EnsureFacadeTexture()
        {
            Directory.CreateDirectory(kHorizonTexDir);
            string path = $"{kHorizonTexDir}/Bldg_Facade.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int N = 256, cells = 4;
            var px = new Color32[N * N];
            var wall = new Color(1f, 1f, 1f);
            Color winA = new Color(0.30f, 0.36f, 0.46f), winB = new Color(0.40f, 0.47f, 0.58f), lit = new Color(0.98f, 0.90f, 0.62f);
            var rng = new System.Random(9);
            for (int i = 0; i < px.Length; i++) px[i] = wall;
            int cell = N / cells;
            for (int cy = 0; cy < cells; cy++)
                for (int cx = 0; cx < cells; cx++)
                {
                    var c = rng.Next(9) == 0 ? lit : Color.Lerp(winA, winB, (float)rng.NextDouble());
                    int x0 = cx * cell + cell * 22 / 100, x1 = cx * cell + cell * 78 / 100;
                    int y0 = cy * cell + cell * 30 / 100, y1 = cy * cell + cell * 80 / 100;
                    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) px[y * N + x] = c;
                    // 창틀 하이라이트(아래쪽 밝은 줄) — 입체감
                    for (int x = x0; x < x1; x++) px[(y0 - 2) * N + x] = new Color(0.82f, 0.82f, 0.82f);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            ApplyTileImportSettings(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material EnsureFacadeMaterial(string name, Texture2D tex, Color tint)
        {
            var mat = EnsureLitMaterial(name, tint);
            if (mat == null) return null;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ───────────────────────────── 나무 산포 ─────────────────────────────
        private static System.Collections.Generic.List<Material> LoadTreeMaterials()
        {
            var list = new System.Collections.Generic.List<Material>();
            string dir = $"{kHorizonTexDir}/Trees";
            if (!Directory.Exists(dir)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string tp = AssetDatabase.GUIDToAssetPath(guid);
                ApplySpriteImportSettings(tp);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                if (tex == null) continue;
                list.Add(EnsureCardMaterial("Mat_Tree_" + Path.GetFileNameWithoutExtension(tp), tex, kTreeTint, 2450, clip: true));
            }
            return list;
        }

        private static void ApplySpriteImportSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            if (imp.alphaIsTransparency && imp.wrapMode == TextureWrapMode.Clamp && imp.mipmapEnabled) return;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        /// <summary>rMin~rMax 도넛 영역에 X자 빌보드 나무를 뿌린다(결정적 난수). 밀도 ≈ 1그루/420㎡, 가까울수록 조금 큼.</summary>
        private static void ScatterTrees(Transform parent, System.Collections.Generic.List<Material> mats, Vector3 center, float rMin, float rMax)
        {
            if (rMax <= rMin + 2f) return;
            var grp = new GameObject("Trees"); grp.transform.SetParent(parent, false);
            var rng = new System.Random(101);
            float area = Mathf.PI * (rMax * rMax - rMin * rMin);
            int count = Mathf.Clamp(Mathf.RoundToInt(area / 420f), 20, 400);
            for (int i = 0; i < count; i++)
            {
                // 면적 균등 샘플
                float r = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, (float)rng.NextDouble()));
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var pos = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                var mat = mats[rng.Next(mats.Count)];
                var tex = mat.GetTexture("_BaseMap") as Texture2D;
                float aspect = tex != null ? tex.height / (float)tex.width : 1.2f;
                float h = Mathf.Lerp(2.6f, 4.4f, (float)rng.NextDouble()) * Mathf.Lerp(1.1f, 0.9f, Mathf.InverseLerp(rMin, rMax, r));   // 맵 안 나무(~4m)보다 크면 안 됨
                if (aspect < 0.95f) h *= 0.4f;            // 납작한 스프라이트 = 덤불 → 작게
                float w = h / aspect;
                var tree = new GameObject($"Tree{i + 1}");
                tree.transform.SetParent(grp.transform, false);
                tree.transform.position = pos;
                tree.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
                for (int q = 0; q < 2; q++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Prep(quad, tree.transform, q == 0 ? "A" : "B", mat);
                    quad.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
                    quad.transform.localRotation = Quaternion.Euler(0f, q * 90f, 0f);
                    quad.transform.localScale = new Vector3(w, h, 1f);
                }
            }
        }

        /// <summary>URP Unlit · 알파 블렌드 · 양면 — 조명 영향 없이 안개만 먹어서 멀리 있는 실루엣처럼 보인다.</summary>
        private static Material EnsureCardMaterial(string name, Texture2D tex, Color tint, int queue, bool clip = false)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) { Debug.LogWarning("[비주얼정리] URP Unlit 셰이더를 못 찾음"); return null; }
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != sh) mat.shader = sh;

            // 텍스처/틴트/큐는 매번 갱신 — PNG를 VARCO 그림으로 바꿔도 바로 반영
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            if (clip)
            {   // 나무: 알파 컷 + ZWrite → 서로 겹쳐도 정렬 문제 없음
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else
            {   // 원경 카드: 알파 블렌드(부드러운 가장자리·바닥 페이드)
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
            }
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.renderQueue = queue;                            // 먼 링이 먼저 그려지도록 링별 큐
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Prep(GameObject go, Transform parent, string name, Material mat)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");   // 시야가림 페이드 콜라이더·클릭 레이 제외
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static System.Collections.Generic.IEnumerable<Renderer> MeshRenderers(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !IsUnder(r.transform, kHorizonName))
                    yield return r;
        }

        private static bool IsUnder(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == name) return true;
            return false;
        }

        /// <summary>바닥 = 플레이 영역(Spot 마커 최저점)보다 높지 않은 렌더러 중 수평 면적이 가장 넓은 것.
        /// (광통교: 물길 큐브와 둔치 큐브가 면적이 같아 '가장 넓은 것'만으로는 둔치(4.9)가 잡혀 맵이 초록 평면에 덮였음)</summary>
        private static Bounds FindFloor(GameObject root, float playMinY)
        {
            Bounds best = new Bounds(new Vector3(0f, playMinY, 0f), new Vector3(20f, 0.1f, 20f));
            float bestArea = -1f;
            bool found = false;
            foreach (var r in MeshRenderers(root))
            {
                var b = r.bounds;
                if (b.min.y > playMinY + 1f) continue;               // 플레이 영역보다 위에 있는 건 바닥이 아님
                float area = b.size.x * b.size.z;
                if (area > bestArea) { bestArea = area; best = b; found = true; }
            }
            if (!found) Debug.LogWarning($"[비주얼정리] {root.name}: 플레이 영역 아래 바닥 렌더러를 못 찾음 — Spot 높이({playMinY:F1})에 깐다");
            return best;
        }

        /// <summary>플레이 영역 = Spot_* 마커의 중심·반경(+여유)·최저 높이. 마커가 없으면 원점 기준.</summary>
        private static void GetPlayArea(GameObject root, out Vector3 center, out float radius, out float minY)
        {
            var spots = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Spot_")).ToList();
            if (spots.Count == 0)
            {
                Debug.LogWarning($"[비주얼정리] {root.name}: Spot_ 마커가 없음 — 원점 반경 20m를 플레이 영역으로 가정");
                center = Vector3.zero; radius = 20f; minY = 0f;
                return;
            }
            var sum = Vector3.zero; minY = float.MaxValue;
            foreach (var t in spots) { sum += t.position; minY = Mathf.Min(minY, t.position.y); }
            center = sum / spots.Count;
            float r = 0f;
            foreach (var t in spots) r = Mathf.Max(r, Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(center.x, center.z)));
            radius = r + 15f;
        }

        /// <summary>스커트를 받칠 플랫폼: 바닥보다 1m 이상 떠 있고, 중심이 플레이 영역 안(≤ playR)이며, 크기가 플레이 영역 규모(≤ playR×3)인
        /// 렌더러 중 가장 넓은 것. (광통교 둔치 200m 큐브처럼 '맵 전체를 두르는' 단차는 스커트 대상이 아님)</summary>
        private static Bounds? FindPlatform(GameObject root, Bounds floor, Vector3 playCenter, float playR)
        {
            Bounds? best = null;
            float bestArea = -1f;
            foreach (var r in MeshRenderers(root))
            {
                var b = r.bounds;
                if (b.min.y < floor.min.y + 1f) continue;
                float dist = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(playCenter.x, playCenter.z));
                if (dist > playR) continue;
                if (Mathf.Max(b.size.x, b.size.z) > playR * 3f) continue;
                float area = b.size.x * b.size.z;
                if (area > bestArea) { bestArea = area; best = b; }
            }
            return best;
        }

        private static Material EnsureLitMaterial(string name, Color color)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogWarning("[비주얼정리] URP Lit 셰이더를 못 찾음"); return null; }
            mat = new Material(sh);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
