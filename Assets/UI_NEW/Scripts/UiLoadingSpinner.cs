using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>iOS 스타일 12칸 로딩 스피너(비동기 대기 표시용).
    ///
    /// 비주얼은 Resources/UI/Common/LoadingSpinner 프리팹(스프라이트 미리 구운 것)을 로드해 쓰고,
    /// 이 컴포넌트는 30°씩 스텝 회전만 돌린다(매 프레임 회전보다 사진 같은 '째깍' 느낌).
    ///
    /// 사용법: var s = UiLoadingSpinner.AttachBeside(button.transform as RectTransform);
    ///        ... 끝나면 s.Detach();  (null 안전 — Detach는 몇 번 불러도 됨)</summary>
    public sealed class UiLoadingSpinner : MonoBehaviour
    {
        private const float kStepSeconds = 1f / 12f;   // 한 바퀴 1초(12스텝)
        private const float kDefaultSize = 44f;
        private const float kGapX = 14f;               // 앵커(버튼) 오른쪽 모서리와의 간격

        private static GameObject s_Prefab;   // Resources 캐시(세션 내 1회 로드)

        private float m_Accum;
        private int m_Step;

        private void Update()
        {
            m_Accum += Time.unscaledDeltaTime;
            while (m_Accum >= kStepSeconds)
            {
                m_Accum -= kStepSeconds;
                m_Step = (m_Step + 1) % 12;
            }
            transform.localRotation = Quaternion.Euler(0f, 0f, -m_Step * 30f);
        }

        /// <summary>앵커(보통 버튼)의 자식으로 스피너를 붙여 오른쪽 옆에 띄운다.
        /// offsetOverride를 주면 앵커 중심 기준 그 위치에 놓는다(기본: 오른쪽 모서리 + 간격).</summary>
        public static UiLoadingSpinner AttachBeside(RectTransform anchor, Vector2? offsetOverride = null)
        {
            if (anchor == null)
                return null;
            if (s_Prefab == null)
                s_Prefab = Resources.Load<GameObject>("UI/Common/LoadingSpinner");
            if (s_Prefab == null)
            {
                Debug.LogWarning("[UiLoadingSpinner] Resources/UI/Common/LoadingSpinner 프리팹이 없습니다.");
                return null;
            }

            var go = Object.Instantiate(s_Prefab, anchor);
            go.name = "@LoadingSpinner";
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offsetOverride
                ?? new Vector2(anchor.rect.width * 0.5f + kGapX + kDefaultSize * 0.5f, 0f);

            // 스피너가 클릭을 막지 않게
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            return go.GetComponent<UiLoadingSpinner>();
        }

        /// <summary>스피너 제거. 인스턴스가 이미 없어도 안전.</summary>
        public void Detach()
        {
            if (this != null && gameObject != null)
                Destroy(gameObject);
        }
    }
}
