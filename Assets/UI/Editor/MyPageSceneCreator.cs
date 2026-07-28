using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 마이페이지 전용 씬 생성 — Tools ▸ MyPage ▸ Create MyPage Scene.
/// 카메라(왼쪽에 캐릭터 보이게 구도) + 조명 + MyPageSceneController(캐릭터·컨트롤러 연결)를 배치하고
/// Assets/Scenes/MyPage.unity로 저장 + 빌드 씬 목록 등록까지 자동.
/// </summary>
public static class MyPageSceneCreator
{
    private const string kScenePath = "Assets/Scenes/MyPage.unity";
    private const string kCharacter = "Assets/Player/Animations/model.fbx";
    private const string kController = "Assets/Player/Animations/PlayerAnim.controller";

    private static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().sharedMaterial = m;
    }

    private static void PlaceProp(Transform parent, string assetPath, string name, Vector3 pos, float yaw, float scale)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null) return;   // 아직 안 뽑았으면 생략(뽑은 뒤 씬 재생성)
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
    }

    [MenuItem("Tools/MyPage/Create MyPage Scene")]
    public static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라 — 캐릭터(원점)가 화면 왼쪽 1/4 지점에 보이는 구도, 배경은 따뜻한 단색
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.96f, 0.90f, 0.78f, 1f);   // 크림톤(옷장 방 배경 나오기 전 임시)
        camGo.transform.position = new Vector3(1.1f, 1.15f, 2.9f);
        camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));

        // 조명
        var lightGo = new GameObject("Directional Light", typeof(Light));
        var light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.97f, 0.92f, 1f);
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        // 바닥(살짝 어두운 원형 느낌 대신 단순 플레인)
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        var fr = floor.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(0.88f, 0.80f, 0.66f, 1f));
        fr.sharedMaterial = mat;

        // 방 = 조립식(벽 2면 + 바닥은 위 floor) — 통짜 디오라마는 image-to-3D에서 뭉개져서 폐기
        var room = new GameObject("Room");
        MakeWall(room.transform, "WallBack", new Vector3(0f, 1.5f, -1.6f), new Vector3(7f, 3f, 0.1f), new Color(0.72f, 0.62f, 0.78f));   // 파스텔 보라 벽
        MakeWall(room.transform, "WallLeft", new Vector3(-3.2f, 1.5f, 0.9f), new Vector3(0.1f, 3f, 5f), new Color(0.68f, 0.58f, 0.74f));
        MakeWall(room.transform, "Rug", new Vector3(0f, 0.011f, 0.4f), new Vector3(2.2f, 0.02f, 2.2f), new Color(0.93f, 0.86f, 0.72f));  // 러그(납작 박스)

        // 가구(바르코 낱개 생성) — 있으면 배치
        PlaceProp(room.transform, "Assets/MyPage/Prop_Wardrobe.glb", "Wardrobe", new Vector3(1.9f, 0f, -1.15f), 200f, 1.6f);
        PlaceProp(room.transform, "Assets/MyPage/Prop_Bed.glb", "Bed", new Vector3(-2.2f, 0f, -0.7f), 115f, 1.5f);
        PlaceProp(room.transform, "Assets/MyPage/Prop_Lamp.glb", "Lamp", new Vector3(0.9f, 0f, -1.25f), 180f, 1.1f);

        // 거울 프레임(바르코 생성) + 반사 기능
        var mirrorAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyPage/Prop_MirrorFrame.glb");
        if (mirrorAsset != null)
        {
            var m = (GameObject)PrefabUtility.InstantiatePrefab(mirrorAsset);
            m.name = "Mirror";
            m.transform.position = new Vector3(-1.25f, 0f, 0.35f);
            m.transform.rotation = Quaternion.Euler(0f, 125f, 0f);   // 캐릭터를 비스듬히 보게
            m.AddComponent<MirrorReflection>();
        }

        // 컨트롤러(+캐릭터·애니 연결)
        var ctrlGo = new GameObject("@MyPageScene", typeof(MyPageSceneController));
        var so = new SerializedObject(ctrlGo.GetComponent<MyPageSceneController>());
        so.FindProperty("m_CharacterPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(kCharacter);
        so.FindProperty("m_IdleController").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(kController);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, kScenePath);

        // 빌드 씬 목록 등록(중복 방지)
        if (!EditorBuildSettings.scenes.Any(s => s.path == kScenePath))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Append(new EditorBuildSettingsScene(kScenePath, true)).ToArray();

        Debug.Log($"[MyPageScene] 생성 완료 ✔ → {kScenePath} (빌드 목록 등록됨)\n다음: 플레이 → 메인 화면 ▸ 마이페이지");
    }
}
