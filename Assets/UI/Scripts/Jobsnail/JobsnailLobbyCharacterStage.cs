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
    // Idle 바운즈에 딱 맞춘 기존 촬영 범위에서는 걷기/회전 중 팔다리가 열 경계를 넘어 잘렸다.
    private const float kHorizontalSafeViewScale = 1.22f;
    // 요청 사양: 현재 촬영 중심은 유지하고 위 30% + 아래 30%를 추가한다(세로 총 60% 확대).
    private const float kVerticalSafeViewScale = 1.60f;
    private const float kCellWorld = 2.0f * kHorizontalSafeViewScale;
    private const float kWorldHeight = 3.0f * kHorizontalSafeViewScale * kVerticalSafeViewScale;
    private const float kTargetHeight = 2.45f;  // 슬롯 세로 공간을 충분히 채우도록 스케일 정규화
    private const int kColumnPixels = 256;

    private static readonly Vector3 kStageOrigin = new(6000f, 6000f, 6000f);

    private Camera m_Camera;
    private RenderTexture m_RT;
    // GPU 내부 복사(RT→Texture2D) 지원 여부 — 지원하면 ReadPixels의 GPU→CPU 동기 스톨 없이
    // 열 복사가 전부 GPU 안에서 끝난다. 미지원 기기만 기존 ReadPixels 경로.
    private bool m_CanCopy;
    private readonly Transform[] m_BoothAnchors = new Transform[kBooths];
    private readonly GameObject[] m_Models = new GameObject[kBooths];
    private readonly Texture2D[] m_CaptureTextures = new Texture2D[kBooths];
    private readonly string[] m_ShownKey = new string[kBooths];   // 현재 부스에 세워진 char|outfit (변경 감지)
    private bool m_Built;
    private float m_NextCaptureAt;

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
        m_CanCopy = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.RTToTexture) != 0;

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

    /// <summary>
    /// 현재 부스 한 칸을 투명 배경 Sprite로 캡처한다.
    /// UI 쪽에는 이미 배치된 Image만 사용하고, 런타임에 별도 UI 오브젝트를 만들지 않는다.
    /// 반환된 Sprite와 Texture2D의 수명은 호출자가 관리한다. 캡처 텍스처는 프리뷰 모션을
    /// 보여주기 위해 스테이지가 초당 10회 갱신한다.
    /// </summary>
    public Sprite CaptureBoothSprite(int index)
    {
        if (!m_Built || m_Camera == null || m_RT == null || index < 0 || index >= kBooths)
            return null;

        m_Camera.Render();
        // RT와 같은 graphicsFormat으로 만들어야 CopyTexture가 유효하다(포맷 불일치 = 플랫폼별 실패).
        var texture = m_CanCopy
            ? new Texture2D(kColumnPixels, m_RT.height, m_RT.graphicsFormat,
                            UnityEngine.Experimental.Rendering.TextureCreationFlags.None)
            : new Texture2D(kColumnPixels, m_RT.height, TextureFormat.RGBA32, false);
        texture.name = $"LobbyCharacter{index}Texture";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        CopyColumnInto(index, texture);
        m_CaptureTextures[index] = texture;

        // 캡처 직후의 알파 실루엣으로 Tight 메시를 만들면 이후 걷기/회전 프레임이
        // 최초 실루엣 밖으로 나갈 때 텍스처 픽셀이 있어도 메시 경계에서 잘린다.
        // 동적으로 갱신되는 캡처이므로 항상 텍스처 전체 사각형을 그린다.
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = $"LobbyCharacter{index}Sprite";
        return sprite;
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
            // Destroy는 프레임 끝까지 지연된다. 새 모델을 즉시 캡처할 때 둘이 겹쳐 찍히지 않도록
            // 먼저 렌더 대상에서 제외한다.
            m_Models[index].SetActive(false);
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

        // 캐릭터별 TargetCharacter가 지정된 코디까지 동일하게 적용한다.
        CodiOutfit.Apply(model, outfitId, charId);

        FrameModel(model, m_BoothAnchors[index]);
        SetLayerRecursive(model.transform, kStageLayer);
        model.AddComponent<JobsnailPreviewMotion>().Initialize(anim);

        m_Models[index] = model;
    }

    private void Update()
    {
        if (!m_Built || m_Camera == null || m_RT == null || Time.unscaledTime < m_NextCaptureAt)
            return;

        bool hasCapture = false;
        for (int i = 0; i < m_CaptureTextures.Length; i++)
            hasCapture |= m_CaptureTextures[i] != null;
        if (!hasCapture)
            return;

        m_NextCaptureAt = Time.unscaledTime + 0.1f;
        m_Camera.Render();
        for (int i = 0; i < m_CaptureTextures.Length; i++)
        {
            Texture2D texture = m_CaptureTextures[i];
            if (texture == null)
                continue;
            CopyColumnInto(i, texture);
        }
    }

    // RT의 부스 열 하나를 캡처 텍스처로 복사. CopyTexture는 GPU 안에서 끝나 스톨이 없고,
    // 스프라이트는 같은 Texture2D를 참조하므로 화면에는 그대로 반영된다.
    private void CopyColumnInto(int index, Texture2D texture)
    {
        if (m_CanCopy && texture.graphicsFormat == m_RT.graphicsFormat)
        {
            Graphics.CopyTexture(m_RT, 0, 0, index * kColumnPixels, 0, kColumnPixels, m_RT.height,
                                 texture, 0, 0, 0, 0);
            return;
        }
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = m_RT;
        texture.ReadPixels(new Rect(index * kColumnPixels, 0, kColumnPixels, m_RT.height), 0, 0, false);
        texture.Apply(false, false);
        RenderTexture.active = previous;
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

        for (int i = 0; i < m_CaptureTextures.Length; i++)
            m_CaptureTextures[i] = null; // 소유권은 CaptureBoothSprite 호출자에게 있다.

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

/// <summary>UI 캐릭터 프리뷰 전용의 잔잔한 반복 모션. 게임 플레이 애니메이터에는 관여하지 않는다.</summary>
internal sealed class JobsnailPreviewMotion : MonoBehaviour
{
    // Idle → 걷기(가속/정속/감속) → 완전 정지 → 우측 → 좌측 → 정면.
    // 정지 구간을 별도 단계로 둬서 걷는 포즈 도중 회전이 시작되지 않게 한다.
    private static readonly float[] kDurations = { 1.5f, 3.0f, 0.7f, 0.9f, 1.4f, 0.9f };
    private const float kWalkAcceleration = 0.65f;
    private const float kWalkDeceleration = 0.9f;
    private const float kMinimumWalkPlayback = 0f;
    private Animator m_Animator;
    private Quaternion m_Forward;
    private int m_Phase;
    private float m_PhaseStartedAt;

    public void Initialize(Animator animator)
    {
        m_Animator = animator;
        m_Forward = transform.localRotation;
        m_PhaseStartedAt = Time.unscaledTime;
        Play("Idle", 0f);
    }

    private void Update()
    {
        float elapsed = Time.unscaledTime - m_PhaseStartedAt;
        if (elapsed >= kDurations[m_Phase])
        {
            m_Phase = (m_Phase + 1) % kDurations.Length;
            m_PhaseStartedAt = Time.unscaledTime;
            EnterPhase();
            elapsed = 0f;
        }

        UpdateWalkPlayback(elapsed);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / kDurations[m_Phase]));
        float yaw = m_Phase switch
        {
            3 => Mathf.Lerp(0f, -35f, t),
            4 => Mathf.Lerp(-35f, 35f, t),
            5 => Mathf.Lerp(35f, 0f, t),
            _ => 0f
        };
        transform.localRotation = m_Forward * Quaternion.Euler(0f, yaw, 0f);
    }

    private void EnterPhase()
    {
        if (m_Animator == null)
            return;

        if (m_Phase == 1)
        {
            m_Animator.speed = kMinimumWalkPlayback;
            Play("Walk", 0.28f);
        }
        else if (m_Phase == 2)
        {
            // 감속이 끝난 뒤 Idle로 충분히 블렌드하고, 이 정지 단계가 끝난 후에만 회전한다.
            m_Animator.speed = 1f;
            Play("Idle", 0.38f);
        }
        else
        {
            m_Animator.speed = 1f;
            if (m_Phase == 0)
                Play("Idle", 0.2f);
        }
    }

    private void UpdateWalkPlayback(float elapsed)
    {
        if (m_Animator == null || m_Phase != 1)
            return;

        float remaining = kDurations[1] - elapsed;
        float playback;
        if (elapsed < kWalkAcceleration)
            playback = Mathf.SmoothStep(kMinimumWalkPlayback, 1f, elapsed / kWalkAcceleration);
        else if (remaining < kWalkDeceleration)
            playback = Mathf.SmoothStep(kMinimumWalkPlayback, 1f, remaining / kWalkDeceleration);
        else
            playback = 1f;

        m_Animator.speed = playback;
    }

    private void Play(string state, float fadeSeconds)
    {
        if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
            return;
        m_Animator.CrossFadeInFixedTime(state, fadeSeconds, 0);
    }
}
