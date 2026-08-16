using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GridSystem;

/// <summary>
/// 플레이 기록 책(팝업) — 옷장(마이페이지 씬)에서 '기록' 버튼으로 연다.
/// MapCatalog의 맵마다 한 페이지: 타임어택 1~4인 최고기록(완성도 우선·시간 차순), 미플레이 "-".
/// 2VS2 승패는 모드 미구현이라 자리만. 기록 키는 정답명 기준이라 맵의 정답 목록 전체에서 최고를 합산 표시.
/// UIManager.ShowPopupUI&lt;RecordBookUI&gt;() 로 표시. 프리팹 생성: Jobsnail ▸ UI ▸ Generate MyPage Prefab.
/// </summary>
public class RecordBookUI : UIPopup
{
    private enum Texts { BookMapName, BookRecords, BookVersus, PageLabel }
    private enum Btns { PrevMapButton, NextMapButton, CloseButton }
    private enum Imgs { BookThumb }

    private int m_Page;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Btns));
        Bind<Image>(typeof(Imgs));

        Wire(Btns.PrevMapButton, () => StepPage(-1));
        Wire(Btns.NextMapButton, () => StepPage(1));
        Wire(Btns.CloseButton, () => UIManager.Instance.ClosePopupUI(this));

        JuicyButton.AttachAll(gameObject);
        RefreshBook();
    }

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) UIManager.Instance.ClosePopupUI(this);
    }

    private void Wire(Btns which, UnityEngine.Events.UnityAction action)
    {
        var b = Get<Button>((int)which);
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); });
        b.onClick.AddListener(action);
    }

    private void StepPage(int dir)
    {
        int count = MapCatalog.Instance != null ? Mathf.Max(1, MapCatalog.Instance.Count) : 1;
        m_Page = ((m_Page + dir) % count + count) % count;
        RefreshBook();
    }

    private void RefreshBook()
    {
        var catalog = MapCatalog.Instance;
        int count = catalog != null ? catalog.Count : 0;
        var def = catalog != null ? catalog.Get(m_Page) : null;

        var name = Get<TextMeshProUGUI>((int)Texts.BookMapName);
        if (name != null) name.text = def != null ? def.DisplayName : "맵 없음";

        var page = Get<TextMeshProUGUI>((int)Texts.PageLabel);
        if (page != null) page.text = count > 0 ? $"{m_Page + 1} / {count}" : "";

        var thumb = Get<Image>((int)Imgs.BookThumb);
        if (thumb != null)
        {
            thumb.sprite = def != null ? def.Thumbnail : null;
            thumb.color = thumb.sprite != null ? Color.white : new Color(0.9f, 0.87f, 0.78f, 1f);
        }

        var rec = Get<TextMeshProUGUI>((int)Texts.BookRecords);
        if (rec != null)
        {
            var sb = new StringBuilder("— 타임어택 최고 기록 —\n");
            for (int p = 1; p <= 4; p++)
                sb.AppendLine($"{p}인    {BestAcrossAnswers(def, p)}");
            rec.text = sb.ToString();
        }

        var vs = Get<TextMeshProUGUI>((int)Texts.BookVersus);
        if (vs != null) vs.text = "— 2 VS 2 —\n(모드 준비 중)";
    }

    // 저장 키는 '정답 구조물 이름' 단위 → 이 맵의 정답들 전체에서 최고를 합산(완성도 우선, 동률 시 시간).
    private static string BestAcrossAnswers(MapDef def, int players)
    {
        int bestPct = -1; float bestSec = 0f;
        void Consider(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!SaveService.TryGetBest(key, players, out int pct, out float sec)) return;
            if (pct > bestPct || (pct == bestPct && sec < bestSec)) { bestPct = pct; bestSec = sec; }
        }
        if (def != null)
        {
            Consider(def.DisplayName);
            if (def.Answers != null)
                foreach (var a in def.Answers) if (a != null) Consider(a.DisplayName);
        }
        if (bestPct < 0) return "-";
        int s = Mathf.RoundToInt(bestSec);
        return $"{bestPct}%  {s / 60}분 {s % 60:00}초";
    }
}
