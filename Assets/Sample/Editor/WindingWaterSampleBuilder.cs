using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// "구불구불 물길 + 양쪽 둑"(청계천 느낌) 샘플 씬을 코드로 생성한다.
/// - 물: 사인 곡선을 따라가는 리본 메쉬 + Toon Water URP 머티리얼
/// - 둑: 곡선 가장자리를 따라 놓은 큐브들
/// - 바닥/하늘/라이트까지 세팅해서 바로 보이게.
/// 메뉴: Tools ▸ Sample ▸ Build Winding Water Scene
/// </summary>
public static class WindingWaterSampleBuilder
{
    const string kWaterMat  = "Assets/ThirdParty/Toon Water URP/Toon Water Material 1.mat";
    const string kSkybox    = "Assets/ThirdParty/FastSky/Materials/StylisedSky.mat";
    const string kScenePath = "Assets/Sample/Scenes/WindingWaterSample.unity";
    const string kMeshPath  = "Assets/Sample/Meshes/WaterRibbon.asset";

    // 곡선/채널 형태 파라미터
    const int   kSegs   = 48;
    const float kLength = 60f;   // 전체 길이(Z)
    const float kAmp    = 6f;    // 좌우 흔들림 폭(X)
    const float kWaves  = 3f;    // 굽이 개수
    const float kHalfW  = 1.8f;  // 물길 반폭
    const float kWaterY = 0.25f; // 수면 높이
    const float kBankH  = 1.0f;  // 둑 높이

    [MenuItem("Tools/Sample/Build Winding Water Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // 작업중 씬 보호

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 라이트(흰색, 낮 각도) ──
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.shadows = LightShadows.Soft;
        light.intensity = 1.1f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── 카메라(굽이가 보이게 비스듬히 위에서) ──
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        camGO.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.Skybox;
        camGO.transform.position = new Vector3(0f, 24f, -26f);
        camGO.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

        // ── 하늘/환경광 = FastSky ──
        var sky = AssetDatabase.LoadAssetAtPath<Material>(kSkybox);
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;

        // ── 바닥(물 바닥/광장) ──
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(9f, 1f, 9f); // Plane 10 → 90

        // ── 곡선 중심선 + 좌우 가장자리 계산 ──
        var center = new Vector3[kSegs + 1];
        for (int i = 0; i <= kSegs; i++)
        {
            float p = i / (float)kSegs;
            float z = p * kLength - kLength * 0.5f;
            float x = kAmp * Mathf.Sin(p * kWaves * Mathf.PI * 2f);
            center[i] = new Vector3(x, 0f, z);
        }
        var left  = new Vector3[kSegs + 1];
        var right = new Vector3[kSegs + 1];
        for (int i = 0; i <= kSegs; i++)
        {
            Vector3 t = (i < kSegs ? center[i + 1] - center[i] : center[i] - center[i - 1]);
            t.y = 0f; t.Normalize();
            Vector3 n = new Vector3(t.z, 0f, -t.x); // 수평 직교(좌우)
            left[i]  = center[i] - n * kHalfW;
            right[i] = center[i] + n * kHalfW;
        }

        // ── 물 리본 메쉬 ──
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        for (int i = 0; i <= kSegs; i++)
        {
            verts.Add(new Vector3(left[i].x,  kWaterY, left[i].z));
            verts.Add(new Vector3(right[i].x, kWaterY, right[i].z));
            float v = i / (float)kSegs * (kLength / (kHalfW * 2f));
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
        }
        for (int i = 0; i < kSegs; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
            tris.Add(a); tris.Add(c); tris.Add(b);   // 윗면(+Y)을 향하도록 와인딩
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        var mesh = new Mesh { name = "WaterRibbon" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Directory.CreateDirectory("Assets/Sample/Meshes");
        AssetDatabase.CreateAsset(mesh, kMeshPath); // 씬에 영속되도록 에셋으로 저장

        var water = new GameObject("WindingWater");
        water.AddComponent<MeshFilter>().sharedMesh = mesh;
        var wmr = water.AddComponent<MeshRenderer>();
        var waterMat = AssetDatabase.LoadAssetAtPath<Material>(kWaterMat);
        if (waterMat != null) wmr.sharedMaterial = waterMat;
        else Debug.LogWarning("[Sample] Toon Water 머티리얼 없음: " + kWaterMat);

        // ── 둑(곡선 가장자리를 따라 큐브) ──
        var banks = new GameObject("Banks").transform;
        for (int i = 0; i < kSegs; i++)
        {
            AddBankCube(banks, left[i],  left[i + 1]);
            AddBankCube(banks, right[i], right[i + 1]);
        }

        // ── 저장 + 열기 ──
        Directory.CreateDirectory("Assets/Sample/Scenes");
        EditorSceneManager.SaveScene(scene, kScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Sample] 구불구불 물길 씬 생성 완료 → " + kScenePath);
    }

    // 한 구간(p0→p1)에 둑 큐브 한 개 배치
    static void AddBankCube(Transform parent, Vector3 p0, Vector3 p1)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.transform.SetParent(parent, false);
        Vector3 dir = p1 - p0;
        float len = dir.magnitude;
        if (len < 1e-4f) { Object.DestroyImmediate(c); return; }
        dir /= len;
        c.transform.position = (p0 + p1) * 0.5f + Vector3.up * (kBankH * 0.5f);
        c.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // +Z를 진행방향에
        c.transform.localScale = new Vector3(0.4f, kBankH, len + 0.05f); // 얇고 길쭉한 연석
    }
}
