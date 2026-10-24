using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static HexitEngine.MultiplayerService;

namespace HexitEngine.Sample
{
    public class ReadyUI : MonoBehaviour
    {
        private const string STARTING_TEXT = "Starting game...";

        private readonly List<ReadyListItem> items = new();

        [Header("Room Info")]
        [SerializeField] private TMP_Text roomNameText;
        [SerializeField] private TMP_Text joinCodeText;
        [SerializeField] private TMP_Text statusText;

        [Header("Ready List")]
        [SerializeField] private Transform listContent;
        [SerializeField] private ReadyListItem itemPrefab;

        [Header("Buttons")]
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonText;
        [SerializeField] private Button playButton;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private Button inviteButton;
        [SerializeField] private Button leaveButton;

        private HEXITEngine hexit;
        private ReadyManagerBase readyManagerBase;

        private bool isLeaving;

        private void Awake()
        {
            if (readyButton != null) readyButton.onClick.AddListener(ToggleReady);
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (copyCodeButton != null) copyCodeButton.onClick.AddListener(CopyJoinCode);
            if (inviteButton != null) inviteButton.onClick.AddListener(Invite);
            if (leaveButton != null) leaveButton.onClick.AddListener(Leave);
        }

        private void Start()
        {
            hexit = HEXITEngine.Singleton;
            readyManagerBase = ReadyManagerBase.Singleton;

            if (hexit == null) Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
            if (readyManagerBase == null) Dev.LogError("ReadyManager가 Ready 씬에 없습니다.");

            if (hexit != null) hexit.OnPlayerDatasChanged += HandlePlayerDatasChanged;

            if (readyManagerBase != null)
            {
                readyManagerBase.OnPlayerReadyStatusChanged += HandleReadyChanged;
                readyManagerBase.OnAllPlayersReady += HandleReadyChanged;
                readyManagerBase.OnAllPlayersReadyCanceled += HandleReadyChanged;
                readyManagerBase.OnStartingBegan += HandleReadyChanged;
                readyManagerBase.OnStartingCanceled += HandleReadyChanged;
            }

            RefreshRoomInfo();
            Refresh();
        }

        private void OnDestroy()
        {
            if (readyButton != null) readyButton.onClick.RemoveListener(ToggleReady);
            if (playButton != null) playButton.onClick.RemoveListener(Play);
            if (copyCodeButton != null) copyCodeButton.onClick.RemoveListener(CopyJoinCode);
            if (inviteButton != null) inviteButton.onClick.RemoveListener(Invite);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(Leave);

            if (hexit != null) hexit.OnPlayerDatasChanged -= HandlePlayerDatasChanged;

            if (readyManagerBase != null)
            {
                readyManagerBase.OnPlayerReadyStatusChanged -= HandleReadyChanged;
                readyManagerBase.OnAllPlayersReady -= HandleReadyChanged;
                readyManagerBase.OnAllPlayersReadyCanceled -= HandleReadyChanged;
                readyManagerBase.OnStartingBegan -= HandleReadyChanged;
                readyManagerBase.OnStartingCanceled -= HandleReadyChanged;
            }
        }

        /// <summary> 내가 레디 상태를 바꿔 달라고 서버에 요청 </summary>
        public void ToggleReady()
        {
            if (readyManagerBase == null || isLeaving) return;

            readyManagerBase.ToggleReady();
        }

        /// <summary> 호스트가 게임을 시작 </summary>
        public void Play()
        {
            if (readyManagerBase == null || hexit == null || !hexit.IsServer || isLeaving) return;

            // 시작 준비에 들어가면 세션이 잠기므로 Play, 레디, 강퇴 버튼이 꺼지도록 다시 그림
            if (readyManagerBase.StartGame_OnlyServer()) Refresh();
        }

        /// <summary> 내가 방 초대 코드를 복사 </summary>
        public void CopyJoinCode()
        {
            if (hexit == null || !hexit.HasJoinedLobby) return;

            hexit.CopyJoinCodeToClipboard();
        }

        /// <summary> 내가 스팀 오버레이를 켬 </summary>
        public void Invite()
        {
            if (hexit == null || !hexit.HasJoinedLobby) return;

            hexit.OpenInviteOverlay();
        }

        /// <summary> 호스트가 플레이어를 강퇴 </summary>
        private void Kick(ulong clientId)
        {
            if (hexit == null || !hexit.IsServer || isLeaving) return;

            hexit.KickPlayer(clientId);
        }

        /// <summary> 내가 방을 떠납니다. </summary>
        public void Leave()
        {
            if (hexit == null || isLeaving) return;

            isLeaving = true;
            Refresh();

            hexit.LeaveLobby();
        }

        private void HandleReadyChanged(object sender, EventArgs e) => Refresh();

        /// <summary> 이름이 바뀌거나 인원이 바뀌면 목록 이름과 방 정보를 다시 그립니다. </summary>
        private void HandlePlayerDatasChanged(object sender, PlayerDatasChangedEventArgs e)
        {
            RefreshRoomInfo();
            Refresh();
        }

        private void RefreshRoomInfo()
        {
            if (hexit == null || !hexit.HasJoinedLobby) return;

            var lobby = hexit.JoinedLobby;
            string roomName = lobby.GetData(LobbyKeys.Name);

            if (roomNameText != null) roomNameText.text = string.IsNullOrEmpty(roomName) ? "(No Name)" : roomName;
            if (joinCodeText != null) joinCodeText.text = $"{lobby.GetData(LobbyKeys.JoinCode)}";
        }

        /// <summary> 버튼 상태, 진행 문구, 레디 목록을 다시 그립니다. </summary>
        private void Refresh()
        {
            bool isHost = hexit != null && hexit.IsServer;
            bool isReady = readyManagerBase != null && readyManagerBase.IsPlayerReady(LocalClientId());
            bool isPlayable = readyManagerBase != null && readyManagerBase.IsAllReady;
            bool isLocked = hexit != null && hexit.IsSessionLocked;

            // 시작 준비 중에는 모두 상태 변경 불가 (클라이언트는 IsSessionLocked를 모르므로 동기화된 IsStarting으로 판단)
            bool isStarting = readyManagerBase != null && readyManagerBase.IsStarting;

            if (readyButton != null)
            {
                readyButton.gameObject.SetActive(!isHost);
                readyButton.interactable = readyManagerBase != null && !isStarting && !isLeaving;
            }

            if (readyButtonText != null) readyButtonText.text = isReady ? "Cancel" : "Ready";

            if (playButton != null)
            {
                playButton.gameObject.SetActive(isHost);
                playButton.interactable = isPlayable && !isLocked && !isStarting && !isLeaving;
            }

            if (copyCodeButton != null) copyCodeButton.interactable = !isLeaving;
            if (inviteButton != null) inviteButton.interactable = !isLeaving;
            if (leaveButton != null) leaveButton.interactable = !isLeaving;

            RefreshStatus(isPlayable, isStarting);
            RefreshList();
        }

        private void RefreshStatus(bool isPlayable, bool isStarting)
        {
            if (statusText == null || readyManagerBase == null) return;

            if (isStarting)
            {
                statusText.text = STARTING_TEXT;
                return;
            }

            int playerCount = readyManagerBase.PlayerCount;
            int minPlayers = readyManagerBase.MinPlayers;

            if (playerCount < minPlayers) statusText.text = $"Waiting for players ({playerCount}/{minPlayers})";
            else if (!isPlayable) statusText.text = $"Waiting for ready ({readyManagerBase.ReadyCount}/{playerCount})";
            else statusText.text = "Ready to play";
        }

        /// <summary> 레디 목록을 그립니다. 항목 오브젝트는 재사용합니다. </summary>
        private void RefreshList()
        {
            if (readyManagerBase == null || listContent == null || itemPrefab == null) return;

            int count = 0;
            ulong localClientId = LocalClientId();
            var multiplayer = MultiplayerService.Singleton;

            // [서버만] 강퇴는 방장 화면에서, Play를 누르기 전까지만 가능 (시작 준비 중 불가)
            bool canKick = hexit != null && hexit.IsServer && !hexit.IsSessionLocked && !readyManagerBase.IsStarting && !isLeaving;

            foreach (var data in readyManagerBase.ReadyDatas)
            {
                if (items.Count <= count) items.Add(Instantiate(itemPrefab, listContent));

                string playerName = multiplayer != null && multiplayer.TryGetPlayerDataByClientId(data.ClientId, out var playerData)
                    ? playerData.PlayerName
                    : "Player";

                bool isHost = data.ClientId == NetworkManager.ServerClientId;

                items[count].gameObject.SetActive(true);
                items[count].Bind(data.ClientId, playerName, data.IsReady, isHost, data.ClientId == localClientId, canKick && !isHost ? Kick : null);
                count++;
            }

            for (int i = count; i < items.Count; i++) items[i].gameObject.SetActive(false);
        }

        private static ulong LocalClientId() => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
    }
}