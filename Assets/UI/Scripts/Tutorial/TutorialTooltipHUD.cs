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

    private static readonly string[] kLines =
    {
        "W / A / S / D : 이동",
        "Shift : 달리기 / Space : 점프",
        "마우스 우클릭 드래그 : 카메라 회전 / 스크롤 : 확대·축소",
        "정답 미리보기 위에서 동일 조작 : 정답 회전",
        "우측 드로어 화살표 : 재료 주문 UI 열고 닫기",
        "좌클릭 : 오브젝트 집기 / 내려놓기",
        "R : 든 오브젝트 회전",
        "E (꾹 누르기) : 공정(고정 등) / Z (꾹 누르기) : 공정 취소",
        "Space 2번 연타 : 비계 설치",
        "Tab : 정답 표시 토글",
    };

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
