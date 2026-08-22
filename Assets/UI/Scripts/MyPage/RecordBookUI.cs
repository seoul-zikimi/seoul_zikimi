using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GridSystem;

/// <summary>
/// 플레이 기록 책(팝업) — 피그마 새로5 리디자인. 각 상태의 원본 화면을 통째로 배경으로 쓰고
/// 동적 데이터(썸네일·이름·기록 수치)만 코드가 덧그린다. 탭/버튼 = 구운 그림 위 투명 핫스팟.
/// 뷰 3개: 목록(맵 카드) / 상세(타임어택·2VS2 기록) / 자유 모드(스크린샷 — 기능 준비 중).
/// UIManager.ShowPopupUI&lt;RecordBookUI&gt;() 로 표시. 프리팹 생성: Jobsnail ▸ UI ▸ Generate RecordBook Prefab.
/// </summary>
public class RecordBookUI : UIPopup
{
    private enum View { List, Detail, Free }

    private const int kSlotCount = 5;

    private View m_View = View.List;
    private int m_MapIdx;
    private readonly System.Collections.Generic.List<MapDef> m_Maps = new();

    private GameObject m_ListView, m_DetailView, m_FreeView;

    public override void Init()
    {
        m_ListView = transform.Find("Cover/ListView")?.gameObject;
        m_DetailView = transform.Find("Cover/DetailView")?.gameObject;
        m_FreeView = transform.Find("Cover/FreeView")?.gameObject;

        WireBtn("Cover/CloseButton", () => UIManager.Instance.ClosePopupUI(this));
        WireBtn("Cover/TabRecord", () => Show(View.List));
        WireBtn("Cover/TabFree", () => Show(View.Free));
        WireBtn("Cover/ListView/FreeBanner", () => Show(View.Free));
        WireBtn("Cover/DetailView/BackToList", () => Show(View.List));
        WireBtn("Cover/DetailView/NextMapButton", () => StepMap(1));

        CollectMaps();
        for (int i = 0; i < kSlotCount; i++)
        {
            int idx = i;
            WireBtn($"Cover/ListView/MapSlot{i}", () => OpenDetail(idx));
        }

        JuicyButton.AttachAll(gameObject);
        Show(View.List);
    }

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) UIManager.Instance.ClosePopupUI(this);
    }

    private void WireBtn(string path, UnityEngine.Events.UnityAction action)
    {
        var b = transform.Find(path)?.GetComponent<Button>();
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); });
        b.onClick.AddListener(action);
    }

    // 기록 대상 맵: 대전용 공터/튜토리얼 제외
    private void CollectMaps()
    {
        m_Maps.Clear();
        var catalog = MapCatalog.Instance;
        if (catalog == null) return;
        for (int i = 0; i < catalog.Count; i++)
        {
            var def = catalog.Get(i);
            if (def == null || def.IsVersusArena) continue;
            if (def.DisplayName.Contains("튜토리얼")) continue;
            m_Maps.Add(def);
        }
    }

    private void Show(View v)
    {
        m_View = v;
        if (m_ListView != null) m_ListView.SetActive(v == View.List);
        if (m_DetailView != null) m_DetailView.SetActive(v == View.Detail);
        if (m_FreeView != null) m_FreeView.SetActive(v == View.Free);
        RefreshTabs();
        if (v == View.List) RefreshList();
        if (v == View.Detail) RefreshDetail();
    }

    // 옆 탭: 활성 = 보라 탭 + 밝은 글씨, 비활성 = 크림 탭 + 잉크 글씨
    private void RefreshTabs()
    {
        var purple = Resources.Load<Sprite>("UI_pngs/MyPage/Book_TabPurple");
        var cream = Resources.Load<Sprite>("UI_pngs/MyPage/Book_TabCream");
        SetTab("Cover/TabRecord", m_View != View.Free, purple, cream);
        SetTab("Cover/TabFree", m_View == View.Free, purple, cream);
    }

    private void SetTab(string path, bool on, Sprite purple, Sprite cream)
    {
        var t = transform.Find(path);
        if (t == null) return;
        var img = t.GetComponent<Image>();
        if (img != null) img.sprite = on ? purple : cream;
        var lbl = t.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (lbl != null) lbl.color = on ? new Color32(0xF2, 0xE7, 0xC8, 255) : new Color32(0x3E, 0x33, 0x2A, 255);
    }

    private void OpenDetail(int idx)
    {
        if (idx >= m_Maps.Count) return;   // 빈 슬롯(추가 예정)
        m_MapIdx = idx;
        Show(View.Detail);
    }

    private void StepMap(int dir)
    {
        if (m_Maps.Count == 0) return;
        m_MapIdx = ((m_MapIdx + dir) % m_Maps.Count + m_Maps.Count) % m_Maps.Count;
        RefreshDetail();
    }

    // ── 목록 ─────────────────────────────────────────────────────────
    private void RefreshList()
    {
        for (int i = 0; i < kSlotCount; i++)
        {
            var slot = transform.Find($"Cover/ListView/MapSlot{i}");
            if (slot == null) continue;
            bool has = i < m_Maps.Count;
            var def = has ? m_Maps[i] : null;

            SetImage(slot, "Thumb", has ? def.Thumbnail : null);
            SetActive(slot, "NamePill", has);
            SetActive(slot, "TrophyIcon", has);
            SetActive(slot, "Lock", !has);
            SetActive(slot, "Soon", !has);
            SetText(slot, "NamePill/Name", has ? def.DisplayName : "");
            SetText(slot, "Pct", has ? $"완성도 {BestPct(def)}%" : "");
            var btn = slot.GetComponent<Button>();
            if (btn != null) btn.interactable = has;
        }
    }

    // ── 상세 ─────────────────────────────────────────────────────────
    private void RefreshDetail()
    {
        if (m_MapIdx >= m_Maps.Count) { Show(View.List); return; }
        var def = m_Maps[m_MapIdx];
        var d = transform.Find("Cover/DetailView");
        if (d == null) return;

        SetImage(d, "BigCard/BookThumb", def.Thumbnail);
        SetText(d, "BookMapName", def.DisplayName);
        SetText(d, "MapDesc", "");   // 맵 설명 데이터가 생기면 연결

        for (int p = 1; p <= 4; p++)
        {
            if (TryBest(def, p, out int pct, out float sec))
            {
                int s = Mathf.RoundToInt(sec);
                SetText(d, $"TaPct{p - 1}", $"{pct}%");
                SetText(d, $"TaTime{p - 1}", $"{s / 60}분 {s % 60:00}초");
            }
            else
            {
                SetText(d, $"TaPct{p - 1}", "-");
                SetText(d, $"TaTime{p - 1}", "");
            }
        }

        SaveService.GetVersus(def.DisplayName, out int w, out int l);
        SetText(d, "VsItem", $"{w}승 {l}패");
    }

    // ── 데이터 ───────────────────────────────────────────────────────
    /// <summary>맵 완성도 = 모든 인원수·정답 중 최고 완성도(없으면 0).</summary>
    private static int BestPct(MapDef def)
    {
        int best = 0;
        for (int p = 1; p <= 4; p++)
            if (TryBest(def, p, out int pct, out _) && pct > best) best = pct;
        return best;
    }

    // 저장 키는 '정답 구조물 이름' 단위 → 맵의 정답들 전체에서 최고(완성도 우선, 동률 시 시간).
    private static bool TryBest(MapDef def, int players, out int bestPct, out float bestSec)
    {
        int pct0 = -1; float sec0 = 0f;
        void Consider(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!SaveService.TryGetBest(key, players, out int pct, out float sec)) return;
            if (pct > pct0 || (pct == pct0 && sec < sec0)) { pct0 = pct; sec0 = sec; }
        }
        if (def != null)
        {
            Consider(def.DisplayName);
            if (def.Answers != null)
                foreach (var a in def.Answers) if (a != null) Consider(a.DisplayName);
        }
        bestPct = pct0; bestSec = sec0;
        return pct0 >= 0;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    private static void SetText(Transform root, string path, string text)
    {
        var t = root.Find(path)?.GetComponent<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    private static void SetImage(Transform root, string path, Sprite sprite)
    {
        var img = root.Find(path)?.GetComponent<Image>();
        if (img == null) return;
        img.sprite = sprite;
        img.enabled = sprite != null;
    }

    private static void SetActive(Transform root, string path, bool on)
    {
        var t = root.Find(path);
        if (t != null) t.gameObject.SetActive(on);
    }
}
