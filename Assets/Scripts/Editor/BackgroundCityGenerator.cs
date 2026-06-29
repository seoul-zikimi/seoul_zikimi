using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

/// <summary>
/// 청계천 배경용 로우폴리 빌딩 실루엣 자동 생성기
/// Tools → Background City Generator
/// </summary>
public class BackgroundCityGenerator : EditorWindow
{
    // --- 배치 설정 ---
    int   m_BuildingCount  = 18;
    float m_SpreadWidth    = 60f;
    float m_ZOffset        = 25f;   // 플레이 영역 뒤로 얼마나
    float m_YOffset        = 0f;    // 지면 Y

    // --- 빌딩 크기 범위 ---
    float m_MinWidth       = 2.5f;
    float m_MaxWidth       = 6f;
    float m_MinHeight      = 6f;
    float m_MaxHeight      = 20f;
    float m_MinDepth       = 2f;
    float m_MaxDepth       = 4f;

    // --- 비주얼 ---
    Color m_BuildingColor  = new Color(0.22f, 0.27f, 0.35f, 1f); // 청회색 실루엣
    float m_ColorVariance  = 0.06f;  // 빌딩마다 색 미세하게 다르게

    // --- 두 번째 레이어 (더 먼 빌딩) ---
    bool  m_AddFarLayer    = true;
    float m_FarZOffset     = 45f;
    float m_FarScaleMulti  = 1.4f;  // 더 크고 단순하게

    string m_ParentName    = "BackgroundCity";

    [MenuItem("Tools/Background City Generator")]
    static void Open() => GetWindow<BackgroundCityGenerator>("City Generator");

    void OnGUI()
    {
        GUILayout.Label("배치", EditorStyles.boldLabel);
        m_BuildingCount = EditorGUILayout.IntSlider("빌딩 수", m_BuildingCount, 5, 40);
        m_SpreadWidth   = EditorGUILayout.Slider("좌우 폭", m_SpreadWidth, 20f, 120f);
        m_ZOffset       = EditorGUILayout.Slider("뒤 거리 (Z)", m_ZOffset, 5f, 60f);
        m_YOffset       = EditorGUILayout.FloatField("지면 Y", m_YOffset);

        EditorGUILayout.Space();
        GUILayout.Label("빌딩 크기", EditorStyles.boldLabel);
        EditorGUILayout.MinMaxSlider("너비", ref m_MinWidth,  ref m_MaxWidth,  1f, 12f);
        EditorGUILayout.LabelField("", $"{m_MinWidth:F1} ~ {m_MaxWidth:F1}", EditorStyles.miniLabel);
        EditorGUILayout.MinMaxSlider("높이", ref m_MinHeight, ref m_MaxHeight, 2f, 35f);
        EditorGUILayout.LabelField("", $"{m_MinHeight:F1} ~ {m_MaxHeight:F1}", EditorStyles.miniLabel);
        EditorGUILayout.MinMaxSlider("깊이", ref m_MinDepth,  ref m_MaxDepth,  1f, 8f);
        EditorGUILayout.LabelField("", $"{m_MinDepth:F1} ~ {m_MaxDepth:F1}", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        GUILayout.Label("비주얼", EditorStyles.boldLabel);
        m_BuildingColor  = EditorGUILayout.ColorField("기본 색상", m_BuildingColor);
        m_ColorVariance  = EditorGUILayout.Slider("색상 편차", m_ColorVariance, 0f, 0.2f);

        EditorGUILayout.Space();
        GUILayout.Label("원경 레이어", EditorStyles.boldLabel);
        m_AddFarLayer    = EditorGUILayout.Toggle("두 번째 레이어 추가", m_AddFarLayer);
        if (m_AddFarLayer)
        {
            m_FarZOffset     = EditorGUILayout.Slider("원경 Z", m_FarZOffset, 30f, 100f);
            m_FarScaleMulti  = EditorGUILayout.Slider("원경 크기 배율", m_FarScaleMulti, 1f, 2.5f);
        }

        EditorGUILayout.Space();
        m_ParentName = EditorGUILayout.TextField("부모 오브젝트 이름", m_ParentName);

        EditorGUILayout.Space();
        if (GUILayout.Button("기존 제거 후 생성", GUILayout.Height(35)))
        {
            DeleteExisting();
            Generate();
        }

        if (GUILayout.Button("기존 제거", GUILayout.Height(25)))
            DeleteExisting();
    }

    void DeleteExisting()
    {
        var existing = GameObject.Find(m_ParentName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }
    }

    void Generate()
    {
        var parent = new GameObject(m_ParentName);
        Undo.RegisterCreatedObjectUndo(parent, "Generate Background City");

        var rng = new System.Random(42); // 시드 고정으로 재생성해도 동일한 배치

        SpawnLayer(parent.transform, rng, m_BuildingCount, m_ZOffset, 1f);

        if (m_AddFarLayer)
            SpawnLayer(parent.transform, rng, Mathf.RoundToInt(m_BuildingCount * 0.7f), m_FarZOffset, m_FarScaleMulti);

        Selection.activeGameObject = parent;
        EditorGUIUtility.PingObject(parent);

        Debug.Log($"[CityGen] 빌딩 {parent.transform.childCount}개 생성 완료 → '{m_ParentName}'");
    }

    void SpawnLayer(Transform parent, System.Random rng, int count, float zOffset, float scaleMulti)
    {
        for (int i = 0; i < count; i++)
        {
            float t   = count == 1 ? 0.5f : (float)i / (count - 1);
            float x   = Mathf.Lerp(-m_SpreadWidth * 0.5f, m_SpreadWidth * 0.5f, t);
            float xJitter = (float)(rng.NextDouble() - 0.5) * (m_SpreadWidth / count) * 0.8f;

            float w   = Lerp(rng, m_MinWidth,  m_MaxWidth)  * scaleMulti;
            float h   = Lerp(rng, m_MinHeight, m_MaxHeight) * scaleMulti;
            float d   = Lerp(rng, m_MinDepth,  m_MaxDepth)  * scaleMulti;

            var pb = ShapeGenerator.GenerateCube(PivotLocation.FirstVertex, new Vector3(w, h, d));
            pb.gameObject.name = $"Building_{i:00}";
            pb.transform.SetParent(parent);
            pb.transform.position = new Vector3(x + xJitter, m_YOffset, zOffset);

            // 색상 변화 적용
            float v   = (float)(rng.NextDouble() - 0.5) * m_ColorVariance;
            var col   = new Color(
                Mathf.Clamp01(m_BuildingColor.r + v),
                Mathf.Clamp01(m_BuildingColor.g + v),
                Mathf.Clamp01(m_BuildingColor.b + v)
            );
            ApplyColor(pb, col);

            // 그림자만 드리우고 받지 않게 (성능)
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows    = false;
            }
        }
    }

    static float Lerp(System.Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);

    static void ApplyColor(ProBuilderMesh pb, Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        // 약간의 smoothness로 도시 건물 느낌
        mat.SetFloat("_Smoothness", 0.15f);
        pb.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
