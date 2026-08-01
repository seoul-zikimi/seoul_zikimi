using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 맵 마커(Spot_*)로 스폰되는 시스템 오브젝트 프리팹을 Resources/SystemObjects/ 에 만든다.
    /// 씬에 같은 이름의 오브젝트가 있으면 그걸 그대로 프리팹으로 저장하고(설정 보존),
    /// 없으면 작업대 기본 구성(큐브 + 콜라이더 + Workstation)으로 새로 만든다.
    /// [메뉴] Tools ▸ Map ▸ 시스템 오브젝트 프리팹 만들기
    /// </summary>
    public static class SystemObjectPrefabTool
    {
        const string kDir = "Assets/Resources/SystemObjects";

        // 작업대 기본 구성값 — 씬에 원본이 없을 때 쓰는 복원용(예전 GameScene 설정과 동일).
        // ProcessType: 1 = 고정(망치), 2 = 페인트
        static readonly (string name, int tool, string modelGuid)[] kStations =
        {
            ("HammerStation", 1, "ecb7f6400f0fa1543b84a4841a08f0a4"),
            ("PaintStation",  2, "eef694b801aebe248af89095bb7a8fbe"),
        };

        [MenuItem("Tools/Map/시스템 오브젝트 프리팹 만들기")]
        public static void Build()
        {
            Directory.CreateDirectory(kDir);

            // 플레이어 스폰 마커 — 빈 오브젝트 + PlayerSpawnPoint 컴포넌트면 충분(위치만 쓰는 마커)
            BuildIfMissing("PlayerSpawnPoint", () =>
            {
                var go = new GameObject("PlayerSpawnPoint");
                go.AddComponent<Player.PlayerSpawnPoint>();
                return go;
            });

            foreach (var (name, tool, modelGuid) in kStations)
                BuildIfMissing(name, () => MakeStation(name, tool, modelGuid));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SystemObject] 완료 — 이제 배경 프리팹에 Spot_HammerStation / Spot_PaintStation 마커만 두면 그 자리에 생깁니다.");
        }

        // 이미 있으면 건드리지 않는다. 씬에 같은 이름 오브젝트가 있으면 그 설정을 그대로 프리팹으로 저장.
        static void BuildIfMissing(string name, System.Func<GameObject> makeDefault)
        {
            string path = $"{kDir}/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log($"[SystemObject] 이미 있음 — 건너뜀: {path}");
                return;
            }

            var scene = GameObject.Find(name);
            var go = scene != null ? Object.Instantiate(scene) : makeDefault();
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[SystemObject] 생성: {path} ({(scene != null ? "씬 오브젝트 복사" : "기본 구성")})");
        }

        static GameObject MakeStation(string name, int tool, string modelGuid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 콜라이더(집기 판정) + 폴백 외형
            go.name = name;

            var ws = go.AddComponent<Player.Workstation>();
            var so = new SerializedObject(ws);
            so.FindProperty("m_Tool").intValue = tool;   // ProcessType은 비트값(Fixed=1, Painted=2) — 인덱스 아님

            string modelPath = AssetDatabase.GUIDToAssetPath(modelGuid);
            var model = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null) so.FindProperty("m_StationModel").objectReferenceValue = model;
            else Debug.LogWarning($"[SystemObject] {name}: 도구함 모델을 못 찾음 — 큐브로 보입니다(모델을 직접 배선하세요)");

            so.FindProperty("m_StationModelScale").floatValue = 1f;
            so.FindProperty("m_StationModelEuler").vector3Value = new Vector3(90f, 0f, 0f);
            so.ApplyModifiedProperties();
            return go;
        }
    }
}
