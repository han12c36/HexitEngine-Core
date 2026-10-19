using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class SampleCode : MonoBehaviour
    {
        private string roomName = "A";
        private int maxCapacity = 4;

        [SerializeField] private Button startHostButton;
        [SerializeField] private Button startClientButton;

        private string displayName => $"Player_{NetworkManager.Singleton.LocalClientId}";
        private HEXITEngine hexit;

        private void Start()
        {
            hexit = HEXITEngine.Singleton;

            if (hexit == null)
            {
                Dev.LogError("[Test] HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
                return;
            }

            if (startHostButton != null) startHostButton.onClick.AddListener(StartHost);
            if (startClientButton != null) startClientButton.onClick.AddListener(StartClient);

            hexit.OnCreateLobbySucceededToLobby += HandleCreateSucceeded;
            hexit.OnCreateLobbyFailedToLobby += HandleCreateFailed;
            hexit.OnJoinSucceededToLobby += HandleJoinSucceeded;
            hexit.OnJoinFailedToLobby += HandleJoinFailed;
            hexit.OnClientConnected += HandleClientConnected;
            hexit.OnDisconnected += HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (startHostButton != null) startHostButton.onClick.RemoveListener(StartHost);
            if (startClientButton != null) startClientButton.onClick.RemoveListener(StartClient);

            if (hexit == null) return;

            hexit.OnCreateLobbySucceededToLobby -= HandleCreateSucceeded;
            hexit.OnCreateLobbyFailedToLobby -= HandleCreateFailed;
            hexit.OnJoinSucceededToLobby -= HandleJoinSucceeded;
            hexit.OnJoinFailedToLobby -= HandleJoinFailed;
            hexit.OnClientConnected -= HandleClientConnected;
            hexit.OnDisconnected -= HandleDisconnected;
        }

        #region Buttons

        /// <summary> 이름이 "a"인 공개 방을 만들고 호스트로 시작합니다. </summary>
        public void StartHost()
        {
            if (hexit == null) return;

            ApplyDisplayName();

            Dev.Log($"방 '{roomName}' 생성 요청 (최대 {maxCapacity}명)");
            hexit.CreateLobby(roomName, maxCapacity, isPrivate: false);
        }

        /// <summary> 로비 목록에서 이름이 "a"인 방을 찾아 참가합니다. </summary>
        public void StartClient()
        {
            if (hexit == null) return;

            ApplyDisplayName();

            Dev.Log($"방 '{roomName}' 검색 시작");
            hexit.RefreshLobbyList();
        }

        #endregion

        #region Event Logs

        private void HandleCreateSucceeded(object sender, EventArgs e) => Dev.Log($"방 '{roomName}' 생성 성공");
        private void HandleCreateFailed(object sender, LobbyService.OnLobbyCreateFailedEventArgs e) => Dev.LogError($"방 생성 실패: {e.Message}");
        private void HandleJoinSucceeded(object sender, EventArgs e) => Dev.Log($"방 '{roomName}' 로비 참가 성공 (네트워크 접속 대기)");
        private void HandleJoinFailed(object sender, LobbyService.OnLobbyJoinFailedEventArgs e) => Dev.LogError($"방 참가 실패: {e.Message}");
        private void HandleClientConnected(object sender, MultiplayerService.OnClientEventArgs e) => Dev.Log($"네트워크 접속 완료 (clientId: {e.clientId})");
        private void HandleDisconnected(object sender, MultiplayerService.OnDisconnectedEventArgs e) => Dev.LogWarn($"연결 끊김: {e.reason}");

        #endregion

        /// <summary> 인스펙터에 인게임 이름을 넣었다면 방 생성/참가 직전에 설정합니다. </summary>
        private void ApplyDisplayName()
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;

            var multiplayer = MultiplayerService.Singleton;
            if (multiplayer != null) multiplayer.LocalDisplayName = displayName;
        }
    }
}