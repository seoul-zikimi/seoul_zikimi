using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 네트워크 로비 없이 굴리는 테스트 씬 — 아이템 FX/사운드, 사거리(2칸) 판정, 재료 주문→배송 낙하.
    /// [메뉴] Tools ▸ Test ▸ 샌드박스 테스트 씬 → Assets/Scenes/SandboxTest.unity 생성/열기.
    /// 이미 있으면 열기만 하되, 빠진 구성요소는 자동으로 채워 넣는다(손으로 꾸민 건 건드리지 않음).
    /// </summary>
    public static class ItemFxTestSceneTool
    {
        const string kPath = "Assets/Scenes/SandboxTest.unity";
        const string kCatalogPath = "Assets/Grid/Data/MaterialCatalog.asset";

        [MenuItem("Tools/Test/샌드박스 테스트 씬")]
        public static void CreateOrOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = System.IO.File.Exists(kPath)
                ? EditorSceneManager.OpenScene(kPath)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            EnsureContents();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, kPath);
            AssetDatabase.Refresh();

            Debug.Log($"[SandboxTest] 준비 완료: {kPath}\n" +
                      "Play 후 — WASD=이동(블록 초록=사거리 안) / 1~5,0,←→=아이템 FX / " +
                      "우상단 주문 HUD로 재료 주문 → Spot_DeliveryZone 자리에 낙하 / 마커를 끌면 노란 표시가 실제 착지 위치\n" +
                      "2vs2 배경은 Tools ▸ Test ▸ 2vs2 배경 미리보기 로 확인");
        }

        // 없는 것만 만든다 — 두 번 실행해도 중복되지 않게.
        static void EnsureContents()
        {
            if (Find("Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.localScale = Vector3.one * 4f;
                ground.transform.position = new Vector3(10f, 0f, 10f);
            }

            var cam = Camera.main;
            if (cam != null && cam.transform.position == Vector3.zero)
            {
                cam.transform.SetPositionAndRotation(new Vector3(8f, 14f, -6f), Quaternion.Euler(50f, 0f, 0f));
                cam.clearFlags = CameraClearFlags.Skybox;
            }

            // UI(주문 HUD)를 띄우려면 UIManager + EventSystem이 씬에 있어야 한다(평소엔 부트스트랩 씬 담당).
            if (Object.FindFirstObjectByType<UIManager>() == null)
                new GameObject("UIManager").AddComponent<UIManager>();
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem").AddComponent<EventSystem>();
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // 네트워크(호스트 자동 시작) — 씬의 NetworkObject가 스폰되면서 주문 HUD가 뜬다.
            if (NetworkManager.Singleton == null && Object.FindFirstObjectByType<NetworkManager>() == null)
            {
                var netGo = new GameObject("NetworkManager");
                var nm = netGo.AddComponent<NetworkManager>();
                netGo.AddComponent<UnityTransport>();
                nm.NetworkConfig.NetworkTransport = netGo.GetComponent<UnityTransport>();
                netGo.AddComponent<SandboxNetworkBoot>();
            }

            // 보급소 세트(GridManager + MaterialDropField + MaterialDepot) — 실제 게임과 같은 컴포넌트 구성
            if (Object.FindFirstObjectByType<MaterialDepot>() == null)
            {
                var depot = new GameObject("GridManager");
                depot.AddComponent<NetworkObject>();
                var grid = depot.AddComponent<GridManager>();
                depot.AddComponent<MaterialDropField>();
                depot.AddComponent<MaterialDepot>();

                var catalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(kCatalogPath);
                if (catalog != null)
                {
                    var so = new SerializedObject(grid);
                    so.FindProperty("m_Catalog").objectReferenceValue = catalog;
                    so.ApplyModifiedProperties();
                }
                else Debug.LogWarning($"[SandboxTest] MaterialCatalog을 못 찾음: {kCatalogPath} — 주문 목록이 비어 보일 수 있음");
            }

            var legacyPoint = Find("DeliveryPoint");   // 예전 이름으로 만든 씬이면 표준 이름으로 갈아끼움
            if (legacyPoint != null) legacyPoint.name = MaterialDepot.kSpotName;
            if (Find(MaterialDepot.kSpotName) == null)
            {
                var p = new GameObject(MaterialDepot.kSpotName);
                p.transform.position = new Vector3(-3.5f, 0f, 4f);
            }

            if (Object.FindFirstObjectByType<ItemFxTester>() == null)
            {
                var anchor = Find("FxAnchor");
                if (anchor == null)
                {
                    anchor = new GameObject("FxAnchor");
                    anchor.transform.position = new Vector3(0f, 0.5f, 0f);
                }
                var fx = new GameObject("ItemFxTester").AddComponent<ItemFxTester>();
                SetRef(fx, "m_Anchor", anchor.transform);
            }

            if (Object.FindFirstObjectByType<SandboxTester>() == null)
            {
                var sandbox = new GameObject("SandboxTester").AddComponent<SandboxTester>();
                SetRef(sandbox, "m_DeliveryPoint", Find(MaterialDepot.kSpotName).transform);
            }
        }

        static GameObject Find(string name) => GameObject.Find(name);

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
