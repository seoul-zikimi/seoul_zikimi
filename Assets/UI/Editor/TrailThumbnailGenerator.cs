using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 트레일 썸네일 생성 — Jobsnail ▸ UI ▸ 트레일 썸네일 생성.
/// 트레일 프리팹의 실제 TrailRenderer(머티리얼·굵기·색 그라데이션)로 S자 리본 메시를 만들어
/// 미리보기 카메라로 렌더 → 어두운 라운드 카드 위 합성 → Thumb_trail_*.png 저장(실물 모양).
/// 무지개(VFX Graph)는 무지개 그라데이션 리본으로 대체 렌더.
/// </summary>
public static class TrailThumbnailGenerator
{
    private const int kSize = 256;
    private const string kOutDir = "Assets/Resources/UI_pngs/MyPage";

    [MenuItem("Jobsnail/UI/트레일 썸네일 생성")]
    public static void Generate()
    {
        foreach (var e in TrailCatalog.All)
        {
            var tex = RenderEntry(e);
            if (tex == null) { Debug.LogWarning($"[TrailThumb] 렌더 실패: {e.Id}"); continue; }
            File.WriteAllBytes($"{kOutDir}/Thumb_{e.Id}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        AssetDatabase.Refresh();
        Debug.Log("[TrailThumb] 트레일 썸네일 생성 완료");
    }

    private static Texture2D RenderEntry(TrailCatalog.Entry e)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            pru.camera.orthographic = true;
            pru.camera.orthographicSize = 1f;
            pru.camera.transform.position = new Vector3(0, 0, -5f);
            pru.camera.transform.rotation = Quaternion.identity;
            pru.camera.backgroundColor = Color.black;
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.camera.nearClipPlane = 0.1f;
            pru.camera.farClipPlane = 20f;

            pru.BeginStaticPreview(new Rect(0, 0, kSize, kSize));

            var prefab = Resources.Load<GameObject>($"Trails/{e.PrefabName}");
            var trails = prefab != null ? prefab.GetComponentsInChildren<TrailRenderer>(true) : null;

            if (trails != null && trails.Length > 0)
            {
                float maxW = 0.01f;
                foreach (var tr in trails) maxW = Mathf.Max(maxW, tr.widthMultiplier);
                // 넓은 것부터(뒤 글로우) → 좁은 것(코어) 순서로 겹쳐 그림
                System.Array.Sort(trails, (a, b) => b.widthMultiplier.CompareTo(a.widthMultiplier));
                foreach (var tr in trails)
                {
                    var mesh = BuildRibbon(tr.widthMultiplier / maxW * 0.46f, tr.widthCurve, tr.colorGradient);
                    var mat = tr.sharedMaterial;
                    if (mat != null) pru.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
                }
            }
            else
            {
                // VFX Graph 등 TrailRenderer 없는 프리팹(무지개) — 무지개 그라데이션 리본
                var g = new Gradient();
                g.SetKeys(new[]
                {
                    new GradientColorKey(new Color(1f, 0.35f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.75f, 0.25f), 0.2f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.35f), 0.4f),
                    new GradientColorKey(new Color(0.45f, 0.9f, 0.45f), 0.6f),
                    new GradientColorKey(new Color(0.35f, 0.6f, 1f), 0.8f),
                    new GradientColorKey(new Color(0.7f, 0.45f, 1f), 1f),
                }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.15f, 1f) });
                var mesh = BuildRibbon(0.46f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f), g);
                var sh = Shader.Find("Sprites/Default");
                if (sh != null) pru.DrawMesh(mesh, Matrix4x4.identity, new Material(sh), 0);
            }

            pru.camera.Render();
            var raw = pru.EndStaticPreview();

            // 어두운 라운드 카드 위 합성(글로우가 밝은 카드에서도 안 죽게)
            return Compose(raw);
        }
        finally { pru.Cleanup(); }
    }

    // S자 곡선 리본 — 머리(진하고 굵음)가 좌하, 꼬리가 우상
    private static Mesh BuildRibbon(float baseWidth, AnimationCurve widthCurve, Gradient colors)
    {
        const int n = 48;
        var verts = new Vector3[n * 2];
        var cols = new Color[n * 2];
        var uvs = new Vector2[n * 2];
        var tris = new int[(n - 1) * 6];

        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            // 곡선: 좌하 → 우상 S자
            float x = Mathf.Lerp(-0.62f, 0.62f, t);
            float y = Mathf.Lerp(-0.5f, 0.55f, t) + Mathf.Sin(t * Mathf.PI * 2f) * 0.18f;
            // 접선 → 법선
            float dx = 1.24f / n;
            float dy = (1.05f / n) + Mathf.Cos(t * Mathf.PI * 2f) * Mathf.PI * 2f * 0.18f / n;
            var nrm = new Vector2(-dy, dx).normalized;

            float w = baseWidth * Mathf.Max(0.02f, widthCurve != null ? widthCurve.Evaluate(t) : 1f) * 0.5f;
            verts[i * 2] = new Vector3(x + nrm.x * w, y + nrm.y * w, 0f);
            verts[i * 2 + 1] = new Vector3(x - nrm.x * w, y - nrm.y * w, 0f);
            var c = colors != null ? colors.Evaluate(t) : Color.white;
            cols[i * 2] = cols[i * 2 + 1] = c;
            uvs[i * 2] = new Vector2(t, 1f);
            uvs[i * 2 + 1] = new Vector2(t, 0f);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int v = i * 2, k = i * 6;
            tris[k] = v; tris[k + 1] = v + 2; tris[k + 2] = v + 1;
            tris[k + 3] = v + 1; tris[k + 4] = v + 2; tris[k + 5] = v + 3;
        }
        return new Mesh { vertices = verts, colors = cols, uv = uvs, triangles = tris };
    }

    // 검정 배경 렌더 결과(가산 성분)를 어두운 라운드 카드에 합성
    private static Texture2D Compose(Texture2D raw)
    {
        var outTex = new Texture2D(kSize, kSize, TextureFormat.RGBA32, false);
        var card = new Color32(0x3A, 0x33, 0x52, 255);   // 진보라 카드
        const int r = 36;
        var px = raw.GetPixels32();
        var dst = new Color32[kSize * kSize];
        for (int y = 0; y < kSize; y++)
            for (int x = 0; x < kSize; x++)
            {
                // 라운드 마스크
                int cx = Mathf.Clamp(x, r, kSize - 1 - r);
                int cy = Mathf.Clamp(y, r, kSize - 1 - r);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                byte a = (byte)(d <= r ? 255 : 0);

                var s = px[y * kSize + x];
                dst[y * kSize + x] = new Color32(
                    (byte)Mathf.Min(255, card.r + s.r),
                    (byte)Mathf.Min(255, card.g + s.g),
                    (byte)Mathf.Min(255, card.b + s.b),
                    a);
            }
        outTex.SetPixels32(dst);
        outTex.Apply();
        return outTex;
    }
}
