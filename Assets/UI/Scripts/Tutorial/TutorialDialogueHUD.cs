using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 상단 중앙 대화창. 텍스트박스를 클릭하거나 스페이스바를 누르면 다음 줄로 넘어간다.
/// (기획서는 "클릭 또는 Enter"였으나, Enter는 GameLoopManager에서 이미 전역적으로 "건축 종료 동의"
/// 토글에 쓰이고 있어 혼자 플레이 세션이 즉시 종료돼버리는 충돌이 있음 — 대신 스페이스바를 지원한다.)
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

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Buttons));

        BindEvent(gameObject, _ => Advance());

        var skip = Get<Button>((int)Buttons.SkipButton);
        if (skip != null) skip.onClick.AddListener(() => OnSkipRequested?.Invoke());

        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            Advance();
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
