using System.Text;
using TMPro;

/// <summary>
/// 좌측 "조작 툴팁" HUD — 기존 코드베이스에 대응 요소가 전혀 없던 유일한 HUD.
/// 현재 퀘스트와 관련된 줄을 강조 표시한다. 플레이스홀더 텍스트(추후 실제 UI 이미지로 교체 예정).
/// 프리팹: Assets/Resources/UI/HUD/TutorialTooltipHUD.prefab.
/// </summary>
public class TutorialTooltipHUD : UIHUD
{
    private enum Texts { Body }

    private static readonly LocCache<string[]> s_kLines = new();
    private static string[] kLines => s_kLines.Get(() => new string[]{
        L.T("W / A / S / D : 이동", "W / A / S / D : Move"),
        L.T("Shift : 달리기 / Space : 점프", "Shift : Run / Space : Jump"),
        L.T("마우스 우클릭 드래그 : 카메라 회전 / 스크롤 : 확대·축소", "Right-drag : Rotate camera / Scroll : Zoom"),
        L.T("정답 미리보기 위에서 동일 조작 : 정답 회전", "Same controls over the blueprint : Rotate blueprint"),
        L.T("우측 하단 휴대폰 : 완공 계획도 확인 / 재료 주문", "Phone (bottom right) : Blueprint / Order materials"),
        L.T("좌클릭 : 오브젝트 집기 / 내려놓기", "Left click : Pick up / Put down"),
        L.T("G (꾹 누르기) : 든 물건 던지기", "G (hold) : Throw held item"),
        L.T("R : 든 오브젝트 회전", "R : Rotate held object"),
        L.T("E (꾹 누르기) : 공정(고정 등) / Z (꾹 누르기) : 공정 취소", "E (hold) : Process (fix etc.) / Z (hold) : Undo process"),
        L.T("Space 2번 연타 : 비계 설치", "Double-tap Space : Place scaffolding"),
        L.T("Tab : 정답 표시 토글", "Tab : Toggle blueprint"),
    });

    private TextMeshProUGUI m_Body;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        m_Body = Get<TextMeshProUGUI>((int)Texts.Body);
        SetHighlightedLine(-1);
    }

    public void SetHighlightedLine(int index)
    {
        if (m_Body == null) return;
        var sb = new StringBuilder();
        for (int i = 0; i < kLines.Length; i++)
        {
            if (i == index) sb.Append("<color=#FFD24D><b>").Append(kLines[i]).Append("</b></color>\n");
            else sb.Append(kLines[i]).Append('\n');
        }
        m_Body.text = sb.ToString();
    }
}
