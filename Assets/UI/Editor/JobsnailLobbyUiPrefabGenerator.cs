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

        Rect("CustomSessionListRoot", pcRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-36, -55), new Vector2(820, 410));
        Text("SessionStatus", pcRoot, "방 목록을 불러오는 중...", 18, new Color(0.25f, 0.18f, 0.12f, 1f), new Vector2(0, -8), new Vector2(520, 34), TextAnchor.MiddleCenter);
        Button("BackButton", pcRoot, "메인으로", new Vector2(-485, -290), new Vector2(105, 50), 18, Color.white);
        return root;
    }

    private static GameObject BuildCreateOverlay()
    {
        var root = Root("JobsnailCreateOverlay");

        var panel = Box("CreateModalPanel", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 285), new Color(1f, 1f, 1f, 0.98f));
        panel.raycastTarget = true;
        var titleBar = Box("CreateModalTitleBar", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 127), new Vector2(430, 30), new Color(0.78f, 0.93f, 0.96f, 1f));
        titleBar.raycastTarget = false;

        Text("CreateTitle", root.transform, "방 생성하기", 12, Color.black, new Vector2(-166, 127), new Vector2(120, 28), TextAnchor.MiddleLeft);
        Button("CloseButton", root.transform, "×", new Vector2(205, 127), new Vector2(28, 28), 18, new Color(0f, 0f, 0f, 0f));

        Text("RoomNameLabel", root.transform, "방 이름", 18, Color.black, new Vector2(-115, 76), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("RoomNameHint", root.transform, "(최대 15자)", 10, Color.black, new Vector2(-115, 57), new Vector2(90, 18), TextAnchor.MiddleRight);
        Input("RoomNameInput", root.transform, "신체 건강한 달팽이 구합니다", new Vector2(82, 73), new Vector2(225, 28));

        Text("MaxPlayersText", root.transform, "최대 인원", 18, Color.black, new Vector2(-115, 30), new Vector2(90, 28), TextAnchor.MiddleRight);
        Button("MaxPlayersButton", root.transform, "", new Vector2(35, 29), new Vector2(90, 26), 16, new Color(0.83f, 0.83f, 0.83f, 1f));
        Text("MaxPlayersLabel", Find(root.transform, "MaxPlayersButton"), "1명 ▼", 16, Color.black, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        BuildMaxPlayersOptions(root.transform, new Vector2(35, 29));

        Text("RoomTypeLabel", root.transform, "방 종류", 18, Color.black, new Vector2(-115, -17), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("RoomTypeHint", root.transform, "(택 1)", 10, Color.black, new Vector2(-115, -35), new Vector2(90, 18), TextAnchor.MiddleRight);
        Button("PrivateRoomButton", root.transform, "비밀방", new Vector2(35, -17), new Vector2(100, 28), 15, new Color(1f, 0.55f, 0.55f, 1f));
        Button("PublicRoomButton", root.transform, "공개방", new Vector2(155, -17), new Vector2(90, 28), 15, new Color(0.83f, 0.83f, 0.83f, 1f));

        Text("PasswordLabel", root.transform, "비밀번호", 18, Color.black, new Vector2(-115, -68), new Vector2(90, 28), TextAnchor.MiddleRight);
        Text("PasswordHint", root.transform, "(8자 이상)", 10, Color.black, new Vector2(-115, -86), new Vector2(90, 18), TextAnchor.MiddleRight);
        Input("PasswordInput", root.transform, "abcdefgh", new Vector2(82, -67), new Vector2(225, 28));
        Text("CreateStatus", root.transform, "", 12, new Color(0.65f, 0.16f, 0.12f, 1f), new Vector2(0, -103), new Vector2(330, 22), TextAnchor.MiddleCenter);
        Button("SubmitButton", root.transform, "방 만들기", new Vector2(0, -122), new Vector2(120, 36), 16);
        return root;
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

        LobbySlot(pcRoot, 0, new Vector2(-250, 60), "방장", "방장 / 준비 완료");
        LobbySlot(pcRoot, 1, new Vector2(60, 60), "팀원 1", "대기중...");
        LobbySlot(pcRoot, 2, new Vector2(-250, -65), "팀원 2", "대기중...");
        LobbySlot(pcRoot, 3, new Vector2(60, -65), "팀원 3", "대기중...");

        Text("MapPreview", pcRoot, "현재 선택된\n맵 이미지", 16, Color.black, new Vector2(312, 8), new Vector2(130, 130), TextAnchor.MiddleCenter, new Color(0.82f, 0.82f, 0.82f, 1f));
        Button("LobbyLeaveButton", pcRoot, "나가기", new Vector2(-340, -205), new Vector2(105, 42), 18, Color.white);
        Button("LobbyStartButton", pcRoot, "게임 시작", new Vector2(305, -104), new Vector2(145, 48), 20, new Color(1f, 0.78f, 0.44f, 1f));
        Button("LobbyReadyButton", pcRoot, "준비", new Vector2(305, -104), new Vector2(145, 48), 20, new Color(1f, 0.42f, 0.42f, 1f));
        Text("LobbyStartHint", pcRoot, "팀원이 준비하면 시작할 수 있어요", 16, new Color(0.35f, 0.25f, 0.18f, 1f), new Vector2(305, -145), new Vector2(260, 30), TextAnchor.MiddleCenter);
        Text("LobbyReadyStatus", pcRoot, "준비 상태를 확인하는 중...", 19, new Color(0.18f, 0.12f, 0.08f, 1f), new Vector2(0, -192), new Vector2(470, 36), TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.76f, 0.95f));
        return root;
    }

    private static void BuildMaxPlayersOptions(Transform parent, Vector2 anchored)
    {
        var root = Rect("MaxPlayersOptions", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored + new Vector2(0, -58), new Vector2(90, 96));
        var image = root.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.98f);
        image.raycastTarget = true;

        for (int i = 1; i <= 4; i++)
            Button($"MaxPlayersOption{i}", root, $"{i}명", new Vector2(0, 36 - (i - 1) * 24), new Vector2(88, 24), 14, i == 1 ? new Color(1f, 0.80f, 0.46f, 1f) : Color.white);

        root.gameObject.SetActive(false);
    }

    private static void LobbySlot(Transform parent, int index, Vector2 anchored, string name, string status)
    {
        var slot = Box($"LobbySlot{index}", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchored, new Vector2(295, 102), new Color(1f, 1f, 1f, 0.98f));
        Text($"LobbySlotAvatar{index}", slot.transform, "유저\n캐릭터", 13, new Color(0.25f, 0.25f, 0.25f, 1f), new Vector2(-94, 0), new Vector2(74, 74), TextAnchor.MiddleCenter, new Color(0.86f, 0.86f, 0.86f, 1f));
        Text($"LobbySlotName{index}", slot.transform, name, 19, Color.black, new Vector2(45, 20), new Vector2(165, 30), TextAnchor.MiddleLeft);
        Text($"LobbySlotStatus{index}", slot.transform, status, 17, new Color(0.25f, 0.18f, 0.12f, 1f), new Vector2(45, -21), new Vector2(165, 30), TextAnchor.MiddleRight);
    }

    private static GameObject Root(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Stretch((RectTransform)go.transform);
        return go;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size)
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

    private static Image Image(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchored, Vector2 size, Color color)
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

    private static Button Button(string name, Transform parent, string label, Vector2 anchored, Vector2 size, int fontSize, Color? color = null)
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

    private static InputField Input(string name, Transform parent, string value, Vector2 anchored, Vector2 size)
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

    private static Text Text(string name, Transform parent, string value, int size, Color color, Vector2 anchored, Vector2 sizeDelta, TextAnchor anchor, Color? background = null)
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Transform Find(Transform root, string name)
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

    private static Sprite Sprite(string resourcesPath)
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
