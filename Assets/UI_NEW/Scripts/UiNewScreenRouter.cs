using System;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewScreenRouter : MonoBehaviour, IUiNewScreenRouter
    {
        [Serializable]
        private struct ScreenBinding
        {
            public UiNewScreen screen;
            public GameObject root;
        }

        [SerializeField] private ScreenBinding[] screens;
        [SerializeField] private UiNewScreen initialScreen = UiNewScreen.RoomList;

        public UiNewScreen Current { get; private set; }

        private void Awake()
        {
            // UI_NEW_Canvas 전체(비활성 팝업 포함)를 한 번에 SUITE Medium으로 통일한다.
            JobsnailUiKit.ApplyFontPolicy(transform);
            foreach (ScreenBinding binding in screens)
                if (binding.root != null)
                    try { AddBackgroundFill(binding.root); }   // 장식용 — 어떤 일이 있어도 로비 진입을 막으면 안 된다
                    catch (Exception e) { Debug.LogError($"[UI_NEW] 배경 여백 채움 실패(무시): {e.Message}"); }
            Show(initialScreen);
        }

        // 화면 아트(1920x1080 통짜 구도)는 16:9가 아니면 여백이 생긴다(캔버스 Expand — 구도는 절대 안 자름).
        // 여백은 아트의 최외곽 2px 줄을 뽑아 그 방향으로 쭉 늘려 채운다(가장자리 색 연장) —
        // 경계에서 색이 정확히 이어지고, 아트에 구워진 창/프레임 같은 구조물은 절대 다시 나타나지 않는다.
        // (아트를 확대하거나 거울 반사하면 창이 유령처럼 겹쳐 보인다 — 이 아트는 프레임이 가장자리에 바짝 붙어 여백이 없다.)
        private static void AddBackgroundFill(GameObject screenRoot)
        {
            var bg = screenRoot.transform.Find("Background");
            var bgImage = bg != null ? bg.GetComponent<Image>() : null;
            if (bgImage == null || bgImage.sprite == null)
                return;   // 팝업(딤 배경) 화면은 해당 없음

            var holder = new GameObject("BackgroundExtend", typeof(RectTransform));
            var hrt = (RectTransform)holder.transform;
            hrt.SetParent(screenRoot.transform, false);
            hrt.SetAsFirstSibling();   // 본 구도·UI 전부의 뒤에 깔린다
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = Vector2.zero;

            // RawImage + uvRect로 가장자리 1px 줄만 잘라 늘린다 — Sprite.Create는 iOS의 ASTC 압축 텍스처에서
            // 서브렉트 스프라이트 생성이 기기 크래시를 낼 수 있어 금지(에디터 무압축에선 멀쩡해 못 잡는다).
            var tex = bgImage.sprite.texture;
            float px = 1f / Mathf.Max(1, tex.width), py = 1f / Mathf.Max(1, tex.height);
            // (UV 샘플 영역, 밴드 중심) — Expand에선 한 축만 여백이 생기므로 모서리 걱정은 없다.
            (Rect uv, Vector2 pos)[] strips =
            {
                (new Rect(0f, 0f, px, 1f),      new Vector2(-1920f, 0f)),   // 왼쪽
                (new Rect(1f - px, 0f, px, 1f), new Vector2(1920f, 0f)),    // 오른쪽
                (new Rect(0f, 1f - py, 1f, py), new Vector2(0f, 1080f)),    // 위
                (new Rect(0f, 0f, 1f, py),      new Vector2(0f, -1080f)),   // 아래
            };
            foreach (var (uv, pos) in strips)
            {
                var strip = new GameObject("EdgeFill", typeof(RectTransform), typeof(RawImage));
                var rt = (RectTransform)strip.transform;
                rt.SetParent(hrt, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(1920f, 1080f);
                rt.anchoredPosition = pos;
                var img = strip.GetComponent<RawImage>();
                img.texture = tex;
                img.uvRect = uv;
                img.raycastTarget = false;
                img.color = new Color(0.9f, 0.9f, 0.9f, 1f);   // 본 구도보다 아주 살짝 가라앉게
            }
        }

        public void Show(UiNewScreen screen)
        {
            Current = screen;
            foreach (ScreenBinding binding in screens)
            {
                if (binding.root != null)
                    binding.root.SetActive(ShouldBeActive(binding.screen, screen));
            }
        }

        private static bool ShouldBeActive(UiNewScreen binding, UiNewScreen requested)
        {
            if (binding == requested)
                return true;

            // 팝업은 실제 화면 위에 겹쳐 보인다. 배경 화면을 함께 활성화해 두되,
            // 팝업의 DimBlocker가 아래 UI 입력을 차단한다.
            return binding == UiNewScreen.RoomList
                && (requested == UiNewScreen.CreateRoom || requested == UiNewScreen.Password)
                || binding == UiNewScreen.Lobby && requested == UiNewScreen.HostLeaveWarning;
        }

        public void ShowRoomList() => Show(UiNewScreen.RoomList);
        public void ShowCreateRoom() => Show(UiNewScreen.CreateRoom);
        public void ShowPassword() => Show(UiNewScreen.Password);
        public void ShowLobby() => Show(UiNewScreen.Lobby);
        public void ShowHostLeaveWarning() => Show(UiNewScreen.HostLeaveWarning);
    }
}
