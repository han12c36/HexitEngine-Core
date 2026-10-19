using System.Collections.Generic;
using Unity.Netcode;
using static HexitEngine.MultiplayerService;

namespace HexitEngine
{
    public partial class MultiplayerService : ServerSingleton<MultiplayerService>
    {
        private NetworkList<PlayerData> playerDatas = new();

        public PlayerData LocalPlayerData { get; private set; }

        protected override void OnNetSpawn()
        {
            playerDatas.OnListChanged -= OnPlayerDataListChanged;
            playerDatas.OnListChanged += OnPlayerDataListChanged;
        }

        protected override void OnNetDespawn()
        {
            playerDatas.OnListChanged -= OnPlayerDataListChanged;
        }

        private void OnPlayerDataListChanged(NetworkListEvent<PlayerData> changeEvent)
        {
            if (TryGetLocalPlayerData(out var myData)) LocalPlayerData = myData;
            else LocalPlayerData = PlayerData.EmptyData;

            List<ulong> validIds = new List<ulong>();

            foreach (var playerData in playerDatas)
            {
                if (!playerData.IsEmpty() && playerData.isConnected)
                {
                    validIds.Add(playerData.clientId);
                }
            }

            OnPlayerDatasChanged?.Invoke(this, new PlayerDatasChangedEventArgs { joinedClientIds = validIds });
        }

        private bool TryGetLocalPlayerData(out PlayerData result)
        {
            result = PlayerData.EmptyData;

            foreach (var playerData in playerDatas)
            {
                if (playerData.clientId != NetworkManager.Singleton.LocalClientId) continue;

                result = playerData;

                return true;
            }

            return false;
        }

        /// <summary>server측에서만 유요한 데이터 접근입니다.</summary>
        public bool TryGetPlayerDataByClientId(ulong clientId, out PlayerData result)
        {
            result = default;

            for (int i = 0; i < playerDatas.Count; i++)
            {
                if (playerDatas[i].clientId == clientId)
                {
                    result = playerDatas[i];

                    return true;
                }
            }

            return false;
        }

        /// <summary>해당 모듈은 PlayerIndex와 PlayerId가 동일한 개념으로 사용됩니다.</summary>
        public bool TryGetPlayerDataByPlayerId(int playerId, out PlayerData result)
        {
            result = default;

            if (playerId < 0 || playerId >= playerDatas.Count)
                return false;

            result = playerDatas[playerId];
            return !result.IsEmpty();
        }

        public bool TryGetPlayerDataBySteamNickname(string steamNickname, out PlayerData result)
        {
            result = default;

            foreach (var playerData in playerDatas)
            {
                if (playerData.steamName.ToString() == steamNickname)
                {
                    result = playerData;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetPlayerDataBySteamId(ulong steamId, out PlayerData result)
        {
            result = default;

            foreach (var playerData in playerDatas)
            {
                if (!playerData.IsEmpty() && playerData.steamId == steamId)
                {
                    result = playerData;
                    return true;
                }
            }

            return false;
        }

        private void AddPlayerData(ulong clientId, ulong steamId, string steamName, string displayName)
        {
            // 1) 중복 접속 방지:
            // 이미 같은 clientId로 접속된 슬롯이 있다면 무시합니다.
            for (int i = 0; i < playerDatas.Count; i++)
            {
                if (playerDatas[i].clientId == clientId) return;
            }

            // 1) 빈 자리(EmptyData) 재사용
            for (int i = 0; i < playerDatas.Count; i++)
            {
                if (playerDatas[i].IsEmpty())
                {
                    var data = new PlayerData(clientId, i, steamId, steamName, displayName, true);

                    playerDatas[i] = data;

                    if (clientId == NetworkManager.Singleton.LocalClientId)
                    {
                        LocalPlayerData = data;
                    }

                    return;
                }
            }

            // 2) 빈 슬롯이 없으면 리스트 맨 뒤에 새로 추가
            var newPlayer = new PlayerData(clientId, playerDatas.Count, steamId, steamName, displayName, true);

            playerDatas.Add(newPlayer);

            // 방장(호스트) 본인이 처음 추가될 때 LocalPlayerData 세팅
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                LocalPlayerData = newPlayer;
            }
        }

        private void RemovePlayerData(ulong clientId)
        {
            if (!TryGetPlayerDataByClientId(clientId, out var playerdata)) return;

            for (int i = 0; i < playerDatas.Count; i++)
            {
                if (playerDatas[i].clientId == clientId)
                {
                    playerDatas[i] = PlayerData.EmptyData;
                    break;
                }
            }
        }

        private void ClearPlayerData()
        {
            //if (!IsSpawned) return;

            playerDatas.Clear();
        }
    }
}