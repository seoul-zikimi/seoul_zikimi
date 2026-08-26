using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>화면 배경 PNG에 그려져 있는 JOBSEOUL 창 헤더(− □ ×)의 × 자리에 얹는 투명 클릭 영역.
    /// 배경은 방 리스트/세션 화면 모두 1920x1080을 화면 중앙에 고정해 쓰므로 좌표를 공유한다.</summary>
    internal static class UiNewWindowCloseButton
    {
        // 배경 이미지 기준 × 중심 픽셀(1566, 128)을 캔버스 중앙 원점 좌표로 옮긴 값.
        private static readonly Vector2 CloseMarkPosition = new(606f, 412f);
        private static readonly Vector2 HitSize = new(56f, 56f);
        private static readonly Color Invisible = new(1f, 1f, 1f, 0f);

        /// <summary>화면 루트에 CloseWindowButton을 만들어 붙인다. ×는 배경에 이미 그려져 있어 그림은 넣지 않는다.</summary>
        public static Button Attach(Transform screenRoot, UnityAction onClick)
        {
            if (screenRoot == null || onClick == null)
                return null;

            var go = new GameObject("CloseWindowButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(screenRoot, false);
            go.transform.SetAsLastSibling();   // 같은 화면 안의 다른 패널에 가리지 않게 맨 앞으로

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = CloseMarkPosition;
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
    }
}
