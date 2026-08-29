using UnityEditor;
using UnityEngine;

/// <summary>
/// 경복궁 사운드 5종을 SoundLibrary.asset에 원클릭 연결(멱등 — 있으면 클립만 갱신).
/// 클립: Assets/Sound/Clips/Gyeongbokgung/*.mp3 (다운로드 원본을 영문명으로 복사해둔 것).
/// FireBurning(타는 중 루프)은 라이브러리가 아니라 Resources/Sfx/FireBurning을
/// FireNetwork의 화염 그룹 AudioSource가 직접 루프 재생한다(원샷 계약인 PlaySFXAt와 분리).
/// </summary>
public static class GyeongbokgungSoundWiring
{
    const string k_LibraryPath = "Assets/Sound/Data/SoundLibrary.asset";
    const string k_ClipDir     = "Assets/Sound/Clips/Gyeongbokgung";

    static readonly (SFXType type, string clip)[] k_Wires =
    {
        (SFXType.FireIgnite, "Fire_Ignite"),
        (SFXType.WaterFill,  "Water_Fill"),
        (SFXType.WaterPour,  "Water_Pour"),
        (SFXType.HolyChime,  "Holy_Chime"),
    };

    [MenuItem("Tools/Sound/경복궁 사운드 연결")]
    static void Apply()
    {
        var lib = AssetDatabase.LoadAssetAtPath<SoundLibrarySO>(k_LibraryPath);
        if (lib == null) { Debug.LogError($"[경복궁사운드] SoundLibrary 없음: {k_LibraryPath}"); return; }

        var so = new SerializedObject(lib);
        var entries = so.FindProperty("sfxEntries");
        int wired = 0, missing = 0;
        foreach (var (type, clipName) in k_Wires)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{k_ClipDir}/{clipName}.mp3");
            if (clip == null) { Debug.LogWarning($"[경복궁사운드] 클립 없음(건너뜀): {k_ClipDir}/{clipName}.mp3"); missing++; continue; }

            int idx = -1;   // 기존 엔트리 찾기(같은 type)
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue == (int)type) { idx = i; break; }
            if (idx < 0) { idx = entries.arraySize; entries.arraySize++; }

            var e = entries.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("type").intValue = (int)type;
            var clips = e.FindPropertyRelative("clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            wired++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[경복궁사운드] 완료 ✔ 연결 {wired}건 / 클립 없음 {missing}건. " +
                  "FireBurning 루프는 Resources/Sfx/FireBurning을 화염이 직접 재생(연결 불필요).");
    }
}
