using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 상단 중앙 대화창. 텍스트박스를 클릭하면 다음 줄로 넘어간다.
/// (기획서는 "클릭 또는 Enter"였으나 Enter는 전역 "건축 종료 동의" 토글과 충돌.
///  한때 스페이스바 넘김을 지원했지만 점프·비계 설치(스페이스 2연타)와 겹쳐
///  대사가 의도치 않게 넘어가는 문제가 있어 클릭 전용으로 고정 — QA 09/01.)
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
    private TextMeshProUGUI m_ClickHint;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Buttons));

        BindEvent(gameObject, _ => Advance());

        var skip = Get<Button>((int)Buttons.SkipButton);
        if (skip != null) skip.onClick.AddListener(() => OnSkipRequested?.Invoke());

        BuildClickHint();
        gameObject.SetActive(false);
    }

    // 클릭으로 넘긴다는 걸 모르는 유저가 많아(스페이스 제거 후 특히) 우하단에 상시 힌트를 붙인다.
    // 프리팹은 기획자 손수정본이라 건드리지 않고 코드로 덧붙인다.
    private void BuildClickHint()
    {
        var go = new GameObject("ClickHint", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-14f, 8f);
        rt.sizeDelta = new Vector2(240f, 24f);

        m_ClickHint = go.AddComponent<TextMeshProUGUI>();
        var line = Get<TextMeshProUGUI>((int)Texts.Line);
        if (line != null) m_ClickHint.font = line.font;   // 한글 글리프 있는 폰트 계승
        m_ClickHint.text = "클릭해서 다음 ▶";
        m_ClickHint.fontSize = 16f;
        m_ClickHint.alignment = TextAlignmentOptions.BottomRight;
        m_ClickHint.raycastTarget = false;
    }

    private void Update()
    {
        // 힌트를 은은하게 깜빡여 시선 유도(로직 없음 — 넘김은 클릭 전용).
        if (m_ClickHint != null)
        {
            float a = 0.45f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            m_ClickHint.color = new Color(1f, 1f, 1f, a);
        }
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
