using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 튜토리얼 대사 텍스트박스(화면 상단 중앙). 한 줄씩 표시하고 클릭 또는 엔터로 다음 줄로 넘어간다.
/// 비주얼은 Resources/UI/HUD/TutorialTextBoxHUD 프리팹(UIBase 규칙 — 코드는 바인딩+로직만).
/// 프리팹 생성/수정: Jobsnail ▸ UI ▸ Generate TutorialTextBoxHud Prefab (이후 에디터에서 자유 편집).
/// TutorialManager가 ShowLines(lines, onFinished)로 대사를 넘겨준다.
/// </summary>
public sealed class TutorialTextBoxHUD : UIHUD
{
    private enum GOs { Box }
    private enum Texts { Line }

    private GameObject m_Box;
    private TextMeshProUGUI m_LineText;
    private string[] m_Lines = Array.Empty<string>();
    private int m_Index;
    private Action m_OnFinished;

    public override void Init()
    {
        Bind<GameObject>(typeof(GOs));
        Bind<TextMeshProUGUI>(typeof(Texts));

        m_Box = Get<GameObject>((int)GOs.Box);
        m_LineText = Get<TextMeshProUGUI>((int)Texts.Line);

        if (m_Box != null)
            BindEvent(m_Box, _ => Advance(), "Click");   // 박스 클릭 = 다음 줄

        if (m_Box != null) m_Box.SetActive(false);
    }

    /// <summary>대사 배열을 처음부터 재생. 다 넘기면 onFinished 호출(줄이 없으면 즉시 호출).</summary>
    public void ShowLines(string[] lines, Action onFinished)
    {
        m_Lines = lines ?? Array.Empty<string>();
        m_Index = 0;
        m_OnFinished = onFinished;

        if (m_Lines.Length == 0)
        {
            if (m_Box != null) m_Box.SetActive(false);
            var cb = m_OnFinished; m_OnFinished = null;
            cb?.Invoke();
            return;
        }

        if (m_Box != null) { m_Box.SetActive(false); m_Box.SetActive(true); }   // UiPopIn류가 있다면 재발동
        if (m_LineText != null) m_LineText.text = m_Lines[0];
    }

    private void Update()
    {
        if (m_Box == null || !m_Box.activeSelf) return;
        var kb = Keyboard.current;
        if (kb != null && kb.enterKey.wasPressedThisFrame) Advance();
    }

    private void Advance()
    {
        if (m_Box == null || !m_Box.activeSelf || m_Index >= m_Lines.Length) return;

        m_Index++;
        if (m_Index >= m_Lines.Length)
        {
            m_Box.SetActive(false);
            var cb = m_OnFinished; m_OnFinished = null;
            cb?.Invoke();
            return;
        }
        if (m_LineText != null) m_LineText.text = m_Lines[m_Index];
    }
}
