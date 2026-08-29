using UnityEditor;

/// <summary>
/// Resources/UI_pngs 임포트 규칙.
/// 데스크톱/에디터는 무압축(UI 선명), iPhone/Android는 ASTC 6x6 오버라이드 —
/// 무압축 2048² 한 장이 16MB라 로비+인트로만으로 모바일 메모리가 수백 MB 부풀었다(iOS EXC_RESOURCE 위험).
/// 단 Emotes/ 하위는 모바일도 무압축 유지 — 런타임 Sprite.Create 대상이라
/// ASTC 텍스처를 넣으면 iOS에서 죽는다(UiNewScreenRouter의 RawImage 전환과 같은 이유).
/// </summary>
public sealed class JobsnailUiTexturePostprocessor : AssetPostprocessor
{
    private const string UiPngRoot = "Assets/Resources/UI_pngs/";
    private const string SpriteCreateSafeRoot = "Assets/Resources/UI_pngs/Emotes/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(UiPngRoot))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        ApplyMobileOverrides(importer, assetPath);
    }

    private static void ApplyMobileOverrides(TextureImporter importer, string path)
    {
        bool compressMobile = !path.StartsWith(SpriteCreateSafeRoot);
        foreach (string platform in new[] { "iPhone", "Android" })
        {
            var s = importer.GetPlatformTextureSettings(platform);
            bool want = compressMobile;
            if (s.overridden == want &&
                (!want || (s.format == TextureImporterFormat.ASTC_6x6 && s.maxTextureSize == 2048)))
                continue;
            s.overridden = want;
            if (want)
            {
                s.format = TextureImporterFormat.ASTC_6x6;
                s.maxTextureSize = 2048;
                s.compressionQuality = 50;
            }
            importer.SetPlatformTextureSettings(s);
        }
    }

    [InitializeOnLoadMethod]
    private static void FixExistingImports()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI_pngs" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            bool compressMobile = !path.StartsWith(SpriteCreateSafeRoot);
            foreach (string platform in new[] { "iPhone", "Android" })
            {
                var s = importer.GetPlatformTextureSettings(platform);
                if (s.overridden != compressMobile ||
                    (compressMobile && (s.format != TextureImporterFormat.ASTC_6x6 || s.maxTextureSize != 2048)))
                {
                    ApplyMobileOverrides(importer, path);
                    changed = true;
                    break;
                }
            }

            if (changed)
                importer.SaveAndReimport();
        }
    }
}
