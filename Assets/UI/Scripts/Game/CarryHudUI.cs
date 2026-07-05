using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 조작법/상태 HUD(좌상단) + 대상 블록 위 공정 로딩바/공정 힌트.
/// UIManager가 Resources/UI/HUD/CarryHudUI 프리팹에서 인스턴스화(구 PlayerCarry.OnGUI 대체).
/// 프리팹 생성: Jobsnail ▸ UI ▸ Generate CarryHud Prefab (생성 후 에디터에서 자유 편집 — 코드는 텍스트/위치만 갱신).
/// </summary>
public class CarryHudUI : UIHUD
{
    private enum GOs { HintPanel, ProcessBar, RevertBar, ProcessHintPanel }
    private enum Texts { HintText, ProcessBarLabel, RevertBarLabel, ProcessHintText }
    private enum Images { ProcessBarFill, RevertBarFill }

    public override void Init()
    {
        Bind<GameObject>(typeof(GOs));
        Bind<Text>(typeof(Texts));
        Bind<Image>(typeof(Images));
        SetProcessBar(false);
        SetRevertBar(false);
        SetProcessHint(false);
    }

    /// <summary>좌상단 조작법/상태 텍스트.</summary>
    public void SetHint(string text)
    {
        var t = Get<Text>((int)Texts.HintText);
        if (t != null) t.text = text;
    }

    /// <summary>E 공정 로딩바. screenPos = 스크린 픽셀 좌표(Overlay 캔버스라 그대로 대입).</summary>
    public void SetProcessBar(bool on, Vector2 screenPos = default, float t01 = 0f, Color fill = default, string label = null)
        => SetBar((int)GOs.ProcessBar, (int)Images.ProcessBarFill, (int)Texts.ProcessBarLabel, on, screenPos, t01, fill, label);

    /// <summary>Z 되돌리기 로딩바.</summary>
    public void SetRevertBar(bool on, Vector2 screenPos = default, float t01 = 0f, Color fill = default, string label = null)
        => SetBar((int)GOs.RevertBar, (int)Images.RevertBarFill, (int)Texts.RevertBarLabel, on, screenPos, t01, fill, label);

    /// <summary>도구 조준 시 "지금 무슨 공정 차례" 안내(대상 블록 위).</summary>
    public void SetProcessHint(bool on, Vector2 screenPos = default, string text = null)
    {
        var go = Get<GameObject>((int)GOs.ProcessHintPanel);
        if (go == null) return;
        go.SetActive(on);
        if (!on) return;
        go.transform.position = screenPos;
        var t = Get<Text>((int)Texts.ProcessHintText);
        if (t != null) t.text = text;
    }

    private void SetBar(int goIdx, int fillIdx, int labelIdx, bool on, Vector2 screenPos, float t01, Color fill, string label)
    {
        var go = Get<GameObject>(goIdx);
        if (go == null) return;
        go.SetActive(on);
        if (!on) return;
        go.transform.position = screenPos;
        var img = Get<Image>(fillIdx);
        if (img != null)
        {
            img.color = fill;
            img.rectTransform.localScale = new Vector3(Mathf.Clamp01(t01), 1f, 1f);   // pivot=왼쪽 → 왼→오 채움
        }
        var txt = Get<Text>(labelIdx);
        if (txt != null) txt.text = label ?? "";
    }
}
