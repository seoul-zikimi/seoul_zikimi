using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>모니터 아트에 그려져 있는 JOBSEOUL 창 헤더(− □ ×)의 × 자리에 얹는 투명 클릭 영역.
    /// 리마스터(2026-09-01) 이후 ×는 화면 자식 'Monitor' 이미지 안에 있으므로, 모니터에 비율 앵커로 붙여
    /// 에디터에서 모니터를 옮기거나 키워도 × 위를 계속 따라간다. (두 모니터 아트 모두 같은 비율 지점.)</summary>
    internal static class UiNewWindowCloseButton
    {
        // 모니터 아트 기준 × 중심의 비율 좌표(방 리스트 .858/세션 .861 → 중간값) — 히트 영역이 오차를 흡수한다.
        private static readonly Vector2 CloseMarkAnchor = new(0.86f, 0.887f);
        private static readonly Vector2 HitSize = new(76f, 76f);
        // 구(통짜 배경) 화면 폴백: 배경 이미지 기준 × 중심 픽셀(1566, 128)의 캔버스 중앙 원점 좌표.
        private static readonly Vector2 LegacyCloseMarkPosition = new(606f, 412f);
        private static readonly Color Invisible = new(1f, 1f, 1f, 0f);

        /// <summary>화면의 Monitor(없으면 화면 루트)에 CloseWindowButton을 만들어 붙인다. ×는 아트에 이미 그려져 있어 그림은 넣지 않는다.</summary>
        public static Button Attach(Transform screenRoot, UnityAction onClick)
        {
            if (screenRoot == null || onClick == null)
                return null;

            var monitor = FindDeep(screenRoot, "Monitor");
            var go = new GameObject("CloseWindowButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(monitor != null ? monitor : screenRoot, false);
            go.transform.SetAsLastSibling();   // 같은 화면 안의 다른 패널에 가리지 않게 맨 앞으로

            var rect = (RectTransform)go.transform;
            if (monitor != null)
            {
                rect.anchorMin = rect.anchorMax = CloseMarkAnchor;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }
            else
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = LegacyCloseMarkPosition;
            }
            rect.sizeDelta = HitSize;

            var image = go.GetComponent<Image>();
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            KeepInvisible(button);
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>ColorTint 전환은 알파까지 덮어써서 투명 영역을 흰 사각형으로 만들어 버린다.
        /// UiNewButtonVisualPolicy가 화면이 켜질 때마다 ColorTint를 다시 걸므로 그 뒤에 한 번 더 불러 준다.</summary>
        public static void KeepInvisible(Button button)
        {
            if (button == null)
                return;
            button.transition = Selectable.Transition.None;
            if (button.image != null)
                button.image.color = Invisible;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
