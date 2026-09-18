using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 상단 중앙 대화창. 클릭(대화창·어두운 배경 어디든) 또는 Space로 다음 줄, 하단 ◀ ▶(또는 ←/→ 키)로 지난 줄을 다시 본다.
/// 아직 안 읽은 줄이 남은 동안은 조작을 잠그고(GameplayInputBlocker.DialogueBlocked) 화면을 어둡게 해 읽게 만든다 —
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
    private int m_SeenIndex;                      // 지금까지 본 가장 뒤 줄 — 뒤로 돌아가 다시 읽어도 잠금이 되살아나지 않게
    private Action m_OnAllDone;
    private GameObject m_AdvanceHint;             // 프리팹의 "클릭 또는 Space로 다음" — 넘길 줄이 있을 때만 보인다
    private TextMeshProUGUI m_AdvanceHintText;
    private GameObject m_Dimmer;
    private GameObject m_NavRoot;
    private Button m_PrevButton, m_NextButton;
    private TextMeshProUGUI m_PageLabel;

    private int LastIndex => m_Lines != null ? m_Lines.Count - 1 : -1;
    // 안 읽은 줄이 남았거나(퀘스트 안내 전), 마지막 줄 뒤에 콜백이 있으면(인트로·아웃트로) 조작을 잠근다.
    private bool Blocking => m_Lines != null && (m_SeenIndex < LastIndex || m_OnAllDone != null);
    private bool CanAdvance => m_Lines != null && (m_LineIndex < LastIndex || m_OnAllDone != null);

    // 읽지 않고 연타로 넘기는 유저가 많아(QA) 새 줄이 뜨면 잠깐 넘김을 막는다. 대화가 새로 열릴 땐 더 길게 —
    // 게임 중이던 클릭(배치·집기)이 방금 뜬 대화를 그대로 넘겨버리는 것도 같이 막는다. 이미 본 줄을 다시 넘길 땐 안 막는다.
    private const float kOpenLockSeconds = 2f;
    private const float kLineLockSeconds = 0.7f;
    private float m_AdvanceLockUntil;
    private bool AdvanceLocked => Time.unscaledTime < m_AdvanceLockUntil;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Buttons));

        BindEvent(gameObject, _ => Advance());
        ApplyStandOutLook();

        var skip = Get<Button>((int)Buttons.SkipButton);
        if (skip != null) skip.onClick.AddListener(() => OnSkipRequested?.Invoke());

        // '목표 : …' 줄이 붙으면서 5줄까지 나온다 — 박스 밖으로 넘치지 않게 자동 축소(최대는 프리팹 크기 그대로) + 하단 [< n/N >] 자리 비움.
        var lineText = Get<TextMeshProUGUI>((int)Texts.Line);
        if (lineText != null)
        {
            lineText.fontSizeMax = lineText.fontSize;
            lineText.fontSizeMin = 16f;
            lineText.enableAutoSizing = true;
            lineText.margin = new Vector4(16f, 8f, 16f, 40f);
        }

        var hint = transform.Find("AdvanceHint");
        if (hint != null)
        {
            m_AdvanceHint = hint.gameObject;
            m_AdvanceHintText = hint.GetComponent<TextMeshProUGUI>();
            // 모바일엔 Space가 없다
            if (m_AdvanceHintText != null && MobileControlsHUD.ShouldUseMobileUI) m_AdvanceHintText.text = "터치해서 다음";
        }

        BuildDimmer();
        BuildNav();
        gameObject.SetActive(false);
    }

    // 대화창이 눈에 안 띈다는 피드백 — ① 좌상단 조작법 패널(화면 폭의 ~34%까지)과 겹치던 것을 오른쪽으로 비켜 세우고
    // (겹치면 조작법의 접기 탭까지 대화창이 가려 못 눌렀다) ② 배경을 더 불투명하게 + 노란 테두리를 두른다.
    // 프리팹은 기획자 손수정본이라 값만 코드로 덮는다(새 박스 디자인이 오면 프리팹으로 옮길 것).
    private void ApplyStandOutLook()
    {
        if (!MobileControlsHUD.ShouldUseMobileUI)   // 모바일엔 좌상단 조작법 패널이 없다 — 가운데 그대로
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0.35f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(0.79f, rt.anchorMax.y);
        }

        var bg = GetComponent<Image>();
        if (bg == null) return;
        var c = bg.color; c.a = 0.95f; bg.color = c;
        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.30f, 1f);   // 퀘스트 머리말([퀘스트 n/12])과 같은 노랑
        outline.effectDistance = new Vector2(4f, -4f);
        outline.useGraphicAlpha = false;
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

    // 대화창 하단 중앙의 [<  2/3  >] — 놓친 줄을 다시 볼 수 있게. 줄이 하나뿐이면 숨긴다.
    // (화살표 글리프 ◀▶는 SUITE 폰트에 없을 수 있어 ASCII 꺾쇠를 쓴다. 디자인 박스가 오면 프리팹 노드로 옮길 것.)
    private void BuildNav()
    {
        var line = Get<TextMeshProUGUI>((int)Texts.Line);
        var font = line != null ? line.font : null;

        m_NavRoot = new GameObject("Nav", typeof(RectTransform));
        m_NavRoot.transform.SetParent(transform, false);
        var rt = (RectTransform)m_NavRoot.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 6f);
        rt.sizeDelta = new Vector2(200f, 32f);

        m_PrevButton = NavButton("PrevButton", "<", -72f, font, Back);
        m_NextButton = NavButton("NextButton", ">", 72f, font, Advance);
        m_PageLabel = NavText("PageLabel", "", 0f, 80f, 18f, font);
        m_PageLabel.raycastTarget = false;
    }

    private Button NavButton(string name, string glyph, float x, TMP_FontAsset font, Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(m_NavRoot.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(48f, 32f);
        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);   // 건너뛰기 버튼과 같은 톤
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var label = NavText("Label", glyph, 0f, 48f, 24f, font);
        label.transform.SetParent(go.transform, false);
        label.rectTransform.anchoredPosition = Vector2.zero;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        return btn;
    }

    private TextMeshProUGUI NavText(string name, string text, float x, float width, float size, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(m_NavRoot.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(width, 32f);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;   // 한글 글리프 있는 폰트 계승
        t.richText = false;                // "<"가 태그로 해석되지 않게
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && m_Lines != null)
        {
            if (kb.leftArrowKey.wasPressedThisFrame) Back();
            if (kb.rightArrowKey.wasPressedThisFrame) Advance();
        }

        if (!Blocking) return;

        // 힌트를 은은하게 깜빡여 시선 유도
        if (m_AdvanceHintText != null)
        {
            float a = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
            m_AdvanceHintText.color = new Color(1f, 1f, 1f, a);
        }

        if (kb != null && kb.spaceKey.wasPressedThisFrame) Advance();
    }

    // 잠금 상태 반영은 LateUpdate에서 — 마지막 줄로 넘긴 Space 입력이 같은 프레임에 점프로 새지 않게.
    private void LateUpdate()
    {
        bool blocking = Blocking;
        GameplayInputBlocker.DialogueBlocked = blocking;
        if (m_AdvanceHint != null) m_AdvanceHint.SetActive(CanAdvance && !AdvanceLocked);   // 힌트가 뜨면 = 이제 넘겨도 된다
        if (m_NextButton != null) m_NextButton.interactable = CanAdvance && !AdvanceLocked;
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
        m_SeenIndex = 0;
        m_OnAllDone = onAllDone;
        m_AdvanceLockUntil = Time.unscaledTime + kOpenLockSeconds;
        gameObject.SetActive(true);
        ShowCurrentLine();
    }

    private void ShowCurrentLine()
    {
        if (m_Lines == null || m_LineIndex > LastIndex) return;
        var txt = Get<TextMeshProUGUI>((int)Texts.Line);
        if (txt != null) txt.text = m_Lines[m_LineIndex];

        if (m_NavRoot == null) return;
        m_NavRoot.SetActive(m_Lines.Count > 1);
        m_PageLabel.text = $"{m_LineIndex + 1} / {m_Lines.Count}";
        m_PrevButton.interactable = m_LineIndex > 0;
        m_NextButton.interactable = CanAdvance;
    }

    private void Back()
    {
        if (m_Lines == null || m_LineIndex <= 0) return;
        m_LineIndex--;
        ShowCurrentLine();
    }

    private void Advance()
    {
        if (m_Lines == null) return;
        if (m_LineIndex >= m_SeenIndex && AdvanceLocked) return;   // 새 줄은 잠깐 못 넘긴다(다시 읽는 중인 줄은 자유)
        if (m_LineIndex < LastIndex)
        {
            m_LineIndex++;
            if (m_LineIndex > m_SeenIndex)
            {
                m_SeenIndex = m_LineIndex;
                m_AdvanceLockUntil = Time.unscaledTime + kLineLockSeconds;
            }
            ShowCurrentLine();
            return;
        }

        // 마지막 줄: 콜백이 있으면(인트로·아웃트로) 끝내고, 없으면(퀘스트 안내) 그대로 남는다 — 지난 줄은 ◀로 다시 볼 수 있다.
        if (m_OnAllDone == null) return;
        var done = m_OnAllDone;
        m_Lines = null;
        m_OnAllDone = null;
        done?.Invoke();
    }
}
