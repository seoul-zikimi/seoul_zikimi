using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class JobsnailLobbyPrefabBinder
{
    private const string SessionOverlayPath = "Assets/Resources/UI/Jobsnail/Prefabs/JobsnailSessionOverlay.prefab";
    private const string CreateOverlayPath = "Assets/Resources/UI/Jobsnail/Prefabs/JobsnailCreateOverlay.prefab";
    private const string LobbyRoomOverlayPath = "Assets/Resources/UI/Jobsnail/Prefabs/JobsnailLobbyRoomOverlay.prefab";

    [MenuItem("Jobsnail/UI/Bind Runtime UI Prefabs")]
    public static void BindAll()
    {
        BindSessionOverlay();
        BindCreateOverlay();
        BindLobbyRoomOverlay();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[JobsnailLobbyPrefabBinder] Runtime UI prefab bindings saved.");
    }

    private static void BindSessionOverlay()
    {
        EditPrefab(SessionOverlayPath, root =>
        {
            var view = EnsureView(root);
            var so = new SerializedObject(view);
            SetEnum(so, "m_Kind", JobsnailLobbyPrefabView.OverlayKind.SessionList);
            SetRef(so, "m_PcRoot", FindComponent<RectTransform>(root, "PcRoot"));
            SetRef(so, "m_CustomSessionListRoot", FindComponent<RectTransform>(root, "CustomSessionListRoot"));
            SetRef(so, "m_SessionStatus", FindComponent<Text>(root, "SessionStatus"));

            // 목록 카드 / 비밀번호 입력은 런타임 코드 생성 대신 프리팹 안의 요소로 존재해야 한다.
            // 이미 있으면 그대로 두고, 없을 때만 추가한다(디자인한 프리팹을 덮어쓰지 않음).
            EnsureScreenBackground(root);
            SetRef(so, "m_SessionCardTemplate",
                EnsureSessionCardTemplate(Find(root.transform, "CustomSessionListRoot")));
            SetRef(so, "m_JoinPasswordOverlay",
                EnsureJoinPasswordOverlay(Find(root.transform, "PcRoot") ?? root.transform));
            SetRef(so, "m_JoinPasswordInput", FindComponent<InputField>(root, "JoinPasswordInput"));
            so.ApplyModifiedPropertiesWithoutUndo();

            SetClick(FindComponent<Button>(root, "CreateButton"), view.OnShowCreateClicked);
            SetClick(FindComponent<Button>(root, "RefreshButton"), view.OnRefreshClicked);
            SetClick(FindComponent<Button>(root, "BackButton"), view.OnBackToMainClicked);
            SetClick(FindComponent<Button>(root, "JoinPasswordConfirmButton"), view.OnJoinPasswordConfirmClicked);
            SetClick(FindComponent<Button>(root, "JoinPasswordCancelButton"), view.OnJoinPasswordCancelClicked);
        });
    }

    private static void BindCreateOverlay()
    {
        EditPrefab(CreateOverlayPath, root =>
        {
            var view = EnsureView(root);
            var so = new SerializedObject(view);
            SetEnum(so, "m_Kind", JobsnailLobbyPrefabView.OverlayKind.CreateRoom);
            SetRef(so, "m_RoomNameInput", FindComponent<InputField>(root, "RoomNameInput"));
            SetRef(so, "m_PasswordInput", FindComponent<InputField>(root, "PasswordInput"));
            SetRef(so, "m_CreateStatus", FindComponent<Text>(root, "CreateStatus"));
            SetRef(so, "m_MaxPlayersLabel", FindComponent<Text>(root, "MaxPlayersLabel"));
            SetRef(so, "m_MaxPlayersOptions", FindObject(root, "MaxPlayersOptions"));
            SetRef(so, "m_PrivateRoomButtonImage", FindComponent<Image>(root, "PrivateRoomButton"));
            SetRef(so, "m_PublicRoomButtonImage", FindComponent<Image>(root, "PublicRoomButton"));
            SetRef(so, "m_PasswordLabel", FindObject(root, "PasswordLabel"));
            SetRef(so, "m_PasswordHint", FindObject(root, "PasswordHint"));
            so.ApplyModifiedPropertiesWithoutUndo();

            SetClick(FindComponent<Button>(root, "CloseButton"), view.OnCloseCreateClicked);
            SetClick(FindComponent<Button>(root, "SubmitButton"), view.OnSubmitCreateClicked);
            SetClick(FindComponent<Button>(root, "MaxPlayersButton"), view.OnToggleMaxPlayersClicked);
            SetClick(FindComponent<Button>(root, "MaxPlayersOption1"), view.OnSelectMaxPlayers1Clicked);
            SetClick(FindComponent<Button>(root, "MaxPlayersOption2"), view.OnSelectMaxPlayers2Clicked);
            SetClick(FindComponent<Button>(root, "MaxPlayersOption3"), view.OnSelectMaxPlayers3Clicked);
            SetClick(FindComponent<Button>(root, "MaxPlayersOption4"), view.OnSelectMaxPlayers4Clicked);
            SetClick(FindComponent<Button>(root, "PrivateRoomButton"), view.OnPrivateRoomClicked);
            SetClick(FindComponent<Button>(root, "PublicRoomButton"), view.OnPublicRoomClicked);
        });
    }

    private static void BindLobbyRoomOverlay()
    {
        EditPrefab(LobbyRoomOverlayPath, root =>
        {
            var view = EnsureView(root);
            var so = new SerializedObject(view);
            SetEnum(so, "m_Kind", JobsnailLobbyPrefabView.OverlayKind.LobbyRoom);
            SetRef(so, "m_LobbySubtitle", FindComponent<Text>(root, "LobbySubtitle"));
            SetRef(so, "m_LobbyStatusBadgeText", FindComponentInSelfOrChildren<Text>(root, "LobbyStatusBadge"));
            SetRef(so, "m_LobbyStatusBadgeImage", FindComponent<Image>(root, "LobbyStatusBadge"));
            SetRef(so, "m_LobbyStartButton", FindComponent<Button>(root, "LobbyStartButton"));
            SetRef(so, "m_LobbyReadyButton", FindComponent<Button>(root, "LobbyReadyButton"));
            SetRef(so, "m_LobbyStartHint", FindComponent<Text>(root, "LobbyStartHint"));
            SetRef(so, "m_LobbyReadyStatus", FindComponentInSelfOrChildren<Text>(root, "LobbyReadyStatus"));
            SetArray(so, "m_LobbySlotRoots", i => FindObject(root, $"LobbySlot{i}"), 4);
            SetArray(so, "m_LobbySlotNames", i => FindComponent<Text>(root, $"LobbySlotName{i}"), 4);
            SetArray(so, "m_LobbySlotStatuses", i => FindComponent<Text>(root, $"LobbySlotStatus{i}"), 4);

            // 모드 토글 / 맵 선택도 런타임 생성 대신 프리팹 요소로 보장한다.
            EnsureScreenBackground(root);
            var lobbyPcRoot = Find(root.transform, "LobbyPcRoot") ?? root.transform;
            EnsureModeButton(lobbyPcRoot);
            EnsureMapSelect(lobbyPcRoot);
            SetRef(so, "m_LobbyModeButton", FindComponent<Button>(root, "LobbyModeButton"));
            SetRef(so, "m_MapThumbnail", FindComponent<Image>(root, "MapThumb"));
            SetRef(so, "m_MapNameText", FindComponent<Text>(root, "MapNameText"));
            SetRef(so, "m_MapPrevButton", FindComponent<Button>(root, "MapPrevButton"));
            SetRef(so, "m_MapNextButton", FindComponent<Button>(root, "MapNextButton"));
            so.ApplyModifiedPropertiesWithoutUndo();

            SetClick(FindComponent<Button>(root, "LobbyLeaveButton"), view.OnLobbyLeaveClicked);
            SetClick(FindComponent<Button>(root, "LobbyStartButton"), view.OnLobbyStartClicked);
            SetClick(FindComponent<Button>(root, "LobbyReadyButton"), view.OnLobbyReadyClicked);
            SetClick(FindComponent<Button>(root, "LobbyModeButton"), view.OnLobbyModeClicked);
            SetClick(FindComponent<Button>(root, "MapPrevButton"), view.OnMapPrevClicked);
            SetClick(FindComponent<Button>(root, "MapNextButton"), view.OnMapNextClicked);
        });
    }

    // ─────────────── 프리팹에 없는 요소만 추가로 만들어 준다(기존 디자인은 건드리지 않음) ───────────────

    /// <summary>화면을 꽉 채우는 로비 배경(예전 JobsnailLobbySkinner.AddBackground가 하던 일).
    /// 비율 유지 + 넘치는 부분 크롭이라 레터박스 여백이 생기지 않는다.</summary>
    private static void EnsureScreenBackground(GameObject root)
    {
        if (Find(root.transform, "JobsnailScreenBackground") != null)
            return;

        var sprite = JobsnailLobbyUiPrefabGenerator.Sprite("UI_pngs/2.sesh/Session_BG");
        if (sprite == null)
            return;

        var background = JobsnailLobbyUiPrefabGenerator.Image("JobsnailScreenBackground", root.transform, sprite, Color.white);
        background.preserveAspect = false;   // 종횡비는 AspectRatioFitter가 담당
        background.raycastTarget = false;

        var rt = background.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        var fitter = background.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1.777f;

        background.transform.SetAsFirstSibling();
    }

    /// <summary>세션 목록 카드 원본. 런타임에는 이 템플릿을 복제해서 목록을 채운다.</summary>
    private static JobsnailSessionCardView EnsureSessionCardTemplate(Transform listRoot)
    {
        if (listRoot == null)
        {
            Debug.LogWarning("[JobsnailLobbyPrefabBinder] CustomSessionListRoot가 없어 세션 카드 템플릿을 만들지 못했습니다.");
            return null;
        }

        var existing = Find(listRoot, "SessionCardTemplate");
        var card = existing != null
            ? existing.GetComponent<JobsnailSessionCardView>()
            : null;

        if (existing == null)
        {
            var go = new GameObject("SessionCardTemplate", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(listRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(360, 118);

            var image = go.GetComponent<Image>();
            image.sprite = JobsnailLobbyUiPrefabGenerator.Sprite("UI_pngs/2.sesh/SessionList_Unit");
            image.color = Color.white;
            image.preserveAspect = image.sprite != null;
            image.raycastTarget = true;
            go.GetComponent<Button>().targetGraphic = image;

            JobsnailLobbyUiPrefabGenerator.Text("SessionCardName", go.transform, "이름 없는 방", 17, Color.black,
                new Vector2(0, 25), new Vector2(300, 32), TextAnchor.MiddleCenter);
            JobsnailLobbyUiPrefabGenerator.Text("SessionCardTag", go.transform, "공개방", 14, Color.black,
                new Vector2(-90, -28), new Vector2(90, 26), TextAnchor.MiddleCenter, new Color(0.58f, 1f, 0.54f, 1f));
            JobsnailLobbyUiPrefabGenerator.Text("SessionCardCount", go.transform, "인원 0 / 4", 14, Color.black,
                new Vector2(105, -28), new Vector2(110, 26), TextAnchor.MiddleCenter);

            go.SetActive(false);
            existing = go.transform;
        }

        if (card == null)
            card = existing.gameObject.GetComponent<JobsnailSessionCardView>()
                   ?? existing.gameObject.AddComponent<JobsnailSessionCardView>();

        var tagText = FindComponentInSelfOrChildren<Text>(existing.gameObject, "SessionCardTag");
        var cardSo = new SerializedObject(card);
        SetRef(cardSo, "m_Button", existing.GetComponent<Button>());
        SetRef(cardSo, "m_NameText", FindComponentInSelfOrChildren<Text>(existing.gameObject, "SessionCardName"));
        SetRef(cardSo, "m_TagText", tagText);
        SetRef(cardSo, "m_TagBackground", FindComponent<Image>(existing.gameObject, "SessionCardTag"));
        SetRef(cardSo, "m_CountText", FindComponentInSelfOrChildren<Text>(existing.gameObject, "SessionCardCount"));
        cardSo.ApplyModifiedPropertiesWithoutUndo();

        return card;
    }

    /// <summary>비밀방 입장용 비밀번호 팝업.</summary>
    private static GameObject EnsureJoinPasswordOverlay(Transform parent)
    {
        if (parent == null)
            return null;

        var existing = Find(parent, "JoinPasswordOverlay");
        if (existing != null)
            return existing.gameObject;

        var overlay = JobsnailLobbyUiPrefabGenerator.Rect("JoinPasswordOverlay", parent,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var dim = JobsnailLobbyUiPrefabGenerator.Box("JoinPasswordDim", overlay,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.25f));
        dim.raycastTarget = true;   // 뒤쪽 카드 클릭 차단

        JobsnailLobbyUiPrefabGenerator.Box("JoinPasswordPanel", overlay,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 190),
            new Color(1f, 1f, 1f, 0.98f));
        JobsnailLobbyUiPrefabGenerator.Text("JoinPasswordTitle", overlay, "비밀번호 입력", 22, Color.black,
            new Vector2(0, 55), new Vector2(220, 36), TextAnchor.MiddleCenter);

        var input = JobsnailLobbyUiPrefabGenerator.Input("JoinPasswordInput", overlay, "",
            new Vector2(0, 8), new Vector2(230, 30));
        input.contentType = InputField.ContentType.Standard;
        input.characterLimit = 32;

        JobsnailLobbyUiPrefabGenerator.Button("JoinPasswordConfirmButton", overlay, "입장",
            new Vector2(-65, -55), new Vector2(100, 34), 16);
        JobsnailLobbyUiPrefabGenerator.Button("JoinPasswordCancelButton", overlay, "취소",
            new Vector2(65, -55), new Vector2(100, 34), 16, Color.white);

        overlay.gameObject.SetActive(false);
        return overlay.gameObject;
    }

    /// <summary>방장 전용 게임 모드 순환 토글.</summary>
    private static void EnsureModeButton(Transform lobbyPcRoot)
    {
        if (lobbyPcRoot == null || Find(lobbyPcRoot, "LobbyModeButton") != null)
            return;

        JobsnailLobbyUiPrefabGenerator.Button("LobbyModeButton", lobbyPcRoot, "모드 · 타임어택",
            new Vector2(305, -190), new Vector2(145, 40), 13, new Color(1f, 0.78f, 0.44f, 1f));
    }

    /// <summary>맵 선택(방장 ◀▶ + 전원 썸네일·이름).</summary>
    private static void EnsureMapSelect(Transform lobbyPcRoot)
    {
        if (lobbyPcRoot == null || Find(lobbyPcRoot, "MapThumb") != null)
            return;

        var thumb = JobsnailLobbyUiPrefabGenerator.Image("MapThumb", lobbyPcRoot, null, Color.white);
        var rt = thumb.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(312, 8);
        rt.sizeDelta = new Vector2(130, 130);
        thumb.preserveAspect = true;
        thumb.raycastTarget = false;
        thumb.enabled = false;   // 썸네일이 없으면 기존 회색 자리 그대로

        JobsnailLobbyUiPrefabGenerator.Text("MapNameText", lobbyPcRoot, "", 15,
            new Color(0.25f, 0.17f, 0.10f, 1f), new Vector2(312, -66), new Vector2(170, 26), TextAnchor.MiddleCenter);
        JobsnailLobbyUiPrefabGenerator.Button("MapPrevButton", lobbyPcRoot, "◀",
            new Vector2(232, 8), new Vector2(26, 44), 15, new Color(1f, 0.92f, 0.76f, 0.95f));
        JobsnailLobbyUiPrefabGenerator.Button("MapNextButton", lobbyPcRoot, "▶",
            new Vector2(392, 8), new Vector2(26, 44), 15, new Color(1f, 0.92f, 0.76f, 0.95f));
    }

    private static void EditPrefab(string path, System.Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static JobsnailLobbyPrefabView EnsureView(GameObject root)
    {
        var view = root.GetComponent<JobsnailLobbyPrefabView>();
        if (view == null)
            view = root.AddComponent<JobsnailLobbyPrefabView>();
        return view;
    }

    private static void SetEnum<T>(SerializedObject so, string propertyName, T value) where T : System.Enum
    {
        so.FindProperty(propertyName).enumValueIndex = System.Convert.ToInt32(value);
    }

    private static void SetRef(SerializedObject so, string propertyName, Object value)
    {
        so.FindProperty(propertyName).objectReferenceValue = value;
    }

    private static void SetArray(SerializedObject so, string propertyName, System.Func<int, Object> valueAt, int count)
    {
        var property = so.FindProperty(propertyName);
        property.arraySize = count;
        for (int i = 0; i < count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = valueAt(i);
    }

    private static void SetClick(Button button, UnityAction action)
    {
        if (button == null)
        {
            Debug.LogWarning("[JobsnailLobbyPrefabBinder] 버튼을 찾지 못해서 OnClick 바인딩을 건너뜁니다.");
            return;
        }

        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);

        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    private static GameObject FindObject(GameObject root, string name)
    {
        var t = Find(root.transform, name);
        return t != null ? t.gameObject : null;
    }

    private static T FindComponent<T>(GameObject root, string name) where T : Component
    {
        var t = Find(root.transform, name);
        return t != null ? t.GetComponent<T>() : null;
    }

    private static T FindComponentInSelfOrChildren<T>(GameObject root, string name) where T : Component
    {
        var t = Find(root.transform, name);
        return t != null ? t.GetComponentInChildren<T>(true) : null;
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
}
