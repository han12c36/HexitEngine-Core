using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// Ready 씬 매니저의 베이스. 상속한 매니저를 Ready 씬에 NetworkObject와 함께 배치합니다. (인스펙터에서 isPersistent를 꺼 주세요)
    /// Host는 항상 ready되어있습니다.
    /// IsAllReady(플레이 가능)는 host를 포함한 minPlayers이상의 플레이어가 ready를 한 상태라면 true를 반환합니다.
    /// Game에서 Ready로 돌아오면 이 씬이 다시 로드되므로 ready상태는 처음부터 다시 시작합니다.
    ///
    /// 게임 시작 (시작 준비 단계)
    /// - 플레이 가능 상태에서 Host가 StartGame_OnlyServer를 호출하면 바로 Game으로 가지 않고 시작 준비(Starting) 단계에 들어갑니다.
    ///   이때 세션이 잠겨 새 참가, 레디 변경, 강퇴가 막힙니다. (상태 프리징)
    /// - 베이스는 얼마나 기다릴지 판단하지 않습니다. 상속한 매니저(게임 코드)가 OnServerStartingBegan에서 원하는 만큼 기다린 뒤
    ///   (1초 정적, 카운트다운 등) ProceedToGame_OnlyServer()를 호출하면 Game 씬으로 이동합니다.
    /// - 시작 준비 중 누가 나가 남은 인원이 MinPlayers 미만이 되면 베이스가 자동으로 취소합니다. (잠금 해제 → Ready 그대로)
    ///   인원이 충분하면 그대로 진행합니다. 방장을 포함해 아무도 직접 취소할 수 없습니다.
    /// - 화면 표시용으로 IsStarting, StartingElapsedSeconds와 OnStartingBegan / OnStartingCanceled를 모두에게 제공합니다.
    ///
    /// 로딩 화면
    /// - 베이스는 Awake에서 로딩을 붙잡기만 하고, 언제 걷을지는 판단하지 않습니다.
    /// - 상속한 매니저(게임 코드)가 자기 초기화(캐릭터 생성 등)를 마친 뒤 ReleaseLoading()을 호출합니다.
    /// - 각자 자기 준비가 끝나면 걷습니다. (방은 사람마다 다른 시점에 들어오므로 기다릴 대상이 없음)
    /// - 캐릭터가 없는 게임이면 OnReadyManagerSpawn에서 바로 ReleaseLoading()을 호출하면 됩니다.
    /// - 호출을 잊으면 엔진의 최대 표시 시간(BootstrapConfig)이 지난 뒤 경고와 함께 걷힙니다.
    /// </summary>
    public abstract class ReadyManagerBase : ServerSingleton<ReadyManagerBase>
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

        /// <summary> 전원 준비에 필요한 최소 인원 (호스트 포함). 호스트는 항상 준비 상태라 혼자일 때 바로 "전원 준비"가 되지 않도록 기본 2명 </summary>
        [SerializeField, Min(1)] private int minPlayers = 2;

        /// <summary> 레디 상태 리스트. read : [모두], write : [서버만] </summary>
        private NetworkList<ReadyData> readyList = new();

        /// <summary> 전원 레디 여부. read : [모두], write : [서버만] </summary>
        private NetworkVariable<bool> n_IsAllReady = new(false);

        /// <summary> 시작 준비 단계 여부. read : [모두], write : [서버만] </summary>
        private NetworkVariable<bool> n_IsStarting = new(false);

        /// <summary> 시작 준비를 시작한 서버 시각 (NetworkManager.ServerTime 기준). read : [모두], write : [서버만] </summary>
        private NetworkVariable<double> n_StartingServerTime = new(0d);

        /// <summary> [서버만] 시작 준비 단계 취소용. 취소되거나 이 매니저가 사라지면 상속한 매니저의 대기도 함께 멈춥니다. </summary>
        private CancellationTokenSource startingCts;

        /// <summary> [모두] 누군가의 준비 상태가 바뀌었을 때 (인원 변동 포함) </summary>
        public event EventHandler OnPlayerReadyStatusChanged;

        /// <summary> [모두] 전원 준비가 되었을 때 </summary>
        public event EventHandler OnAllPlayersReady;

        /// <summary> [모두] 전원 준비가 풀렸을 때 </summary>
        public event EventHandler OnAllPlayersReadyCanceled;

        /// <summary> [나만] 내 Ready 씬 준비가 끝나 로딩 화면이 걷힘 </summary>
        public event EventHandler OnLoadingReleased;

        /// <summary> [모두] 시작 준비 단계에 들어감 (방장 Play) </summary>
        public event EventHandler OnStartingBegan;

        /// <summary> [모두] 시작 준비가 취소됨 (인원 부족, Game 씬 로드 실패). Ready 상태로 돌아갑니다. </summary>
        public event EventHandler OnStartingCanceled;

        /// <summary> [모두] 전원 준비 상태인지 (= 플레이 가능. 최소 인원 이상 + host를 뺀 전원 ready) </summary>
        public bool IsAllReady => n_IsAllReady.Value;

        /// <summary> [모두] 전원 준비에 필요한 최소 인원 (호스트 포함) </summary>
        public int MinPlayers => minPlayers;

        /// <summary> [나만] 로딩 화면을 놓았는지 (Ready 씬 준비 완료) </summary>
        public bool IsLoadingReleased { get; private set; }

        /// <summary> [모두] 시작 준비 단계인지 (방장 Play ~ Game 이동 또는 취소) </summary>
        public bool IsStarting => n_IsStarting.Value;

        /// <summary> [모두] 시작 준비가 시작된 뒤 지난 시간(초). 시작 준비 중이 아니면 0 (카운트다운 표시용) </summary>
        public float StartingElapsedSeconds
        {
            get
            {
                if (!IsStarting || NetworkManager.Singleton == null) return 0f;
                return Mathf.Max(0f, (float)(NetworkManager.Singleton.ServerTime.Time - n_StartingServerTime.Value));
            }
        }

        /// <summary> [모두] 현재 준비 목록 (UI 표시용) </summary>
        public IEnumerable<ReadyData> ReadyDatas
        {
            get
            {
                foreach (var data in readyList) yield return data;
            }
        }

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

        protected HEXITEngine HEXIT { get; private set; }

        #region Lifecycle

        protected sealed override void OnAwake()
        {
            HEXIT = HEXITEngine.Singleton;

            if (HEXIT == null) Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
            // [나만] 씬이 로드되는 동안(Awake) 붙잡아 두고, 준비가 끝나면 놓음 (Ready 씬의 로딩은 이 매니저가 걷음)
            else HEXIT.HoldLoading(this);

            OnReadyManagerAwake();
        }

        protected sealed override void OnNetSpawn()
        {
            readyList.OnListChanged -= HandleReadyListChanged;
            readyList.OnListChanged += HandleReadyListChanged;
            n_IsAllReady.OnValueChanged -= HandleAllReadyChanged;
            n_IsAllReady.OnValueChanged += HandleAllReadyChanged;
            n_IsStarting.OnValueChanged -= HandleStartingChanged;
            n_IsStarting.OnValueChanged += HandleStartingChanged;

            if (IsServer)
            {
                if (HEXIT != null)
                {
                    HEXIT.OnClientConnected -= HandleClientConnected;
                    HEXIT.OnClientConnected += HandleClientConnected;
                    HEXIT.OnHostDisconnected -= HandleClientDisconnected;
                    HEXIT.OnHostDisconnected += HandleClientDisconnected;
                }

                // 이 매니저가 스폰되기 전에 이미 접속해 있던 클라이언트 추가 (Game에서 돌아온 경우 포함)
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    AddPlayer_OnlyServer(clientId);
                }

                EvaluateState_OnlyServer();
            }
            else
            {
                // 클라이언트는 스폰 시점의 초기 목록에 대해 OnListChanged가 오지 않으므로, UI가 처음 그릴 수 있도록 한 번 알림
                OnPlayerReadyStatusChanged?.Invoke(this, EventArgs.Empty);
            }

            OnReadyManagerSpawn();
        }

        protected sealed override void OnNetDespawn()
        {
            readyList.OnListChanged -= HandleReadyListChanged;
            n_IsAllReady.OnValueChanged -= HandleAllReadyChanged;
            n_IsStarting.OnValueChanged -= HandleStartingChanged;

            UnsubscribeServerEvents();
            DisposeStartingToken();

            OnReadyManagerDespawn();
        }

        protected sealed override void OnDestroyed()
        {
            UnsubscribeServerEvents();
            DisposeStartingToken();

            // 붙잡은 채로 사라지면 로딩이 안 걷히므로 반드시 놓음
            if (HEXIT != null) HEXIT.ReleaseLoading(this);

            OnReadyManagerDestroyed();
        }

        private void UnsubscribeServerEvents()
        {
            if (HEXIT != null)
            {
                HEXIT.OnClientConnected -= HandleClientConnected;
                HEXIT.OnHostDisconnected -= HandleClientDisconnected;
            }
        }

        #endregion

        #region Loading

        /// <summary>
        /// [나만] Ready 씬 준비가 끝났으므로 로딩 화면을 놓습니다. 상속한 매니저가 자기 초기화를 마친 뒤 호출합니다. (한 번만 동작)
        /// 스폰 전에는 준비 목록을 받기 전이므로 무시합니다.
        /// </summary>
        protected void ReleaseLoading()
        {
            if (IsLoadingReleased) return;

            if (!IsSpawned)
            {
                Dev.LogWarn("스폰 전에는 로딩을 걷을 수 없습니다. OnReadyManagerSpawn 이후에 호출하세요.");
                return;
            }

            IsLoadingReleased = true;

            if (HEXIT != null) HEXIT.ReleaseLoading(this);

            OnLoadingReleasedHook();
            OnLoadingReleased?.Invoke(this, EventArgs.Empty);
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
            // Play를 누른 뒤(세션 잠김)에는 준비 상태를 바꾸지 않습니다.
            if (IsSessionLocked()) return;

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

        private bool IsSessionLocked() => HEXIT != null && HEXIT.IsSessionLocked;

        #endregion

        #region Server

        /// <summary>
        /// [서버만] 게임 시작을 요청합니다. 방장의 Play 버튼에서 호출합니다. 바로 Game으로 가지 않고 시작 준비 단계에 들어갑니다.
        /// 버튼이 켜져 있던 사이에 누가 들어오거나 레디를 풀 수 있으므로, 실행 시점에 플레이 가능을 다시 계산해서 아니면 무시합니다.
        /// 시작 준비에 들어갔으면 true
        /// </summary>
        public bool StartGame_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 시작할 수 있습니다.");
                return false;
            }

            if (HEXIT == null || IsSessionLocked() || IsStarting) return false;

            EvaluateState_OnlyServer();

            if (!IsAllReady)
            {
                Dev.LogWarn("플레이 가능 상태가 아니어서 시작하지 않습니다.");
                return false;
            }

            // 세션 잠금: 이 순간부터 새 참가, 레디 변경, 강퇴가 막힘 (상태 프리징)
            if (!HEXIT.LockSessionForStart_OnlyServer()) return false;

            DisposeStartingToken();
            startingCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            n_StartingServerTime.Value = NetworkManager.Singleton.ServerTime.Time;
            n_IsStarting.Value = true;

            OnServerStartingBegan(startingCts.Token);
            return true;
        }

        /// <summary>
        /// [서버만] 시작 준비를 마치고 Game 씬으로 이동합니다. 상속한 매니저가 OnServerStartingBegan에서 기다린 뒤 호출합니다.
        /// 이동 직전에 인원을 다시 확인하고, 부족하거나 Game 씬 로드를 시작하지 못하면 취소합니다.
        /// 이동을 시작했으면 true
        /// </summary>
        protected bool ProceedToGame_OnlyServer()
        {
            if (!IsServer || !IsSpawned || !IsStarting) return false;

            if (readyList.Count < minPlayers)
            {
                CancelStarting_OnlyServer();
                return false;
            }

            if (!HEXIT.LoadGame_OnlyServer())
            {
                CancelStarting_OnlyServer();
                return false;
            }

            // 씬이 바뀌며 이 매니저도 사라지므로 시작 준비 상태는 그대로 둠
            DisposeStartingToken();
            return true;
        }

        /// <summary> [서버만] 시작 준비 취소: 대기를 멈추고 잠금을 풀어 Ready 상태로 돌아갑니다. (레디 상태는 유지) </summary>
        private void CancelStarting_OnlyServer()
        {
            if (!IsServer || !IsStarting) return;

            DisposeStartingToken();

            n_IsStarting.Value = false;
            HEXIT.UnlockSession_OnlyServer();

            EvaluateState_OnlyServer();
            OnServerStartingCanceled();
        }

        private void DisposeStartingToken()
        {
            if (startingCts == null) return;

            startingCts.Cancel();
            startingCts.Dispose();
            startingCts = null;
        }

        /// <summary> [서버만] 모두 준비 해제합니다. 호스트는 준비 상태를 유지합니다. </summary>
        public void ResetAllReadyStates_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 준비 상태를 초기화할 수 있습니다.");
                return;
            }

            if (IsSessionLocked()) return;

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

            // 시작 준비 중 인원이 최소 인원 미만이 되면 자동 취소 (충분하면 그대로 진행)
            if (IsStarting && readyList.Count < minPlayers) CancelStarting_OnlyServer();
        }

        /// <summary> 인원 / 준비 상태로 전원 준비 여부를 계산합니다. 바뀌면 모든 컴퓨터에 이벤트가 발생합니다. </summary>
        private void EvaluateState_OnlyServer()
        {
            if (!IsServer || !IsSpawned) return;

            bool isAllReadyNow = readyList.Count >= minPlayers;

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

        private void HandleStartingChanged(bool previousValue, bool newValue)
        {
            if (previousValue == newValue) return;

            if (newValue) OnStartingBegan?.Invoke(this, EventArgs.Empty);
            else OnStartingCanceled?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Hooks

        /// <summary> [모두] Awake. (OnAwake 대신 사용) </summary>
        protected virtual void OnReadyManagerAwake() { }

        /// <summary> [모두] 네트워크 스폰. 준비 목록을 받은 뒤 호출됩니다. (OnNetSpawn 대신 사용) 준비가 끝나면 ReleaseLoading()을 호출합니다. </summary>
        protected virtual void OnReadyManagerSpawn() { }

        /// <summary> [모두] 네트워크 디스폰. (OnNetDespawn 대신 사용) </summary>
        protected virtual void OnReadyManagerDespawn() { }

        /// <summary> [모두] 파괴. (OnDestroyed 대신 사용) </summary>
        protected virtual void OnReadyManagerDestroyed() { }

        /// <summary> [나만] 로딩 화면이 걷힘 (OnLoadingReleased 이벤트보다 먼저) </summary>
        protected virtual void OnLoadingReleasedHook() { }

        /// <summary>
        /// [서버만] 시작 준비 단계에 들어감. 원하는 만큼 기다린 뒤(1초 정적, 카운트다운 등) ProceedToGame_OnlyServer()를 호출합니다.
        /// ct는 시작 준비가 취소되거나 이 매니저가 사라지면 취소되므로 대기에 넘겨 사용합니다. 기본은 기다리지 않고 바로 이동합니다.
        /// </summary>
        protected virtual void OnServerStartingBegan(CancellationToken ct) => ProceedToGame_OnlyServer();

        /// <summary> [서버만] 시작 준비가 취소됨 (인원 부족, Game 씬 로드 실패). 모두에게는 OnStartingCanceled가 발생합니다. </summary>
        protected virtual void OnServerStartingCanceled() { }

        #endregion
    }
}