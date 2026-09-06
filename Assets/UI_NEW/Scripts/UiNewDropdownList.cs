using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>
    /// 맵/모드 드롭다운 펼침 목록의 런타임 조립(로비·방 생성 공용).
    /// 프리팹은 옵션 행(Option_N)을 평평하게 들고 있고, 여기서 스크롤 구조로 재조립한다:
    ///   루트(ScrollRect + DropdownBg 패널) ▸ Viewport(RectMask2D) ▸ Content ▸ 행들 (+오른쪽 스크롤바)
    /// 규칙(QA): 최대 4행만 보이고 나머지는 스크롤 · 패널은 닫힘 셀렉터보다 좁게 · 바깥 클릭 시 닫힘.
    /// 행 너비는 프리팹 값을 믿지 않고(스트레치 sizeDelta 사고 전력) 매번 여기서 확정한다.
    /// </summary>
    public static class UiNewDropdownList
    {
        private const int kMaxVisibleRows = 4;
        private const float kPad = 7f;        // 패널 안쪽 여백
        private const float kNarrower = 46f;  // 닫힘 셀렉터보다 이만큼 좁게

        public static void Setup(GameObject optionsRoot, RectTransform selector)
        {
            if (optionsRoot == null) return;
            if (optionsRoot.transform is not RectTransform rootRt) return;

            // ① 스크롤 구조 확보(재호출 안전)
            var viewport = rootRt.Find("Viewport") as RectTransform;
            RectTransform content;
            if (viewport == null)
            {
                viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).transform;
                viewport.SetParent(rootRt, false);
                content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
                content.SetParent(viewport, false);
            }
            else
            {
                content = viewport.Find("Content") as RectTransform;
                if (content == null)
                {
                    content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
                    content.SetParent(viewport, false);
                }
            }
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(kPad, kPad);
            viewport.offsetMax = new Vector2(-kPad, -kPad);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            // ② 행 수집(첫 호출: 루트 밑 / 재호출: Content 밑) → Content로 이사 + 위에서부터 재적층
            var rows = new List<RectTransform>();
            CollectRows(rootRt, rows);
            CollectRows(content, rows);
            float rowH = 50f;
            foreach (var r in rows)
                if (r.sizeDelta.y > 20f) { rowH = r.sizeDelta.y; break; }

            int active = 0;
            foreach (var r in rows)
            {
                r.SetParent(content, false);
                r.anchorMin = new Vector2(0f, 1f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.sizeDelta = new Vector2(0f, rowH);   // Content 가로에 꽉 — 너비는 루트가 결정
                if (!r.gameObject.activeSelf) continue;
                r.anchoredPosition = new Vector2(0f, -active * rowH);
                active++;
            }
            content.sizeDelta = new Vector2(0f, active * rowH);

            // ③ 루트 크기 — 너비는 셀렉터 실측에서, 높이는 '보이는 행'만큼만
            int visible = Mathf.Min(kMaxVisibleRows, Mathf.Max(1, active));
            float width = selector != null && selector.rect.width > 60f
                ? selector.rect.width - kNarrower : rootRt.sizeDelta.x;
            rootRt.sizeDelta = new Vector2(width, visible * rowH + kPad * 2f);

            // ④ 흰 패널은 루트에 꽉(행 수 계산 불필요 — 루트 높이가 이미 정답)
            if (rootRt.Find("DropdownBg") is RectTransform bg)
            {
                bg.anchorMin = Vector2.zero;
                bg.anchorMax = Vector2.one;
                bg.offsetMin = Vector2.zero;
                bg.offsetMax = Vector2.zero;
                bg.SetAsFirstSibling();
            }

            // ⑤ 스크롤(4행 넘을 때만) + 스크롤바
            var scroll = optionsRoot.GetComponent<ScrollRect>();
            if (scroll == null) scroll = optionsRoot.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = active > kMaxVisibleRows;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            if (scroll.vertical) EnsureScrollbar(rootRt, scroll);

            // ⑥ 바깥 클릭 시 닫힘 + 열릴 때 맨 위로
            var auto = optionsRoot.GetComponent<UiNewDropdownAutoClose>();
            if (auto == null) auto = optionsRoot.AddComponent<UiNewDropdownAutoClose>();
            auto.KeepOpenAreas = selector != null ? new[] { rootRt, selector } : new[] { rootRt };
        }

        private static void CollectRows(Transform parent, List<RectTransform> into)
        {
            if (parent == null) return;
            foreach (Transform child in parent)
                if (child.name.StartsWith("Option_") && child is RectTransform rt)
                    into.Add(rt);
        }

        private static void EnsureScrollbar(RectTransform rootRt, ScrollRect scroll)
        {
            var sbT = rootRt.Find("Scrollbar") as RectTransform;
            if (sbT == null)
            {
                sbT = (RectTransform)new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).transform;
                sbT.SetParent(rootRt, false);
                sbT.anchorMin = new Vector2(1f, 0f);
                sbT.anchorMax = new Vector2(1f, 1f);
                sbT.pivot = new Vector2(1f, 0.5f);
                sbT.anchoredPosition = new Vector2(-3f, 0f);
                sbT.sizeDelta = new Vector2(6f, -kPad * 2f);
                var track = sbT.GetComponent<Image>();
                track.color = new Color(0f, 0f, 0f, 0.05f);
                track.raycastTarget = true;

                var handleT = (RectTransform)new GameObject("Handle", typeof(RectTransform), typeof(Image)).transform;
                handleT.SetParent(sbT, false);
                handleT.sizeDelta = Vector2.zero;
                var handleImg = handleT.GetComponent<Image>();
                handleImg.sprite = Resources.Load<Sprite>("UI_NEW/Common/DropdownScrollbar");   // 한글 경로는 macOS(NFD 파일명)에서 Resources.Load가 실패해 ASCII 경로로 이동
                handleImg.color = handleImg.sprite != null ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
                handleImg.raycastTarget = true;

                var sb = sbT.GetComponent<Scrollbar>();
                sb.direction = Scrollbar.Direction.BottomToTop;
                sb.handleRect = handleT;
                sb.targetGraphic = handleImg;
            }
            scroll.verticalScrollbar = sbT.GetComponent<Scrollbar>();
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
    }

    /// <summary>펼침 목록 바깥을 클릭/터치하면 닫는다. 셀렉터 클릭은 토글 onClick에 맡긴다(이중 처리 방지).</summary>
    internal sealed class UiNewDropdownAutoClose : MonoBehaviour
    {
        public RectTransform[] KeepOpenAreas;
        private ScrollRect m_Scroll;

        private void OnEnable()
        {
            if (m_Scroll == null) m_Scroll = GetComponent<ScrollRect>();
            if (m_Scroll != null) m_Scroll.verticalNormalizedPosition = 1f;   // 열릴 때 항상 맨 위부터
        }

        private void Update()
        {
            Vector2 pos;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                pos = mouse.position.ReadValue();
            else if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                pos = touch.primaryTouch.position.ReadValue();
            else return;

            if (KeepOpenAreas != null)
            {
                foreach (var area in KeepOpenAreas)
                    if (area != null && RectTransformUtility.RectangleContainsScreenPoint(area, pos, CameraFor(area)))
                        return;
            }
            gameObject.SetActive(false);
        }

        private static Camera CameraFor(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }
}
