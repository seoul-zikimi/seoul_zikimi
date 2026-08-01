using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 네트워크 없이 굴리는 테스트 씬 생성기 — 아이템 FX/사운드 + 사거리(2칸) 판정 + 배송 지점.
    /// [메뉴] Tools ▸ Test ▸ 샌드박스 테스트 씬 → Assets/Scenes/SandboxTest.unity 생성 후 열림.
    /// 이미 있으면 열기만 한다(손으로 꾸며놨을 수 있으니 덮어쓰지 않음).
    /// </summary>
    public static class ItemFxTestSceneTool
    {
        const string kPath = "Assets/Scenes/SandboxTest.unity";

        [MenuItem("Tools/Test/샌드박스 테스트 씬")]
        public static void CreateOrOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (System.IO.File.Exists(kPath)) { EditorSceneManager.OpenScene(kPath); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * 4f;
            ground.transform.position = new Vector3(10f, 0f, 10f);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(new Vector3(8f, 14f, -6f), Quaternion.Euler(50f, 0f, 0f));
                cam.clearFlags = CameraClearFlags.Skybox;
            }

            // 아이템 FX(숫자키)
            var anchor = new GameObject("FxAnchor");
            anchor.transform.position = new Vector3(0f, 0.5f, 0f);
            var fxTester = new GameObject("ItemFxTester").AddComponent<ItemFxTester>();
            SetRef(fxTester, "m_Anchor", anchor.transform);

            // 사거리·배송 샌드박스(WASD)
            var point = new GameObject("DeliveryPoint");
            point.transform.position = new Vector3(-3.5f, 0f, 4f);
            var sandbox = new GameObject("SandboxTester").AddComponent<SandboxTester>();
            SetRef(sandbox, "m_DeliveryPoint", point.transform);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, kPath);
            AssetDatabase.Refresh();

            Debug.Log($"[SandboxTest] 생성: {kPath}\n" +
                      "Play 후 — WASD=이동(블록 초록=사거리 안) / 1~5,0,←→=아이템 FX / DeliveryPoint 끌면 노란 마커=실제 착지 위치\n" +
                      "2vs2 배경은 Tools ▸ Test ▸ 2vs2 배경 미리보기 로 확인");
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
