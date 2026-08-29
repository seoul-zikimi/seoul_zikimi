using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 최초 실행 인트로 — 건축레인저 결성 컷씬 슬라이드쇼(페이드 전환) + 초기 캐릭터 선택.
/// 완료 시 SaveService.IntroSeen 저장, 이후엔 다시 뜨지 않음(설정에서 재시청 없음 — 필요 시 IntroSeen 리셋).
/// 카드는 Resources/Characters/{id}.prefab 이 있을 때만 선택 가능(달팽이·거북이·게 모두 열려 있음).
/// </summary>
public sealed class IntroCutscene : MonoBehaviour
{
    private struct Slide
    {
        public string SpritePath;
        public string Caption;
        public Slide(string path, string caption) { SpritePath = path; Caption = caption; }
    }

    private static readonly Slide[] kSlides =
    {
        new("UI_pngs/0.intro/Intro_1_Disaster",
            "어느 날, 원인불명의 재난이 서울을 덮쳤다.\n남산타워도, 광화문도, 서울역도... 와르르."),
        new("UI_pngs/0.intro/Intro_3_Poster",
            "그때 거리에 나붙은 공고 한 장.\n[서울시 명소 재건 사업 긴급 인력 모집] 보수 확실 보장!"),
        new("UI_pngs/0.intro/Intro_2_Shell",
            "\"추락 시 다치지 않는 자 우대 (등껍질 보유자 등)\"\n\"...어? 우리 등껍질 있는데?\""),
        new("UI_pngs/0.intro/Intro_4_Huddle",
            "등껍질 삼총사, 그 자리에서 의기투합.\n\"우리가 서울을 다시 세운다!\""),
        new("UI_pngs/0.intro/Intro_5_Rangers",
            "그렇게 탄생한 자칭 히어로,\n건축레인저!"),
        new("UI_pngs/0.intro/Intro_6_Work",
            "...히어로도 땀은 흘려야 한다.\n보수는 확실하다니까, 일단 짓자!"),
    };

    private struct Pick
    {
        public string Id;
        public string PortraitPath;
        /// <summary>표시 이름은 CharacterCatalog가 원본 — 여기서 따로 들고 있지 않는다(옷장과 어긋나지 않게).</summary>
        public string DisplayName => CharacterCatalog.DisplayName(Id);
        public Pick(string id, string portrait) { Id = id; PortraitPath = portrait; }
    }

    // 인트로 전용 일러스트 경로만 정한다(파일명 hermitcrab은 아트 에셋 이름이라 그대로 둠).
    private static readonly Pick[] kPicks =
    {
        new("", "UI_pngs/0.intro/Select_default"),
        new("char_turtle", "UI_pngs/0.intro/Select_char_turtle"),
        new("char_crab", "UI_pngs/0.intro/Select_char_hermitcrab"),
    };

    private const float kFadeSeconds = 0.4f;

    private Action m_OnComplete;
    private int m_SlideIndex = -1;
    private bool m_Fading;
    private bool m_SelectPhase;

    private Image m_SlideImage;
    private CanvasGroup m_SlideGroup;
    private TextMeshProUGUI m_Caption;
    private GameObject m_SlideRoot;

    private string m_SelectedId;
    private bool m_HasSelection;
    private Image[] m_CardFrames;
    private Button m_ConfirmButton;
    private Image m_ConfirmImage;

    public static void Show(Action onComplete)
    {
        EnsureEventSystem();
        var canvas = JobsnailUiKit.EnsureOverlayCanvas("@IntroCutscene", 600);
        if (canvas.GetComponent<IntroCutscene>() == null)
        {
            var intro = canvas.gameObject.AddComponent<IntroCutscene>();
            intro.m_OnComplete = onComplete;
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void Awake()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.SetPhase(global::GamePhase.Lobby);
        BuildSlideshow();
        NextSlide();
    }

    // ── 1부: 슬라이드쇼 ──

    private void BuildSlideshow()
    {
        var root = transform;
        JobsnailUiKit.Box("Backdrop", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.black);

        m_SlideRoot = new GameObject("SlideRoot", typeof(RectTransform));
        m_SlideRoot.transform.SetParent(root, false);
        var slideRt = (RectTransform)m_SlideRoot.transform;
        slideRt.anchorMin = Vector2.zero;
        slideRt.anchorMax = Vector2.one;
        slideRt.offsetMin = Vector2.zero;
        slideRt.offsetMax = Vector2.zero;
        m_SlideGroup = m_SlideRoot.AddComponent<CanvasGroup>();
        m_SlideGroup.alpha = 0f;
        m_SlideGroup.blocksRaycasts = false;

        m_SlideImage = JobsnailUiKit.Image("Slide", m_SlideRoot.transform, null);

        // 자막 띠(하단) — 반투명 검정 위 흰 글씨, 폰트는 서울한강 장체
        var band = JobsnailUiKit.Box("CaptionBand", m_SlideRoot.transform, new Vector2(0f, 0f), new Vector2(1f, 0.16f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
        m_Caption = JobsnailUiKit.Label("Caption", band.transform, "", 30, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        // 전체 화면 클릭 = 다음 슬라이드 (투명 버튼, 스킵 버튼보다 먼저 깔림)
        var advance = JobsnailUiKit.Button("AdvanceCatcher", root, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, NextSlide);
        var advanceImage = advance.GetComponent<Image>();
        if (advanceImage != null)
            advanceImage.color = new Color(0f, 0f, 0f, 0f);

        var hint = JobsnailUiKit.Label("Hint", root, "클릭해서 계속 >", 20, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.BottomRight, Vector2.zero, Vector2.zero);
        hint.raycastTarget = false;   // 클릭은 아래 AdvanceCatcher가 받도록
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0.72f, 0.005f);
        hintRt.anchorMax = new Vector2(0.985f, 0.05f);
        hintRt.offsetMin = Vector2.zero;
        hintRt.offsetMax = Vector2.zero;

        var skip = JobsnailUiKit.Button("SkipButton", root, null, new Vector2(0.90f, 0.93f), new Vector2(0.985f, 0.985f), Vector2.zero, Vector2.zero, SkipToSelect);
        var skipImage = skip.GetComponent<Image>();
        if (skipImage != null)
            skipImage.color = new Color(0f, 0f, 0f, 0.35f);
        JobsnailUiKit.Label("Label", skip.transform, "건너뛰기 >>", 18, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
    }

    private void Update()
    {
        if (m_SelectPhase)
            return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null)
            return;
        if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            NextSlide();
        else if (kb.escapeKey.wasPressedThisFrame)
            SkipToSelect();
    }

    private void NextSlide()
    {
        if (m_Fading || m_SelectPhase)
            return;

        if (m_SlideIndex + 1 >= kSlides.Length)
        {
            SkipToSelect();
            return;
        }

        m_SlideIndex++;
        StartCoroutine(CoCrossfade(kSlides[m_SlideIndex]));
    }

    private IEnumerator CoCrossfade(Slide slide)
    {
        m_Fading = true;
        yield return CoFadeGroup(m_SlideGroup, m_SlideGroup.alpha, 0f, m_SlideIndex == 0 ? 0f : kFadeSeconds);

        var sprite = JobsnailUiKit.Sprite(slide.SpritePath);
        m_SlideImage.sprite = sprite;
        m_SlideImage.enabled = sprite != null;
        if (sprite != null)
            JobsnailUiKit.CoverFill(m_SlideImage);
        m_Caption.text = slide.Caption;

        yield return CoFadeGroup(m_SlideGroup, 0f, 1f, kFadeSeconds);
        m_Fading = false;
    }

    private static IEnumerator CoFadeGroup(CanvasGroup group, float from, float to, float seconds)
    {
        if (seconds <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
            yield return null;
        }
        group.alpha = to;
    }

    // ── 2부: 초기 캐릭터 선택 ──

    private void SkipToSelect()
    {
        if (m_SelectPhase)
            return;
        m_SelectPhase = true;
        StopAllCoroutines();
        m_Fading = false;
        BuildCharacterSelect();
    }

    private void BuildCharacterSelect()
    {
        var root = transform;
        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        JobsnailUiKit.Box("Backdrop", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.13f, 0.10f, 0.08f, 1f));

        JobsnailUiKit.Label("Title", root, "첫 번째 레인저를 선택하세요!", 44, JobsnailUiKit.Cream, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero)
            .rectTransform.SetAnchors(new Vector2(0f, 0.84f), new Vector2(1f, 0.96f));
        JobsnailUiKit.Label("SubTitle", root, "나머지 레인저는 보수를 모아 마이페이지 옷장에서 영입할 수 있어요.", 22, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero)
            .rectTransform.SetAnchors(new Vector2(0f, 0.79f), new Vector2(1f, 0.85f));

        m_CardFrames = new Image[kPicks.Length];
        for (int i = 0; i < kPicks.Length; i++)
        {
            float cx = 0.5f + (i - 1) * 0.24f;
            BuildCard(root, i, new Vector2(cx - 0.10f, 0.28f), new Vector2(cx + 0.10f, 0.74f));
        }

        m_ConfirmButton = JobsnailUiKit.Button("ConfirmButton", root, null, new Vector2(0.40f, 0.10f), new Vector2(0.60f, 0.20f), Vector2.zero, Vector2.zero, Confirm);
        m_ConfirmImage = m_ConfirmButton.GetComponent<Image>();
        if (m_ConfirmImage != null)
            m_ConfirmImage.color = JobsnailUiKit.SoftGray;
        JobsnailUiKit.Label("Label", m_ConfirmButton.transform, "이 레인저로 출동!", 26, JobsnailUiKit.Brown, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
        m_ConfirmButton.interactable = false;

        JuicyButton.AttachAll(gameObject);
    }

    private void BuildCard(Transform root, int index, Vector2 anchorMin, Vector2 anchorMax)
    {
        var pick = kPicks[index];
        bool available = string.IsNullOrEmpty(pick.Id) || CharacterCatalog.LoadPrefab(pick.Id) != null;

        var frame = JobsnailUiKit.Box("Card_" + pick.DisplayName, root, anchorMin, anchorMax, Vector2.zero, Vector2.zero, new Color(1f, 0.97f, 0.86f, available ? 1f : 0.45f));
        m_CardFrames[index] = frame;

        var portraitSprite = JobsnailUiKit.Sprite(pick.PortraitPath);
        var portrait = JobsnailUiKit.Image("Portrait", frame.transform, portraitSprite);
        var portraitRt = portrait.rectTransform;
        portraitRt.anchorMin = new Vector2(0.05f, 0.16f);
        portraitRt.anchorMax = new Vector2(0.95f, 0.96f);
        portraitRt.offsetMin = Vector2.zero;
        portraitRt.offsetMax = Vector2.zero;
        portrait.preserveAspect = true;
        if (!available)
            portrait.color = new Color(0.55f, 0.55f, 0.6f, 1f);

        var nameBand = JobsnailUiKit.Box("NameBand", frame.transform, new Vector2(0f, 0f), new Vector2(1f, 0.15f), Vector2.zero, Vector2.zero, new Color(1f, 0.79f, 0.46f, available ? 1f : 0.5f));
        JobsnailUiKit.Label("Name", nameBand.transform, available ? pick.DisplayName : pick.DisplayName + " (준비 중)", 26, JobsnailUiKit.Brown, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);

        if (!available)
            return;

        frame.raycastTarget = true;   // Box 기본값이 false라 버튼 클릭이 안 먹음
        var button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        string id = pick.Id;
        button.onClick.AddListener(() =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
            SelectCard(index, id);
        });
    }

    private void SelectCard(int index, string id)
    {
        m_SelectedId = id;
        m_HasSelection = true;
        for (int i = 0; i < m_CardFrames.Length; i++)
        {
            if (m_CardFrames[i] == null) continue;
            bool on = i == index;
            m_CardFrames[i].color = on
                ? JobsnailUiKit.Apricot
                : new Color(1f, 0.97f, 0.86f, m_CardFrames[i].GetComponent<Button>() != null ? 1f : 0.45f);
        }

        if (m_ConfirmButton != null)
        {
            m_ConfirmButton.interactable = true;
            if (m_ConfirmImage != null)
                m_ConfirmImage.color = JobsnailUiKit.Apricot;
        }
    }

    private void Confirm()
    {
        if (!m_HasSelection)
            return;

        SaveService.EquippedCharacter = m_SelectedId;
        SaveService.GrantCharacter(m_SelectedId);
        SaveService.IntroSeen = true;
        StartCoroutine(CoFinish());
    }

    private IEnumerator CoFinish()
    {
        // 검정으로 덮고 종료 → 메인 메뉴가 아래에서 열린 뒤 커튼 제거
        var curtain = JobsnailUiKit.Box("Curtain", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
        var group = curtain.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        yield return CoFadeGroup(group, 0f, 1f, kFadeSeconds);

        m_OnComplete?.Invoke();
        Destroy(gameObject);
    }
}

internal static class IntroRectExtensions
{
    public static void SetAnchors(this RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
