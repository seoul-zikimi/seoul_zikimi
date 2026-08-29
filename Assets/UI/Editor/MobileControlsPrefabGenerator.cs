using System.IO;
using System.Collections.Generic;
using Player;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 모바일 컨트롤 프리팹 '초기' 생성기 — 기획서 레이아웃(배틀그라운드식):
///  좌하단 조이스틱 / 우하단 점프·던지기·공정 클러스터(+알약형 공정취소) /
///  좌상단 눈 아이콘(정답 고스트 토글) / 우상단 감정표현 드롭다운 / 하단 중앙 휴대폰.
/// 스타일: 반투명 라이트 그레이 원형 버튼 + 짙은 회색 텍스트(미니멀 플랫).
///
/// ⚠ 운영 원칙(2026-08-30 확정): 레이아웃의 소스 오브 트루스는 이 코드가 아니라
/// **MobileControlsCanvas.prefab 자체**다. 위치·크기 조정은 프리팹을 더블클릭해 프리팹 모드에서
/// 직접 옮기고 저장하면 된다(에디트 모드 — 저장됨). 이 메뉴는 프리팹을 처음 만들거나 구조 자체를
/// 갈아엎을 때만 쓰고, 실행하면 수동 편집이 전부 덮어써지므로 확인 대화상자를 띄운다.
/// 단, 오브젝트 '이름'은 바꾸면 안 된다 — MobileControlsHUD가 이름으로 와이어링한다(WireClick).
/// 감정표현 행(Emote1..N)은 런타임 RebuildEmoteRows가 재배치하므로 수동 조정 대상이 아니다.
/// </summary>
public static class MobileControlsPrefabGenerator
{
    private const string kDirectory = "Assets/Resources/UI/Mobile";
    private const string kPrefabPath = kDirectory + "/MobileControlsCanvas.prefab";
    private const string kPreviewPref = "Jobsnail_ForceMobileUI";

    private static readonly Color BtnFill = new(0.94f, 0.94f, 0.93f, 0.88f);
    private static readonly Color BtnFillSoft = new(0.94f, 0.94f, 0.93f, 0.55f);
    private static readonly Color Ink = new(0.20f, 0.20f, 0.19f, 1f);
    private static readonly Color InkSoft = new(0.20f, 0.20f, 0.19f, 0.72f);
    private static readonly Color PanelFill = new(0.96f, 0.96f, 0.95f, 0.97f);

    // 감정표현 행 라벨은 실제 발동 대사(EmoteDefs)에서 가져온다 — UI와 대사 불일치 방지.
    // 런타임(MobileControlsHUD.RebuildEmoteRows)에서도 같은 원본·같은 행 크기로 다시 쓰므로 프리팹이 낡아도 안전.
    // 행 크기 — 대사 11종이 우상단 버튼 아래에 다 들어가도록 기존(48/56)보다 조금 좁혔다(런타임 재구성과 동일 값 유지 필수).
    internal const float kEmoteRowHeight = 44f;
    internal const float kEmoteRowStep = 48f;
    private const float kEmotePanelTop = -213f;   // EmoteButton(y -170, 높이 70) 바로 아래

    [MenuItem("Jobsnail/UI/Mobile/Generate Mobile Controls Prefab")]
    public static void Generate()
    {
        // 소스 오브 트루스는 프리팹(수동 편집) — 이미 있으면 덮어쓰기 전에 반드시 확인받는다.
        if (File.Exists(kPrefabPath) && !EditorUtility.DisplayDialog(
                "모바일 컨트롤 프리팹 재생성",
                "MobileControlsCanvas.prefab을 코드 기본 레이아웃으로 다시 만듭니다.\n\n" +
                "⚠ 프리팹 모드에서 수동으로 조정한 위치·크기가 전부 덮어써집니다.\n" +
                "위치만 바꾸려면 취소하고 프리팹을 직접 편집하세요.",
                "덮어쓰고 재생성", "취소"))
            return;

        Directory.CreateDirectory(kDirectory);
        var root = new GameObject("MobileControlsCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(NoJuicyButtonMotion), typeof(MobileControlsHUD));
        var rootRt = root.GetComponent<RectTransform>();
        Stretch(rootRt);

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;   // HUD(10) 위, Popup(30) 아래 — 키 설정 등 팝업이 컨트롤에 가리지 않게


        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var controls = Rect("ControlLayer", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var safe = Rect("SafeArea", controls, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<MobileSafeArea>();

        BuildGestureHint(safe);
        BuildJoystick(safe);
        BuildAnswerToggle(safe);
        BuildActionCluster(safe);
        BuildPhoneButton(safe);
        BuildEmotes(safe);

        JobsnailUiKit.ApplyFontPolicy(root.transform);
        PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Mobile UI] 생성 완료: {kPrefabPath}");
    }

    [MenuItem("Jobsnail/UI/Mobile/Toggle Editor Preview")]
    public static void ToggleEditorPreview()
    {
        bool enabled = PlayerPrefs.GetInt(kPreviewPref, 0) != 1;
        PlayerPrefs.SetInt(kPreviewPref, enabled ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[Mobile UI] 에디터 프리뷰: {(enabled ? "ON" : "OFF")}");
    }

    [MenuItem("Jobsnail/UI/Mobile/Preview Fullscreen Order UI")]
    public static void PreviewFullscreenOrderUi()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Mobile UI] 먼저 GameScene을 Play한 뒤 이 메뉴를 다시 눌러 주세요.");
            return;
        }

        EnableEditorPreview();
        if (UIManager.Instance == null)
            new GameObject("@UIManager_MobilePreview").AddComponent<UIManager>();

        var hud = UIManager.Instance.ShowHUDUI<AnswerPanelHUD>();
        var previewItems = new List<AnswerPanelHUD.OrderEntry>();
        for (int i = 0; i < 9; i++)
        {
            previewItems.Add(new AnswerPanelHUD.OrderEntry
            {
                Id = i,
                Name = $"재료 {i + 1}",
                Limit = i % 3 == 0 ? 4 : -1,
                Sub = i % 2 == 0 ? "가공 필요" : "바로 사용 가능"
            });
        }

        hud.BuildOrders(previewItems, _ => { });
        var controls = GameObject.Find("@MobileControlsCanvas/ControlLayer");
        if (controls != null) controls.SetActive(false);
        Selection.activeGameObject = hud.gameObject;
        Debug.Log("[Mobile UI] 모바일 전체화면 주문 UI 프리뷰를 열었습니다.");
    }

    public static void EnableEditorPreview()
    {
        PlayerPrefs.SetInt(kPreviewPref, 1);
        PlayerPrefs.Save();
    }

    public static void DisableEditorPreview()
    {
        PlayerPrefs.SetInt(kPreviewPref, 0);
        PlayerPrefs.Save();
    }

    private static void BuildGestureHint(Transform parent)
    {
        // 상단 타이머(TopBar, y -11~-86)를 가리지 않게 그 아래에 두고, 레이캐스트도 막지 않는다.
        var pill = Panel("GestureHint", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -112f), new Vector2(470f, 40f), BtnFillSoft);
        pill.GetComponent<Image>().raycastTarget = false;
        Label("Label", pill, "빈 화면 드래그 · 카메라    두 손가락 · 확대/축소", 18, InkSoft);
    }

    private static void BuildJoystick(Transform parent)
    {
        var baseRt = Rect("MoveJoystick", parent, Vector2.zero, Vector2.zero,
            new Vector2(145f, 173f), new Vector2(300f, 300f));   // 실기기 엄지 위치 튜닝값(2026-08-30)
        var baseImage = baseRt.gameObject.AddComponent<Image>();
        baseImage.sprite = CircleSprite();
        baseImage.color = new Color(0.94f, 0.94f, 0.93f, 0.38f);
        baseImage.raycastTarget = true;

        var knob = Rect("JoystickKnob", baseRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(120f, 120f));
        var knobImage = knob.gameObject.AddComponent<Image>();
        knobImage.sprite = CircleSprite();
        knobImage.color = new Color(0.94f, 0.94f, 0.93f, 0.92f);
        knobImage.raycastTarget = false;

        baseRt.gameObject.AddComponent<MobileJoystickControl>().Configure(knob, 90f);
    }

    // 좌상단 눈 아이콘 — 정답 고스트(기존 TAB의 인월드 표시) 켜기/끄기. 꺼짐 상태는 HUD가 CanvasGroup 알파로 표시.
    private static void BuildAnswerToggle(Transform parent)
    {
        var rt = CircleButton("AnswerToggleButton", parent, BtnFill,
            new Vector2(104f, -96f), new Vector2(96f, 96f), new Vector2(0f, 1f));
        rt.gameObject.AddComponent<CanvasGroup>();

        // 눈 아이콘: 원 스프라이트 조합(흰자 타원 + 눈동자) — 폰트 이모지 의존 없음
        var almond = Rect("EyeAlmond", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(56f, 34f));
        var almondImg = almond.gameObject.AddComponent<Image>();
        almondImg.sprite = CircleSprite(); almondImg.color = Ink; almondImg.raycastTarget = false;

        var iris = Rect("EyeIris", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(22f, 22f));
        var irisImg = iris.gameObject.AddComponent<Image>();
        irisImg.sprite = CircleSprite(); irisImg.color = new Color(0.97f, 0.97f, 0.96f, 1f); irisImg.raycastTarget = false;

        var pupil = Rect("EyePupil", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(10f, 10f));
        var pupilImg = pupil.gameObject.AddComponent<Image>();
        pupilImg.sprite = CircleSprite(); pupilImg.color = Ink; pupilImg.raycastTarget = false;
    }

    // 우하단 클러스터(기획서 배치): 점프(큰 원, 우하단) / 던지기(그 위) / 공정(좌상) / 공정취소(점프 왼쪽)
    private static void BuildActionCluster(Transform parent)
    {
        Vector2 bottomRight = new(1f, 0f);
        ActionButton("JumpButton", parent, "점프", new Vector2(-185f, 200f), new Vector2(190f, 190f), bottomRight);

        var throwButton = ActionButton("ThrowButton", parent, "던지기",
            new Vector2(-165f, 565f), new Vector2(150f, 150f), bottomRight);
        throwButton.gameObject.AddComponent<MobileHoldButton>().Configure(MobileHoldButton.ActionType.Throw);

        var processButton = ActionButton("ProcessButton", parent, "공정",
            new Vector2(-400f, 470f), new Vector2(165f, 165f), bottomRight);
        processButton.gameObject.AddComponent<MobileHoldButton>().Configure(MobileHoldButton.ActionType.Process);

        // 공정취소: 원형 클러스터에서 빼서 감정표현처럼 '알약형'으로, 우측 상단 클러스터 위에(기획 프로토타입 2026-08-30).
        var revertButton = Rect("RevertButton", parent, bottomRight, bottomRight,
            new Vector2(-183f, 353f), new Vector2(220f, 70f));
        var revertImg = revertButton.gameObject.AddComponent<Image>();
        revertImg.sprite = RoundSprite(); revertImg.type = Image.Type.Sliced; revertImg.color = BtnFill;
        var revertBtn = revertButton.gameObject.AddComponent<Button>();
        revertBtn.targetGraphic = revertImg;
        SetFlatColors(revertBtn, BtnFill);
        Label("Label", revertButton, "공정취소", 30, Ink);
        revertButton.gameObject.AddComponent<MobileHoldButton>().Configure(MobileHoldButton.ActionType.Revert);

        // 기획서 외 보조 버튼(기능 유지) — 클러스터 왼쪽에 작고 옅게.
        // 좌표는 실기기에서 엄지 닿는 위치로 직접 튜닝한 값(2026-08-30). 공정취소(-500..-350, y150..300)와
        // 살짝 겹치지만 회전=재료 든 상태·공정취소=공정 중이라 동시에 뜨는 상황이 없다 — 동시 노출이 생기면 재배치.
        SmallButton("RotateButton", parent, "회전", new Vector2(-381f, 190f), bottomRight);
        SmallButton("ScaffoldButton", parent, "비계", new Vector2(-335f, 315f), bottomRight);
    }

    private static void BuildPhoneButton(Transform parent)
    {
        // y 52 → 90: 아이폰 하단 홈 인디케이터 제스처 영역과 겹쳐 스와이프가 Siri/AI를 소환하던 문제 — 살짝 위로.
        var rt = Rect("PhoneButton", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 90f), new Vector2(300f, 86f));
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = RoundSprite();
        image.type = Image.Type.Sliced;
        image.color = BtnFill;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        SetFlatColors(button, BtnFill);
        Label("Label", rt, "휴대폰", 30, Ink);
    }

    // 우상단 감정표현 — 버튼을 누르면 아래로 드롭다운(배틀그라운드식)
    private static void BuildEmotes(Transform parent)
    {
        // 우상단 설정 톱니·종료요청 버튼(y -17~-81)과 겹치지 않게 그 아래에 배치
        var button = Rect("EmoteButton", parent, Vector2.one, Vector2.one,
            new Vector2(-130f, -170f), new Vector2(200f, 70f));
        var image = button.gameObject.AddComponent<Image>();
        image.sprite = RoundSprite(); image.type = Image.Type.Sliced; image.color = BtnFill;
        var btn = button.gameObject.AddComponent<Button>(); btn.targetGraphic = image;
        SetFlatColors(btn, BtnFill);
        Label("Label", button, "감정표현 ▾", 24, Ink);

        // 행 수·라벨의 원본은 EmoteDefs — 대사를 추가/수정하면 이 메뉴를 다시 돌리기만 하면 된다.
        // (예전엔 옛 이모지 이름 8개가 여기 하드코딩돼 있어, 라벨과 실제로 나가는 대사가 서로 달랐다.)
        int count = EmoteDefs.Count;
        float height = count * kEmoteRowStep + 16f;
        var panel = Panel("EmotePanel", parent, Vector2.one, Vector2.one,
            new Vector2(-130f, kEmotePanelTop - height * 0.5f), new Vector2(220f, height), PanelFill);

        float firstRowY = height * 0.5f - 8f - kEmoteRowHeight * 0.5f;
        for (int i = 0; i < count; i++)
        {
            var rt = Rect($"Emote{i + 1}", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, firstRowY - i * kEmoteRowStep), new Vector2(196f, kEmoteRowHeight));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = RoundSprite(); img.type = Image.Type.Sliced;
            img.color = new Color(0.90f, 0.90f, 0.89f, 1f);
            var rowBtn = rt.gameObject.AddComponent<Button>(); rowBtn.targetGraphic = img;
            SetFlatColors(rowBtn, img.color);
            var label = Label("Label", rt, EmoteDefs.All[i].Line, 20, Ink);
            label.textWrappingMode = TextWrappingModes.NoWrap;   // 긴 대사는 줄바꿈 대신 글자 축소
            label.enableAutoSizing = true;
            label.fontSizeMax = 20f; label.fontSizeMin = 12f;
        }
    }

    private static RectTransform ActionButton(string name, Transform parent, string text,
        Vector2 anchored, Vector2 size, Vector2 anchor)
    {
        var rt = CircleButton(name, parent, BtnFill, anchored, size, anchor);
        Label("Label", rt, text, Mathf.RoundToInt(Mathf.Clamp(size.x * 0.19f, 22f, 34f)), Ink);
        return rt;
    }

    private static RectTransform SmallButton(string name, Transform parent, string text,
        Vector2 anchored, Vector2 anchor)
    {
        var rt = CircleButton(name, parent, BtnFillSoft, anchored, new Vector2(100f, 100f), anchor);
        Label("Label", rt, text, 22, InkSoft);
        return rt;
    }

    private static RectTransform CircleButton(string name, Transform parent, Color fill,
        Vector2 anchored, Vector2 size, Vector2 anchor)
    {
        var rt = Rect(name, parent, anchor, anchor, anchored, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = CircleSprite(); image.color = fill;
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        SetFlatColors(button, fill);
        return rt;
    }

    private static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchored, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax, anchored, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = RoundSprite(); image.type = Image.Type.Sliced; image.color = color;
        return rt;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string text, int fontSize, Color color,
        Vector2? anchored = null, Vector2? size = null)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, anchored ?? Vector2.zero, size ?? Vector2.zero);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = JobsnailUiKit.TmpFont;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored; rt.sizeDelta = size;
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetFlatColors(Button button, Color normal)
    {
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = Color.Lerp(normal, Color.white, 0.25f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(normal, Ink, 0.22f);
        colors.disabledColor = new Color(0.62f, 0.61f, 0.58f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    private static Sprite RoundSprite() => Resources.Load<Sprite>("UI_pngs/MyPage/RoundRect");
    private static Sprite CircleSprite() => Resources.Load<Sprite>("UI_pngs/EmoteWheel_Disc") ?? RoundSprite();
}
