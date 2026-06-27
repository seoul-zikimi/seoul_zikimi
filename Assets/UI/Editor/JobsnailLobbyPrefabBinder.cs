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
            so.ApplyModifiedPropertiesWithoutUndo();

            SetClick(FindComponent<Button>(root, "CreateButton"), view.OnShowCreateClicked);
            SetClick(FindComponent<Button>(root, "RefreshButton"), view.OnRefreshClicked);
            SetClick(FindComponent<Button>(root, "BackButton"), view.OnBackToMainClicked);
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
            so.ApplyModifiedPropertiesWithoutUndo();

            SetClick(FindComponent<Button>(root, "LobbyLeaveButton"), view.OnLobbyLeaveClicked);
            SetClick(FindComponent<Button>(root, "LobbyStartButton"), view.OnLobbyStartClicked);
            SetClick(FindComponent<Button>(root, "LobbyReadyButton"), view.OnLobbyReadyClicked);
        });
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
