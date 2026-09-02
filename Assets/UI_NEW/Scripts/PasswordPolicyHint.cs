using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    /// <summary>
    /// 비밀번호 입력창에서 허용 밖 문자가 걸러졌을 때 입력창 바로 아래 잠깐 나타나는 안내 라벨.
    /// "왜 입력이 안 되지?"를 없애는 용도 — 문구는 SessionPasswordPolicy.HintText 한 곳에서 온다.
    /// 프리팹 수정 없이 입력창에 런타임 부착(방 만들기·비번 입장 팝업 공용).
    /// </summary>
    internal sealed class PasswordPolicyHint : MonoBehaviour
    {
        private const float kShowSeconds = 2.5f;
        private Text m_Label;
        private Coroutine m_HideCo;

        public static PasswordPolicyHint Attach(InputField input)
        {
            if (input == null)
                return null;
            var existing = input.GetComponentInChildren<PasswordPolicyHint>(true);
            if (existing != null)
                return existing;

            var go = new GameObject("PasswordPolicyHint", typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(input.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);          // 입력창 하단에 붙여
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(260f, 30f);       // 입력창보다 좌우로 넓게(문구가 길다)

            var hint = go.AddComponent<PasswordPolicyHint>();
            hint.m_Label = go.AddComponent<Text>();
            hint.m_Label.font = JobsnailUiKit.LegacyFont;
            hint.m_Label.fontSize = 19;
            hint.m_Label.alignment = TextAnchor.UpperCenter;
            hint.m_Label.color = new Color(0.85f, 0.35f, 0.25f, 1f);   // 부드러운 경고 톤
            hint.m_Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.m_Label.raycastTarget = false;
            hint.m_Label.text = SessionPasswordPolicy.HintText;
            go.SetActive(false);
            return hint;
        }

        /// <summary>안내를 표시하고 잠시 뒤 자동으로 숨긴다(연속 호출 시 타이머 연장).</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            if (m_HideCo != null)
                StopCoroutine(m_HideCo);
            m_HideCo = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSecondsRealtime(kShowSeconds);
            gameObject.SetActive(false);
            m_HideCo = null;
        }
    }
}
