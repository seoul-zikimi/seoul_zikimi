using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>
    /// 서버 응답이 너무 늦을 때 띄우는 안내 팝업. 비밀번호 입력 팝업과 같은 창 스타일을 쓰되
    /// 문구가 그림에 박혀 있지 않은 빈 프레임(공통 안내 팝업 배경)을 쓰고 글자는 런타임에 얹는다.
    /// 우상단 ×로 닫을 수 있다.
    /// </summary>
    public static class UiNewNetworkNotice
    {
        private const string FramePath = "UI_NEW/03_팝업 화면들/공통 안내 팝업/안내 팝업 배경";
        private const string CanvasName = "@UiNewNetworkNotice";
        private const string DefaultMessage = "서버와의 통신이\n원활하지 않습니다.";

        // 원본 팝업(405x297) 안에서의 자리 — 비밀번호 팝업과 같은 규격.
        private static readonly Vector2 FrameSize = new(405f, 297f);
        private static readonly Vector2 CloseButtonPos = new(171.2f, 121f);
        private static readonly Color TitleColor = new(0.29f, 0.18f, 0.14f, 1f);
        private static readonly Color BodyColor = new(0.25f, 0.22f, 0.20f, 1f);

        /// <summary>안내 팝업을 띄운다. 이미 떠 있으면 아무것도 하지 않는다.</summary>
        public static void Show(string message = null)
        {
            Canvas canvas = JobsnailUiKit.EnsureOverlayCanvas(CanvasName, 800);
            if (canvas.transform.childCount > 0)
                return;   // 이미 떠 있음 — 겹쳐 띄우지 않는다

            Image dim = JobsnailUiKit.Box("DimBlocker", canvas.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.52f));
            dim.raycastTarget = true;   // 팝업 뒤 클릭 차단

            RectTransform frame = JobsnailUiKit.Rect("NoticeFrame", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, FrameSize);
            var frameImage = frame.gameObject.AddComponent<Image>();

            // 스프라이트를 못 읽어도 흰 네모가 뜨지 않도록 색 박스로 대체한다.
            Sprite background = JobsnailUiKit.Sprite(FramePath);
            if (background != null)
            {
                frameImage.sprite = background;
                frameImage.preserveAspect = true;
            }
            else
            {
                frameImage.color = new Color(0.16f, 0.12f, 0.09f, 0.94f);
            }

            // 헤더 — 배경에 그려진 경고 아이콘 오른쪽에 제목만 얹는다.
            JobsnailUiKit.Label("Title", frame, "안내", 20, TitleColor,
                TextAlignmentOptions.Left, new Vector2(-46.5f, 121.5f), new Vector2(200f, 34f));

            JobsnailUiKit.Label("Message", frame, string.IsNullOrEmpty(message) ? DefaultMessage : message,
                28, BodyColor, TextAlignmentOptions.Center, new Vector2(0f, -8f), new Vector2(340f, 140f));

            // 배경에 그려진 우상단 × 위 투명 히트박스(비밀번호 팝업과 같은 좌표·크기).
            Button close = JobsnailUiKit.Button("CloseButton", frame, null,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), CloseButtonPos, new Vector2(44f, 44f),
                () => Close(canvas));
            close.image.color = Color.clear;
        }

        private static void Close(Canvas canvas)
        {
            if (canvas != null)
                Object.Destroy(canvas.gameObject);
        }
    }
}
