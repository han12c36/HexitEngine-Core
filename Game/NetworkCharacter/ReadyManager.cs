using System;
using Unity.Netcode;

namespace HexitEngine
{
    /// <summary>
    /// Game씬에 배치해야합니다.
    /// Host는 항상 ready되어있습니다.
    /// IsAllReady는 host를 포함한 MIN_PLAYERS이상의 플레이어가 ready를 한 상태라면 true를 반환합니다.
    /// Host가 GameManager의 GameStarted_OnlyServer를 호출하면 ready상태를 초기화 합니다.
    /// </summary>
    public class ReadyManager : ServerSingleton<ReadyManager>
    {
        public struct ReadyData : INetworkSerializable, IEquatable<ReadyData>
        {
            public ulong ClientId;
            public bool IsReady;

            public ReadyData(ulong clientId, bool isReady)
            {
                ClientId = clientId;
                IsReady = isReady;
            }

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ClientId);
                serializer.SerializeValue(ref IsReady);
            }

            public bool Equals(ReadyData other) => ClientId == other.ClientId && IsReady == other.IsReady;
        }

        /// <summary> 전원 준비에 필요한 최소 인원 (호스트 포함). 호스트는 항상 준비 상태라 혼자일 때 바로 "전원 준비"가 되지 않도록 2명부터 </summary>
        private const int MIN_PLAYERS = 2;

        /// <summary> 레디 상태 리스트. read : [모두], write : [서버만] </summary>
        private NetworkList<ReadyData> readyList = new();

        /// <summary> 전원 레디 여부. read : [모두], write : [서버만] </summary>
        private NetworkVariable<bool> n_IsAllReady = new(false);

        /// <summary> [모두] 누군가의 준비 상태가 바뀌었을 때 (인원 변동 포함) </summary>
        public event EventHandler OnPlayerReadyStatusChanged;

        /// <summary> [모두] 전원 준비가 되었을 때 </summary>
        public event EventHandler OnAllPlayersReady;

        /// <summary> [모두] 전원 준비가 풀렸을 때 </summary>
        public event EventHandler OnAllPlayersReadyCanceled;

        /// <summary> [모두] 전원 준비 상태인지 </summary>
        public bool IsAllReady => n_IsAllReady.Value;

        /// <summary> [모두] 준비 목록 인원 </summary>
        public int PlayerCount => readyList.Count;

        /// <summary> [모두] 준비한 인원 </summary>
        public int ReadyCount
        {
            get
            {
                int count = 0;
                foreach (var data in readyList) if (data.IsReady) count++;
                return count;
            }
        }

        private HEXITEngine HEXIT;

        #region Lifecycle

        protected override void OnAwake()
        {
            HEXIT = HEXITEngine.Singleton;
        }

        protected override void OnNetSpawn()
        {
            readyList.OnListChanged -= HandleReadyListChanged;
            readyList.OnListChanged += HandleReadyListChanged;
            n_IsAllReady.OnValueChanged -= HandleAllReadyChanged;
            n_IsAllReady.OnValueChanged += HandleAllReadyChanged;

            if (!IsServer) return;

            if (HEXIT != null)
            {
                HEXIT.OnClientConnected -= HandleClientConnected;
                HEXIT.OnClientConnected += HandleClientConnected;
                HEXIT.OnHostDisconnected -= HandleClientDisconnected;
                HEXIT.OnHostDisconnected += HandleClientDisconnected;
            }

            if (GameManager.HasInstance)
            {
                GameManager.Singleton.OnGameStarted -= HandleGameStarted;
                GameManager.Singleton.OnGameStarted += HandleGameStarted;
            }

            // 이 매니저가 스폰되기 전에 이미 접속해 있던 클라이언트 추가
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayer_OnlyServer(clientId);
            }

            EvaluateState_OnlyServer();
        }

        protected override void OnNetDespawn()
        {
            readyList.OnListChanged -= HandleReadyListChanged;
            n_IsAllReady.OnValueChanged -= HandleAllReadyChanged;

            UnsubscribeServerEvents();
        }

        protected override void OnDestroyed()
        {
            UnsubscribeServerEvents();
        }

        private void UnsubscribeServerEvents()
        {
            if (HEXIT != null)
            {
                HEXIT.OnClientConnected -= HandleClientConnected;
                HEXIT.OnHostDisconnected -= HandleClientDisconnected;
            }

            if (GameManager.HasInstance)
            {
                GameManager.Singleton.OnGameStarted -= HandleGameStarted;
            }
        }

        #endregion

        #region Request

        /// <summary> [모두] 내 준비 상태를 반대로 바꿔 달라고 서버에 요청합니다. </summary>
        public void ToggleReady()
        {
            if (!IsSpawned) return;

            RequestReadyRpc(!IsPlayerReady(NetworkManager.Singleton.LocalClientId));
        }

        /// <summary> [모두] 내 준비 상태를 지정한 값으로 바꿔 달라고 서버에 요청합니다. </summary>
        public void RequestReady(bool isReady)
        {
            if (!IsSpawned) return;

            RequestReadyRpc(isReady);
        }

        [Rpc(SendTo.Server)]
        private void RequestReadyRpc(bool isReady, RpcParams rpcParams = default)
        {
            // 게임 중에는 준비 상태를 바꾸지 않습니다.
            if (IsGameStarted()) return;

            // NGO가 인증한 발신자 ID (변조 불가)
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            // 호스트는 항상 준비 상태
            if (senderClientId == NetworkManager.ServerClientId) return;

            int index = IndexOf(senderClientId);
            if (index < 0 || readyList[index].IsReady == isReady) return;

            var data = readyList[index];
            data.IsReady = isReady;
            readyList[index] = data;

            EvaluateState_OnlyServer();
        }

        #endregion

        #region Query

        /// <summary> [모두] 해당 클라이언트가 준비 상태인지 </summary>
        public bool IsPlayerReady(ulong clientId)
        {
            int index = IndexOf(clientId);
            return index >= 0 && readyList[index].IsReady;
        }

        private int IndexOf(ulong clientId)
        {
            for (int i = 0; i < readyList.Count; i++)
            {
                if (readyList[i].ClientId == clientId) return i;
            }

            return -1;
        }

        private static bool IsGameStarted() => GameManager.HasInstance && GameManager.Singleton.IsGameStarted;

        #endregion

        #region Server

        /// <summary> [서버만] 모두 준비 해제합니다. 호스트는 준비 상태를 유지합니다. </summary>
        public void ResetAllReadyStates_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 준비 상태를 초기화할 수 있습니다.");
                return;
            }

            for (int i = 0; i < readyList.Count; i++)
            {
                var data = readyList[i];
                bool targetState = data.ClientId == NetworkManager.ServerClientId;

                if (data.IsReady == targetState) continue;

                data.IsReady = targetState;
                readyList[i] = data;
            }

            EvaluateState_OnlyServer();
        }

        private void AddPlayer_OnlyServer(ulong clientId)
        {
            if (IndexOf(clientId) >= 0) return;

            // 호스트는 항상 준비 상태로 시작
            readyList.Add(new ReadyData(clientId, clientId == NetworkManager.ServerClientId));
        }

        private void HandleClientConnected(object sender, MultiplayerService.OnClientEventArgs e)
        {
            if (!IsServer || !IsSpawned) return;

            AddPlayer_OnlyServer(e.clientId);
            EvaluateState_OnlyServer();
        }

        private void HandleClientDisconnected(object sender, MultiplayerService.OnClientEventArgs e)
        {
            if (!IsServer || !IsSpawned) return;

            int index = IndexOf(e.clientId);
            if (index < 0) return;

            readyList.RemoveAt(index);
            EvaluateState_OnlyServer();
        }

        /// <summary> 게임이 시작되면 준비 상태를 초기화합니다. (게임이 끝나고 돌아왔을 때 다시 준비부터) </summary>
        private void HandleGameStarted(object sender, EventArgs e)
        {
            if (!IsServer || !IsSpawned) return;

            ResetAllReadyStates_OnlyServer();
        }

        /// <summary> 인원 / 준비 상태로 전원 준비 여부를 계산합니다. 바뀌면 모든 컴퓨터에 이벤트가 발생합니다. </summary>
        private void EvaluateState_OnlyServer()
        {
            if (!IsServer || !IsSpawned) return;

            bool isAllReadyNow = readyList.Count >= MIN_PLAYERS;

            if (isAllReadyNow)
            {
                foreach (var data in readyList)
                {
                    if (data.IsReady) continue;

                    isAllReadyNow = false;
                    break;
                }
            }

            if (n_IsAllReady.Value != isAllReadyNow) n_IsAllReady.Value = isAllReadyNow;
        }

        #endregion

        #region Events

        private void HandleReadyListChanged(NetworkListEvent<ReadyData> changeEvent)
        {
            OnPlayerReadyStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void HandleAllReadyChanged(bool previousValue, bool newValue)
        {
            if (previousValue == newValue) return;

            if (newValue) OnAllPlayersReady?.Invoke(this, EventArgs.Empty);
            else OnAllPlayersReadyCanceled?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}