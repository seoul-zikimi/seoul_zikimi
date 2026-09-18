using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 상단 중앙 대화창. 클릭(대화창·어두운 배경 어디든) 또는 Space로 다음 줄로 넘어간다.
/// 읽을 줄이 남은 동안은 조작을 잠그고(GameplayInputBlocker.DialogueBlocked) 화면을 어둡게 해 읽게 만든다 —
/// 움직이느라 대화를 안 읽는 유저가 많았다. 마지막 줄(퀘스트 안내)이 뜨면 잠금이 풀리고 그 줄은 화면에 남는다.
/// (Space 넘김은 한때 점프·비계(스페이스 2연타)와 겹쳐 뺐었지만(QA 09/01), 이제 대화 중엔 조작이 잠겨 있어 안 겹친다.
///  Enter는 전역 "건축 종료 동의" 토글과 충돌해 쓰지 않는다.)
/// 프리팹: Assets/Resources/UI/HUD/TutorialDialogueHUD.prefab.
/// </summary>
public class TutorialDialogueHUD : UIHUD
{
    private enum Texts { Line }
    private enum Buttons { SkipButton }

    public event Action OnSkipRequested;

    private IReadOnlyList<string> m_Lines;
    private int m_LineIndex;
    private Action m_OnAllDone;
    private GameObject m_AdvanceHint;            // 프리팹의 "클릭 또는 Space로 다음" — 잠금 중에만 보인다
    private TextMeshProUGUI m_AdvanceHintText;
    private GameObject m_Dimmer;

    // 읽을 줄이 남았거나(퀘스트 안내 전), 마지막 줄 뒤에 콜백이 있으면(인트로·아웃트로) 조작을 잠근다.
    private bool Blocking => m_Lines != null && (m_LineIndex < m_Lines.Count - 1 || m_OnAllDone != null);

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Buttons));

        BindEvent(gameObject, _ => Advance());

        var skip = Get<Button>((int)Buttons.SkipButton);
        if (skip != null) skip.onClick.AddListener(() => OnSkipRequested?.Invoke());

        var hint = transform.Find("AdvanceHint");
        if (hint != null)
        {
            m_AdvanceHint = hint.gameObject;
            m_AdvanceHintText = hint.GetComponent<TextMeshProUGUI>();
        }

        BuildDimmer();
        gameObject.SetActive(false);
    }

    // 대화 잠금 중 화면 전체를 살짝 어둡게 — "지금은 읽는 시간"이 한눈에 보이고, 아무 데나 클릭해도 넘어간다.
    // 프리팹은 기획자 손수정본이라 건드리지 않고 코드로 덧붙인다. 대화창 바로 뒤(같은 HUD 루트의 앞 형제)에 깐다.
    private void BuildDimmer()
    {
        m_Dimmer = new GameObject("TutorialDialogueDimmer", typeof(RectTransform), typeof(Image));
        m_Dimmer.transform.SetParent(transform.parent, false);
        m_Dimmer.transform.SetSiblingIndex(transform.GetSiblingIndex());
        var rt = (RectTransform)m_Dimmer.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        m_Dimmer.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        BindEvent(m_Dimmer, _ => Advance());
        m_Dimmer.SetActive(false);
    }

    private void Update()
    {
        if (!Blocking) return;

        // 힌트를 은은하게 깜빡여 시선 유도
        if (m_AdvanceHintText != null)
        {
            float a = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
            m_AdvanceHintText.color = new Color(1f, 1f, 1f, a);
        }

        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) Advance();
    }

    // 잠금 상태 반영은 LateUpdate에서 — 마지막 줄로 넘긴 Space 입력이 같은 프레임에 점프로 새지 않게.
    private void LateUpdate()
    {
        bool blocking = Blocking;
        GameplayInputBlocker.DialogueBlocked = blocking;
        if (m_AdvanceHint != null) m_AdvanceHint.SetActive(blocking);
        if (m_Dimmer != null) m_Dimmer.SetActive(blocking);
    }

    private void OnDisable()
    {
        GameplayInputBlocker.DialogueBlocked = false;
        if (m_Dimmer != null) m_Dimmer.SetActive(false);
    }

    public void ShowLines(IReadOnlyList<string> lines, Action onAllDone)
    {
        m_Lines = lines;
        m_LineIndex = 0;
        m_OnAllDone = onAllDone;
        gameObject.SetActive(true);
        ShowCurrentLine();
    }

    private void ShowCurrentLine()
    {
        if (m_Lines == null || m_LineIndex >= m_Lines.Count) return;
        var txt = Get<TextMeshProUGUI>((int)Texts.Line);
        if (txt != null) txt.text = m_Lines[m_LineIndex];
    }

    private void Advance()
    {
        if (m_Lines == null) return;
        m_LineIndex++;
        if (m_LineIndex >= m_Lines.Count)
        {
            var done = m_OnAllDone;
            m_Lines = null;
            m_OnAllDone = null;
            done?.Invoke();
            return;
        }
        ShowCurrentLine();
    }
}
