using UnityEditor;
using UnityEngine;

/// <summary>
/// 브금(BGM) 임포트 규칙: Load Type을 Streaming으로 강제.
/// DecompressOnLoad는 재생 시 곡 전체를 PCM으로 풀어 4MB mp3 한 곡이 메모리 ~40MB를 먹는다 —
/// 한 번에 한 곡만 도는 BGM은 Streaming이 정답(메모리 수백 KB, 모바일 크래시 예방).
/// 효과음(짧은 클립)은 지연 없는 DecompressOnLoad가 맞으므로 브금 폴더만 건드린다.
/// </summary>
public sealed class JobsnailBgmImportPostprocessor : AssetPostprocessor
{
    private const string BgmRoot = "Assets/Sound/Sound_file/브금/";

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(BgmRoot))
            return;

        var importer = (AudioImporter)assetImporter;
        var s = importer.defaultSampleSettings;
        if (s.loadType == AudioClipLoadType.Streaming)
            return;
        s.loadType = AudioClipLoadType.Streaming;
        importer.defaultSampleSettings = s;
    }

    [InitializeOnLoadMethod]
    private static void FixExistingImports()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Sound/Sound_file/브금" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                continue;
            var s = importer.defaultSampleSettings;
            if (s.loadType == AudioClipLoadType.Streaming)
                continue;
            s.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }
    }
}
