using System;

namespace SeoulZikimi.UI.New
{
    public enum UiNewScreen
    {
        RoomList,
        CreateRoom,
        Password,
        Lobby,
        HostLeaveWarning
    }

    public enum RoomVisibility
    {
        Public,
        Private
    }

    public enum RoomListFilter
    {
        All,
        Public,
        Private
    }

    public readonly struct CreateRoomRequest
    {
        public CreateRoomRequest(string roomName, RoomVisibility visibility, string password, int mapIndex, int modeIndex, bool weatherEnabled)
        {
            RoomName = roomName;
            Visibility = visibility;
            Password = password;
            MapIndex = mapIndex;
            ModeIndex = modeIndex;
            WeatherEnabled = weatherEnabled;
        }

        public string RoomName { get; }
        public RoomVisibility Visibility { get; }
        public string Password { get; }
        public int MapIndex { get; }
        public int ModeIndex { get; }
        public bool WeatherEnabled { get; }
    }

    public readonly struct UiNewSessionRoom
    {
        public UiNewSessionRoom(string sessionId, string name, string passwordHash, int joined, int maxPlayers, string mapName)
        {
            SessionId = sessionId;
            Name = name;
            PasswordHash = passwordHash;
            Joined = joined;
            MaxPlayers = maxPlayers;
            MapName = mapName;
        }

        public string SessionId { get; }
        public string Name { get; }

        /// <summary>방 생성 때 세션 프로퍼티에 올려둔 비밀번호 해시. 입장 검증은 이 값과 비교한다.</summary>
        public string PasswordHash { get; }
        public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
        public int Joined { get; }
        public int MaxPlayers { get; }
        public string MapName { get; }
    }

    public interface IUiNewScreenRouter
    {
        UiNewScreen Current { get; }
        void Show(UiNewScreen screen);
    }

    public interface IRoomListActions
    {
        event Action RefreshRequested;
        event Action<RoomListFilter> FilterChanged;
        event Action<UiNewSessionRoom> RoomJoinRequested;
    }

    public interface IRoomCreationActions
    {
        event Action<CreateRoomRequest> CreateRequested;
    }

    public interface IPasswordEntryActions
    {
        event Action<string> PasswordSubmitted;
    }

    public interface ILobbyActions
    {
        event Action LeaveRequested;
        event Action ReadyRequested;
        event Action StartRequested;
        event Action<int> QuickChatRequested;
        event Action<string> TextChatRequested;
    }
}
