using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 블록 프리팹을 작은 썸네일 Texture2D로 1회 렌더해 캐시한다. 주문 HUD가 "무슨 블록인지" 보여줄 때 사용.
/// 멀리 떨어진 곳에 임시 인스턴스 + 임시 카메라/라이트로 1프레임 렌더 → ReadPixels → 정리.
/// </summary>
public static class BlockThumbnail
{
    private const int kLayer = 30;   // 정답 미리보기와 동일한 격리 레이어(메인 카메라에서 제외됨)
    private static readonly Dictionary<GameObject, Texture2D> s_Cache = new();

    public static Texture2D Get(GameObject prefab, int size = 96)
    {
        if (prefab == null) return null;
        if (s_Cache.TryGetValue(prefab, out var cached) && cached != null) return cached;

        // 멀리 떨어진 임시 위치에 인스턴스(메인 씬 간섭 0)
        var root = new GameObject("~ThumbRoot") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, -5000f, 0f);
        var inst = Object.Instantiate(prefab, root.transform);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);   // 살짝 돌려 입체감
        foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        SetLayer(inst, kLayer);

        // 렌더러 바운드(없으면 기본)
        var rends = inst.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(root.transform.position, Vector3.one);
        bool first = true;
        foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }

        float radius = Mathf.Max(0.4f, b.extents.magnitude);

        // 임시 카메라(해당 레이어만, 투명 배경)
        var camGO = new GameObject("~ThumbCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGO.AddComponent<Camera>();
        cam.cullingMask = 1 << kLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 32f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = radius * 12f + 50f;
        Vector3 dir = new Vector3(0.5f, 0.55f, -0.85f).normalized;       // 살짝 위·옆 쿼터뷰
        cam.transform.position = b.center + dir * (radius * 3.2f);
        cam.transform.LookAt(b.center);

        // 임시 라이트(없으면 검게 나옴)
        var lightGO = new GameObject("~ThumbLight") { hideFlags = HideFlags.HideAndDontSave };
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
        light.intensity = 1.2f;

        // 1회 렌더 → Texture2D
        var rt = RenderTexture.GetTemporary(size, size, 16, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);

        // 정리
        Object.Destroy(lightGO);
        Object.Destroy(camGO);
        Object.Destroy(root);

        s_Cache[prefab] = tex;
        return tex;
    }

    private static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }
}
