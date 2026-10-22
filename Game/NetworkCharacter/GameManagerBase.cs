using HexitEngine.Sample;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    public abstract class GameManagerBase : ServerSingleton<GameManagerBase>
    {
        //[SerializeField] private ClientGamePlayer gamePlayerPrefab;
        //[SerializeField] private List<Transform> playerSpawnPoints = new();

        private NetworkVariable<bool> n_IsGameStarted = new(value: false);

        protected HEXITEngine HEXIT { get; private set; }

        /// <summary> [서버만] Ready로 돌아가는 중인지 (게임 종료 중복 호출 방지) </summary>
        private bool isEnding;

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
            if (HEXIT != null) HEXIT.OnPlayerReadyToSpawn -= HandlePlayerReadyToSpawn;

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

            SpawnGamePlayer_OnlyServer(e.playerData);
        }

        /// <summary> [서버만] 게임 플레이어를 생성해 그 플레이어 소유로 스폰합니다. 생성 방식을 통째로 바꾸려면 override 합니다. </summary>
        protected virtual void SpawnGamePlayer_OnlyServer(PlayerData playerData)
        {
            //if (gamePlayerPrefab == null)
            //{
            //    Dev.LogError("Game Player Prefab이 지정되지 않았습니다.");
            //    return;
            //}
            //
            //if (ClientGamePlayer.TryGetByClientId(playerData.clientId, out _)) return;
            //
            //GetSpawnPose(playerData, out var position, out var rotation);
            //
            //var gamePlayer = Instantiate(gamePlayerPrefab, position, rotation);
            //gamePlayer.SpawnGamePlayer_OnlyServer(playerData);
        }

        /// <summary> [서버만] 생성 위치. 기본은 playerId 번째 스폰 지점(부족하면 순환, 없으면 이 오브젝트 위치)입니다. </summary>
        protected virtual void GetSpawnPose(PlayerData playerData, out Vector3 position, out Quaternion rotation)
        {
            var point = GetSpawnPoint(playerData.playerId);

            position = point.position;
            rotation = point.rotation;
        }

        private Transform GetSpawnPoint(int playerId)
        {
            //if (playerSpawnPoints == null || playerSpawnPoints.Count == 0)
            //{
            //    Dev.LogWarn("스폰 지점이 없어 GameManager 위치에 생성합니다.");
            //    return transform;
            //}
            //
            //if (playerId >= playerSpawnPoints.Count)
            //{
            //    Dev.LogWarn($"스폰 지점({playerSpawnPoints.Count}개)보다 playerId({playerId})가 커서 순환 배치합니다.");
            //}
            //
            //int index = Mathf.Abs(playerId) % playerSpawnPoints.Count;
            //var point = playerSpawnPoints[index];
            //
            //return point != null ? point : transform;

            return null;
        }


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
    }
}