using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Netcode.Transports.Facepunch;
using Steamworks;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static HexitEngine.MultiplayerService;
using static Unity.Netcode.NetworkManager;

namespace HexitEngine
{
    public partial class MultiplayerService : ServerSingleton<MultiplayerService>
    {
        [System.Serializable]
        public class ConnectionPayload
        {
            public string userName;
            public string authId;
            public string displayName;

            public byte[] ToBytes()
            {
                string json = JsonUtility.ToJson(this);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }

            public static ConnectionPayload FromBytes(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0)
                {
                    return new ConnectionPayload { userName = "Unknown", authId = "None" };
                }

                string json = System.Text.Encoding.UTF8.GetString(bytes);
                return JsonUtility.FromJson<ConnectionPayload>(json);
            }

            public void SetupNetworkConfig()
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionData = ToBytes();
            }
        }

        private readonly HashSet<ulong> s_SceneReady = new();

        private Dictionary<ulong, ConnectionPayload> s_PendingPayload = new();

        /// <summary> 방 참가 직전에 설정하는 내 인게임 이름. 비어 있으면 Steam 닉네임이 사용됩니다. </summary>
        public string LocalDisplayName { get; set; } = string.Empty;

        public ulong ServerClientId => NetworkManager.ServerClientId;
        public ulong LocalClientId => NetworkManager.Singleton.LocalClientId;

        /// <summary> 게임 진행 중 여부를 알려주는 함수. HEXITEngine이 초기화 시 연결합니다. (LobbyService를 직접 참조하지 않기 위함) </summary>
        private Func<bool> gameStartedResolver;

        /// <summary> 게임 진행 중 여부. 연결된 판단 함수가 없으면 false </summary>
        public bool GameStarted => gameStartedResolver != null && gameStartedResolver();

        /// <summary> 게임 진행 중 여부 판단 함수를 연결합니다. (HEXITEngine 전용) </summary>
        public void SetGameStartedResolver(Func<bool> resolver) => gameStartedResolver = resolver;

        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            if (NetworkManager.Singleton == null) return UniTask.CompletedTask;

            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.ConnectionApprovalCallback += OnApproval;

            return UniTask.CompletedTask;
        }

        private void OnApproval(ConnectionApprovalRequest req, ConnectionApprovalResponse resp)
        {
            ConnectionPayload payload = ConnectionPayload.FromBytes(req.Payload);

            if (payload.authId != "None") AddPendingPayloadData(req.ClientNetworkId, payload);

            resp.Approved = !GameStarted;
            if (!resp.Approved)
            {
                // 번역하지 않은 사유 키를 보냄. 받은 클라이언트의 HEXITEngine이 번역합니다.
                resp.Reason = SystemKeys.GameAlreadyStarted;
            }

            resp.CreatePlayerObject = false;
            resp.Pending = false;
        }

        public bool StartHost(LobbyResult result)
        {
            if (!result.LobbyData.HasValue)
            {
                Dev.LogError("유효하지 않은 로비 데이터로 Host 시작 시도");

                // false를 반환하는 모든 경로에서 OnDisconnected가 발생하도록 (UI가 실패를 알 수 있게)
                OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = SystemKeys.HostStartFailed });
                return false;
            }

            NetworkManager.Singleton.OnClientConnectedCallback -= Host_OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += Host_OnClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= Host_OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += Host_OnClientDisconnected;

            ClearPlayerData();
            ClearSceneReadyData();
            ClearPendingPayloadData();

            var startHostResult = NetworkManager.Singleton.StartHost();

            if (!startHostResult)
            {
                string failReasonKey = string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason)
                    ? SystemKeys.HostStartFailed
                    : NetworkManager.Singleton.DisconnectReason;

                OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = failReasonKey });

                return false;
            }

            NetworkManager.Singleton.SceneManager.OnSceneEvent -= Host_OnSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += Host_OnSceneEvent;

            return true;
        }

        public void StopHost()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= Host_OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= Host_OnClientDisconnected;
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= Host_OnSceneEvent;
        }

        public bool StartClient(LobbyResult result)
        {
            if (!result.LobbyData.HasValue)
            {
                Dev.LogError("유효하지 않은 로비 데이터로 Client 시작 시도");

                // false를 반환하는 모든 경로에서 OnDisconnected가 발생하도록 (UI가 실패를 알 수 있게)
                OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = SystemKeys.ClientStartFailed });
                return false;
            }

            var lobby = result.LobbyData.Value;
            var transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
            transport.targetSteamId = lobby.Owner.Id;

            new ConnectionPayload { userName = SteamClient.Name, authId = SteamClient.SteamId.ToString(), displayName = LocalDisplayName }.SetupNetworkConfig();

            NetworkManager.Singleton.OnClientConnectedCallback -= Client_OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += Client_OnClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= Client_OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += Client_OnClientDisconnected;

            var startClientResult = NetworkManager.Singleton.StartClient();

            if (!startClientResult)
            {
                string failReasonKey = string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason)
                    ? SystemKeys.ClientStartFailed
                    : NetworkManager.Singleton.DisconnectReason;

                OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = failReasonKey });

                return false;
            }

            return true;
        }

        public void StopClient()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= Client_OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= Client_OnClientDisconnected;
        }

        /// <summary> 본인 포함 타인이 접속시 호스트에서만 계속 호출  </summary>
        private void Host_OnClientConnected(ulong clientId)
        {
            if (GameStarted)
            {
                // 틈새를 뚫고 들어온 유저이므로 명단에 넣지 않고 즉시 연결을 강제 종료시킴 (사유 키는 받은 쪽 HEXITEngine이 번역)
                NetworkManager.Singleton.DisconnectClient(clientId, SystemKeys.GameAlreadyStarted);
                return;
            }

            string authId = "";
            string userName = "";
            string displayName = "";

            if (clientId == ServerClientId)
            {
                authId = SteamClient.SteamId.ToString();
                userName = SteamClient.Name;
                displayName = LocalDisplayName;
            }
            else
            {
                if (s_PendingPayload.TryGetValue(clientId, out ConnectionPayload payload))
                {
                    authId = payload.authId;
                    userName = payload.userName;
                    displayName = payload.displayName;
                    RemovePendingPayloadData(clientId);
                }
                else //fallback
                {
                    authId = clientId.ToString();
                    userName = $"Player_{authId.Substring(0, 4)}";
                }
            }

            ulong.TryParse(authId, out ulong steamId);

            AddPlayerData(clientId, steamId, userName, displayName);

            TrySpawnIfSceneReady(clientId);

            OnClientConnected?.Invoke(this, new OnClientEventArgs
            {
                clientId = clientId,
                reason = NetworkManager.Singleton.DisconnectReason
            });
        }

        /// <summary> 본인이 접속 성공 시 본인만 호출 </summary>
        private void Client_OnClientConnected(ulong clientId)
        {
            //본인만의 접속 감지, 서버 접속 감지 못함.
            OnClientConnected?.Invoke(this, new OnClientEventArgs
            {
                clientId = clientId,
                reason = NetworkManager.Singleton.DisconnectReason
            });
        }

        /// <summary> 본인 포함 타인이 나갈때마다 호출  </summary>
        private void Host_OnClientDisconnected(ulong clientId)
        {
            if (clientId == ServerClientId)
            {
                ResetData();

                OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = NetworkManager.Singleton.DisconnectReason });

                return;
            }

            if (GameStarted)
            {
                for (int i = 0; i < playerDatas.Count; i++)
                {
                    if (playerDatas[i].clientId == clientId)
                    {
                        var data = playerDatas[i];

                        data.isConnected = false;

                        playerDatas[i] = data;
                        break;
                    }
                }
            }
            else RemovePlayerData(clientId);

            RemoveSceneReadyData(clientId);
            RemovePendingPayloadData(clientId);

            OnHostDisconnected?.Invoke(this, new OnClientEventArgs { clientId = clientId, reason = NetworkManager.Singleton.DisconnectReason });
        }

        /// <summary>  서버가 끊키거나 내가끊켰을때 내가 호출됨  </summary>
        private void Client_OnClientDisconnected(ulong clientId)
        {
            ResetData();

            string failReasonKey = NetworkManager.Singleton.DisconnectReason;

            if (string.IsNullOrEmpty(failReasonKey))
            {
                failReasonKey = GameStarted ? SystemKeys.DisconnectedFromServer : SystemKeys.ServerConnectionTimeout;
            }

            OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs { reasonKey = failReasonKey });
        }

        public void ShutdownNetwork()
        {
            bool isHost = NetworkManager.Singleton.IsHost;

            NetworkManager.Singleton.Shutdown();

            ResetData();

            if (isHost) StopHost();
            else StopClient();
        }

        public List<string> GetAllValidAuthIds()
        {
            List<string> validIds = new List<string>();

            foreach (var p in playerDatas)
            {
                // 빈 슬롯이 아니고, 유효한 SteamID가 있는 유저만 추출
                if (!p.IsEmpty() && !string.IsNullOrEmpty(p.AuthId))
                {
                    validIds.Add(p.AuthId);
                }
            }

            return validIds;
        }

        private void Host_OnSceneEvent(SceneEvent e)
        {
            if (!IsServer) return;

            // 서버가 씬 전환을 마친 직후 (모든 클라이언트 대상)
            if (e.SceneEventType == SceneEventType.LoadEventCompleted)
            {
                foreach (var cid in e.ClientsThatCompleted)
                {
                    SetClientReadyAndTrySpawn(cid, e.SceneName);
                }
                return;
            }

            // 개별 클라이언트 로드 완료 시
            if (e.SceneEventType == SceneEventType.LoadComplete || e.SceneEventType == SceneEventType.SynchronizeComplete)
            {
                SetClientReadyAndTrySpawn(e.ClientId, e.SceneName);
            }
        }

        private void SetClientReadyAndTrySpawn(ulong clientId, string sceneName)
        {
            AddSceneReadyData(clientId);

            // 씬이 바뀌었음을 명시적으로 인지시키기 위해 sceneName을 넘깁니다.
            TrySpawnIfSceneReady(clientId, sceneName);
        }

        private void TrySpawnIfSceneReady(ulong clientId, string sceneName = null)
        {
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;
            if (!s_SceneReady.Contains(clientId)) return;

            if (!TryGetPlayerDataByClientId(clientId, out var playerData)) return;



            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var cc))
            {
                if (cc.PlayerObject != null && cc.PlayerObject.IsSpawned) return;
            }

            OnPlayerReadyToSpawn?.Invoke(this, new OnPlayerSpawnEventArgs
            {
                sceneName = sceneName,
                playerData = playerData,
            });
        }

        /// <summary> [서버만] 대상의 슬롯을 비우고 연결을 끊습니다. 로비 쪽 강퇴 기록은 HEXITEngine이 처리합니다. </summary>
        public void KickPlayer(ulong targetClientId)
        {
            if (!IsServer || targetClientId == ServerClientId) return;

            if (TryGetPlayerDataByClientId(targetClientId, out _))
            {
                for (int i = 0; i < playerDatas.Count; i++)
                {
                    if (playerDatas[i].clientId == targetClientId)
                    {
                        playerDatas[i] = PlayerData.EmptyData;
                        break;
                    }
                }

                NetworkManager.Singleton.DisconnectClient(targetClientId);
            }
        }

        /// <summary>
        /// [서버만] 대상의 연결만 끊습니다. 슬롯은 건드리지 않으므로 일반 이탈과 같은 경로(Host_OnClientDisconnected)로 처리됩니다.
        /// (Ready에서는 슬롯 제거, 게임 중에는 끊김 표시)
        /// </summary>
        public void DisconnectClient_OnlyServer(ulong clientId)
        {
            if (!IsServer || clientId == ServerClientId) return;
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

            NetworkManager.Singleton.DisconnectClient(clientId);
        }

        /// <summary>
        /// [서버만] 호스트를 제외한 모든 클라이언트의 연결을 사유와 함께 끊습니다. (방장 퇴장 등)
        /// NGO가 사유 메시지를 먼저 보낸 뒤 연결을 닫으므로, 받은 쪽은 NetworkManager.DisconnectReason으로 사유를 받습니다.
        /// </summary>
        public void DisconnectAllClients_OnlyServer(string reasonKey)
        {
            if (!IsServer || NetworkManager.Singleton == null) return;

            // 끊는 동안 목록이 바뀌므로 복사본으로 순회
            var clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

            foreach (ulong clientId in clientIds)
            {
                if (clientId == ServerClientId) continue;
                NetworkManager.Singleton.DisconnectClient(clientId, reasonKey);
            }
        }

        private void ResetData()
        {
            LocalPlayerData = PlayerData.EmptyData;

            if (IsServer)
            {
                ClearPlayerData();
                ClearSceneReadyData();
                ClearPendingPayloadData();
            }
        }

        private void AddSceneReadyData(ulong clientId)
        {
            if (s_SceneReady.TryGetValue(clientId, out var _)) return;

            s_SceneReady.Add(clientId);
        }

        private void RemoveSceneReadyData(ulong clientId)
        {
            if (!s_SceneReady.TryGetValue(clientId, out var _)) return;

            s_SceneReady.Remove(clientId);
        }

        private void ClearSceneReadyData()
        {
            s_SceneReady.Clear();
        }

        private void AddPendingPayloadData(ulong clientId, ConnectionPayload payload)
        {
            if (s_PendingPayload.ContainsKey(clientId)) return;

            s_PendingPayload.Add(clientId, payload);
        }

        private void RemovePendingPayloadData(ulong clientId)
        {
            if (!s_PendingPayload.TryGetValue(clientId, out var _)) return;

            s_PendingPayload.Remove(clientId);
        }

        private void ClearPendingPayloadData()
        {
            s_PendingPayload.Clear();
        }

        public void PurgeDisconnectedPlayers()
        {
            if (!IsServer) return;

            for (int i = 0; i < playerDatas.Count; i++)
            {
                if (!playerDatas[i].IsEmpty() && !playerDatas[i].isConnected)
                {
                    playerDatas[i] = PlayerData.EmptyData;
                }
            }
        }
    }
}