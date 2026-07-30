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

    // 아이템 id 접두사 = 카테고리 컨벤션 (skin_ 등). 아웃핏 = Resources/CodiOutfits
    private string m_Filter = "";
    private readonly System.Collections.Generic.List<(Button btn, Image thumb, Image lockIcon, TextMeshProUGUI label)> m_Slots = new();

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Button>(typeof(Btns));

        Wire(Btns.BookButton, () => UIManager.Instance.ShowPopupUI<RecordBookUI>());   // 책 = 팝업
        Wire(Btns.ApplyButton, () => SetClosetList("아이템을 누르면 바로 착용/해제돼요."));
        Wire(Btns.RevertButton, RefreshCloset);
        Wire(Btns.CloseButton, Close);

        CollectSlots();
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

    /// <summary>프리팹의 Panel/Slot0..N 을 아이템 칸으로 셋업(자물쇠 아이콘 + 라벨 자식 추가).</summary>
    private void CollectSlots()
    {
        m_Slots.Clear();
        var panel = transform.Find("Panel");
        if (panel == null) return;
        var lockSprite = Resources.Load<Sprite>("UI_pngs/MyPage/Lock");
        for (int i = 0; ; i++)
        {
            var t = panel.Find($"Slot{i}");
            if (t == null) break;
            var btn = t.GetComponent<Button>();
            if (btn == null) btn = t.gameObject.AddComponent<Button>();
            var slotImg = t.GetComponent<Image>();
            if (slotImg != null) { slotImg.raycastTarget = true; btn.targetGraphic = slotImg; }   // UiKit.Box 기본이 raycast off → 클릭 안 먹힘

            Image thumb;
            var thumbTr = t.Find("ThumbBG");
            if (thumbTr == null)
            {
                var rt = JobsnailUiKit.Rect("ThumbBG", t, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                rt.SetAsFirstSibling();   // 슬롯 맨 밑에 깔림(자물쇠·가격이 위에)
                thumb = rt.gameObject.AddComponent<Image>();
                thumb.preserveAspect = true;
                thumb.raycastTarget = false;
            }
            else thumb = thumbTr.GetComponent<Image>();

            Image lockIcon;
            var lockTr = t.Find("LockIcon");
            if (lockTr == null)
            {
                var rt = JobsnailUiKit.Rect("LockIcon", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(36, 36));
                lockIcon = rt.gameObject.AddComponent<Image>();
                lockIcon.sprite = lockSprite;
                lockIcon.preserveAspect = true;
                lockIcon.raycastTarget = false;
            }
            else lockIcon = lockTr.GetComponent<Image>();

            TextMeshProUGUI label;
            var labelTr = t.Find("ItemLabel");
            if (labelTr == null)
            {
                label = JobsnailUiKit.Label("ItemLabel", t, "", 14, new Color(0.35f, 0.30f, 0.50f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(88, 88));
                label.raycastTarget = false;
            }
            else label = labelTr.GetComponent<TextMeshProUGUI>();

            m_Slots.Add((btn, thumb, lockIcon, label));
        }
    }

    private void RefreshCloset()
    {
        var coin = Get<TextMeshProUGUI>((int)Texts.CoinText);
        if (coin != null) coin.text = $"보유 코인  {SaveService.Coins}";

        // 아웃핏 카탈로그에서 현재 카테고리(접두사)만
        var items = new System.Collections.Generic.List<CodiOutfit>();
        foreach (var o in CodiOutfit.Catalog())
            if (o.name.StartsWith(m_Filter)) items.Add(o);

        string equipped = SaveService.EquippedOutfit;
        for (int i = 0; i < m_Slots.Count; i++)
        {
            var (btn, thumb, lockIcon, label) = m_Slots[i];
            btn.onClick.RemoveAllListeners();
            if (i >= items.Count)
            {
                if (thumb != null) thumb.enabled = false;
                if (lockIcon != null) lockIcon.enabled = false;
                if (label != null) label.text = "";
                btn.interactable = false;
                continue;
            }

            var item = items[i];
            string id = item.name;
            bool owned = SaveService.HasCodiItem(id) || item.Price <= 0;
            bool on = equipped == id;
            if (thumb != null)
            {
                var sp = item.ResolveThumbnail();
                thumb.enabled = sp != null;
                thumb.sprite = sp;
                thumb.color = owned ? Color.white : new Color(0.65f, 0.65f, 0.7f, 1f);   // 잠금 = 어둡게
            }
            if (lockIcon != null) lockIcon.enabled = !owned;   // 잠금 = 자물쇠 표시
            if (label != null)
            {
                label.alignment = owned ? TextAlignmentOptions.Center : TextAlignmentOptions.Bottom;
                label.text = owned
                    ? (on ? $"{item.DisplayName}\n[착용중]" : item.DisplayName)
                    : $"{item.Price}코인";                       // 잠금 = 가격만
            }
            btn.interactable = true;
            btn.onClick.AddListener(() => { if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick); OnClickItem(item); });
        }

        SetClosetList(items.Count == 0 ? "이 카테고리엔 아이템이 없어요." : "");
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
