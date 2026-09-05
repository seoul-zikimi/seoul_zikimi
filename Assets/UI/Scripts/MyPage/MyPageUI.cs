using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 마이페이지 씬의 옷장 HUD(피그마 새로4 리디자인) — 왼쪽엔 씬의 3D 캐릭터, 오른쪽 패널에 커스터마이징 UI.
/// - 섹션 구성(피그마): 캐릭터 / 등 껍질 / 옷 / 스킨 — 각 줄 0번 카드 = 현재 모습(체크), 뒤로 아이템.
/// - 탭 = 섹션 필터(전체면 전부 표시). 모자/가방은 아이템 생기면 섹션 추가.
/// - '기록' 버튼 = 플레이 기록 책 팝업(RecordBookUI). 좌우 화살표 = 보유 캐릭터 순환.
/// UIManager.ShowHUDUI&lt;MyPageUI&gt;() 로 표시. 프리팹 생성: Jobsnail ▸ UI ▸ Generate MyPage Prefab.
/// </summary>
public class MyPageUI : UIHUD
{
    private enum Texts { CoinText, ClosetList }
    private enum Btns { BookButton, ApplyButton, RevertButton, CloseButton }

    // 피그마 팔레트
    private static readonly Color kTabOn = new Color32(0xED, 0xE8, 0xF8, 255);
    private static readonly Color kTabOff = new Color32(0xB9, 0xAE, 0xE0, 255);
    private static readonly Color kTextDeep = new Color32(0x4A, 0x3F, 0x66, 255);
    private static readonly Color kLockedTint = new Color(0.65f, 0.65f, 0.7f, 1f);

    private class Slot
    {
        public Button btn;
        public Image card, thumb, lockIcon, checkIcon;
        public TextMeshProUGUI label;
    }

    private class Section
    {
        public string prefix;           // "char_" / "shell_" / "cloth_" / "skin_" / "trail_"
        public int row;                 // 단독 표시 끌어올림용 줄 번호(트레일 클론은 원본 줄 사용)
        public bool soloOnly;           // 전체 탭에선 숨김(패널에 줄이 모자람 — 트레일)
        public GameObject root;
        public readonly System.Collections.Generic.List<Slot> slots = new();
    }

    // 생성기 Sec0..3 순서와 동일(+트레일은 런타임 클론)
    private static readonly string[] kSectionPrefixes = { "char_", "shell_", "cloth_", "skin_" };
    private static readonly string[] kTabPrefixes = { "", "char_", "skin_", "hat_", "cloth_", "bag_", "shell_", "trail_" };

    private string m_Filter = "";
    private readonly System.Collections.Generic.List<Section> m_Sections = new();
    private readonly System.Collections.Generic.List<(string prefix, Image bg, Image icon, TextMeshProUGUI label)> m_Tabs = new();

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Btns));

        Wire(Btns.BookButton, () => UIManager.Instance.ShowPopupUI<RecordBookUI>());   // 책 = 팝업
        Wire(Btns.ApplyButton, () => SetClosetList("아이템을 누르면 바로 착용/해제돼요."));
        Wire(Btns.RevertButton, RefreshCloset);
        Wire(Btns.CloseButton, Close);

        WireExtra("Panel/PanelClose", Close);
        WireExtra("CharPrevButton", () => CycleCharacter(-1));
        WireExtra("CharNextButton", () => CycleCharacter(+1));

        CollectTabs();
        CollectSections();
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

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private void WireExtra(string path, UnityEngine.Events.UnityAction action)
    {
        var t = transform.Find(path);
        if (t == null) t = FindDeep(transform, path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path);
        var b = t != null ? t.GetComponent<Button>() : null;
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); });
        b.onClick.AddListener(action);
    }

    /// <summary>카테고리 탭이 호출(프리팹의 탭 onClick에 연결됨). prefix 예: cloth_.</summary>
    public void SetFilter(string prefix) { m_Filter = prefix ?? ""; RefreshTabs(); RefreshCloset(); }

    // ── 수집 ─────────────────────────────────────────────────────────
    private void CollectTabs()
    {
        m_Tabs.Clear();
        EnsureTrailTab();
        for (int i = 0; i < kTabPrefixes.Length; i++)
        {
            var t = FindDeep(transform, $"Cat{i}");
            if (t == null) break;
            m_Tabs.Add((kTabPrefixes[i], t.GetComponent<Image>(), t.Find("Icon")?.GetComponent<Image>(), t.Find("Label")?.GetComponent<TextMeshProUGUI>()));
        }
        RefreshTabs();
    }

    // 트레일 탭(Cat7)이 프리팹에 없으면 마지막 탭을 복제해 만들고, 탭 줄을 8칸으로 재배치.
    private void EnsureTrailTab()
    {
        if (FindDeep(transform, "Cat7") != null) return;
        var last = FindDeep(transform, "Cat6");
        if (last == null) return;

        var clone = Instantiate(last.gameObject, last.parent);
        clone.name = "Cat7";
        var lbl = clone.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (lbl != null) lbl.text = "트레일";
        var ico = clone.transform.Find("Icon")?.GetComponent<Image>();
        if (ico != null)
        {
            var sp = Resources.Load<Sprite>("UI_pngs/MyPage/Tab_Trail");
            if (sp != null) ico.sprite = sp;
        }
        var btn = clone.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick = new Button.ButtonClickedEvent();   // 복제된 영속 리스너(등껍질) 제거
            btn.onClick.AddListener(() =>
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                SetFilter("trail_");
            });
        }

        // 기존 7칸 줄 폭에 8칸을 균등 배치(크기 7/8로 축소)
        var rects = new System.Collections.Generic.List<RectTransform>();
        for (int i = 0; i <= 7; i++)
        {
            var t = FindDeep(transform, $"Cat{i}");
            if (t != null) rects.Add((RectTransform)t);
        }
        if (rects.Count < 2) return;
        float w = rects[0].sizeDelta.x;
        float left = rects[0].anchoredPosition.x - w * 0.5f;
        float right = rects[rects.Count - 2].anchoredPosition.x + w * 0.5f;   // 마지막(복제 전) 탭 기준
        float span = right - left;
        float cell = span / rects.Count;
        float w2 = Mathf.Min(w, cell - 3f);
        for (int i = 0; i < rects.Count; i++)
        {
            var rt = rects[i];
            rt.sizeDelta = new Vector2(w2, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(left + cell * (i + 0.5f), rt.anchoredPosition.y);
        }
    }

    // 평소 = 흰 배경 + 보라 아이콘/글씨, 선택 = 보라 배경 + 흰 아이콘/글씨
    private void RefreshTabs()
    {
        var purple = new Color32(0x8B, 0x7B, 0xC5, 255);
        foreach (var (prefix, bg, icon, label) in m_Tabs)
        {
            bool on = prefix == m_Filter;
            if (bg != null) bg.color = on ? (Color)purple : Color.white;
            if (icon != null) icon.color = on ? Color.white : (Color)purple;
            if (label != null) label.color = on ? Color.white : (Color)kTextDeep;
        }
    }

    private void CollectSections()
    {
        m_Sections.Clear();
        var lockSprite = Resources.Load<Sprite>("UI_pngs/MyPage/Icon_LockCircle");
        var checkSprite = Resources.Load<Sprite>("UI_pngs/MyPage/Icon_Check");
        for (int s = 0; s < kSectionPrefixes.Length; s++)
        {
            var secTr = FindDeep(transform, $"Sec{s}");
            if (secTr == null) break;
            var sec = new Section { prefix = kSectionPrefixes[s], row = s, root = secTr.gameObject };
            for (int i = 0; ; i++)
            {
                var t = secTr.Find($"Slot{i}");
                if (t == null) break;
                sec.slots.Add(BuildSlot(t, lockSprite, checkSprite));
            }
            m_Sections.Add(sec);
        }

        // 트레일 섹션: Sec3(스킨) 클론 — 전체 탭엔 줄이 모자라 트레일 탭에서만 표시
        var src = FindDeep(transform, "Sec3");
        if (src != null && FindDeep(transform, "Sec4") == null)
        {
            var clone = Instantiate(src.gameObject, src.parent);
            clone.name = "Sec4";
            var header = clone.transform.Find("Header")?.GetComponent<TextMeshProUGUI>();
            if (header != null) header.text = "트레일";

            // 트레일은 11종 — 슬롯 줄을 2줄 더 복제해 15칸(단독 표시라 패널을 다 쓸 수 있음)
            int baseCount = 0;
            while (clone.transform.Find($"Slot{baseCount}") != null) baseCount++;
            for (int r = 1; r <= 2; r++)
                for (int i = 0; i < baseCount; i++)
                {
                    var srcSlot = clone.transform.Find($"Slot{i}");
                    if (srcSlot == null) continue;
                    var s2 = Instantiate(srcSlot.gameObject, clone.transform);
                    s2.name = $"Slot{r * baseCount + i}";
                    var rt2 = (RectTransform)s2.transform;
                    rt2.anchoredPosition = new Vector2(rt2.anchoredPosition.x, rt2.anchoredPosition.y - 156f * r);
                }

            var sec = new Section { prefix = "trail_", row = 3, soloOnly = true, root = clone };
            for (int i = 0; ; i++)
            {
                var t = clone.transform.Find($"Slot{i}");
                if (t == null) break;
                sec.slots.Add(BuildSlot(t, lockSprite, checkSprite));
            }
            m_Sections.Add(sec);
        }
    }

    /// <summary>슬롯 자식(썸네일/자물쇠/체크/라벨) 구성 — 프리팹엔 카드 배경만 있음.</summary>
    private Slot BuildSlot(Transform t, Sprite lockSprite, Sprite checkSprite)
    {
        var slot = new Slot();
        slot.card = t.GetComponent<Image>();
        slot.btn = t.GetComponent<Button>();
        if (slot.btn == null) slot.btn = t.gameObject.AddComponent<Button>();
        if (slot.card != null) { slot.card.raycastTarget = true; slot.btn.targetGraphic = slot.card; }

        var thumbTr = t.Find("ThumbBG");
        if (thumbTr == null)
        {
            // 썸네일 = 카드 위쪽(아래는 이름·가격 자리)
            var rt = JobsnailUiKit.Rect("ThumbBG", t, new Vector2(0.07f, 0.32f), new Vector2(0.93f, 0.95f), Vector2.zero, Vector2.zero);
            rt.SetAsFirstSibling();
            slot.thumb = rt.gameObject.AddComponent<Image>();
            slot.thumb.preserveAspect = true;
            slot.thumb.raycastTarget = false;
        }
        else slot.thumb = thumbTr.GetComponent<Image>();
        slot.thumb.transform.SetSiblingIndex(1);   // 카드 배경 위

        var lockTr = t.Find("LockIcon");
        if (lockTr == null)
        {
            var rt = JobsnailUiKit.Rect("LockIcon", t, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(30, 30));
            slot.lockIcon = rt.gameObject.AddComponent<Image>();
            slot.lockIcon.sprite = lockSprite;
            slot.lockIcon.preserveAspect = true;
            slot.lockIcon.raycastTarget = false;
        }
        else slot.lockIcon = lockTr.GetComponent<Image>();

        var checkTr = t.Find("CheckIcon");
        if (checkTr == null)
        {
            var rt = JobsnailUiKit.Rect("CheckIcon", t, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -18), new Vector2(30, 30));
            slot.checkIcon = rt.gameObject.AddComponent<Image>();
            slot.checkIcon.sprite = checkSprite;
            slot.checkIcon.preserveAspect = true;
            slot.checkIcon.raycastTarget = false;
        }
        else slot.checkIcon = checkTr.GetComponent<Image>();

        var labelTr = t.Find("ItemLabel");
        if (labelTr == null)
        {
            slot.label = JobsnailUiKit.Label("ItemLabel", t, "", 13, kTextDeep, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            slot.label.raycastTarget = false;
        }
        else slot.label = labelTr.GetComponent<TextMeshProUGUI>();
        var lrt = slot.label.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, 21f);
        lrt.sizeDelta = new Vector2(-6f, 40f);
        slot.label.color = kTextDeep;
        slot.label.fontStyle = FontStyles.Bold;
        slot.label.enableAutoSizing = true;
        slot.label.fontSizeMin = 9;
        slot.label.fontSizeMax = 13;
        return slot;
    }

    // ── 갱신 ─────────────────────────────────────────────────────────
    private void RefreshCloset()
    {
        var coin = Get<TextMeshProUGUI>((int)Texts.CoinText);
        if (coin != null) coin.text = $"{SaveService.Coins:N0}";   // 코인 필엔 수치만

        bool anyVisible = false;
        for (int s = 0; s < m_Sections.Count; s++)
        {
            var sec = m_Sections[s];
            bool solo = m_Filter == sec.prefix;
            bool show = solo || (string.IsNullOrEmpty(m_Filter) && !sec.soloOnly);
            sec.root.SetActive(show);
            // 단독 표시면 맨 위 줄로 끌어올림(줄 간격 156, 생성기와 동일)
            var rt = (RectTransform)sec.root.transform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, solo ? 156f * sec.row : 0f);
            if (!show) continue;
            anyVisible = true;
            if (sec.prefix == "char_") FillCharacterSection(sec);
            else if (sec.prefix == "trail_") FillTrailSection(sec);
            else FillOutfitSection(sec);
        }

        SetClosetList(anyVisible ? "" : "이 카테고리엔 아이템이 없어요.");
    }

    // 캐릭터 섹션 — 0번 = 선택 중인 캐릭터(체크), 이후 나머지(카탈로그 순)
    private void FillCharacterSection(Section sec)
    {
        var chars = CharacterCatalog.All;
        string selected = SaveService.EquippedCharacter;

        var order = new System.Collections.Generic.List<CharacterCatalog.Entry>();
        foreach (var e in chars) if (e.Id == selected) order.Add(e);
        foreach (var e in chars) if (e.Id != selected) order.Add(e);

        for (int i = 0; i < sec.slots.Count; i++)
        {
            var slot = sec.slots[i];
            slot.btn.onClick.RemoveAllListeners();
            if (i >= order.Count) { HideSlot(slot); continue; }

            var entry = order[i];
            bool owned = SaveService.HasCharacter(entry.Id) || entry.Price <= 0;
            bool on = selected == entry.Id;
            // 캐릭터마다 능력이 달라 이름 밑에 소개 한 줄. 카드가 좁아 짧은 쪽 문구를 쓰고,
            // 색은 인트로 선택 카드와 같은 초록(#296642)으로 맞춘다. 문구 원본 = CharacterAbility 표.
            string desc = CharacterCatalog.ShortDescription(entry.Id);
            FitCharacterLabel(slot.label, desc != "");
            string caption = desc == "" ? entry.DisplayName
                                        : $"{entry.DisplayName}\n<size=85%><color=#296642>{desc}</color></size>";
            ShowSlot(slot, CharacterCatalog.LoadThumbnail(entry.Id), owned, on,
                owned ? caption : $"{caption}\n{entry.Price:N0}코인");
            string id = entry.Id; string dn = entry.DisplayName; int price = entry.Price;
            slot.btn.onClick.AddListener(() =>
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                OnClickCharacter(id, dn, price);
            });
        }
    }

    /// <summary>캐릭터 카드 라벨은 '이름 + 소개(+가격)' 3줄까지 들어간다 — 그만큼 키우고 더 작게 줄어들게 한다.
    /// 섹션마다 자기 슬롯을 쓰므로 여기서 바꿔도 옷·트레일 카드엔 영향이 없다.</summary>
    private static void FitCharacterLabel(TextMeshProUGUI label, bool hasDesc)
    {
        if (label == null) return;
        label.richText = true;   // 소개는 <size>·<color> 태그로 이름과 구분
        label.rectTransform.anchoredPosition = new Vector2(0f, hasDesc ? 25f : 21f);
        label.rectTransform.sizeDelta = new Vector2(-6f, hasDesc ? 48f : 40f);
        label.fontSizeMin = hasDesc ? 7 : 9;
    }

    // 아웃핏 섹션 — 0번 = 이 카테고리의 현재 상태(착용 중이면 그 아이템, 아니면 '기본'), 이후 아이템
    private void FillOutfitSection(Section sec)
    {
        string charId = SaveService.EquippedCharacter;
        string equipped = SaveService.EquippedOutfit;

        var items = new System.Collections.Generic.List<CodiOutfit>();
        foreach (var o in CodiOutfit.Catalog())
            if (o.name.StartsWith(sec.prefix) && o.TargetCharacter == charId) items.Add(o);

        CodiOutfit wearing = null;
        foreach (var o in items) if (o.name == equipped) { wearing = o; break; }

        for (int i = 0; i < sec.slots.Count; i++)
        {
            var slot = sec.slots[i];
            slot.btn.onClick.RemoveAllListeners();

            if (i == 0)
            {
                // 현재 카드: 착용 중 아이템 또는 기본 모습. 클릭 = 벗기
                if (wearing != null)
                    ShowSlot(slot, wearing.ResolveThumbnail(), true, true, $"{wearing.DisplayName}");
                else
                    // 기본 카드 = 현재 선택한 캐릭터의 맨몸 썸네일
                    ShowSlot(slot, CharacterCatalog.LoadThumbnail(charId), true, true, sec.prefix == "shell_" ? "기본 모양" : "기본");
                slot.btn.onClick.AddListener(() =>
                {
                    if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                    SaveService.EquippedOutfit = "";
                    MyPageSceneController.RefreshEquip();
                    RefreshCloset();
                });
                continue;
            }

            // 이후 카드: 착용 중인 것 제외한 아이템들
            int idx = i - 1;
            var list = new System.Collections.Generic.List<CodiOutfit>();
            foreach (var o in items) if (o != wearing) list.Add(o);
            if (idx >= list.Count) { HideSlot(slot); continue; }

            var item = list[idx];
            bool owned = SaveService.HasCodiItem(item.name) || item.Price <= 0;
            ShowSlot(slot, item.ResolveThumbnail(), owned, false,
                owned ? item.DisplayName : $"해금 조건\n{item.Price:N0}코인");
            var captured = item;
            slot.btn.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); OnClickItem(captured); });
        }
    }

    // 트레일 섹션 — 0번 = 현재 트레일(체크) 또는 '없음', 이후 아이템(코디 저장 재사용, id: trail_*)
    private void FillTrailSection(Section sec)
    {
        string equipped = SaveService.EquippedTrail;

        for (int i = 0; i < sec.slots.Count; i++)
        {
            var slot = sec.slots[i];
            slot.btn.onClick.RemoveAllListeners();

            if (i == 0)
            {
                if (TrailCatalog.TryFind(equipped, out var cur))
                    ShowSlot(slot, TrailCatalog.LoadThumbnail(cur.Id), true, true, cur.DisplayName);
                else
                    ShowSlot(slot, CharacterCatalog.LoadThumbnail(SaveService.EquippedCharacter), true, true, "없음");
                slot.btn.onClick.AddListener(() =>
                {
                    if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                    SaveService.EquippedTrail = "";
                    MyPageSceneController.RefreshEquip();
                    RefreshCloset();
                });
                continue;
            }

            int idx = i - 1;
            var list = new System.Collections.Generic.List<TrailCatalog.Entry>();
            foreach (var e in TrailCatalog.All) if (e.Id != equipped) list.Add(e);
            if (idx >= list.Count) { HideSlot(slot); continue; }

            var item = list[idx];
            bool owned = SaveService.HasCodiItem(item.Id) || item.Price <= 0;
            ShowSlot(slot, TrailCatalog.LoadThumbnail(item.Id), owned, false,
                owned ? item.DisplayName : $"해금 조건\n{item.Price:N0}코인");
            var captured = item;
            slot.btn.onClick.AddListener(() =>
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                OnClickTrail(captured);
            });
        }
    }

    private void OnClickTrail(TrailCatalog.Entry item)
    {
        if (!SaveService.HasCodiItem(item.Id) && item.Price > 0)
        {
            if (SaveService.BuyCodiItem(item.Id, item.Price))
            {
                SetClosetList($"'{item.DisplayName}' 트레일 구매 완료! (-{item.Price}코인)");
                SaveService.EquippedTrail = item.Id;   // 구매 즉시 착용
                MyPageSceneController.RefreshEquip();
            }
            else { SetClosetList("코인이 부족해요."); return; }
        }
        else
        {
            SaveService.EquippedTrail = SaveService.EquippedTrail == item.Id ? "" : item.Id;   // 토글
            MyPageSceneController.RefreshEquip();
        }
        RefreshCloset();
    }

    private static void HideSlot(Slot slot)
    {
        if (slot.card != null) slot.card.color = new Color(1f, 1f, 1f, 0.35f);
        if (slot.thumb != null) slot.thumb.enabled = false;
        if (slot.lockIcon != null) slot.lockIcon.enabled = false;
        if (slot.checkIcon != null) slot.checkIcon.enabled = false;
        if (slot.label != null) slot.label.text = "";
        slot.btn.interactable = false;
    }

    private static void ShowSlot(Slot slot, Sprite thumb, bool owned, bool equipped, string label)
    {
        if (slot.card != null) slot.card.color = Color.white;
        if (slot.thumb != null)
        {
            slot.thumb.enabled = thumb != null;
            slot.thumb.sprite = thumb;
            slot.thumb.color = owned ? Color.white : kLockedTint;
        }
        if (slot.lockIcon != null) slot.lockIcon.enabled = !owned;
        if (slot.checkIcon != null) slot.checkIcon.enabled = equipped && owned;
        if (slot.label != null) slot.label.text = label;
        slot.btn.interactable = true;
    }

    // ── 동작 ─────────────────────────────────────────────────────────
    /// <summary>좌우 화살표 — 보유 캐릭터만 순환 선택.</summary>
    private void CycleCharacter(int dir)
    {
        var chars = CharacterCatalog.All;
        var owned = new System.Collections.Generic.List<string>();
        foreach (var e in chars) if (SaveService.HasCharacter(e.Id) || e.Price <= 0) owned.Add(e.Id);
        if (owned.Count <= 1) { SetClosetList("보유한 다른 캐릭터가 없어요."); return; }
        int cur = owned.IndexOf(SaveService.EquippedCharacter);
        int next = ((cur < 0 ? 0 : cur) + dir + owned.Count) % owned.Count;
        SaveService.EquippedCharacter = owned[next];
        MyPageSceneController.RefreshCharacter();
        RefreshCloset();
    }

    private void OnClickCharacter(string id, string displayName, int price)
    {
        string message = null;
        if (!SaveService.HasCharacter(id) && price > 0)
        {
            if (SaveService.BuyCharacter(id, price))
            {
                message = $"'{displayName}' 영입 완료! (-{price}코인)";
                SaveService.EquippedCharacter = id;   // 영입 즉시 선택
                MyPageSceneController.RefreshCharacter();
            }
            else
            {
                SetClosetList("코인이 부족해요.");
                return;
            }
        }
        else
        {
            SaveService.EquippedCharacter = id;
            MyPageSceneController.RefreshCharacter();
        }
        RefreshCloset();
        if (message != null)
            SetClosetList(message);   // 새로고침이 안내문으로 덮으므로 결과 메시지는 마지막에
    }

    private void OnClickItem(CodiOutfit item)
    {
        string id = item.name;
        if (!SaveService.HasCodiItem(id) && item.Price > 0)
        {
            if (SaveService.BuyCodiItem(id, item.Price))
            {
                SetClosetList($"'{item.DisplayName}' 구매 완료! (-{item.Price}코인)");
                SaveService.EquippedOutfit = id;               // 구매 즉시 착용
                MyPageSceneController.RefreshEquip();
            }
            else SetClosetList("코인이 부족해요.");
        }
        else
        {
            SaveService.EquippedOutfit = SaveService.EquippedOutfit == id ? "" : id;   // 토글
            MyPageSceneController.RefreshEquip();
        }
        RefreshCloset();
    }

    private void SetClosetList(string msg)
    {
        var t = Get<TextMeshProUGUI>((int)Texts.ClosetList);
        if (t != null) t.text = msg;
    }
}
