#if UNITY_EDITOR
using System.Collections.Generic;
using CartoonFX;
using GridSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SeoulZikimi.Weather.Editor
{
    internal static class Weather3DAssetBuilder
    {
        private const string Folder = "Assets/Resources/UI_NEW/Weather/3D";
        private const string PrefabPath = Folder + "/Weather3DVfxRig.prefab";
        private const string BuildVersion = "weather-3d-v10-leaf-tint-fix";
        private const string GroundKitPath = Folder + "/WeatherGroundKit.asset";
        private const string SnowmanPrefabPath = Folder + "/Snowman.prefab";
        private const string TestScenePath = "Assets/Scenes/WeatherTest.unity";

        // Cartoon FX Remaster(FREE) 프리팹/텍스처. 톤 통일을 위해 절차 생성 대신 우선 사용한다.
        private const string CfxrRoot = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster";
        private const string CfxrRainPrefab = CfxrRoot + "/CFXR Prefabs/Nature/CFXR4 Rain Falling.prefab";
        private const string CfxrWindPrefab = CfxrRoot + "/CFXR Prefabs/Nature/CFXR4 Wind Trails.prefab";
        private const string CfxrPetalMaterial = CfxrRoot + "/CFXR Assets/Graphics/cfxr petal pink x4 ab lit normal.mat";
        private const string CfxrLeafMaterial = CfxrRoot + "/CFXR Assets/Graphics/cfxr leave a ab lit normal.mat";
        private const string CfxrLeafTexture = CfxrRoot + "/CFXR Assets/Graphics/cfxr leave a.png";
        private const string CfxrPetalTexture = CfxrRoot + "/CFXR Assets/Graphics/cfxr petal pink x4.png";

        [InitializeOnLoadMethod]
        private static void BuildWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                var importer = AssetImporter.GetAtPath(PrefabPath);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null
                    || importer == null || importer.userData != BuildVersion)
                    Build(true);
            };
        }

        [MenuItem("Tools/UI NEW/Rebuild 3D Weather VFX")]
        private static void Rebuild() => Build(true);

        /// <summary>자동 확인용: 테스트 씬을 열고 플레이모드 진입. WeatherTestDriver가 -weatherCapture 인자를 보고 캡처 후 종료한다.</summary>
        private static void CaptureTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null) CreateTestScene();
            EditorSceneManager.OpenScene(TestScenePath);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/UI NEW/Open Weather Test Scene")]
        private static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
                CreateTestScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(TestScenePath);
        }

        private static void Build(bool replaceExisting)
        {
            EnsureFolder(Folder);
            if (replaceExisting && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                AssetDatabase.DeleteAsset(PrefabPath);

            Mesh rainMesh = SaveMesh("RainStreak", CreateBox(0.025f, 1.6f, 0.025f));
            Mesh snowMesh = SaveMesh("SnowCrystal", CreateRadial(6, 1f, 0.24f));
            Mesh windMesh = SaveMesh("WindRibbon", CreateRibbon());
            Mesh leafMesh = SaveMesh("MapleLeaf", CreateMapleLeaf());
            Mesh petalMesh = SaveMesh("CherryPetal", CreatePetal());

            Material rain = SaveMaterial("Rain3D", new Color(0.55f, 0.78f, 1f, 0.36f));
            Material snow = SaveMaterial("Snow3D", new Color(0.88f, 0.96f, 1f, 0.95f));
            Material wind = SaveMaterial("Wind3D", new Color(0.50f, 0.92f, 1f, 0.38f));
            Material leaf = SaveMaterial("AutumnLeaf3D", new Color(0.96f, 0.43f, 0.055f, 0.90f));
            Material petal = SaveMaterial("CherryPetal3D", new Color(1f, 0.58f, 0.74f, 0.92f));

            var root = new GameObject("Weather3DVfxRig");
            var rig = root.AddComponent<Weather3DVfxRig>();

            // 비/바람: CFXR 프리팹이 있으면 중첩 프리팹으로 붙이고, 없으면 절차 생성으로 대체한다.
            ParticleSystem rainFx = InstantiateCfxr(root.transform, "Rain", CfxrRainPrefab, 1f, 0f)
                ?? CreateSystem(root.transform, "Rain", rainMesh, rain, 260f, 2.0f,
                    new Vector3(0f, -15f, 0f), 0.28f, false, 0.04f);
            ParticleSystem snowFx = CreateSystem(root.transform, "Snow", snowMesh, snow, 70f, 6f,
                new Vector3(0.25f, -1.3f, 0.15f), 0.13f, true, 0.32f);
            SlowRotation(snowFx);
            Texture2D flakeTex = SaveTexture("SnowFlake", 64, PaintSnowFlake);
            SaveMaterial("SnowFlakeParticle", new Color(1f, 1f, 1f, 0.95f), flakeTex);
            ApplyCfxrSprite(snowFx, $"{Folder}/SnowFlakeParticle.mat", 1, 1,
                new Color(0.92f, 0.97f, 1f), Color.white, 0.11f);
            ParticleSystem windFx = InstantiateCfxr(root.transform, "StrongWind", CfxrWindPrefab, 1f, 0f)
                ?? CreateSystem(root.transform, "StrongWind", windMesh, wind, 28f, 2.0f,
                    new Vector3(8f, -0.15f, 2f), 0.62f, true, 0.18f);
            ParticleSystem typhoonRain = InstantiateCfxr(root.transform, "TyphoonRain", CfxrRainPrefab, 1.8f, 24f)
                ?? CreateSystem(root.transform, "TyphoonRain", rainMesh, rain, 390f, 1.5f,
                    new Vector3(8f, -18f, 2.5f), 0.34f, false, 0.08f);
            ParticleSystem typhoonWind = InstantiateCfxr(root.transform, "TyphoonWind", CfxrWindPrefab, 1.8f, 0f)
                ?? CreateSystem(root.transform, "TyphoonWind", windMesh, wind, 42f, 1.6f,
                    new Vector3(12f, -0.7f, 3f), 0.72f, true, 0.38f);

            // 낙엽/꽃잎: CFXR 텍스처(흰 잎·핑크 꽃잎 2x2)를 빌보드로 쓰고 색은 틴트로 맞춘다.
            ParticleSystem autumn = CreateSystem(root.transform, "AutumnLeaves", leafMesh, leaf, 36f, 5.2f,
                new Vector3(1.7f, -1.25f, 0.65f), 0.31f, true, 0.40f);
            // CFXR 'lit normal' 머티리얼은 파티클 색을 무시해 분홍으로 나온다 → 텍스처만 빌려 자체 머티리얼로 틴트
            SaveMaterial("LeafParticle", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrLeafTexture));
            ApplyCfxrSprite(autumn, $"{Folder}/LeafParticle.mat", 1, 1,
                new Color(0.95f, 0.45f, 0.08f), new Color(0.78f, 0.16f, 0.06f), 0.24f);
            ParticleSystem cherry = CreateSystem(root.transform, "CherryBlossom", petalMesh, petal, 48f, 5.8f,
                new Vector3(1.2f, -0.95f, 0.45f), 0.25f, true, 0.36f);
            SaveMaterial("PetalParticle", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrPetalTexture));
            ApplyCfxrSprite(cherry, $"{Folder}/PetalParticle.mat", 2, 2,
                new Color(1f, 0.80f, 0.88f), new Color(1f, 0.62f, 0.78f), 0.13f);

            rig.Configure(rainFx, snowFx, windFx, typhoonRain, typhoonWind, autumn, cherry);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var prefabImporter = AssetImporter.GetAtPath(PrefabPath);
            if (prefabImporter != null)
            {
                prefabImporter.userData = BuildVersion;
                prefabImporter.SaveAndReimport();
            }
            BuildGroundKit();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
                CreateTestScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Weather] 3D 날씨 VFX 프리팹 생성 완료: {PrefabPath}");
            LogSummary();
        }

        /// <summary>
        /// CFXR 프리팹을 중첩 프리팹으로 붙인다. 런타임 리그가 Stop/Play로 제어하므로
        /// CFXR 기본 동작(재생 끝나면 파괴)은 끄고, 카메라 추적 시 입자가 끌리지 않게 월드 공간으로 바꾼다.
        /// 프리팹이 없으면 null (절차 생성으로 대체).
        /// </summary>
        /// <summary>생성 결과 확인용. 각 시스템의 핵심 수치를 로그로 남긴다.</summary>
        private static void LogSummary()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;
            foreach (var system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                Debug.Log($"[Weather]   {system.transform.parent?.name}/{system.name}: "
                          + $"space={system.main.simulationSpace} rate={system.emission.rateOverTimeMultiplier:0.#} "
                          + $"bursts={BurstTotal(system):0.#} max={system.main.maxParticles} mode={renderer.renderMode} "
                          + $"mat={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null")} "
                          + $"nested={PrefabUtility.IsPartOfPrefabInstance(system)}");
            }
        }

        private static float BurstTotal(ParticleSystem system)
        {
            var bursts = new ParticleSystem.Burst[system.emission.burstCount];
            system.emission.GetBursts(bursts);
            float total = 0f;
            foreach (var burst in bursts) total += burst.count.constantMax;
            return total;
        }

        private static ParticleSystem InstantiateCfxr(
            Transform parent, string name, string assetPath, float emissionMultiplier, float tiltDegrees)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Weather] CFXR 프리팹 없음, 절차 생성으로 대체: {assetPath}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, tiltDegrees);

            foreach (var effect in instance.GetComponentsInChildren<CFXR_Effect>(true))
                effect.clearBehavior = CFXR_Effect.ClearBehavior.None;

            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.playOnAwake = false;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                if (!Mathf.Approximately(emissionMultiplier, 1f))
                    ScaleEmission(system, emissionMultiplier);
            }
            return instance.GetComponent<ParticleSystem>();
        }

        /// <summary>연속 발생량과 버스트 수를 함께 배수로 키운다(CFXR 바람은 버스트만 쓴다).</summary>
        private static void ScaleEmission(ParticleSystem system, float multiplier)
        {
            var main = system.main;
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * multiplier);

            // CFXR 비는 CFXR_EmissionBySurface가 면적×밀도로 발생량을 다시 계산하므로 밀도를 키운다.
            var bySurface = system.GetComponent<CFXR_EmissionBySurface>();
            if (bySurface != null)
                bySurface.particlesPerUnit *= multiplier;

            var emission = system.emission;
            emission.rateOverTime = Scale(emission.rateOverTime, multiplier);

            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            for (int i = 0; i < bursts.Length; i++)
                bursts[i].count = Scale(bursts[i].count, multiplier);
            emission.SetBursts(bursts);
        }

        /// <summary>납작한 메쉬가 빠르게 돌면 옆에서 막대처럼 보인다. 눈은 천천히 굴리기.</summary>
        private static void SlowRotation(ParticleSystem system)
        {
            var rotation = system.rotationOverLifetime;
            rotation.x = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            rotation.y = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            rotation.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        }

        private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float multiplier)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    curve.constant *= multiplier;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    curve.constantMin *= multiplier;
                    curve.constantMax *= multiplier;
                    break;
                default:
                    curve.curveMultiplier *= multiplier;
                    break;
            }
            return curve;
        }

        /// <summary>절차 생성 메쉬 대신 CFXR 텍스처 빌보드를 쓰고 색 범위를 틴트로 준다.</summary>
        private static void ApplyCfxrSprite(
            ParticleSystem system, string materialPath, int tilesX, int tilesY,
            Color colorA, Color colorB, float size)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Debug.LogWarning($"[Weather] CFXR 머티리얼 없음, 절차 메쉬 유지: {materialPath}");
                return;
            }

            var main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
            // 빌보드는 Z축 회전만 의미가 있다.
            main.startRotation3D = false;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rotation = system.rotationOverLifetime;
            rotation.separateAxes = false;
            rotation.z = new ParticleSystem.MinMaxCurve(-2.8f, 2.8f);

            if (tilesX * tilesY > 1)
            {
                var sheet = system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = tilesX;
                sheet.numTilesY = tilesY;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.9999f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tilesX * tilesY - 0.001f);
                sheet.cycleCount = 0;
            }

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.mesh = null;
            renderer.sharedMaterial = material;
        }

        private static ParticleSystem CreateSystem(
            Transform parent, string name, Mesh mesh, Material material,
            float rate, float lifetime, Vector3 velocity, float size,
            bool rotate, float noiseStrength)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var system = gameObject.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.72f, size * 1.28f);
            // 비처럼 방향성이 있는 입자는 생성 시 회전시키면 막대가 사방으로 흩어진다.
            // 눈/낙엽/꽃잎처럼 회전이 필요한 날씨에만 무작위 3축 회전을 적용한다.
            if (rotate)
            {
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            else
            {
                main.startRotation3D = false;
                main.startRotation = 0f;
            }
            main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.35f);

            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // 카메라가 보는 범위에 집중시켜 형태가 점처럼 흩어지지 않게 한다.
            shape.scale = new Vector3(12f, 1.8f, 9f);

            var velocityModule = system.velocityOverLifetime;
            velocityModule.enabled = true;
            velocityModule.space = ParticleSystemSimulationSpace.World;
            velocityModule.x = velocity.x;
            velocityModule.y = velocity.y;
            velocityModule.z = velocity.z;

            if (rotate)
            {
                var rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                rotation.x = new ParticleSystem.MinMaxCurve(-2.4f, 2.4f);
                rotation.y = new ParticleSystem.MinMaxCurve(-3.2f, 3.2f);
                rotation.z = new ParticleSystem.MinMaxCurve(-2.8f, 2.8f);
            }

            if (noiseStrength > 0f)
            {
                var noise = system.noise;
                noise.enabled = true;
                noise.strength = noiseStrength;
                noise.frequency = 0.32f;
                noise.scrollSpeed = 0.28f;
                noise.damping = true;
            }

            var alpha = system.colorOverLifetime;
            alpha.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f)
                });
            alpha.color = gradient;

            var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        // ───────────────────────── 바닥 키트 ─────────────────────────

        /// <summary>
        /// 바닥 연출용 키트(WeatherGroundKit) 생성. 텍스처는 코드로 그려서 무료 에셋 의존 없이 만들고,
        /// 잎/꽃잎만 CFXR Free 텍스처를 빌린다. 기존 키트가 있으면 수치(개수·크기)는 보존하고 참조만 갱신한다.
        /// </summary>
        private static void BuildGroundKit()
        {
            Mesh disc = SaveMesh("GroundDisc", CreateGroundDisc(28));
            Mesh quad = SaveMesh("GroundQuad", CreateGroundQuad());

            Texture2D puddleTex = SaveTexture("PuddleBlob", 128, PaintPuddle);
            Texture2D snowTex = SaveTexture("SnowBlob", 128, PaintSnowPatch);
            Texture2D trailTex = SaveTexture("SnowTrail", 64, PaintSnowTrail);
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrLeafTexture);
            var petalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrPetalTexture);

            Material puddle = SaveMaterial("PuddleGround", new Color(0.58f, 0.80f, 1f, 0.82f), puddleTex);
            Material snowPatch = SaveMaterial("SnowPatchGround", new Color(0.95f, 0.98f, 1f, 0.94f), snowTex);
            Material snowTrail = SaveMaterial("SnowTrailGround", new Color(0.66f, 0.76f, 0.90f, 0.85f), trailTex);
            Material leafOrange = SaveMaterial("LeafOrangeGround", new Color(0.95f, 0.45f, 0.08f, 1f), leafTex);
            Material leafRed = SaveMaterial("LeafRedGround", new Color(0.78f, 0.16f, 0.06f, 1f), leafTex);
            Material leafYellow = SaveMaterial("LeafYellowGround", new Color(0.98f, 0.72f, 0.12f, 1f), leafTex);
            Material petal = SaveMaterial("PetalGround", new Color(1f, 0.72f, 0.84f, 1f), petalTex);

            var kit = AssetDatabase.LoadAssetAtPath<WeatherGroundKit>(GroundKitPath);
            bool isNew = kit == null;
            if (isNew) kit = ScriptableObject.CreateInstance<WeatherGroundKit>();
            kit.Disc = disc;
            kit.Quad = quad;
            kit.Puddle = puddle;
            kit.SnowPatch = snowPatch;
            kit.SnowTrail = snowTrail;
            kit.Leaves = new[] { leafOrange, leafRed, leafYellow };
            kit.Petal = petal;
            kit.Snowman = BuildSnowman();
            if (isNew) AssetDatabase.CreateAsset(kit, GroundKitPath);
            else EditorUtility.SetDirty(kit);
        }

        private static Material SaveMaterial(string name, Color color, Texture2D texture)
        {
            Material material = SaveMaterial(name, color);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            // 바닥 데칼은 한 면만 보이지만 경사면에서 뒤집혀 보이지 않게 양면으로 둔다.
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D SaveTexture(string name, int size, System.Func<float, float, Color> paint)
        {
            string path = $"{Folder}/{name}.png";
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 중심 (0,0), 가장자리 ±1
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = paint(u, v);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>GLSL smoothstep: x가 a→b를 지나며 0→1.</summary>
        private static float SmoothEdge(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>살짝 울퉁불퉁한 블롭 반지름. 카툰 웅덩이/눈 더미 실루엣.</summary>
        private static float BlobRadius(float u, float v, float wobble)
        {
            float angle = Mathf.Atan2(v, u);
            return 0.80f + wobble * (0.55f * Mathf.Sin(3f * angle + 0.7f) + 0.30f * Mathf.Sin(5f * angle + 2.1f)
                                     + 0.15f * Mathf.Sin(7f * angle + 4.2f));
        }

        private static Color PaintPuddle(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float edge = BlobRadius(u, v, 0.16f);
            if (r > edge) return Color.clear;
            // 바깥 테두리는 밝은 띠(카툰 외곽선), 안쪽은 머티리얼 색 그대로
            float rim = Mathf.InverseLerp(edge - 0.09f, edge, r);
            Color fill = new Color(0.80f, 0.90f, 1f, 1f);
            Color rimColor = new Color(1f, 1f, 1f, 1f);
            return Color.Lerp(fill, rimColor, rim);
        }

        private static Color PaintSnowPatch(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float edge = BlobRadius(u, v, 0.22f);
            // 가장자리만 짧게 부드럽게: 또렷한 더미 실루엣 유지
            float alpha = 1f - SmoothEdge(edge - 0.10f, edge, r);
            if (alpha <= 0f) return Color.clear;
            // 중앙이 살짝 더 밝아 볼록해 보이게
            float bright = Mathf.Lerp(0.93f, 1f, 1f - Mathf.Clamp01(r / edge));
            return new Color(bright, bright, 1f, alpha);
        }

        private static Color PaintSnowTrail(float u, float v)
        {
            // 진행 방향(v)으로 긴 타원. 양 끝은 흐리게 이어 붙여 연속 자국처럼 보이게.
            float d = Mathf.Sqrt(u * u * 2.2f + v * v * 0.9f);
            float alpha = 1f - SmoothEdge(0.55f, 0.95f, d);
            if (alpha <= 0f) return Color.clear;
            return new Color(1f, 1f, 1f, alpha);
        }

        private static Color PaintSnowFlake(float u, float v)
        {
            // 또렷한 동그라미 + 아주 짧은 부드러운 가장자리
            float r = Mathf.Sqrt(u * u + v * v);
            float alpha = 1f - SmoothEdge(0.70f, 0.92f, r);
            return alpha <= 0f ? Color.clear : new Color(1f, 1f, 1f, alpha);
        }

        // ───────────────────────── 눈사람 ─────────────────────────

        /// <summary>프리미티브로 만든 눈사람 프리팹. 콜라이더는 전부 제거(플레이/레이캐스트 방해 X).</summary>
        private static GameObject BuildSnowman()
        {
            Material snow = SaveLitMaterial("SnowmanBody", new Color(0.97f, 0.98f, 1f));
            Material coal = SaveLitMaterial("SnowmanCoal", new Color(0.12f, 0.12f, 0.14f));
            Material carrot = SaveLitMaterial("SnowmanCarrot", new Color(1f, 0.52f, 0.12f));
            Material wood = SaveLitMaterial("SnowmanWood", new Color(0.42f, 0.27f, 0.14f));
            Material scarf = SaveLitMaterial("SnowmanScarf", new Color(0.90f, 0.20f, 0.25f));

            var root = new GameObject("Snowman");
            Part(root, PrimitiveType.Sphere, snow, new Vector3(0f, 0.42f, 0f), Vector3.one * 0.84f, Vector3.zero);
            Part(root, PrimitiveType.Sphere, snow, new Vector3(0f, 1.02f, 0f), Vector3.one * 0.60f, Vector3.zero);
            // 목도리
            Part(root, PrimitiveType.Cylinder, scarf, new Vector3(0f, 0.76f, 0f), new Vector3(0.50f, 0.05f, 0.50f), Vector3.zero);
            Part(root, PrimitiveType.Cube, scarf, new Vector3(0.14f, 0.60f, 0.22f), new Vector3(0.10f, 0.26f, 0.05f), new Vector3(0f, 0f, -10f));
            // 눈
            Part(root, PrimitiveType.Sphere, coal, new Vector3(-0.10f, 1.08f, 0.26f), Vector3.one * 0.07f, Vector3.zero);
            Part(root, PrimitiveType.Sphere, coal, new Vector3(0.10f, 1.08f, 0.26f), Vector3.one * 0.07f, Vector3.zero);
            // 당근 코
            Part(root, PrimitiveType.Cube, carrot, new Vector3(0f, 1.00f, 0.38f), new Vector3(0.07f, 0.07f, 0.22f), Vector3.zero);
            // 단추
            Part(root, PrimitiveType.Sphere, coal, new Vector3(0f, 0.56f, 0.38f), Vector3.one * 0.07f, Vector3.zero);
            Part(root, PrimitiveType.Sphere, coal, new Vector3(0f, 0.42f, 0.42f), Vector3.one * 0.07f, Vector3.zero);
            Part(root, PrimitiveType.Sphere, coal, new Vector3(0f, 0.28f, 0.40f), Vector3.one * 0.07f, Vector3.zero);
            // 나뭇가지 팔
            Part(root, PrimitiveType.Cylinder, wood, new Vector3(-0.55f, 0.62f, 0f), new Vector3(0.05f, 0.28f, 0.05f), new Vector3(0f, 0f, 60f));
            Part(root, PrimitiveType.Cylinder, wood, new Vector3(0.55f, 0.62f, 0f), new Vector3(0.05f, 0.28f, 0.05f), new Vector3(0f, 0f, -60f));
            // 모자
            Part(root, PrimitiveType.Cylinder, coal, new Vector3(0f, 1.30f, 0f), new Vector3(0.46f, 0.02f, 0.46f), Vector3.zero);
            Part(root, PrimitiveType.Cylinder, coal, new Vector3(0f, 1.44f, 0f), new Vector3(0.30f, 0.13f, 0.30f), Vector3.zero);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, SnowmanPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void Part(GameObject parent, PrimitiveType type, Material material,
            Vector3 position, Vector3 scale, Vector3 euler)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = type.ToString();
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        private static Material SaveLitMaterial(string name, Color color)
        {
            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard");
            var material = existing != null ? existing : new Material(shader) { name = name };
            material.shader = shader;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // ───────────────────────── 테스트 씬 ─────────────────────────

        /// <summary>
        /// 날씨만 빠르게 확인하는 씬. 바닥 + 장애물 몇 개 + 'Player' 더미 + WeatherTestDriver(버튼/숫자키).
        /// 현재 열린 씬은 건드리지 않고(Additive) 만들어 저장한 뒤 닫는다.
        /// </summary>
        private static void CreateTestScene()
        {
            EnsureFolder("Assets/Scenes");
            // 저장 안 된 Untitled 씬 위에는 Additive 생성이 불가. 에디터면 작업 중인 씬을 날리지 않게 건너뛴다.
            bool untitled = string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
            if (untitled && !Application.isBatchMode)
            {
                Debug.Log("[Weather] 현재 씬이 저장되지 않아 테스트 씬 생성을 건너뜀. 씬 저장 후 Tools > UI NEW > Open Weather Test Scene");
                return;
            }
            NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            SceneManager.MoveGameObjectToScene(light.gameObject, scene);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(0f, 11f, -11f), Quaternion.Euler(45f, 0f, 0f));
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.gameObject.AddComponent<AudioListener>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);

            Material ground = SaveLitMaterial("TestGround", new Color(0.55f, 0.72f, 0.45f));
            Material block = SaveLitMaterial("TestBlock", new Color(0.80f, 0.70f, 0.55f));
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = new Vector3(3f, 1f, 3f);   // 30x30
            floor.GetComponent<MeshRenderer>().sharedMaterial = ground;
            SceneManager.MoveGameObjectToScene(floor, scene);

            Vector3[] blocks = { new(3f, 0.5f, 2f), new(-4f, 0.5f, -1f), new(1f, 0.5f, -4f), new(-2f, 1f, 4f) };
            foreach (Vector3 p in blocks)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Block";
                cube.transform.position = p;
                cube.transform.localScale = new Vector3(1f, p.y * 2f, 1f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = block;
                SceneManager.MoveGameObjectToScene(cube, scene);
            }

            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "PlayerDummy";
            dummy.tag = "Player";
            dummy.transform.position = new Vector3(0f, 1f, 0f);
            dummy.GetComponent<MeshRenderer>().sharedMaterial = SaveLitMaterial("TestDummy", new Color(0.95f, 0.85f, 0.30f));
            SceneManager.MoveGameObjectToScene(dummy, scene);

            var driver = new GameObject("WeatherTestDriver").AddComponent<WeatherTestDriver>();
            driver.Configure(dummy.transform);
            SceneManager.MoveGameObjectToScene(driver.gameObject, scene);

            EditorSceneManager.SaveScene(scene, TestScenePath);
            if (mode == NewSceneMode.Additive) EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[Weather] 날씨 테스트 씬 생성: {TestScenePath}  (Tools > UI NEW > Open Weather Test Scene)");
        }

        private static Mesh CreateGroundQuad()
        {
            var vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
            };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateGroundDisc(int segments)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                float c = Mathf.Cos(t), s = Mathf.Sin(t);
                vertices[i + 1] = new Vector3(c * 0.5f, 0f, s * 0.5f);
                uv[i + 1] = new Vector2(c * 0.5f + 0.5f, s * 0.5f + 0.5f);
            }
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;   // 위(+Y)를 향하도록
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material SaveMaterial(string name, Color color)
        {
            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default");
            var material = existing != null ? existing : new Material(shader) { name = name };
            material.shader = shader;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = $"{Folder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh CreateBox(float width, float height, float depth)
        {
            float x = width * 0.5f, y = height * 0.5f, z = depth * 0.5f;
            var vertices = new[]
            {
                new Vector3(-x,-y,-z), new Vector3(x,-y,-z), new Vector3(x,y,-z), new Vector3(-x,y,-z),
                new Vector3(-x,-y,z),  new Vector3(x,-y,z),  new Vector3(x,y,z),  new Vector3(-x,y,z)
            };
            int[] triangles =
            {
                0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4,
                2,3,7, 2,7,6, 1,2,6, 1,6,5, 3,0,4, 3,4,7
            };
            return BuildMesh(vertices, triangles);
        }

        private static Mesh CreateRadial(int arms, float outer, float inner)
        {
            var points = new Vector2[arms * 2];
            for (int i = 0; i < points.Length; i++)
            {
                float radius = i % 2 == 0 ? outer : inner;
                float angle = i * Mathf.PI / arms;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return CreateFlat(points);
        }

        private static Mesh CreateMapleLeaf()
        {
            // 멀리서도 표창이 아니라 낙엽으로 읽히는 둥근 피침형 실루엣.
            Mesh mesh = CreateFlat(new[]
            {
                new Vector2(0f, 1f), new Vector2(0.28f, 0.72f),
                new Vector2(0.48f, 0.25f), new Vector2(0.42f, -0.28f),
                new Vector2(0.18f, -0.72f), new Vector2(0f, -1f),
                new Vector2(-0.18f, -0.72f), new Vector2(-0.42f, -0.28f),
                new Vector2(-0.48f, 0.25f), new Vector2(-0.28f, 0.72f)
            });
            Vector3[] vertices = mesh.vertices;
            for (int i = 1; i < vertices.Length; i++)
                vertices[i].z = Mathf.Abs(vertices[i].x) * 0.16f;
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePetal()
        {
            var points = new Vector2[16];
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (float)points.Length * Mathf.PI * 2f;
                points[i] = new Vector2(Mathf.Cos(t) * 0.45f, Mathf.Sin(t) * 0.9f);
            }
            points[4] = new Vector2(0f, 1.15f);
            return CreateFlat(points);
        }

        private static Mesh CreateRibbon()
        {
            const int segments = 8;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float y = Mathf.Sin(t * Mathf.PI) * 0.38f;
                float halfWidth = Mathf.Lerp(0.08f, 0.015f, t);
                vertices[i * 2] = new Vector3(Mathf.Lerp(-1.4f, 1.4f, t), y - halfWidth, 0f);
                vertices[i * 2 + 1] = new Vector3(Mathf.Lerp(-1.4f, 1.4f, t), y + halfWidth, 0f);
                if (i == segments) continue;
                int v = i * 2, q = i * 6;
                triangles[q] = v; triangles[q + 1] = v + 1; triangles[q + 2] = v + 2;
                triangles[q + 3] = v + 1; triangles[q + 4] = v + 3; triangles[q + 5] = v + 2;
            }
            return BuildMesh(vertices, triangles);
        }

        private static Mesh CreateFlat(IReadOnlyList<Vector2> outline)
        {
            var vertices = new Vector3[outline.Count + 1];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < outline.Count; i++)
                vertices[i + 1] = new Vector3(outline[i].x, outline[i].y, 0f);
            var triangles = new int[outline.Count * 3];
            for (int i = 0; i < outline.Count; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % outline.Count + 1;
            }
            return BuildMesh(vertices, triangles);
        }

        private static Mesh BuildMesh(Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            string[] pieces = path.Split('/');
            string current = pieces[0];
            for (int i = 1; i < pieces.Length; i++)
            {
                string next = current + "/" + pieces[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, pieces[i]);
                current = next;
            }
        }
    }
}
#endif
