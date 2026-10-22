using System;
using UnityEngine;
using static HexitEngine.LobbyService;
using static HexitEngine.MultiplayerService;

namespace HexitEngine.Sample
{
    public class MainMenuMessageUI : SessionMessageUI
    {
        [Header("Progress Keys (System_Table)")]
        [SerializeField] private string creatingKey = "SYS_Progress_CreatingRoom";
        [SerializeField] private string joiningKey = "SYS_Progress_JoiningRoom";
        [SerializeField] private string connectingKey = "SYS_Progress_Connecting";

        [Header("Title Keys (System_Table)")]
        [SerializeField] private string createFailedTitleKey = "SYS_Title_CreateRoomFailed";
        [SerializeField] private string joinFailedTitleKey = "SYS_Title_JoinRoomFailed";
        [SerializeField] private string quickJoinFailedTitleKey = "SYS_Title_QuickJoinFailed";
        [SerializeField] private string disconnectedTitleKey = "SYS_Title_ConnectFailed";

        protected override void BindEvents(HEXITEngine hexit)
        {
            hexit.OnCreateLobbyStartedToLobby += HandleCreateStarted;
            hexit.OnJoinStartedToLobby += HandleJoinStarted;
            hexit.OnCreateLobbySucceededToLobby += HandleSucceeded;
            hexit.OnJoinSucceededToLobby += HandleSucceeded;
            hexit.OnCreateLobbyFailedToLobby += HandleCreateFailed;
            hexit.OnJoinFailedToLobby += HandleJoinFailed;
            hexit.OnQuickJoinFailedToLobby += HandleQuickJoinFailed;
            hexit.OnDisconnected += HandleDisconnected;
        }

        protected override void UnbindEvents(HEXITEngine hexit)
        {
            hexit.OnCreateLobbyStartedToLobby -= HandleCreateStarted;
            hexit.OnJoinStartedToLobby -= HandleJoinStarted;
            hexit.OnCreateLobbySucceededToLobby -= HandleSucceeded;
            hexit.OnJoinSucceededToLobby -= HandleSucceeded;
            hexit.OnCreateLobbyFailedToLobby -= HandleCreateFailed;
            hexit.OnJoinFailedToLobby -= HandleJoinFailed;
            hexit.OnQuickJoinFailedToLobby -= HandleQuickJoinFailed;
            hexit.OnDisconnected -= HandleDisconnected;
        }

        /// <summary> 새 요청 시작: 이전 실패가 떠 있어도 진행 중으로 바꿈 </summary>
        private void HandleCreateStarted(object sender, EventArgs e) => ShowProgress(creatingKey);
        private void HandleJoinStarted(object sender, EventArgs e) => ShowProgress(joiningKey);

        /// <summary> 로비 단계 성공. 호스트 시작/서버 접속 후 씬이 바뀔 때까지 유지합니다. </summary>
        private void HandleSucceeded(object sender, EventArgs e) => UpdateProgress(connectingKey);

        private void HandleCreateFailed(object sender, OnLobbyCreateFailedEventArgs e) => ShowNotice(createFailedTitleKey, e.Message);
        private void HandleJoinFailed(object sender, OnLobbyJoinFailedEventArgs e) => ShowNotice(joinFailedTitleKey, e.Message);
        private void HandleQuickJoinFailed(object sender, OnLobbyQuickJoinFailedEventArgs e) => ShowNotice(quickJoinFailedTitleKey, e.Message);

        private void HandleDisconnected(object sender, OnDisconnectedEventArgs e) => ShowNotice(disconnectedTitleKey, e.reason);
    }
}