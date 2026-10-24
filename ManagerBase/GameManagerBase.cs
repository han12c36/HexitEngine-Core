using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// Game 씬 매니저의 베이스. 상속한 매니저(게임 코드)를 Game 씬에 NetworkObject와 함께 배치합니다. (Is Persistent 끄기)
    ///
    /// 게임 플레이어 생성 (필수)
    /// - 플레이어의 씬 로드가 끝나면 SpawnGamePlayer_OnlyServer가 호출됩니다. 상속한 매니저가 반드시 구현합니다.
    ///
    /// 로딩 화면
    /// - 베이스는 Awake에서 로딩을 붙잡기만 하고, 언제 걷을지는 판단하지 않습니다.
    /// - 상속한 매니저가 자기 초기화(플레이어 생성, 맵 준비 등)를 마친 뒤 ReleaseLoadingForAll_OnlyServer()를 호출하면
    ///   모든 컴퓨터에서 동시에 걷히고 OnAllGamePlayersLoaded가 발생합니다. (동시 시작)
    /// - 판단 재료로 AreAllGamePlayersReady_OnlyServer()를 제공합니다.
    /// - 호출을 잊으면 엔진의 최대 표시 시간(BootstrapConfig)이 지난 뒤 경고와 함께 걷힙니다.
    /// </summary>
    public abstract class GameManagerBase : ServerSingleton<GameManagerBase>
    {

        private NetworkVariable<bool> n_IsGameStarted = new(value: false);

        protected HEXITEngine HEXIT { get; private set; }

        /// <summary> [서버만] Ready로 돌아가는 중인지 (게임 종료 중복 호출 방지) </summary>
        private bool isEnding;

        /// <summary> [서버만] 로딩 해제를 이미 보냈는지 </summary>
        private bool hasReleasedLoading;

        /// <summary> [모두] 로딩 화면이 걷혔는지 (ReleaseLoadingForAll_OnlyServer 수신) </summary>
        public bool IsAllGamePlayersLoaded { get; private set; }

        /// <summary> [모두] 게임이 시작되었는지 </summary>
        public bool IsGameStarted => n_IsGameStarted.Value;

        /// <summary> [모두] 준비된 모든 게임 플레이어 (playerId 순) </summary>
        public IReadOnlyList<ClientGamePlayer> GamePlayers => ClientGamePlayer.ClientGamePlayers;

        /// <summary> [모두] 준비된 게임 플레이어 수 </summary>
        public int GamePlayerCount => ClientGamePlayer.ClientGamePlayerCount;

        /// <summary> [나만] 이 컴퓨터의 내 게임 플레이어. 아직 준비 전이면 null </summary>
        public ClientGamePlayer LocalGamePlayer => ClientGamePlayer.LocalClientGamePlayer;


        /// <summary> 게임시작 이벤트 - All Client </summary>
        public event EventHandler OnGameStarted;

        /// <summary> 게임종료 이벤트 - All Client </summary>
        public event EventHandler OnGameEnded;

        /// <summary> [모두] 게임 플레이어 준비됨 (스폰 + PlayerData 보장). 내 플레이어는 player.IsLocal로 구분합니다. </summary>
        public event Action<ClientGamePlayer> OnGamePlayerReady;

        /// <summary> [모두] 게임 플레이어가 사라지기 직전 (주인 이탈, 씬 이동). 아직 GamePlayers에 남아 있습니다. </summary>
        public event Action<ClientGamePlayer> OnGamePlayerDespawned;

        /// <summary> [모두] 로딩 화면이 걷힘. 모든 컴퓨터에서 거의 동시에 발생합니다. (카운트다운 등 동시 시작용) </summary>
        public event EventHandler OnAllGamePlayersLoaded;


        protected sealed override void OnAwake()
        {
            HEXIT = HEXITEngine.Singleton;

            if (HEXIT == null)
            {
                Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
            }
            else
            {
                // [서버만] 실제로 이벤트가 오는 곳은 서버뿐입니다.
                HEXIT.OnPlayerReadyToSpawn -= HandlePlayerReadyToSpawn;
                HEXIT.OnPlayerReadyToSpawn += HandlePlayerReadyToSpawn;

                // [서버만] 플레이어가 나가면 상속한 매니저가 남은 인원으로 다시 판단할 수 있도록 훅 전달
                HEXIT.OnHostDisconnected -= HandleHostDisconnected;
                HEXIT.OnHostDisconnected += HandleHostDisconnected;

                // [나만] 씬이 로드되는 동안(Awake) 붙잡아 두고, 준비가 끝나면 놓음 (Game 씬의 로딩은 이 매니저가 걷음)
                HEXIT.HoldLoading(this);
            }

            ClientGamePlayer.OnAnyGamePlayerReady -= HandleAnyGamePlayerReady;
            ClientGamePlayer.OnAnyGamePlayerReady += HandleAnyGamePlayerReady;

            ClientGamePlayer.OnAnyGamePlayerDespawned -= HandleAnyGamePlayerDespawned;
            ClientGamePlayer.OnAnyGamePlayerDespawned += HandleAnyGamePlayerDespawned;

            OnGameManagerAwake();
        }

        protected sealed override void OnNetSpawn()
        {
            n_IsGameStarted.OnValueChanged -= HandleGameStartedChanged;
            n_IsGameStarted.OnValueChanged += HandleGameStartedChanged;

            OnGameManagerSpawn();
        }

        protected sealed override void OnNetDespawn()
        {
            n_IsGameStarted.OnValueChanged -= HandleGameStartedChanged;

            OnGameManagerDespawn();
        }

        protected sealed override void OnDestroyed()
        {
            if (HEXIT != null)
            {
                HEXIT.OnPlayerReadyToSpawn -= HandlePlayerReadyToSpawn;
                HEXIT.OnHostDisconnected -= HandleHostDisconnected;

                // 붙잡은 채로 사라지면 로딩이 안 걷히므로 반드시 놓음
                HEXIT.ReleaseLoading(this);
            }

            ClientGamePlayer.OnAnyGamePlayerReady -= HandleAnyGamePlayerReady;
            ClientGamePlayer.OnAnyGamePlayerDespawned -= HandleAnyGamePlayerDespawned;

            OnGameManagerDestroyed();
        }


        /// <summary> [서버만] 게임을 시작합니다. 이미 시작되었으면 무시합니다. </summary>
        public void GameStarted_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 시작할 수 있습니다.");
                return;
            }

            if (IsGameStarted || isEnding) return;

            OnServerGameStarted();

            // 값이 바뀌면 모든 컴퓨터에서 OnGameStarted가 발생합니다. (호스트 포함)
            n_IsGameStarted.Value = true;
        }

        /// <summary> [서버만] 게임을 끝내고 모두 Ready 씬으로 돌아갑니다. 시작 전이어도 돌아갈 수 있습니다. </summary>
        public void GameEnded_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 끝낼 수 있습니다.");
                return;
            }

            if (isEnding) return;
            isEnding = true;

            OnServerGameEnded();

            // 값이 바뀌면 모든 컴퓨터에서 OnGameEnded가 발생합니다. (호스트 포함)
            if (IsGameStarted) n_IsGameStarted.Value = false;

            // 로비 잠금 해제 + Ready 씬 로드 (이탈자 슬롯 정리 포함)
            HEXIT.ReturnToReady_OnlyServer();

            // Ready 씬 로드를 시작하지 못하면 세션이 잠긴 채로 남으므로 다시 시도할 수 있게 함
            if (HEXIT.IsSessionLocked) isEnding = false;
        }

        private void HandleGameStartedChanged(bool previousValue, bool newValue)
        {
            if (previousValue == newValue) return;

            if (newValue) OnGameStarted?.Invoke(this, EventArgs.Empty);
            else OnGameEnded?.Invoke(this, EventArgs.Empty);
        }

        private void HandlePlayerReadyToSpawn(object sender, MultiplayerService.OnPlayerSpawnEventArgs e)
        {
            if (!IsServer || isEnding) return;

            // 이미 생성된 플레이어면 다시 만들지 않음
            if (ClientGamePlayer.TryGetByClientId(e.playerData.clientId, out _)) return;

            SpawnGamePlayer_OnlyServer(e.playerData);
        }

        /// <summary>
        /// [서버만] 게임 플레이어를 생성해 그 플레이어 소유로 스폰합니다. (필수 구현)
        /// 예: Instantiate(prefab, 위치, 회전).SpawnGamePlayer_OnlyServer(playerData)
        /// </summary>
        protected abstract void SpawnGamePlayer_OnlyServer(PlayerData playerData);


        /// <summary> [모두] clientId로 게임 플레이어를 찾습니다. </summary>
        public bool TryGetGamePlayer(ulong clientId, out ClientGamePlayer gamePlayer) => ClientGamePlayer.TryGetByClientId(clientId, out gamePlayer);

        /// <summary> [모두] playerId(슬롯 번호)로 게임 플레이어를 찾습니다. </summary>
        public bool TryGetGamePlayerByPlayerId(int playerId, out ClientGamePlayer gamePlayer) => ClientGamePlayer.TryGetByPlayerId(playerId, out gamePlayer);

        /// <summary> [모두] SteamID로 게임 플레이어를 찾습니다. </summary>
        public bool TryGetGamePlayerBySteamId(ulong steamId, out ClientGamePlayer gamePlayer) => ClientGamePlayer.TryGetBySteamId(steamId, out gamePlayer);

        /// <summary> [모두] clientId로 게임 플레이어를 찾아 게임별 플레이어 타입으로 받습니다. 타입이 다르면 false </summary>
        public bool TryGetGamePlayer<TPlayer>(ulong clientId, out TPlayer gamePlayer) where TPlayer : ClientGamePlayer
        {
            gamePlayer = ClientGamePlayer.TryGetByClientId(clientId, out var found) ? found as TPlayer : null;
            return gamePlayer != null;
        }

        /// <summary> [나만] 내 게임 플레이어를 게임별 플레이어 타입으로 받습니다. 준비 전이거나 타입이 다르면 null </summary>
        public TPlayer GetLocalGamePlayer<TPlayer>() where TPlayer : ClientGamePlayer => ClientGamePlayer.LocalClientGamePlayer as TPlayer;

        /// <summary> [모두] 모든 게임 플레이어를 게임별 플레이어 타입으로 순회합니다. (playerId 순) </summary>
        public IEnumerable<TPlayer> GetGamePlayers<TPlayer>() where TPlayer : ClientGamePlayer
        {
            foreach (var gamePlayer in ClientGamePlayer.ClientGamePlayers)
            {
                if (gamePlayer is TPlayer typed) yield return typed;
            }
        }

        private void HandleAnyGamePlayerReady(ClientGamePlayer gamePlayer)
        {
            OnGamePlayerReadyHook(gamePlayer);
            OnGamePlayerReady?.Invoke(gamePlayer);
        }

        private void HandleAnyGamePlayerDespawned(ClientGamePlayer gamePlayer)
        {
            OnGamePlayerDespawnedHook(gamePlayer);
            OnGamePlayerDespawned?.Invoke(gamePlayer);
        }

        private void HandleHostDisconnected(object sender, MultiplayerService.OnClientEventArgs e)
        {
            if (!IsServer || !IsSpawned) return;
            OnServerClientDisconnectedHook(e.clientId);
        }

        #region Loading

        /// <summary>
        /// [서버만] 모든 컴퓨터의 로딩 화면을 동시에 걷습니다. 상속한 매니저가 준비(플레이어 생성 등)를 마친 뒤 호출합니다. (한 번만 동작)
        /// 게임 플레이어 스폰 뒤에 보내는 RPC라, 받는 쪽에는 그 전에 스폰된 게임 플레이어가 이미 생성되어 있습니다.
        /// 보냈으면 true, 서버가 아니거나 스폰 전이거나 이미 보냈으면 false
        /// </summary>
        protected bool ReleaseLoadingForAll_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 로딩을 걷을 수 있습니다.");
                return false;
            }

            if (!IsSpawned || hasReleasedLoading) return false;

            hasReleasedLoading = true;
            ReleaseLoadingRpc();
            return true;
        }

        /// <summary> [서버만] 접속한 모든 클라이언트의 게임 플레이어가 준비되었는지. (로딩을 걷을지 판단하는 재료) </summary>
        protected bool AreAllGamePlayersReady_OnlyServer()
        {
            if (!IsServer) return false;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (!ClientGamePlayer.TryGetByClientId(clientId, out _)) return false;
            }

            return true;
        }

        [Rpc(SendTo.Everyone)]
        private void ReleaseLoadingRpc()
        {
            if (IsAllGamePlayersLoaded) return;
            IsAllGamePlayersLoaded = true;

            if (HEXIT != null) HEXIT.ReleaseLoading(this);

            OnAllGamePlayersLoadedHook();
            OnAllGamePlayersLoaded?.Invoke(this, EventArgs.Empty);
        }

        #endregion


        /// <summary> [모두] Awake. (OnAwake 대신 사용) </summary>
        protected virtual void OnGameManagerAwake() { }

        /// <summary> [모두] 네트워크 스폰. (OnNetSpawn 대신 사용) </summary>
        protected virtual void OnGameManagerSpawn() { }

        /// <summary> [모두] 네트워크 디스폰. (OnNetDespawn 대신 사용) </summary>
        protected virtual void OnGameManagerDespawn() { }

        /// <summary> [모두] 파괴. (OnDestroyed 대신 사용) </summary>
        protected virtual void OnGameManagerDestroyed() { }

        /// <summary> [서버만] 게임 시작 직전 (모두에게 OnGameStarted가 가기 전) </summary>
        protected virtual void OnServerGameStarted() { }

        /// <summary> [서버만] 게임 종료 직전 (Ready 씬 로드 전) </summary>
        protected virtual void OnServerGameEnded() { }

        /// <summary> [모두] 게임 플레이어 준비됨 (OnGamePlayerReady 이벤트보다 먼저) </summary>
        protected virtual void OnGamePlayerReadyHook(ClientGamePlayer gamePlayer) { }

        /// <summary> [모두] 게임 플레이어가 사라지기 직전 (OnGamePlayerDespawned 이벤트보다 먼저) </summary>
        protected virtual void OnGamePlayerDespawnedHook(ClientGamePlayer gamePlayer) { }

        /// <summary> [서버만] 다른 플레이어의 연결이 끊김. (기다리던 사람이 나가 남은 인원으로 준비가 끝났는지 다시 판단할 때 사용) </summary>
        protected virtual void OnServerClientDisconnectedHook(ulong clientId) { }

        /// <summary> [모두] 로딩 화면이 걷힘 (OnAllGamePlayersLoaded 이벤트보다 먼저) </summary>
        protected virtual void OnAllGamePlayersLoadedHook() { }
    }
}