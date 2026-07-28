using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 마이페이지 씬의 옷장 HUD(기획 07/27) — 왼쪽엔 씬의 3D 캐릭터, 오른쪽 패널에 커스터마이징 UI.
/// - 카테고리(전체/캐릭터/스킨/모자/옷/가방/등껍질) + 보유 목록 + 적용/되돌리기.
///   아이템 3D/상점은 미구현이라 틀만(아이템 id 접두사 컨벤션으로 확장).
/// - '기록' 버튼 = 플레이 기록 책 팝업(RecordBookUI).
/// UIManager.ShowHUDUI&lt;MyPageUI&gt;() 로 표시. 프리팹 생성: Jobsnail ▸ UI ▸ Generate MyPage Prefab.
/// </summary>
public class MyPageUI : UIHUD
{
    private enum Texts { CoinText, ClosetList }
    private enum Btns { BookButton, ApplyButton, RevertButton, CloseButton }

    // 아이템 id 접두사 = 카테고리 컨벤션 (상점/아이템 생기면 이 규칙으로 등록)
    private string m_Filter = "";

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Btns));

        Wire(Btns.BookButton, () => UIManager.Instance.ShowPopupUI<RecordBookUI>());   // 책 = 팝업
        Wire(Btns.ApplyButton, () => SetClosetList("적용할 아이템이 아직 없어요. (상점 준비 중)"));
        Wire(Btns.RevertButton, RefreshCloset);
        Wire(Btns.CloseButton, Close);

        JuicyButton.AttachAll(gameObject);
        RefreshCloset();
    }

    private void OnEnable() => RefreshCloset();

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    private void Close()
    {
        UIManager.Instance.HideHUDUI<MyPageUI>();
        MyPageSceneController.ReturnToMain();   // 마이페이지 = 전용 씬 → 닫기 = 메인 복귀
    }

    private void Wire(Btns which, UnityEngine.Events.UnityAction action)
    {
        var b = Get<Button>((int)which);
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); });
        b.onClick.AddListener(action);
    }

    /// <summary>카테고리 탭이 호출(프리팹의 카테고리 버튼 onClick에 연결됨). prefix 예: hat_, cloth_.</summary>
    public void SetFilter(string prefix) { m_Filter = prefix ?? ""; RefreshCloset(); }

    private void RefreshCloset()
    {
        var coin = Get<TextMeshProUGUI>((int)Texts.CoinText);
        if (coin != null) coin.text = $"보유 코인  {SaveService.Coins}";

        var sb = new StringBuilder();
        int n = 0;
        foreach (var id in SaveService.Skins)
            if (id.StartsWith(m_Filter)) { sb.AppendLine($"- {id}  (스킨)"); n++; }
        foreach (var id in SaveService.CodiItems)
            if (id.StartsWith(m_Filter)) { sb.AppendLine($"- {id}"); n++; }

        SetClosetList(n > 0 ? sb.ToString() : "보유한 아이템이 없어요.\n게임에서 코인을 모아보세요! (상점 준비 중)");
    }

    private void SetClosetList(string msg)
    {
        var t = Get<TextMeshProUGUI>((int)Texts.ClosetList);
        if (t != null) t.text = msg;
    }
}
