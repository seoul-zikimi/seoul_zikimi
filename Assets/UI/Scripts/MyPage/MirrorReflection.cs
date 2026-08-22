using UnityEngine;

/// <summary>
/// 옷장 거울 — 물리 반사가 아니라 '항상 달팽이 정면'을 비추는 전용 카메라 방식(옷장 용도에 최적).
/// 전용 카메라가 캐릭터 정면(캐릭터가 보는 방향 앞)에서 캐릭터를 촬영 → 타원 거울면에 좌우반전으로 표시.
/// 조정: 거울(프레임) 자식 "SurfaceAnchor" = 거울면 위치·크기(스케일 X=폭, Y=높이). 방향은 자동.
/// 캐릭터가 어딜 보든 그 정면이 비침 — CharacterSpot 회전으로 달팽이 방향을 정하면 됨.
/// </summary>
public class MirrorReflection : MonoBehaviour
{
    [SerializeField] private float m_Fov = 40f;
    [SerializeField] private float m_Zoom = 1f;   // 1=전신 여유, 키우면 확대

    private const int kMirrorLayer = 30;   // TagManager "MirrorOnly"

    private Camera m_Cam;
    private RenderTexture m_RT;
    private Transform m_Anchor;
    private Transform m_SurfaceTr;
    private Transform m_Char;
    private float m_CharHeight = 1f;
    private float m_FeetY;
    private Renderer[] m_CharRenderers;

    private void Start()
    {
        m_Anchor = transform.Find("SurfaceAnchor");
        if (m_Anchor == null)
        {
            Debug.LogWarning("[Mirror] 자식 'SurfaceAnchor'가 없어요 — 유리 위치에 만들어 주세요.");
            enabled = false;
            return;
        }

        m_RT = new RenderTexture(512, 768, 16);

        // 타원 거울면(씬 루트 독립 — 부모 스케일 오염 방지)
        var go = new GameObject("~MirrorSurface", typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = BuildEllipse(48);
        m_SurfaceTr = go.transform;

        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        mat.SetTexture("_BaseMap", m_RT);
        mat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));   // 좌우반전 = 거울상
        mat.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
        mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 전용 촬영 카메라
        var camGo = new GameObject("~MirrorCam");
        camGo.transform.SetParent(transform, false);
        m_Cam = camGo.AddComponent<Camera>();
        m_Cam.targetTexture = m_RT;
        m_Cam.fieldOfView = m_Fov;
        m_Cam.nearClipPlane = 0.05f;
        m_Cam.clearFlags = CameraClearFlags.SolidColor;
        m_Cam.backgroundColor = new Color(0.87f, 0.90f, 0.95f, 1f);   // 옅은 거울톤
        m_Cam.cullingMask = 1 << kMirrorLayer;   // 달팽이만 찍는다(가구·벽에 안 가림)
    }

    private void OnDestroy()
    {
        if (m_RT != null) { m_RT.Release(); Destroy(m_RT); }
        if (m_SurfaceTr != null) Destroy(m_SurfaceTr.gameObject);
    }

    private void LateUpdate()
    {
        if (m_Cam == null || m_Anchor == null || m_SurfaceTr == null) return;
        if (m_Char == null)
        {
            var ch = GameObject.Find("~PreviewCharacter");
            if (ch == null) return;
            m_Char = ch.transform;
        }

        // 옷 갈아입기(조각 생성/파괴)로 렌더러가 바뀌므로 매 프레임 다시 수집 + 레이어 재적용
        SetLayerRecursively(m_Char, kMirrorLayer);   // 거울 카메라 전용 레이어(메인 카메라도 이 레이어를 그림)
        m_CharRenderers = m_Char.GetComponentsInChildren<Renderer>();

        // 매 프레임 실제 키·발 위치 측정(스폰 직후 낙하 → 착지로 위치가 변해도 따라감)
        bool got = false;
        var b = new Bounds();
        foreach (var r in m_CharRenderers)
        {
            if (r == null) continue;
            if (!got) { b = r.bounds; got = true; }
            else b.Encapsulate(r.bounds);
        }
        if (got)
        {
            m_CharHeight = Mathf.Max(0.3f, b.size.y);
            m_FeetY = b.min.y;
        }

        // 거울면 = 앵커 그대로(위치·회전·크기 WYSIWYG) — 플레이 중 앵커를 움직여 유리에 맞추면 됨
        // 단, 프레임에 구워진 유리 메시에 파묻히지 않게 보는 쪽으로 1.5cm 띄운다
        Vector3 outDir = m_Anchor.forward;
        var mainCam = Camera.main;
        if (mainCam != null && Vector3.Dot(mainCam.transform.position - m_Anchor.position, outDir) < 0f)
            outDir = -outDir;
        m_SurfaceTr.SetPositionAndRotation(m_Anchor.position + outDir * 0.015f, m_Anchor.rotation);
        var s = m_Anchor.lossyScale;
        m_SurfaceTr.localScale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), 1f);

#if UNITY_EDITOR
        // 플레이 중 조정값 백업 → 정지 후 Tools ▸ MyPage ▸ Apply Mirror Tuning 으로 씬에 반영
        PlayerPrefs.SetString("MyPage_MirrorTuning", JsonUtility.ToJson(new MirrorTuning
        {
            pos = m_Anchor.position,
            rot = m_Anchor.rotation.eulerAngles,
            scale = m_Anchor.localScale
        }));
#endif

        // 촬영 카메라: 무조건 캐릭터 '정면'(캐릭터 forward 앞) — 키 기준 자동 전신 프레이밍
        float h = m_CharHeight;
        float dist = 2.3f * h / Mathf.Max(0.2f, m_Zoom);          // FOV 40 기준 전신+여유
        Vector3 feet = new Vector3(m_Char.position.x, m_FeetY, m_Char.position.z);
        Vector3 focus = feet + Vector3.up * (h * 0.68f);   // 조준점을 올리면 거울 속 상이 내려감
        m_Cam.transform.position = feet + m_Char.forward * dist + Vector3.up * (h * 0.15f);   // 발목 높이서 살짝 올려다봄 → 배에 다리 안 가림
        m_Cam.transform.LookAt(focus);
        m_Cam.fieldOfView = m_Fov;
    }

    private static void SetLayerRecursively(Transform t, int layer)
    {
        if (t.name == "~Trail") return;   // 트레일은 거울 레이어 제외 — 거울에 안 비침
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }

    private static Mesh BuildEllipse(int segments)
    {
        var mesh = new Mesh { name = "~MirrorEllipse" };
        var verts = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * 0.5f, y = Mathf.Sin(a) * 0.5f;
            verts[i + 1] = new Vector3(x, y, 0f);
            uvs[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
        }
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % segments;
            tris[i * 3 + 2] = 1 + i;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    [System.Serializable]
    public struct MirrorTuning { public Vector3 pos; public Vector3 rot; public Vector3 scale; }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var anchor = transform.Find("SurfaceAnchor");
        if (anchor == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(anchor.position, anchor.rotation, anchor.lossyScale);
        for (int i = 0; i < 32; i++)
        {
            float a0 = i / 32f * Mathf.PI * 2f, a1 = (i + 1) / 32f * Mathf.PI * 2f;
            Gizmos.DrawLine(new Vector3(Mathf.Cos(a0) * 0.5f, Mathf.Sin(a0) * 0.5f, 0f),
                            new Vector3(Mathf.Cos(a1) * 0.5f, Mathf.Sin(a1) * 0.5f, 0f));
        }
    }
#endif
}
