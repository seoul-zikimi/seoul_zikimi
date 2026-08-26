#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SeoulZikimi.UI.New.Editor
{
    /// <summary>UI_NEW 날씨 PNG를 항상 투명 단일 Sprite로 임포트한다.</summary>
    internal static class WeatherSpriteAtlasImporter
    {
        private static readonly string[] s_SpritePaths =
        {
            "Assets/Resources/UI_NEW/Weather/UI/Sunny.png",
            "Assets/Resources/UI_NEW/Weather/UI/Rain.png",
            "Assets/Resources/UI_NEW/Weather/UI/Snow.png",
            "Assets/Resources/UI_NEW/Weather/UI/StrongWind.png",
            "Assets/Resources/UI_NEW/Weather/UI/Typhoon.png",
            "Assets/Resources/UI_NEW/Weather/UI/AutumnLeaves.png",
            "Assets/Resources/UI_NEW/Weather/UI/CherryBlossom.png",
            "Assets/Resources/UI_NEW/Weather/FX/RainDrop.png",
            "Assets/Resources/UI_NEW/Weather/FX/Snowflake.png",
            "Assets/Resources/UI_NEW/Weather/FX/WindStreak.png",
            "Assets/Resources/UI_NEW/Weather/FX/AutumnLeaf.png",
            "Assets/Resources/UI_NEW/Weather/FX/CherryPetal.png"
        };

        private static bool s_IsRunning;

        [InitializeOnLoadMethod]
        private static void ScheduleImport()
        {
            EditorApplication.delayCall += EnsureSprites;
        }

        [MenuItem("Tools/UI NEW/Refresh Weather Sprites")]
        private static void EnsureSprites()
        {
            if (s_IsRunning) return;
            s_IsRunning = true;
            try
            {
                foreach (string path in s_SpritePaths)
                    EnsureSprite(path);
            }
            finally
            {
                s_IsRunning = false;
            }
        }

        private static void EnsureSprite(string path)
        {
            // 무조건 ImportAsset을 먼저 부르면 리로드마다 12장 강제 임포트가 돌아
            // "Asset Database is set to Read Only" / "assets queued up" 경고가 난다.
            // 이미 임포트된 에셋은 GetAtPath로 충분 — 설정이 틀린 것만 재임포트한다.
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            bool changed = importer.textureType != TextureImporterType.Sprite
                           || importer.spriteImportMode != SpriteImportMode.Single
                           || importer.mipmapEnabled
                           || !importer.alphaIsTransparency
                           || importer.wrapMode != TextureWrapMode.Clamp;
            if (!changed) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }
}
#endif
