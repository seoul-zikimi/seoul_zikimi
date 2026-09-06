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
            "Assets/Resources/UI_NEW/Weather/FX/CherryPetal.png",
            // 2vs2 아이템 아이콘(HeldItemBubble·버프바·배너) — 플랫 세트 13종(gpt-image 시트 분할)
            "Assets/Resources/UI_NEW/Items/Rain.png",
            "Assets/Resources/UI_NEW/Items/Snow.png",
            "Assets/Resources/UI_NEW/Items/StrongWind.png",
            "Assets/Resources/UI_NEW/Items/Typhoon.png",
            "Assets/Resources/UI_NEW/Items/Earthquake.png",
            "Assets/Resources/UI_NEW/Items/Fog.png",
            "Assets/Resources/UI_NEW/Items/MovementSlow.png",
            "Assets/Resources/UI_NEW/Items/ProcessSlow.png",
            "Assets/Resources/UI_NEW/Items/OrderHack.png",
            "Assets/Resources/UI_NEW/Items/Umbrella.png",
            "Assets/Resources/UI_NEW/Items/MovementBoost.png",
            "Assets/Resources/UI_NEW/Items/ProcessBoost.png",
            "Assets/Resources/UI_NEW/Items/Cannon.png",
            // 로비 맵 선택 화살표(JobsnailLobbyPrefabBinder) — 글자 ◀▶ 모바일 깨짐 대체 이미지
            "Assets/Resources/UI_pngs/MapArrow_Left.png",
            "Assets/Resources/UI_pngs/MapArrow_Right.png",
            // 로비 '랜덤' 맵 선택 썸네일(JobsnailLobbySkinner)
            "Assets/Resources/UI_pngs/MapThumb_Random.png",
            // 드롭다운 스크롤바 핸들(UiNewDropdownList) · 안내/방폭파 팝업 배경 · 예 버튼 — 코드 경로 로드용 ASCII 사본 위치
            "Assets/Resources/UI_NEW/Common/DropdownScrollbar.png",
            "Assets/Resources/UI_NEW/Common/NoticeFrame.png",
            "Assets/Resources/UI_NEW/Common/RoomClosedFrame.png",
            "Assets/Resources/UI_NEW/Common/YesButton.png",
            // 좌우 화살표 공용 삼각형(피그마 Polygon) — 세션 화면 맵 화살표·주문 폰 건물 페이지 넘김.
            // 한글 경로(맵 화살표 왼쪽.png)는 macOS에서 파일명이 NFD로 저장돼 Resources.Load(NFC 리터럴)가 실패하므로 ASCII 경로에 둔다.
            "Assets/Resources/UI_NEW/Common/Polygon_2.png"
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
