using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 기획자용 "구불구불 물길" 생성기.
/// GameObject에 붙이고 인스펙터 슬라이더(굽이/너비/둑 등)를 움직이면 씬에서 물 리본 + 양쪽 둑이
/// 실시간으로 다시 그려진다. 코드 수정 불필요.
/// 생성물은 "_Generated" 자식으로 들어가고 씬엔 저장 안 됨(켤 때마다 재생성) → 오브젝트 이 컴포넌트만 옮기면 됨.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class WindingWaterAuthoring : MonoBehaviour
{
    [Header("곡선 모양")]
    [Tooltip("좌우로 굽이치는 폭. 클수록 더 꼬불꼬불.")]
    [Range(0f, 15f)]  public float amplitude = 6f;
    [Tooltip("굽이(S자) 개수.")]
    [Range(0.5f, 8f)] public float waves = 3f;
    [Tooltip("물길 전체 길이.")]
    [Range(10f, 120f)] public float length = 60f;

    [Header("물 / 둑 크기")]
    [Tooltip("물길 너비(반폭). 클수록 물이 넓어짐.")]
    [Range(0.5f, 8f)] public float halfWidth = 1.8f;
    [Tooltip("수면 높이(둑보다 낮게).")]
    [Range(0f, 3f)]   public float waterHeight = 0.25f;
    [Tooltip("양쪽 둑(연석) 높이. 0이면 둑 없음.")]
    [Range(0f, 3f)]   public float bankHeight = 1f;
    [Tooltip("둑 두께.")]
    [Range(0.1f, 1.5f)] public float bankThickness = 0.4f;

    [Header("해상도")]
    [Tooltip("곡선 분할 수. 높을수록 매끈하지만 무거움.")]
    [Range(8, 120)]   public int segments = 48;

    [Header("머티리얼")]
    [Tooltip("물 표면 머티리얼(Toon Water 추천). 비우면 에디터에서 자동 지정.")]
    public Material waterMaterial;
    [Tooltip("둑 머티리얼. 비우면 기본 회색.")]
    public Material bankMaterial;

    const string kGenName = "_Generated";
    Transform m_Gen;
    bool m_Queued;

    void OnEnable()   => RequestRebuild();
    void OnValidate() => RequestRebuild();

    void RequestRebuild()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (m_Queued) return;                      // OnValidate 연타 코얼레싱
            m_Queued = true;
            EditorApplication.delayCall += DelayedRebuild;  // OnValidate 중 파괴/생성 금지 → 다음 틱
            return;
        }
#endif
        Rebuild();
    }

#if UNITY_EDITOR
    void DelayedRebuild()
    {
        m_Queued = false;
        if (this == null) return;   // 그 사이 삭제됨
        Rebuild();
    }
#endif

    [ContextMenu("Rebuild Now")]
    public void Rebuild()
    {
        var prev = transform.Find(kGenName);
        if (prev != null) DestroyImmediateSafe(prev.gameObject);

        m_Gen = new GameObject(kGenName).transform;
        m_Gen.SetParent(transform, false);
        m_Gen.gameObject.hideFlags = HideFlags.DontSave;

        int segs = Mathf.Max(2, segments);

        // 중심선(로컬) + 좌우 가장자리
        var center = new Vector3[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            float p = i / (float)segs;
            center[i] = new Vector3(amplitude * Mathf.Sin(p * waves * Mathf.PI * 2f),
                                    0f, p * length - length * 0.5f);
        }
        var left  = new Vector3[segs + 1];
        var right = new Vector3[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            Vector3 t = (i < segs ? center[i + 1] - center[i] : center[i] - center[i - 1]);
            t.y = 0f; t.Normalize();
            Vector3 n = new Vector3(t.z, 0f, -t.x);
            left[i]  = center[i] - n * halfWidth;
            right[i] = center[i] + n * halfWidth;
        }

        BuildWater(segs, left, right);
        BuildBanks(segs, left, right);
    }

    void BuildWater(int segs, Vector3[] left, Vector3[] right)
    {
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        for (int i = 0; i <= segs; i++)
        {
            verts.Add(new Vector3(left[i].x,  waterHeight, left[i].z));
            verts.Add(new Vector3(right[i].x, waterHeight, right[i].z));
            float v = i / (float)segs * (length / Mathf.Max(0.1f, halfWidth * 2f));
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
        }
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
            tris.Add(a); tris.Add(c); tris.Add(b);   // 윗면(+Y) 와인딩
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        var mesh = new Mesh { name = "WaterRibbon", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go = new GameObject("Water");
        go.transform.SetParent(m_Gen, false);   // 로컬 0 → 부모(이 오브젝트) 따라 같이 움직임
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = waterMaterial;
    }

    void BuildBanks(int segs, Vector3[] left, Vector3[] right)
    {
        if (bankHeight <= 0f) return;
        var banks = new GameObject("Banks");
        banks.transform.SetParent(m_Gen, false);
        banks.hideFlags = HideFlags.DontSave;
        for (int i = 0; i < segs; i++)
        {
            AddBank(banks.transform, left[i],  left[i + 1]);
            AddBank(banks.transform, right[i], right[i + 1]);
        }
    }

    void AddBank(Transform parent, Vector3 p0, Vector3 p1)
    {
        Vector3 dir = p1 - p0; float len = dir.magnitude;
        if (len < 1e-4f) return;
        dir /= len;
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.hideFlags = HideFlags.DontSave;
        c.transform.SetParent(parent, false);
        c.transform.localPosition = (p0 + p1) * 0.5f + Vector3.up * (bankHeight * 0.5f);
        c.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
        c.transform.localScale    = new Vector3(bankThickness, bankHeight, len + 0.05f);
        if (bankMaterial != null) c.GetComponent<MeshRenderer>().sharedMaterial = bankMaterial;
    }

    static void DestroyImmediateSafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
