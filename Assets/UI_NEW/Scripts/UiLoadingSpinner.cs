using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>iOS 스타일 12칸 로딩 스피너(비동기 대기 표시용).
    ///
    /// 비주얼은 Resources/UI/Common/LoadingSpinner 프리팹(스프라이트 미리 구운 것)을 로드해 쓰고,
    /// 이 컴포넌트는 30°씩 스텝 회전만 돌린다(매 프레임 회전보다 사진 같은 '째깍' 느낌).
    ///
    /// 스피너가 하나라도 떠 있는 동안은 화면 전체 입력이 막힌다(투명 블로커 1장을 최상단에 깔아
    /// 모든 클릭을 흡수). 응답을 기다리는 중에 다른 버튼을 눌러 요청이 겹치는 것을 막기 위함.
    ///
    /// 사용법: var s = UiLoadingSpinner.AttachBeside(button.transform as RectTransform);
    ///        ... 끝나면 s.Detach();  (null 안전 — Detach는 몇 번 불러도 됨)</summary>
    public sealed class UiLoadingSpinner : MonoBehaviour
    {
        private const float kStepSeconds = 1f / 12f;   // 한 바퀴 1초(12스텝)
        private const float kDefaultSize = 44f;
        private const float kGapX = 14f;               // 앵커(버튼) 오른쪽 모서리와의 간격

        private static GameObject s_Prefab;   // Resources 캐시(세션 내 1회 로드)

        // 화면 전체 입력 차단(스피너 여러 개가 겹칠 수 있어 참조 카운트로 관리)
        private static GameObject s_Blocker;
        private static int s_BlockerRefs;

        // 응답이 이만큼 안 오면 안내 팝업을 띄우고 차단을 푼다(무한 대기로 화면이 잠기는 것 방지).
        private const float kTimeoutSeconds = 8f;
        private static readonly List<UiLoadingSpinner> s_Active = new();
        private static float s_BlockStartedAt;
        private static bool s_TimeoutFired;

        private bool m_HoldsBlock;
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

            if (m_HoldsBlock && !s_TimeoutFired && Time.unscaledTime - s_BlockStartedAt >= kTimeoutSeconds)
                FireTimeout();
        }

        // 응답 지연 — 안내 팝업을 띄우고 떠 있는 스피너를 전부 걷는다(차단도 함께 풀린다).
        private static void FireTimeout()
        {
            s_TimeoutFired = true;
            UiNewNetworkNotice.Show();
            foreach (var s in s_Active.ToArray())
                if (s != null) s.Detach();
        }

        /// <summary>앵커(보통 버튼)의 자식으로 스피너를 붙여 오른쪽 옆에 띄운다.
        /// offsetOverride를 주면 앵커 중심 기준 그 위치에 놓는다(기본: 오른쪽 모서리 + 간격).
        /// 띄우는 즉시 화면 입력이 막히고, Detach(또는 파괴) 시 풀린다.</summary>
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

            // 스피너가 클릭을 막지 않게(입력 차단은 아래 블로커가 전담)
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            var spinner = go.GetComponent<UiLoadingSpinner>();
            if (spinner != null)
            {
                s_Active.Add(spinner);
                spinner.m_HoldsBlock = AcquireBlocker(anchor);
            }
            return spinner;
        }

        /// <summary>스피너 제거. 인스턴스가 이미 없어도 안전.</summary>
        public void Detach()
        {
            if (this != null && gameObject != null)
                Destroy(gameObject);   // 입력 차단 해제는 OnDestroy가 맡는다
        }

        // 어떤 경로로 사라지든(Detach·부모 파괴·씬 언로드) 차단이 풀리도록 파괴 시점에 반납한다.
        private void OnDestroy()
        {
            s_Active.Remove(this);
            if (!m_HoldsBlock)
                return;
            m_HoldsBlock = false;
            ReleaseBlocker();
        }

        // 루트 캔버스 최상단에 투명 이미지 1장을 깔아 모든 클릭을 흡수한다.
        // (알파 0이어도 raycastTarget이 켜져 있으면 레이캐스트는 막힌다)
        private static bool AcquireBlocker(RectTransform anchor)
        {
            var canvas = anchor.GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            if (canvas == null)
                return false;   // 캔버스 밖이면 막을 대상도 없다

            if (s_Blocker == null)
            {
                s_Blocker = new GameObject("@LoadingInputBlocker", typeof(RectTransform), typeof(Image));
                var img = s_Blocker.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = true;
                s_BlockerRefs = 0;   // 씬 전환으로 이전 블로커가 사라진 경우 카운트도 새로 센다
            }

            var brt = (RectTransform)s_Blocker.transform;
            brt.SetParent(canvas.transform, false);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            brt.SetAsLastSibling();   // 그리기 순서 = 레이캐스트 우선순위. 항상 모든 UI 위에.
            s_Blocker.SetActive(true);

            if (s_BlockerRefs == 0)
            {
                s_BlockStartedAt = Time.unscaledTime;   // 첫 스피너부터 대기 시간을 잰다
                s_TimeoutFired = false;
            }
            s_BlockerRefs++;
            return true;
        }

        private static void ReleaseBlocker()
        {
            s_BlockerRefs = Mathf.Max(0, s_BlockerRefs - 1);
            if (s_BlockerRefs == 0 && s_Blocker != null)
                s_Blocker.SetActive(false);
        }
    }
}
