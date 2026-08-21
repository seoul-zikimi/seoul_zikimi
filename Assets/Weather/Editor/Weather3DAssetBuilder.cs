#if UNITY_EDITOR
using System.Collections.Generic;
using GridSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulZikimi.Weather.Editor
{
    internal static class Weather3DAssetBuilder
    {
        private const string Folder = "Assets/Resources/UI_NEW/Weather/3D";
        private const string PrefabPath = Folder + "/Weather3DVfxRig.prefab";
        private const string BuildVersion = "weather-3d-v4-rain-alignment";

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
            ParticleSystem rainFx = CreateSystem(root.transform, "Rain", rainMesh, rain, 260f, 2.0f,
                new Vector3(0f, -15f, 0f), 0.28f, false, 0.04f);
            ParticleSystem snowFx = CreateSystem(root.transform, "Snow", snowMesh, snow, 58f, 6f,
                new Vector3(0.25f, -1.6f, 0.15f), 0.29f, true, 0.32f);
            ParticleSystem windFx = CreateSystem(root.transform, "StrongWind", windMesh, wind, 28f, 2.0f,
                new Vector3(8f, -0.15f, 2f), 0.62f, true, 0.18f);
            ParticleSystem typhoonRain = CreateSystem(root.transform, "TyphoonRain", rainMesh, rain, 390f, 1.5f,
                new Vector3(8f, -18f, 2.5f), 0.34f, false, 0.08f);
            ParticleSystem typhoonWind = CreateSystem(root.transform, "TyphoonWind", windMesh, wind, 42f, 1.6f,
                new Vector3(12f, -0.7f, 3f), 0.72f, true, 0.38f);
            ParticleSystem autumn = CreateSystem(root.transform, "AutumnLeaves", leafMesh, leaf, 36f, 5.2f,
                new Vector3(1.7f, -1.25f, 0.65f), 0.31f, true, 0.40f);
            ParticleSystem cherry = CreateSystem(root.transform, "CherryBlossom", petalMesh, petal, 48f, 5.8f,
                new Vector3(1.2f, -0.95f, 0.45f), 0.25f, true, 0.36f);

            rig.Configure(rainFx, snowFx, windFx, typhoonRain, typhoonWind, autumn, cherry);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var prefabImporter = AssetImporter.GetAtPath(PrefabPath);
            if (prefabImporter != null)
            {
                prefabImporter.userData = BuildVersion;
                prefabImporter.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Weather] 3D 날씨 VFX 프리팹 생성 완료: {PrefabPath}");
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
