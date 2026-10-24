using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    public class ClientGamePlayer : NetworkBehaviour
    {
        private static readonly List<ClientGamePlayer> clientGamePlayers = new();

        /// <summary> [모두] 준비된 모든 게임 플레이어 (playerId 순) </summary>
        public static IReadOnlyList<ClientGamePlayer> ClientGamePlayers => clientGamePlayers;

        /// <summary> [모두] 준비된 게임 플레이어 수 </summary>
        public static int ClientGamePlayerCount => clientGamePlayers.Count;

        /// <summary> [모두] 이 컴퓨터의 내 게임 플레이어. 아직 준비 전이면 null </summary>
        public static ClientGamePlayer LocalClientGamePlayer { get; private set; }

        /// <summary> [모두] 어떤 게임 플레이어든 준비됨 (스폰 + PlayerData 보장, 각 Ready 훅 이후). GameManagerBase가 중계합니다. </summary>
        public static event Action<ClientGamePlayer> OnAnyGamePlayerReady;

        /// <summary> [모두] 어떤 게임 플레이어든 사라지기 직전 (각 Despawn 훅 이후, 목록에서 빠지기 전). GameManagerBase가 중계합니다. </summary>
        public static event Action<ClientGamePlayer> OnAnyGamePlayerDespawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            clientGamePlayers.Clear();
            LocalClientGamePlayer = null;
            OnAnyGamePlayerReady = null;
            OnAnyGamePlayerDespawned = null;
        }

        /// <summary> [모두] clientId로 ClientGamePlayer를 찾습니다. </summary>
        public static bool TryGetByClientId(ulong clientId, out ClientGamePlayer gamePlayer)
        {
            foreach (var c in clientGamePlayers)
            {
                if (c.ClientId == clientId) { gamePlayer = c; return true; }
            }

            gamePlayer = null;
            return false;
        }

        /// <summary> [모두] playerId(슬롯 번호)로 ClientGamePlayer를 찾습니다. </summary>
        public static bool TryGetByPlayerId(int playerId, out ClientGamePlayer gamePlayer)
        {
            foreach (var c in clientGamePlayers)
            {
                if (c.PlayerId == playerId) { gamePlayer = c; return true; }
            }

            gamePlayer = null;
            return false;
        }

        /// <summary> [모두] SteamID로 ClientGamePlayer를 찾습니다. </summary>
        public static bool TryGetBySteamId(ulong steamId, out ClientGamePlayer gamePlayer)
        {
            foreach (var c in clientGamePlayers)
            {
                if (c.SteamId == steamId) { gamePlayer = c; return true; }
            }

            gamePlayer = null;
            return false;
        }

        private static void Register(ClientGamePlayer gamePlayer)
        {
            if (clientGamePlayers.Contains(gamePlayer)) return;

            clientGamePlayers.Add(gamePlayer);
            clientGamePlayers.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

            if (gamePlayer.IsLocal) LocalClientGamePlayer = gamePlayer;
        }

        private static void Unregister(ClientGamePlayer gamePlayer)
        {
            clientGamePlayers.Remove(gamePlayer);
            if (LocalClientGamePlayer == gamePlayer) LocalClientGamePlayer = null;
        }


        /// <summary> 주인의 PlayerData. read : [모두], write : [서버만] </summary>
        private NetworkVariable<PlayerData> n_PlayerData = new(PlayerData.EmptyData);

        /// <summary> [모두] 주인의 PlayerData. IsReady 이후 항상 유효합니다. </summary>
        public PlayerData PlayerData => n_PlayerData.Value;

        /// <summary> [모두] 주인의 슬롯 번호 </summary>
        public int PlayerId => n_PlayerData.Value.playerId;

        /// <summary> [모두] 주인의 clientId (OwnerClientId와 같음) </summary>
        public ulong ClientId => n_PlayerData.Value.clientId;

        /// <summary> [모두] 주인의 SteamID </summary>
        public ulong SteamId => n_PlayerData.Value.steamId;

        /// <summary> [모두] 주인의 화면 표시 이름 </summary>
        public string PlayerName => n_PlayerData.Value.PlayerName;

        /// <summary> [모두] 이 컴퓨터의 내 게임 플레이어인지 </summary>
        public bool IsLocal => IsSpawned && IsOwner;

        /// <summary> [모두] 호스트의 게임 플레이어인지 </summary>
        public bool IsHostGamePlayer => IsSpawned && OwnerClientId == NetworkManager.ServerClientId;

        /// <summary> [모두] 스폰 + PlayerData가 준비되어 Ready 훅이 호출되었는지 </summary>
        public bool IsPlayerDataReady { get; private set; }


        /// <summary> [서버만] 
        /// PlayerData를 넣고 해당 플레이어 소유로 스폰.
        /// 주인이 나가면 게임 플레이어도 함께 사라짐.
        /// GameManagerBase가 호출합니다.
        /// </summary>
        public void SpawnGamePlayer_OnlyServer(PlayerData playerData)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Dev.LogWarn("서버에서만 게임 플레이어를 스폰할 수 있습니다.");
                return;
            }

            if (playerData.IsEmpty())
            {
                Dev.LogError("빈 PlayerData로는 게임 플레이어를 스폰할 수 없습니다.");
                return;
            }

            NetworkObject.DontDestroyWithOwner = false;

            // 스폰 전에 넣어 두면 스폰 메시지와 함께 모든 컴퓨터에 도착합니다. (PlayerData 보장의 핵심)
            n_PlayerData.Value = playerData;

            NetworkObject.SpawnAsPlayerObject(playerData.clientId, destroyWithScene: true);
        }

        public sealed override void OnNetworkSpawn()
        {
            n_PlayerData.OnValueChanged -= HandlePlayerDataChanged;
            n_PlayerData.OnValueChanged += HandlePlayerDataChanged;

            // 대부분 이 시점에 PlayerData가 이미 도착해 있어 바로 준비됩니다.
            TryBecomeReady();
        }

        public sealed override void OnNetworkDespawn()
        {
            n_PlayerData.OnValueChanged -= HandlePlayerDataChanged;

            if (!IsPlayerDataReady) return;

            // [서버만] → [모두] → 목록에서 제거
            if (IsServer) OnServerGamePlayerDespawned();
            OnGamePlayerDespawned();

            OnAnyGamePlayerDespawned?.Invoke(this);

            Unregister(this);
            IsPlayerDataReady = false;
        }

        private void HandlePlayerDataChanged(PlayerData previousValue, PlayerData newValue)
        {
            if (!IsPlayerDataReady)
            {
                TryBecomeReady();
                return;
            }

            OnPlayerDataChanged(previousValue, newValue);
        }

        /// <summary> 스폰 + PlayerData가 모두 준비되면 Ready 훅을 한 번만 호출합니다. </summary>
        private void TryBecomeReady()
        {
            if (IsPlayerDataReady || !IsSpawned || n_PlayerData.Value.IsEmpty()) return;

            IsPlayerDataReady = true;

            // 훅 안에서 ClientGamePlayers / LocalClientGamePlayer로 자기 자신도 찾을 수 있도록 먼저 등록
            Register(this);

            OnGamePlayerReady();
            if (IsServer) OnServerGamePlayerReady();
            if (IsLocal) OnLocalGamePlayerReady();

            OnAnyGamePlayerReady?.Invoke(this);
        }

        #region Hooks

        /// <summary> [모두] 스폰 + PlayerData 보장. 모든 컴퓨터의 모든 게임 플레이어에서 호출. (IsLocal로 내 게임 플레이어 구분 가능) </summary>
        protected virtual void OnGamePlayerReady() { }

        /// <summary> [서버만] 스폰 + PlayerData 보장. 서버에서 모든 게임 플레이어에 대해 호출. </summary>
        protected virtual void OnServerGamePlayerReady() { }

        /// <summary> [나만] 스폰 + PlayerData + 소유권 보장. 각 컴퓨터의 내 게임 플레이어에서만 호출. </summary>
        protected virtual void OnLocalGamePlayerReady() { }

        /// <summary> [모두] Ready 이후 PlayerData가 바뀌었을 때 (이름 변경, 연결 상태 등) </summary>
        protected virtual void OnPlayerDataChanged(PlayerData previous, PlayerData current) { }

        /// <summary> [서버만] 사라지기 직전 (OnGamePlayerDespawned 이전) </summary>
        protected virtual void OnServerGamePlayerDespawned() { }

        /// <summary> [모두] 사라지기 직전. 아직 목록(ClientGamePlayers)에 남아 있습니다. </summary>
        protected virtual void OnGamePlayerDespawned() { }

        #endregion
    }
}