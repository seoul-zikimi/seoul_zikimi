#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 메뉴: Game > Setup > Create SoundManager Demo Scene
///
/// 전제 조건 (씬 생성 전 먼저 완료):
///   1. Assets/Sound/Data/GameAudioMixer.mixer 생성
///   2. Assets/Sound/Data/SoundLibrary.asset 생성 + 클립 연결
///   위 에셋이 없으면 씬은 생성되지만 Inspector에서 수동 연결 필요.
/// </summary>
public static class SoundManagerDemoSceneSetup
{
    const string k_ScenePath   = "Assets/Scenes/SoundManagerDemoScene.unity";
    const string k_MixerPath   = "Assets/Sound/Data/GameAudioMixer.mixer";
    const string k_LibraryPath = "Assets/Sound/Data/SoundLibrary.asset";

    [MenuItem("Game/Setup/Create SoundManager Demo Scene")]
    static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        var camGO = new GameObject("Main Camera");
        camGO.AddComponent<Camera>().tag = "MainCamera";
        camGO.transform.position         = new Vector3(0, 1, -5);

        // EventSystem
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // SoundManager
        var smGO = new GameObject("@SoundManager");
        var sm   = smGO.AddComponent<SoundManager>();

        // 에셋 자동 연결 (미리 생성돼 있으면)
        var so      = new SerializedObject(sm);
        var mixer   = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(k_MixerPath);
        var library = AssetDatabase.LoadAssetAtPath<SoundLibrarySO>(k_LibraryPath);
        if (mixer   != null) so.FindProperty("_mixer").objectReferenceValue   = mixer;
        if (library != null) so.FindProperty("_library").objectReferenceValue = library;
        so.ApplyModifiedProperties();

        if (mixer   == null) Debug.LogWarning("[SoundManagerDemo] AudioMixer 없음 → 수동 연결 필요: " + k_MixerPath);
        if (library == null) Debug.LogWarning("[SoundManagerDemo] SoundLibrary 없음 → 수동 연결 필요: " + k_LibraryPath);

        // SoundManagerDemo
        var demoGO = new GameObject("@SoundManagerDemo");
        demoGO.AddComponent<SoundManagerDemo>();

        EditorSceneManager.SaveScene(scene, k_ScenePath);
        AssetDatabase.Refresh();
        Debug.Log("[SoundManagerDemo] 씬 생성 완료: " + k_ScenePath);
    }
}
#endif
