#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New.Editor
{
    /// <summary>
    /// UI_NEW_Canvas 프리팹의 맵/모드 드롭다운을 피그마 시안대로 리스킨한다.
    /// 기존: 옵션 행마다 '닫힘 알약' 스프라이트를 재사용(핀·▼가 행마다 반복 — QA 사진1).
    /// 변경: 흰 패널(드롭박스 영역) + 평소 투명·호버 시 하늘색 강조박스 행 + 왼쪽 핀 아이콘(사진2).
    /// 행 개수는 런타임 FitPool이 복제로 늘리므로 패널 크기는 UiNewMapOptions.FitDropdownBg가 매번 맞추고,
    /// 행 색 상태(투명/호버)는 UiNewButtonVisualPolicy가 Option_ 이름으로 구분해 강제한다.
    /// </summary>
    internal static class DropdownSkinTool
    {
        private const string kPrefab = "Assets/Prefabs/UI_NEW_Canvas.prefab";
        private const string kDir = "Assets/Resources/UI_NEW/02_세션 화면";
        private const string kPanelPng = kDir + "/드롭박스 - 펼쳐졌을 때 영역.png";
        private const string kHighlightPng = kDir + "/드롭박스 - 펼쳐졌을 때 커서 올린 곳 강조박스.png";
        private const string kMapPinPng = kDir + "/맵 아이콘.png";
        private const string kModePinPng = kDir + "/모드 아이콘.png";

        [MenuItem("Tools/UI NEW/드롭박스 스킨 적용")]
        public static void Apply()
        {
            // 9-slice 안 하면 늘어난 패널의 둥근 모서리가 찌그러진다
            EnsureSliced(kPanelPng, new Vector4(12f, 12f, 12f, 12f));
            EnsureSliced(kHighlightPng, new Vector4(8f, 8f, 8f, 8f));

            var panel = AssetDatabase.LoadAssetAtPath<Sprite>(kPanelPng);
            var highlight = AssetDatabase.LoadAssetAtPath<Sprite>(kHighlightPng);
            var mapPin = AssetDatabase.LoadAssetAtPath<Sprite>(kMapPinPng);
            var modePin = AssetDatabase.LoadAssetAtPath<Sprite>(kModePinPng);
            if (panel == null || highlight == null)
            {
                Debug.LogError("[DropdownSkin] 드롭박스 스프라이트를 못 읽음 — 02_세션 화면 폴더 확인");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(kPrefab);
            try
            {
                // Restyle이 Pin을 DestroyImmediate하므로, 캐시된 전체 순회 배열을 들고 있으면
                // 죽은 Transform의 name 접근에서 MissingReferenceException이 난다 — 대상만 먼저 모은다.
                var targets = new System.Collections.Generic.List<RectTransform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name is "LobbyMapOptions" or "CreateMapOptions" or "LobbyModeOptions" or "CreateModeOptions")
                        targets.Add((RectTransform)t);
                }
                int count = 0;
                foreach (var t in targets)
                {
                    Restyle(t, panel, highlight, t.name.Contains("Map") ? mapPin : modePin);
                    count++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, kPrefab);
                Debug.Log($"[DropdownSkin] 드롭다운 {count}개 리스킨 완료 ✔ (플레이해서 펼침 모습 확인)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Restyle(RectTransform optionsRoot, Sprite panel, Sprite highlight, Sprite pin)
        {
            // 행 너비는 여기서 만지지 않는다 — 프리팹 단계의 rect는 레이아웃 전이라 못 믿는다
            // (스트레치 앵커면 팝업 통째 너비가 나와 목록이 뻥튀기됐던 사고).
            // 실제 너비 맞춤은 런타임 UiNewMapOptions.FitDropdownBg(셀렉터 기준)가 한다.

            // 흰 패널(맨 뒤) — 크기는 런타임 FitDropdownBg가 매번 맞추므로 여기선 자리만 만든다
            var bgT = optionsRoot.Find("DropdownBg");
            if (bgT == null)
            {
                var go = new GameObject("DropdownBg", typeof(RectTransform), typeof(Image));
                bgT = go.transform;
                bgT.SetParent(optionsRoot, false);
            }
            var bgImg = bgT.GetComponent<Image>();
            bgImg.sprite = panel;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            bgImg.raycastTarget = true;   // 패널 틈으로 뒤 요소가 눌리는 것 방지
            bgT.SetAsFirstSibling();

            foreach (Transform child in optionsRoot)
            {
                if (!child.name.StartsWith("Option_")) continue;

                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = highlight;          // 평소엔 ColorTint(투명)로 숨고 호버 때만 보임
                    img.type = Image.Type.Sliced;
                    img.color = Color.white;
                }

                // 왼쪽 아이콘(핀/게임기) — 지웠다 새로 만들어 크기 변경이 재실행에도 항상 반영되게.
                // 원본 아트대로 넣으면 게임기가 너무 크다(QA) — 높이 18, 폭 22로 캡.
                var oldPin = child.Find("Pin");
                if (oldPin != null)
                    Object.DestroyImmediate(oldPin.gameObject);
                if (pin != null)
                {
                    const float kMaxH = 18f, kMaxW = 22f;
                    float aspect = pin.rect.width / Mathf.Max(1f, pin.rect.height);
                    float w = kMaxH * aspect, h = kMaxH;
                    if (w > kMaxW) { w = kMaxW; h = kMaxW / aspect; }

                    var pinGo = new GameObject("Pin", typeof(RectTransform), typeof(Image));
                    var rt = (RectTransform)pinGo.transform;
                    rt.SetParent(child, false);
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(w, h);
                    rt.anchoredPosition = new Vector2(14f + w * 0.5f, 0f);
                    var pi = pinGo.GetComponent<Image>();
                    pi.sprite = pin;
                    pi.preserveAspect = true;
                    pi.raycastTarget = false;
                }

                // 행은 가로 스트레치(0..1) — sizeDelta.x는 부모 대비 확장분. 셀렉터(=루트 너비)보다
                // 24px 좁게 = -24 고정(절대값이라 재실행해도 동일). 이전 실행이 망친 322 같은 값도 여기서 복구.
                if (child is RectTransform rowRt
                    && !Mathf.Approximately(rowRt.anchorMin.x, rowRt.anchorMax.x))
                    rowRt.sizeDelta = new Vector2(-24f, rowRt.sizeDelta.y);
            }
        }

        private static void EnsureSliced(string path, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool changed = importer.textureType != TextureImporterType.Sprite
                           || importer.spriteBorder != border
                           || importer.mipmapEnabled
                           || !importer.alphaIsTransparency;
            if (!changed) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
#endif
