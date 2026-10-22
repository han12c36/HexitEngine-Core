namespace HexitEngine
{
    /// <summary>
    /// 시스템 문구 키 모음. 키 값은 Localization 테이블(TABLE)의 키와 같습니다.
    /// - 서비스(Lobby, Multiplayer)는 번역하지 않은 키만 보내고, 번역은 HEXITEngine.GetSystemText가 합니다.
    /// - 서비스끼리 서로를 참조하지 않도록, 어느 서비스에도 속하지 않는 독립 클래스로 둡니다.
    /// - 샘플 UI의 제목 키는 여기에 두지 않고 각 UI의 인스펙터 값으로 둡니다. (같은 SYS_ 접두어 사용)
    /// </summary>
    public static class SystemKeys
    {
        public const string TABLE = "System_Table";

        /// <summary> 시스템 키 접두어. 이 접두어가 없는 문자열(트랜스포트 원문 등)은 번역하지 않고 그대로 표시합니다. </summary>
        public const string PREFIX = "SYS_";

        //============= 연결 끊김 / 거절 사유 (MultiplayerService) ==============================================================

        public const string GameAlreadyStarted = "SYS_GameAlreadyStarted";
        public const string HostStartFailed = "SYS_HostStartFailed";
        public const string ClientStartFailed = "SYS_ClientStartFailed";
        public const string DisconnectedFromServer = "SYS_DisconnectedFromServer";
        public const string ServerConnectionTimeout = "SYS_ServerConnectionTimeout";
        public const string HostLeftSession = "SYS_HostLeftSession";

        //============= 로비 실패 사유 (LobbyService) ==========================================================================

        public const string AlreadyJoiningLobby = "SYS_AlreadyJoiningLobby";
        public const string SteamLoginRequired = "SYS_SteamLoginRequired";
        public const string LobbyCreateFailed = "SYS_LobbyCreateFailed";
        public const string InvalidLobbyId = "SYS_InvalidLobbyId";
        public const string InvalidJoinCode = "SYS_InvalidJoinCode";
        public const string NoAvailableRoom = "SYS_NoAvailableRoom";
        public const string RoomNotFound = "SYS_RoomNotFound";
        public const string RoomFull = "SYS_RoomFull";
        public const string LobbyJoinFailed = "SYS_LobbyJoinFailed";

        //============= 세션 요청 실패 사유 (HEXITEngine) ======================================================================

        public const string PreviousSessionNotEnded = "SYS_PreviousSessionNotEnded";
        public const string ReadySceneLoadFailed = "SYS_ReadySceneLoadFailed";
        public const string CreateCanceled = "SYS_CreateCanceled";
        public const string JoinCanceled = "SYS_JoinCanceled";
        public const string QuickJoinCanceled = "SYS_QuickJoinCanceled";

        //============= 채팅 시스템 메시지 (ChatManager). {0} = 플레이어 이름 ==================================================

        public const string ChatPlayerJoined = "SYS_Chat_PlayerJoined";
        public const string ChatPlayerLeft = "SYS_Chat_PlayerLeft";

        /// <summary> 시스템 키인지 (SYS_ 접두어) </summary>
        public static bool IsSystemKey(string key) => !string.IsNullOrEmpty(key) && key.StartsWith(PREFIX, System.StringComparison.Ordinal);
    }
}