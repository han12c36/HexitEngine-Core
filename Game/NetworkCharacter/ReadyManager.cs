using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// Ready씬에 배치해야합니다. (인스펙터에서 isPersistent를 꺼 주세요)
    /// Host는 항상 ready되어있습니다.
    /// IsAllReady(플레이 가능)는 host를 포함한 minPlayers이상의 플레이어가 ready를 한 상태라면 true를 반환합니다.
    /// 플레이 가능 상태에서 Host가 StartGame_OnlyServer를 호출하면 그 순간 세션이 잠기고 Game 씬으로 이동합니다.
    /// Game에서 Ready로 돌아오면 이 씬이 다시 로드되므로 ready상태는 처음부터 다시 시작합니다.
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

        /// <summary> 전원 준비에 필요한 최소 인원 (호스트 포함). 호스트는 항상 준비 상태라 혼자일 때 바로 "전원 준비"가 되지 않도록 기본 2명 </summary>
        [SerializeField, Min(1)] private int minPlayers = 2;

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

        /// <summary> [모두] 전원 준비 상태인지 (= 플레이 가능. 최소 인원 이상 + host를 뺀 전원 ready) </summary>
        public bool IsAllReady => n_IsAllReady.Value;

        /// <summary> [모두] 전원 준비에 필요한 최소 인원 (호스트 포함) </summary>
        public int MinPlayers => minPlayers;

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

            // 이 매니저가 스폰되기 전에 이미 접속해 있던 클라이언트 추가 (Game에서 돌아온 경우 포함)
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayer_OnlyServer(clientId);
            }

            EvaluateState_OnlyServer();

            // 클라이언트는 스폰 시점의 초기 목록에 대해 OnListChanged가 오지 않으므로, UI가 처음 그릴 수 있도록 한 번 알림
            if (!IsServer) OnPlayerReadyStatusChanged?.Invoke(this, EventArgs.Empty);
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
        /// [서버만] 게임을 시작합니다. 방장의 Play 버튼에서 호출합니다.
        /// 버튼이 켜져 있던 사이에 누가 들어오거나 레디를 풀 수 있으므로, 실행 시점에 플레이 가능을 다시 계산해서 아니면 무시합니다.
        /// </summary>
        public bool StartGame_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 시작할 수 있습니다.");
                return false;
            }

            if (HEXIT == null || IsSessionLocked()) return false;

            EvaluateState_OnlyServer();

            if (!IsAllReady)
            {
                Dev.LogWarn("플레이 가능 상태가 아니어서 시작하지 않습니다.");
                return false;
            }

            HEXIT.StartGame_OnlyServer();
            return true;
        }

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

        #endregion
    }
}