using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// MaterialDef 인스펙터 — 프리팹을 연결하는 '그 순간' 규약 위반을 보여준다.
    /// (규약: 피벗=min-corner, 모델 크기=footprint 칸. 어기면 어서링과 게임 배치가 어긋남.)
    /// 어긋나면 빨간 박스 + [자동으로 칸에 맞추기] 버튼 — 툴 메뉴를 기억할 필요가 없다.
    /// </summary>
    [CustomEditor(typeof(MaterialDef))]
    [CanEditMultipleObjects]
    public class MaterialDefEditor : Editor
    {
        string m_Problem;
        Object m_CheckedPrefab;
        Vector3Int m_CheckedFootprint;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var def = (MaterialDef)target;
            if (def.Prefab == null) return;

            // 프리팹/footprint가 바뀐 경우에만 재검사(인스펙터는 매 프레임 그려지므로 캐시)
            if (!ReferenceEquals(m_CheckedPrefab, def.Prefab) || m_CheckedFootprint != def.Footprint)
            {
                m_CheckedPrefab = def.Prefab;
                m_CheckedFootprint = def.Footprint;
                m_Problem = MaterialPrefabFitTool.Check(def);
            }

            if (string.IsNullOrEmpty(m_Problem))
            {
                EditorGUILayout.HelpBox("프리팹 규약 OK — 어서링에서 칠한 그대로 게임에 지어집니다.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox("프리팹 규약 위반!\n" + m_Problem, MessageType.Error);
            if (GUILayout.Button("자동으로 칸에 맞추기 (래퍼 프리팹 생성 + 교체)"))
            {
                MaterialPrefabFitTool.FitOne(def);
                m_CheckedPrefab = null;   // 재검사 유도
                EditorApplication.ExecuteMenuItem("Grid Setup/Create Autotiles3D Tiles From Catalog");   // 팔레트 동기화
            }
        }
    }
}
