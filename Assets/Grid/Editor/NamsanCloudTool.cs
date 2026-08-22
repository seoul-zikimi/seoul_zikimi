using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 남산 구름 깔기 — 산 아래(평지와 도시 사이 높이)에 반투명 구름 띠를 둘러
    /// 빈약한 하부 배경을 채운다. 기획자가 꾸민 MapBg_NamsanTower.prefab을 '재생성 없이'
    /// "~Clouds" 그룹만 추가/갱신한다(재실행 시 기존 구름 교체 — 멱등).
    /// 개별 구름이 마음에 안 들면 프리팹에서 ~Clouds 아래 것들을 지우거나 옮겨도 된다.
    /// </summary>
    public static class NamsanCloudTool
    {
        private const string kBgPath = "Assets/Map/Prefabs/MapBg_NamsanTower.prefab";
        private const string kMatPath = "Assets/Map/Materials/Mat_NamsanCloud.mat";

        [MenuItem("Tools/Map/★ 남산 구름 깔기")]
        public static void Generate()
        {
            var mat = EnsureCloudMaterial();
            var root = PrefabUtility.LoadPrefabContents(kBgPath);
            if (root == null) { Debug.LogError($"[남산구름] 배경 프리팹이 없음: {kBgPath}"); return; }

            try
            {
                var old = root.transform.Find("~Clouds");
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var group = new GameObject("~Clouds");
                group.transform.SetParent(root.transform, false);

                // 산 둘레 링 + 남쪽(도시 방향)에 조금 더 — 평지(-2.5)와 도시(-14.5) 사이 높이
                var rng = new System.Random(42);
                int clusters = 14;
                for (int i = 0; i < clusters; i++)
                {
                    float ang = (i / (float)clusters) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                    float radius = 30f + (float)rng.NextDouble() * 16f;
                    var center = new Vector3(Mathf.Cos(ang) * radius, -7.5f - (float)rng.NextDouble() * 4f,
                                             Mathf.Sin(ang) * radius - 8f);
                    MakeCluster(group.transform, mat, rng, $"Cloud{i + 1}", center);
                }
                // 남쪽 스카이라인 앞에 낮은 구름 몇 덩이(도시가 구름 사이로 보이게)
                MakeCluster(group.transform, mat, rng, "CloudS1", new Vector3(-18f, -10f, -48f));
                MakeCluster(group.transform, mat, rng, "CloudS2", new Vector3(12f, -11f, -52f));
                MakeCluster(group.transform, mat, rng, "CloudS3", new Vector3(38f, -9.5f, -46f));

                PrefabUtility.SaveAsPrefabAsset(root, kBgPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[남산구름] 완료 ✔ 산 아래 구름 띠 17덩이 — 재실행하면 새로 깔림, 개별 수정은 ~Clouds 아래에서.");
        }

        // 찌부 스피어 3~5개 뭉친 구름 한 덩이
        private static void MakeCluster(Transform parent, Material mat, System.Random rng, string name, Vector3 center)
        {
            var cluster = new GameObject(name);
            cluster.transform.SetParent(parent, false);
            cluster.transform.localPosition = center;

            int puffs = 3 + rng.Next(3);
            for (int i = 0; i < puffs; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = $"puff{i + 1}";
                s.transform.SetParent(cluster.transform, false);
                s.transform.localPosition = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 7f,
                    ((float)rng.NextDouble() - 0.5f) * 1.2f,
                    ((float)rng.NextDouble() - 0.5f) * 4f);
                float w = 4f + (float)rng.NextDouble() * 5f;
                s.transform.localScale = new Vector3(w, 1.4f + (float)rng.NextDouble() * 1.2f, w * (0.6f + (float)rng.NextDouble() * 0.3f));
                var col = s.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                s.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // 반투명 흰 구름 머티리얼(에셋으로 저장 — 프리팹이 참조 가능해야 하므로 런타임 머티리얼 금지)
        private static Material EnsureCloudMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kMatPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(sh);
                Directory.CreateDirectory(Path.GetDirectoryName(kMatPath));
                AssetDatabase.CreateAsset(mat, kMatPath);
            }
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var c = new Color(1f, 1f, 1f, 0.62f);
            mat.SetColor("_BaseColor", c);
            mat.SetColor("_Color", c);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
