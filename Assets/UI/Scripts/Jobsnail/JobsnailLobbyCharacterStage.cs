using UnityEngine;
using UnityEngine.UI;

/// <summary>대기방 슬롯에 실시간 3D 캐릭터(아웃핏 포함)를 보여주기 위한 렌더 스테이지.
///
/// 화면에서 멀리 떨어진 곳(layer 31)에 4개의 부스를 한 줄로 세우고, 단일 직교 카메라로
/// 넓은 RenderTexture에 담는다. 각 슬롯의 RawImage는 이 RT의 1/4 열(uvRect)만 보여준다.
/// 캔버스가 스크린 오버레이라 3D를 직접 못 넣으므로 이 방식을 쓴다.</summary>
public sealed class JobsnailLobbyCharacterStage : MonoBehaviour
{
    private const int kStageLayer = 31;         // 스테이지 전용 레이어(빈 레이어)
    private const int kBooths = 4;              // RT는 항상 4열(0.25씩)
    private const float kCellWorld = 2.0f;      // 부스 1칸이 차지하는 월드 폭
    private const float kWorldHeight = 3.0f;    // 카메라 세로 시야(월드)
    private const float kTargetHeight = 1.5f;   // 캐릭터를 이 높이에 맞춰 스케일 정규화
    private const int kColumnPixels = 256;

    private static readonly Vector3 kStageOrigin = new(6000f, 6000f, 6000f);

    private Camera m_Camera;
    private RenderTexture m_RT;
    private readonly Transform[] m_BoothAnchors = new Transform[kBooths];
    private readonly GameObject[] m_Models = new GameObject[kBooths];
    private readonly string[] m_ShownKey = new string[kBooths];   // 현재 부스에 세워진 char|outfit (변경 감지)
    private bool m_Built;

    public Texture Texture => m_RT;

    public static Rect UvRectFor(int index)
        => new(Mathf.Clamp(index, 0, kBooths - 1) / (float)kBooths, 0f, 1f / kBooths, 1f);

    public void EnsureBuilt()
    {
        if (m_Built)
            return;
        m_Built = true;

        transform.position = kStageOrigin;

        m_RT = new RenderTexture(kColumnPixels * kBooths, Mathf.RoundToInt(kColumnPixels * kBooths * kWorldHeight / (kCellWorld * kBooths)), 16, RenderTextureFormat.ARGB32)
        {
            name = "LobbyCharacterRT",
            antiAliasing = 1
        };
        m_RT.Create();

        // 카메라 — 4부스를 한 줄로 담는 직교 카메라(투명 배경).
        var camGo = new GameObject("StageCamera");
        camGo.transform.SetParent(transform, false);
        m_Camera = camGo.AddComponent<Camera>();
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = kWorldHeight * 0.5f;
        m_Camera.cullingMask = 1 << kStageLayer;   // 부스만 렌더(로비 씬 제외)
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        m_Camera.nearClipPlane = 0.05f;
        m_Camera.farClipPlane = 50f;
        m_Camera.targetTexture = m_RT;
        m_Camera.allowHDR = false;
        m_Camera.allowMSAA = false;

        float totalWidth = kCellWorld * kBooths;                 // 8
        var camLocal = new Vector3(totalWidth * 0.5f, kTargetHeight * 0.5f, -8f);
        m_Camera.transform.localPosition = camLocal;
        m_Camera.transform.localRotation = Quaternion.identity;  // +Z 바라봄

        // 전용 조명 — layer 31만 비춘다(로비 씬 조명에 영향 없음).
        var lightGo = new GameObject("StageLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localRotation = Quaternion.Euler(35f, 160f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.cullingMask = 1 << kStageLayer;

        // 부스 앵커(x = (i+0.5)*cell, 바닥 y=0).
        for (int i = 0; i < kBooths; i++)
        {
            var anchor = new GameObject($"Booth{i}").transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = new Vector3((i + 0.5f) * kCellWorld, 0f, 0f);
            anchor.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 카메라(-Z쪽)를 바라보게
            m_BoothAnchors[i] = anchor;
        }

        gameObject.layer = kStageLayer;
    }

    public void SetActiveRendering(bool on)
    {
        if (m_Camera != null)
            m_Camera.enabled = on;
    }

    /// <summary>부스에 캐릭터를 세운다(변경 시에만 재생성). occupied=false면 모델 제거.</summary>
    public void SetBooth(int index, bool occupied, string charId, string outfitId)
    {
        if (!m_Built || index < 0 || index >= kBooths)
            return;

        charId ??= "";
        outfitId ??= "";
        string key = occupied ? charId + "|" + outfitId : null;

        if (m_ShownKey[index] == key)
            return;
        m_ShownKey[index] = key;

        if (m_Models[index] != null)
        {
            Destroy(m_Models[index]);
            m_Models[index] = null;
        }

        if (!occupied)
            return;

        var prefab = string.IsNullOrEmpty(charId)
            ? Resources.Load<GameObject>("Characters/_snail_preview")
            : CharacterCatalog.LoadPrefab(charId);
        if (prefab == null)
            prefab = Resources.Load<GameObject>("Characters/_snail_preview");
        if (prefab == null)
            return;

        var model = Instantiate(prefab, m_BoothAnchors[index]);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        var anim = model.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            if (anim.runtimeAnimatorController != null)
                anim.Play("Idle", 0, 0f);
        }

        // 기본(달팽이)만 아웃핏 적용 — 아웃핏은 달팽이 본 기준.
        if (string.IsNullOrEmpty(charId))
            CodiOutfit.Apply(model, outfitId);

        FrameModel(model, m_BoothAnchors[index]);
        SetLayerRecursive(model.transform, kStageLayer);

        m_Models[index] = model;
    }

    /// <summary>렌더러 바운즈로 캐릭터를 목표 높이에 맞춰 스케일하고, 발을 바닥(y=0)·앵커 x중심에 정렬.</summary>
    private static void FrameModel(GameObject model, Transform anchor)
    {
        if (!TryGetBounds(model, out var b))
            return;

        float h = Mathf.Max(0.0001f, b.size.y);
        float scale = kTargetHeight / h;
        model.transform.localScale *= scale;

        if (!TryGetBounds(model, out b))
            return;

        // 앵커 로컬 기준으로 발바닥 y=0, 좌우 중심을 앵커에 맞춘다.
        Vector3 centerLocal = anchor.InverseTransformPoint(b.center);
        Vector3 minLocal = anchor.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
        var p = model.transform.localPosition;
        p.x -= centerLocal.x;
        p.z -= centerLocal.z;
        p.y -= minLocal.y;
        model.transform.localPosition = p;
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer)
                continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < m_Models.Length; i++)
            if (m_Models[i] != null) Destroy(m_Models[i]);

        if (m_Camera != null)
            m_Camera.targetTexture = null;

        if (m_RT != null)
        {
            m_RT.Release();
            Destroy(m_RT);
            m_RT = null;
        }
    }
}
