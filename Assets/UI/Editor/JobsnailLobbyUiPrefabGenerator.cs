using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class JobsnailLobbyUiPrefabGenerator
{
    private const string PrefabDir = "Assets/Resources/UI/Jobsnail/Prefabs";

    [MenuItem("Jobsnail/UI/Generate Runtime UI Prefabs")]
    public static void Generate()
    {
        Directory.CreateDirectory(PrefabDir);

        SavePrefab(BuildSessionOverlay(), $"{PrefabDir}/JobsnailSessionOverlay.prefab");
        SavePrefab(BuildCreateOverlay(), $"{PrefabDir}/JobsnailCreateOverlay.prefab");
        SavePrefab(BuildLobbyRoomOverlay(), $"{PrefabDir}/JobsnailLobbyRoomOverlay.prefab");
        JobsnailLobbyPrefabBinder.BindAll();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[JobsnailLobbyUiPrefabGenerator] Runtime UI prefabs generated.");
    }

    private static GameObject BuildSessionOverlay()
    {
        var root = Root("JobsnailSessionOverlay");
        var pcRoot = Rect("PcRoot", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1210, 765));

        var pcSprite = Sprite("UI_pngs/2.sesh/Session_PC_BG");
        var pc = Image("SessionPc", pcRoot, pcSprite, new Color(1f, 1f, 1f, 1f));
        Stretch(pc.rectTransform);
        pc.preserveAspect = true;

        Text("SessionTitle", pcRoot, "구인 건설 현장 리스트", 34, Color.black, new Vector2(0, 205), new Vector2(520, 60), TextAnchor.MiddleCenter);
        Button("CreateButton", pcRoot, "방 만들기", new Vector2(322, 204), new Vector2(122, 38), 17);
        Button("RefreshButton", pcRoot, "새로고침", new Vector2(205, 204), new Vector2(105, 38), 16, new Color(1f, 0.90f, 0.70f, 1f));

        var listPanel = Box("ListPanel", pcRoot, new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero, new Color(1f, 0.76f, 0.42f, 1f));
        listPanel.raycastTarget = false;

        BuildSessionListScroll(listPanel.transform);
        Text("SessionStatus", pcRoot, "방 목록을 불러오는 중...", 18, new Color(0.25f, 0.18f, 0.12f, 1f), new Vector2(0, -8), new Vector2(520, 34), TextAnchor.MiddleCenter);
        Button("BackButton", pcRoot, "메인으로", new Vector2(-485, -290), new Vector2(105, 50), 18, Color.white);
        return root;
    }

    private static GameObject BuildCreateOverlay()
    {
        var root = Root("JobsnailCreateOverlay");

        var panel = Box("CreateModalPanel", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 360), new Color(1f, 1f, 1f, 0.98f));
        panel.raycastTarget = true;
        var titleBar = Box("CreateModalTitleBar", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 165), new Vector2(460, 30), new Color(0.78f, 0.93f, 0.96f, 1f));
        titleBar.raycastTarget = false;

        Text("CreateTitle", root.transform, "방 생성하기", 12, Color.black, new Vector2(-186, 165), new Vector2(120, 28), TextAnchor.MiddleLeft);
        Button("CloseButton", root.transform, "×", new Vector2(218, 165), new Vector2(28, 28), 18, new Color(0f, 0f, 0f, 0f));

        // 방 이름 (최대 15자)
        Text("RoomNameLabel", root.transform, "방 이름", 18, Color.black, new Vector2(-150, 118), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("RoomNameHint", root.transform, "(최대 15자)", 10, Color.black, new Vector2(-150, 100), new Vector2(90, 18), TextAnchor.MiddleRight);
        Input("RoomNameInput", root.transform, "신체 건강한 달팽이 구합니다", new Vector2(70, 114), new Vector2(240, 28));

        // 맵 선택(드롭다운) + 날씨 ON/OFF 토글
        Text("MapText", root.transform, "맵", 18, Color.black, new Vector2(-150, 65), new Vector2(90, 28), TextAnchor.MiddleRight);
        Button("MapSelectButton", root.transform, "", new Vector2(15, 65), new Vector2(160, 28), 15, new Color(0.90f, 0.90f, 0.90f, 1f));
        Text("MapSelectLabel", Find(root.transform, "MapSelectButton"), "맵 선택 ▼", 15, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        Button("WeatherToggleButton", root.transform, "날씨 ON", new Vector2(152, 65), new Vector2(92, 28), 14, new Color(0.58f, 1f, 0.54f, 1f));
        SelectOptionsPanel("MapOptions", root.transform, new Vector2(15, 51), 160);

        // 모드 선택(드롭다운)
        Text("ModeText", root.transform, "모드", 18, Color.black, new Vector2(-150, 23), new Vector2(90, 28), TextAnchor.MiddleRight);
        Button("ModeSelectButton", root.transform, "", new Vector2(15, 23), new Vector2(200, 28), 15, new Color(0.90f, 0.90f, 0.90f, 1f));
        Text("ModeSelectLabel", Find(root.transform, "ModeSelectButton"), "모드 선택 ▼", 15, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        SelectOptionsPanel("ModeOptions", root.transform, new Vector2(15, 9), 200);

        // 방 종류
        Text("RoomTypeLabel", root.transform, "방 종류", 18, Color.black, new Vector2(-150, -20), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("RoomTypeHint", root.transform, "(택 1)", 10, Color.black, new Vector2(-150, -38), new Vector2(90, 18), TextAnchor.MiddleRight);
        Button("PrivateRoomButton", root.transform, "비밀방", new Vector2(25, -20), new Vector2(100, 28), 15, new Color(1f, 0.55f, 0.55f, 1f));
        Button("PublicRoomButton", root.transform, "공개방", new Vector2(140, -20), new Vector2(86, 28), 15, new Color(0.83f, 0.83f, 0.83f, 1f));

        // 비밀번호 (공백 제외, 최대 8자)
        Text("PasswordLabel", root.transform, "비밀번호", 18, Color.black, new Vector2(-150, -64), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("PasswordHint", root.transform, "(공백X, 최대 8자)", 10, Color.black, new Vector2(-150, -82), new Vector2(90, 18), TextAnchor.MiddleRight);
        Input("PasswordInput", root.transform, "", new Vector2(70, -66), new Vector2(240, 28));

        Text("CreateStatus", root.transform, "", 12, new Color(0.65f, 0.16f, 0.12f, 1f), new Vector2(0, -104), new Vector2(360, 22), TextAnchor.MiddleCenter);
        Button("SubmitButton", root.transform, "방 만들기", new Vector2(0, -130), new Vector2(120, 36), 16);
        return root;
    }

    /// <summary>맵/모드 선택 드롭다운의 옵션 컨테이너. 옵션 버튼은 런타임에 채운다(위→아래로 자람).</summary>
    private static void SelectOptionsPanel(string name, Transform parent, Vector2 topCenter, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);          // 위쪽 고정, 아래로 펼쳐짐
        rt.anchoredPosition = topCenter;
        rt.sizeDelta = new Vector2(width, 60);

        var image = go.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.98f);
        image.raycastTarget = true;

        go.SetActive(false);
    }

    private static GameObject BuildLobbyRoomOverlay()
    {
        var root = Root("JobsnailLobbyRoomOverlay");
        var pcRoot = Rect("LobbyPcRoot", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1210, 765));

        var pc = Image("LobbySessionPc", pcRoot, Sprite("UI_pngs/2.sesh/Session_PC_BG"), Color.white);
        Stretch(pc.rectTransform);
        pc.preserveAspect = true;

        Text("LobbyTitle", pcRoot, "구인 대기", 36, Color.black, new Vector2(0, 212), new Vector2(520, 60), TextAnchor.MiddleCenter);
        Text("LobbySubtitle", pcRoot, "신체 건강한 달팽이 구합니다", 24, Color.black, new Vector2(0, 160), new Vector2(620, 42), TextAnchor.MiddleCenter);
        Text("LobbyStatusBadge", pcRoot, "모집중", 16, Color.black, new Vector2(330, 116), new Vector2(94, 30), TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.44f, 1f));

        // 세로형 캐릭터 카드 4개를 왼쪽에 가로 한 줄로 배치(우측 맵/모드 패널과 겹치지 않게).
        LobbySlot(pcRoot, 0, new Vector2(-330, 5), "방장", "방장");
        LobbySlot(pcRoot, 1, new Vector2(-182, 5), "팀원 1", "대기중...");
        LobbySlot(pcRoot, 2, new Vector2(-34, 5), "팀원 2", "대기중...");
        LobbySlot(pcRoot, 3, new Vector2(114, 5), "팀원 3", "대기중...");

        Text("MapPreview", pcRoot, "현재 선택된\n맵 이미지", 16, Color.black, new Vector2(312, 8), new Vector2(130, 130), TextAnchor.MiddleCenter, new Color(0.82f, 0.82f, 0.82f, 1f));
        Button("LobbyLeaveButton", pcRoot, "나가기", new Vector2(-340, -205), new Vector2(105, 42), 18, Color.white);
        Button("LobbyStartButton", pcRoot, "게임 시작", new Vector2(305, -104), new Vector2(145, 48), 20, new Color(1f, 0.78f, 0.44f, 1f));
        Button("LobbyReadyButton", pcRoot, "준비", new Vector2(305, -104), new Vector2(145, 48), 20, new Color(1f, 0.42f, 0.42f, 1f));
        Text("LobbyStartHint", pcRoot, "팀원이 준비하면 시작할 수 있어요", 16, new Color(0.35f, 0.25f, 0.18f, 1f), new Vector2(305, -145), new Vector2(260, 30), TextAnchor.MiddleCenter);
        Text("LobbyReadyStatus", pcRoot, "준비 상태를 확인하는 중...", 19, new Color(0.18f, 0.12f, 0.08f, 1f), new Vector2(0, -192), new Vector2(470, 36), TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.76f, 0.95f));
        return root;
    }

    // 세션 목록: 2열 그리드 + 세로 스크롤. 카드가 6개(=3행)를 넘으면 아래로 쌓이며 스크롤된다.
    // ListPanel 안쪽을 꽉 채우도록 붙여서 Viewport(마스크)=패널 경계가 되고, 카드는 항상 패널 안으로 클립된다.
    // Content 오브젝트 이름은 CustomSessionListRoot로 유지해(=카드가 붙는 곳) 바인더/뷰와 호환된다.
    private static void BuildSessionListScroll(Transform listPanel)
    {
        // 스크롤 영역 루트: ListPanel 안쪽에 여백 두고 스트레치. 배경은 투명(뒤의 ListPanel이 배경 역할).
        var scrollGo = new GameObject("SessionListScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(listPanel, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(16, 16);
        scrollRt.offsetMax = new Vector2(-16, -16);

        var scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.color = new Color(1f, 1f, 1f, 0f);
        scrollBg.raycastTarget = true;   // 빈 곳 드래그로도 스크롤되게

        // Viewport: 넘치는 카드를 잘라내는 마스크(= 패널 안쪽 영역).
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        Stretch(viewportRt);

        // Content(=CustomSessionListRoot): 카드가 쌓이는 곳. 위쪽 고정, 아래로 자라난다.
        var content = new GameObject("CustomSessionListRoot",
            typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;

        var grid = content.GetComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.cellSize = new Vector2(300, 116);                 // 패널 폭(≈678)에 2열이 들어가는 크기
        grid.spacing = new Vector2(20, 12);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;   // 왼쪽 위부터
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;      // 왼→오 먼저 채우고 다음 줄
        grid.childAlignment = TextAnchor.UpperLeft;            // 카드 1개여도 왼쪽 위
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;                              // 항상 2열

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = viewportRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
    }

    private static void LobbySlot(Transform parent, int index, Vector2 anchored, string name, string status)
    {
        // 세로형(포트레이트) 카드: 위=닉네임, 가운데=캐릭터, 아래=상태.
        var slot = Box($"LobbySlot{index}", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, new Vector2(135, 285), new Color(1f, 1f, 1f, 0.98f));
        Text($"LobbySlotName{index}", slot.transform, name, 17, Color.black, new Vector2(0, 102), new Vector2(125, 26), TextAnchor.MiddleCenter);
        Text($"LobbySlotAvatar{index}", slot.transform, "유저\n캐릭터", 13, new Color(0.25f, 0.25f, 0.25f, 1f), new Vector2(0, 0), new Vector2(115, 165), TextAnchor.MiddleCenter, new Color(0.86f, 0.86f, 0.86f, 1f));
        Text($"LobbySlotStatus{index}", slot.transform, status, 15, new Color(0.25f, 0.18f, 0.12f, 1f), new Vector2(0, -118), new Vector2(125, 24), TextAnchor.MiddleCenter);
    }

    internal static GameObject Root(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Stretch((RectTransform)go.transform);
        return go;
    }

    internal static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
        {
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        return rt;
    }

    internal static Image Image(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    internal static Image Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size, Color color)
    {
        var image = Image(name, parent, null, color);
        var rt = image.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
        {
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        return image;
    }

    internal static Button Button(string name, Transform parent, string label, Vector2 anchored, Vector2 size, int fontSize, Color? color = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = color ?? new Color(1f, 0.78f, 0.44f, 1f);
        image.raycastTarget = true;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        if (!string.IsNullOrEmpty(label))
            Text($"{name}Label", go.transform, label, fontSize, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);

        return button;
    }

    internal static InputField Input(string name, Transform parent, string value, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.86f, 0.86f, 0.86f, 1f);
        image.raycastTarget = true;

        var text = Text($"{name}Text", go.transform, value, 20, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        var placeholder = Text($"{name}Placeholder", go.transform, "Enter text...", 20, new Color(0f, 0f, 0f, 0.35f), Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        input.characterLimit = 20;
        return input;
    }

    internal static Text Text(string name, Transform parent, string value, int size, Color color, Vector2 anchored, Vector2 sizeDelta, TextAnchor anchor, Color? background = null)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rt = (RectTransform)root.transform;
        rt.anchorMin = sizeDelta == Vector2.zero ? Vector2.zero : new Vector2(0.5f, 0.5f);
        rt.anchorMax = sizeDelta == Vector2.zero ? Vector2.one : new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = sizeDelta;

        GameObject textGo = root;
        if (background.HasValue)
        {
            var bg = root.AddComponent<Image>();
            bg.color = background.Value;
            bg.raycastTarget = false;

            textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            Stretch((RectTransform)textGo.transform);
        }

        var label = textGo.AddComponent<Text>();
        label.text = value ?? string.Empty;
        label.font = JobsnailUiKit.LegacyFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = anchor;
        label.raycastTarget = false;
        return label;
    }

    internal static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    internal static Transform Find(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    internal static Sprite Sprite(string resourcesPath)
    {
        return Resources.Load<Sprite>(resourcesPath);
    }

    private static void SavePrefab(GameObject root, string path)
    {
        if (File.Exists(path))
            AssetDatabase.DeleteAsset(path);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
