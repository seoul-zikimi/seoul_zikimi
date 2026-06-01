#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 메뉴: Game/Setup/Add Bloom to Current Scene
/// 현재 열린 씬에 Global Volume + Bloom 을 추가합니다.
/// URP 전용. 이미 @GlobalVolume이 있으면 덮어씁니다.
/// </summary>
public static class BloomSetupEditor
{
    const string k_ProfilePath = "Assets/Settings/BloomProfile.asset";

    [MenuItem("Game/Setup/Add Bloom to Current Scene")]
    static void AddBloom()
    {
        // ── 1. 기존 @GlobalVolume 제거 (중복 방지) ────────────────
        var existing = GameObject.Find("@GlobalVolume");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
            Debug.Log("[BloomSetup] 기존 @GlobalVolume 제거 후 재생성");
        }

        // ── 2. VolumeProfile 생성 (또는 덮어쓰기) ────────────────
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        var bloom = profile.Add<Bloom>(overrides: true);
        bloom.active         = true;
        bloom.intensity.value = 3f;
        bloom.threshold.value = 0.7f;   // 낮을수록 더 많이 빛남
        bloom.scatter.value   = 0.65f;

        // 기존 에셋 덮어쓰기
        var prev = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_ProfilePath);
        if (prev != null) AssetDatabase.DeleteAsset(k_ProfilePath);
        AssetDatabase.CreateAsset(profile, k_ProfilePath);

        // ── 3. Global Volume GameObject 생성 ─────────────────────
        var go     = new GameObject("@GlobalVolume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.profile  = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_ProfilePath);

        // ── 4. Main Camera Post Processing 활성화 ─────────────────
        var cam = Camera.main;
        if (cam == null)
        {
            // Main Camera 태그 없는 경우 씬에서 첫 번째 카메라 찾기
            cam = Object.FindFirstObjectByType<Camera>();
        }

        if (cam != null)
        {
            var urpData = cam.GetUniversalAdditionalCameraData();
            if (urpData != null)
            {
                urpData.renderPostProcessing = true;
                EditorUtility.SetDirty(urpData);
                Debug.Log("[BloomSetup] 카메라 Post Processing 활성화: " + cam.name);
            }
        }
        else
        {
            Debug.LogWarning("[BloomSetup] Main Camera를 찾을 수 없습니다.");
        }

        // ── 5. 씬 저장 ───────────────────────────────────────────
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[BloomSetup] 완료 — Bloom Intensity: 3, Threshold: 0.7");
    }
}
#endif
