using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using static HexitEngine.LobbyService;
using static HexitEngine.MultiplayerService;

namespace HexitEngine
{
    /// <summary>
    /// 고정 사이클(메인메뉴 → 방 생성/참가 → 보이스 → 나가기 → 종료)의 지휘자.
    /// 각 서비스를 연결하고, UI가 구독할 이벤트를 한곳으로 모아서 다시 내보냅니다.
    ///
    /// - 모든 서비스를 참조하므로 부트스트랩 초기화 순서의 맨 마지막에 둡니다.
    /// - 콜드부트 초대(+connect_lobby)는 부트스트랩이 끝나고 첫 씬에 도착한 뒤 처리합니다.
    /// - 앱 종료 정리 순서(네트워크 → 로비 → Steam)를 지휘합니다.
    /// </summary>
    public partial class HEXITEngine : LocalSingleton<HEXITEngine>
    {
        private const float SHUTDOWN_WAIT_TIMEOUT_SEC = 1.5f;
        private const string PREVIOUS_SESSION_MESSAGE = "The previous session has not ended yet.";

        public string SteamNickName => SteamAuthService.Nickname;
        public FriendState SteamUserState => SteamAuthService.UserState;

        /// <summary> 참가 중인 로비가 있는지 </summary>
        public bool HasJoinedLobby => lobbyService != null && lobbyService.JoinedLobby.HasValue;

        /// <summary> 참가 중인 로비. 없으면 기본값이므로 HasJoinedLobby로 먼저 확인합니다. </summary>
        public Lobby JoinedLobby => lobbyService != null ? lobbyService.JoinedLobby ?? default : default;

        public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        private LobbyService lobbyService;
        private MultiplayerService multiplayerService;
        private VoiceService voiceService;

        /// <summary> 콜드부트 초대 처리를 이미 했는지 </summary>
        private bool hasStartedProcess;

        #region Lifecycle

        /// <summary>
        /// Steam을 가장 먼저 초기화합니다.
        /// 모든 씬 오브젝트의 Awake는 부트스트랩이 InitializeAsync를 호출하기 전에 끝나므로,
        /// Lobby, Voice 등 Steam이 필요한 서비스보다 항상 먼저 실행됩니다.
        /// </summary>
        protected override void OnAwake()
        {
            SteamAuthService.Initialize();
        }

        /// <summary>
        /// 서비스 참조를 모으고 이벤트를 연결합니다. 앞 순서의 모든 서비스가 초기화된 뒤 호출됩니다.
        /// 기다릴 작업이 없으므로 즉시 완료됩니다.
        /// </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            // 1. 서비스 참조 (없으면 설정 오류이므로 부팅 중단)
            lobbyService = LobbyService.Singleton;
            multiplayerService = MultiplayerService.Singleton;
            voiceService = VoiceService.Singleton;

            if (lobbyService == null || multiplayerService == null || voiceService == null)
            {
                throw new InvalidOperationException("[HEXITEngine] 필수 서비스(Lobby, Multiplayer, Voice)가 BootstrapScene에 없습니다.");
            }

            // 2. 이전 세션 잔여물 정리
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
            {
                Dev.LogWarn("[HEXITEngine] 이전 세션의 네트워크 잔여물을 감지하여 종료합니다.");
                networkManager.Shutdown();
            }

            // 3. 이벤트 연결
            BindEvents();

            // 4. 부트스트랩 씬이 아닌 곳에서 시작한 경우(씬 라우팅 끔) 바로 시작 처리
            if (!IsBootstrapScene(SceneManager.GetActiveScene())) StartProcessOnce();

            return UniTask.CompletedTask;
        }

        /// <summary> 부트스트랩이 끝나고 첫 씬이 활성화되면 콜드부트 초대를 처리합니다. </summary>
        protected override void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (!IsInitialized || IsBootstrapScene(newScene)) return;

            StartProcessOnce();
        }

        protected override void OnDestroyed()
        {
            UnbindEvents();
        }

        /// <summary> BootstrapConfig에 지정된 부트스트랩 씬인지 </summary>
        private static bool IsBootstrapScene(Scene scene)
        {
            var config = BootstrapConfig.Instance;
            return config != null && config.IsBootstrapScene(scene.name);
        }

        /// <summary> 앱 종료 정리. 순서: 보이스 → 네트워크 → 로비 → Steam (종료 중에는 Singleton이 null이므로 필드 사용) </summary>
        private void OnApplicationQuit()
        {
            Dev.Log("[HEXITEngine] 애플리케이션 종료 정리 시작");

            try
            {
                if (voiceService != null && voiceService.IsStartedSession) voiceService.StopSession();
                if (multiplayerService != null) multiplayerService.ShutdownNetwork();
                if (lobbyService != null) lobbyService.LeaveLobby();
            }
            catch (Exception e) { Dev.LogWarn($"[HEXITEngine] 종료 정리 중 예외 (무시): {e.Message}"); }
            finally { SteamAuthService.Shutdown(); }
        }

        #endregion

        #region Start Process (Cold Boot Invite)

        private void StartProcessOnce()
        {
            if (hasStartedProcess) return;
            hasStartedProcess = true;

            if (!SteamAuthService.IsLoggedOn) Dev.LogError("[HEXITEngine] 스팀 로그인 상태가 아닙니다. 멀티플레이가 제한될 수 있습니다.");

            // Steam 초대로 게임을 실행한 경우 해당 로비로 바로 참가
            if (lobbyService.TryGetColdBootInviteLobbyId(out string inviteLobbyId))
            {
                Dev.Log($"[HEXITEngine] 콜드부트 초대 감지. 대상 로비: {inviteLobbyId}");
                JoinAsClientProcess(ct => lobbyService.JoinLobbyAsync(inviteLobbyId, ct), RaiseJoinFailed, "Joining the lobby was canceled.");
                return;
            }

            lobbyService.RefreshLobbyList();
        }

        #endregion

        #region Funcs

        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void CopyJoinCodeToClipboard()
        {
            string code = HasJoinedLobby ? JoinedLobby.GetData(LobbyKeys.JoinCode) : null;

            if (!string.IsNullOrEmpty(code)) GUIUtility.systemCopyBuffer = code;
            else Dev.LogError("[HEXITEngine] 복사할 참가 코드가 존재하지 않습니다.");
        }

        /// <summary> 스팀 초대 오버레이를 활성화합니다. </summary>
        public void OpenInviteOverlay() => lobbyService.OpenInviteOverlay();

        /// <summary> 로비 리스트를 새로고침합니다. </summary>
        public void RefreshLobbyList() => lobbyService.RefreshLobbyList();

        /// <summary> 매개변수로 전달된 로비의 Waiting or Playing 상태를 반환합니다. </summary>
        public LobbyState GetLobbyState(Lobby lobby) => lobbyService.GetLobbyState(lobby);

        /// <summary> 로비를 생성하고 호스트로 시작합니다. </summary>
        public void CreateLobby(string lobbyName, int maxCapacity, bool isPrivate) => CreateLobbyProcess(lobbyName, maxCapacity, isPrivate);

        /// <summary> 로비에 일반 참가를 시도합니다. </summary>
        public void JoinLobby(Lobby lobby) => JoinAsClientProcess(ct => lobbyService.JoinLobbyAsync(lobby.Id.ToString(), ct), RaiseJoinFailed, "Joining the lobby was canceled.");

        /// <summary> 유저가 입력한 코드로 참가를 시도합니다. </summary>
        public void JoinLobbyByCode(string joinCode) => JoinAsClientProcess(ct => lobbyService.JoinLobbyByCodeAsync(joinCode, ct), RaiseJoinFailed, "Joining the lobby was canceled.");

        /// <summary> 유효한 로비에 빠른 참가를 시도합니다. </summary>
        public void QuickJoinLobby() => JoinAsClientProcess(ct => lobbyService.QuickJoinLobbyAsync(ct), RaiseQuickJoinFailed, "Quick join was canceled.");

        /// <summary> 현재 참가한 로비에서 나가고 메인메뉴로 돌아갑니다. </summary>
        public void LeaveLobby() => LeaveSession(loadMainMenu: true);

        /// <summary> [호스트] 게임 시작: 로비를 잠가 새 참가를 막습니다. </summary>
        public void LockLobby() => LockLobbyProcess();

        /// <summary> 기존 호출부 호환용 (LockLobby와 같음) </summary>
        public void LockLobbyProcess()
        {
            if (!lobbyService.IsLobbyHost) return;
            lobbyService.LockLobby();
        }

        /// <summary> [호스트] 로비로 복귀: 게임 중 이탈자 슬롯을 정리하고 로비를 다시 엽니다. </summary>
        public void UnlockLobby()
        {
            if (!lobbyService.IsLobbyHost) return;

            multiplayerService.PurgeDisconnectedPlayers();
            lobbyService.UnlockLobby();
        }

        /// <summary> [호스트] 플레이어를 강퇴합니다. </summary>
        public void KickPlayer(ulong targetClientId)
        {
            if (!IsServer) return;
            multiplayerService.KickPlayer(targetClientId);
        }

        #endregion

        #region Session Flows

        /// <summary> 로비 생성 → 호스트 시작 → 게임 씬 로드. 호스트 시작에 실패하면 구독 해제 후 로비를 나갑니다. </summary>
        private async void CreateLobbyProcess(string lobbyName, int maxCapacity, bool isPrivate)
        {
            var ct = destroyCancellationToken;

            try
            {
                if (!await WaitForNetworkShutdownAsync(ct))
                {
                    OnCreateLobbyFailedToLobby?.Invoke(this, new OnLobbyCreateFailedEventArgs(PREVIOUS_SESSION_MESSAGE));
                    return;
                }

                var result = await lobbyService.CreateLobbyAsync(lobbyName, maxCapacity, isPrivate, ct);

                // 실패 이벤트는 LobbyService가 이미 발생시킴
                if (!result.IsSuccess) return;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                if (multiplayerService.StartHost(result))
                {
                    Loader.LoadNetwork(Loader.Scene.Game);
                }
                else
                {
                    multiplayerService.StopHost();
                    lobbyService.LeaveLobby();
                }
            }
            catch (OperationCanceledException)
            {
                OnCreateLobbyFailedToLobby?.Invoke(this, new OnLobbyCreateFailedEventArgs("Lobby creation was canceled."));
            }
            catch (Exception e)
            {
                Dev.LogError($"[HEXITEngine] CreateLobby 예외: {e.Message}");
                OnCreateLobbyFailedToLobby?.Invoke(this, new OnLobbyCreateFailedEventArgs(e.Message));
            }
        }

        /// <summary>
        /// 로비 참가(일반/코드/빠른/초대) → 클라이언트 시작. 네 가지 참가 방식이 공유합니다.
        /// 클라이언트 시작에 실패하면 구독 해제 후 로비를 나갑니다.
        /// </summary>
        private async void JoinAsClientProcess(Func<CancellationToken, Task<LobbyResult>> joinLobbyAsync, Action<string> raiseFailed, string canceledMessage)
        {
            var ct = destroyCancellationToken;

            try
            {
                if (!await WaitForNetworkShutdownAsync(ct))
                {
                    raiseFailed(PREVIOUS_SESSION_MESSAGE);
                    return;
                }

                var result = await joinLobbyAsync(ct);

                // 실패 이벤트는 LobbyService가 이미 발생시킴
                if (!result.IsSuccess) return;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                if (!multiplayerService.StartClient(result))
                {
                    multiplayerService.StopClient();
                    lobbyService.LeaveLobby();
                }
            }
            catch (OperationCanceledException)
            {
                raiseFailed(canceledMessage);
            }
            catch (Exception e)
            {
                Dev.LogError($"[HEXITEngine] JoinLobby 예외: {e.Message}");
                raiseFailed(e.Message);
            }
        }

        /// <summary> 이전 NGO 세션이 종료 중이면 최대 SHUTDOWN_WAIT_TIMEOUT_SEC 동안 기다립니다. 끝나지 않으면 false </summary>
        private static async UniTask<bool> WaitForNetworkShutdownAsync(CancellationToken ct)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.ShutdownInProgress) return true;

            float deadline = Time.realtimeSinceStartup + SHUTDOWN_WAIT_TIMEOUT_SEC;

            while (networkManager != null && networkManager.ShutdownInProgress && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            return networkManager == null || !networkManager.ShutdownInProgress;
        }

        /// <summary> 보이스 → 로비 → 네트워크 순으로 세션을 나갑니다. </summary>
        private void LeaveSession(bool loadMainMenu)
        {
            if (voiceService.IsStartedSession) voiceService.StopSession();

            lobbyService.LeaveLobby();
            multiplayerService.ShutdownNetwork();

            if (loadMainMenu) Loader.Load(Loader.Scene.MainMenu);
        }

        private void RaiseJoinFailed(string message) => OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(message));
        private void RaiseQuickJoinFailed(string message) => OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(message));

        #endregion

        #region Events

        private void BindEvents()
        {
            UnbindEvents();

            lobbyService.OnCreateLobbyStartedToLobby += HandleCreateLobbyStartedToLobby;
            lobbyService.OnJoinStartedToLobby += HandleJoinStartedToLobby;
            lobbyService.OnCreateLobbySucceededToLobby += HandleCreateLobbySucceededToLobby;
            lobbyService.OnJoinSucceededToLobby += HandleJoinSucceededToLobby;
            lobbyService.OnCreateLobbyFailedToLobby += HandleCreateLobbyFailedToLobby;
            lobbyService.OnQuickJoinFailedToLobby += HandleQuickJoinFailedToLobby;
            lobbyService.OnJoinFailedToLobby += HandleJoinFailedToLobby;
            lobbyService.OnLobbyListChanged += HandleLobbyListChanged;
            lobbyService.OnKickedFromLobby += HandleKickedFromLobby;
            lobbyService.OnJoinRequestedViaOverlay += HandleJoinRequestedViaOverlay;

            multiplayerService.OnPlayerDatasChanged += HandlePlayerDatasChanged;
            multiplayerService.OnPlayerReadyToSpawn += HandlePlayerReadyToSpawn;
            multiplayerService.OnInGamePlayerDisconnected += HandleInGamePlayerDisconnected;
            multiplayerService.OnClientConnected += HandleOnClientConnected;
            multiplayerService.OnDisconnected += HandleDisconnected;
            multiplayerService.OnHostDisconnected += HandleHostDisconnected;
        }

        private void UnbindEvents()
        {
            if (lobbyService != null)
            {
                lobbyService.OnCreateLobbyStartedToLobby -= HandleCreateLobbyStartedToLobby;
                lobbyService.OnJoinStartedToLobby -= HandleJoinStartedToLobby;
                lobbyService.OnCreateLobbySucceededToLobby -= HandleCreateLobbySucceededToLobby;
                lobbyService.OnJoinSucceededToLobby -= HandleJoinSucceededToLobby;
                lobbyService.OnCreateLobbyFailedToLobby -= HandleCreateLobbyFailedToLobby;
                lobbyService.OnQuickJoinFailedToLobby -= HandleQuickJoinFailedToLobby;
                lobbyService.OnJoinFailedToLobby -= HandleJoinFailedToLobby;
                lobbyService.OnLobbyListChanged -= HandleLobbyListChanged;
                lobbyService.OnKickedFromLobby -= HandleKickedFromLobby;
                lobbyService.OnJoinRequestedViaOverlay -= HandleJoinRequestedViaOverlay;
            }

            if (multiplayerService != null)
            {
                multiplayerService.OnPlayerDatasChanged -= HandlePlayerDatasChanged;
                multiplayerService.OnPlayerReadyToSpawn -= HandlePlayerReadyToSpawn;
                multiplayerService.OnInGamePlayerDisconnected -= HandleInGamePlayerDisconnected;
                multiplayerService.OnClientConnected -= HandleOnClientConnected;
                multiplayerService.OnDisconnected -= HandleDisconnected;
                multiplayerService.OnHostDisconnected -= HandleHostDisconnected;
            }
        }

        //============= LobbyService Events ===================================================================================

        public event EventHandler OnCreateLobbyStartedToLobby;
        public event EventHandler OnJoinStartedToLobby;
        public event EventHandler OnCreateLobbySucceededToLobby;
        public event EventHandler OnJoinSucceededToLobby;
        public event EventHandler<OnLobbyQuickJoinFailedEventArgs> OnQuickJoinFailedToLobby;
        public event EventHandler<OnLobbyCreateFailedEventArgs> OnCreateLobbyFailedToLobby;
        public event EventHandler<OnLobbyJoinFailedEventArgs> OnJoinFailedToLobby;
        public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

        /// <summary> 호스트에게 강퇴됨 (LobbyService의 KickTarget 감지) </summary>
        public event EventHandler OnKickedFromLobby;

        private void HandleCreateLobbyStartedToLobby(object sender, EventArgs e) => OnCreateLobbyStartedToLobby?.Invoke(this, e);
        private void HandleJoinStartedToLobby(object sender, EventArgs e) => OnJoinStartedToLobby?.Invoke(this, e);
        private void HandleCreateLobbySucceededToLobby(object sender, EventArgs e) => OnCreateLobbySucceededToLobby?.Invoke(this, e);
        private void HandleJoinSucceededToLobby(object sender, EventArgs e) => OnJoinSucceededToLobby?.Invoke(this, e);
        private void HandleCreateLobbyFailedToLobby(object sender, OnLobbyCreateFailedEventArgs e) => OnCreateLobbyFailedToLobby?.Invoke(this, e);
        private void HandleQuickJoinFailedToLobby(object sender, OnLobbyQuickJoinFailedEventArgs e) => OnQuickJoinFailedToLobby?.Invoke(this, e);
        private void HandleJoinFailedToLobby(object sender, OnLobbyJoinFailedEventArgs e) => OnJoinFailedToLobby?.Invoke(this, e);
        private void HandleLobbyListChanged(object sender, OnLobbyListChangedEventArgs e) => OnLobbyListChanged?.Invoke(this, e);

        /// <summary> 강퇴됨: 로비 퇴장과 네트워크 종료는 LobbyService가 이미 했으므로 보이스만 정리하고 전달합니다. </summary>
        private void HandleKickedFromLobby(object sender, EventArgs e)
        {
            if (voiceService.IsStartedSession) voiceService.StopSession();
            OnKickedFromLobby?.Invoke(this, e);
        }

        /// <summary> 오버레이 초대 수락: 다른 방에 있으면 메인메뉴로 가지 않고 세션만 나간 뒤 바로 새 방에 참가 </summary>
        private void HandleJoinRequestedViaOverlay(object sender, Lobby targetLobby)
        {
            if (lobbyService.JoinedLobby.HasValue)
            {
                if (lobbyService.JoinedLobby.Value.Id == targetLobby.Id) return;

                Dev.Log("[HEXITEngine] 초대를 수락하여 기존 로비에서 나갑니다.");
                LeaveSession(loadMainMenu: false);
            }

            JoinLobby(targetLobby);
        }

        //============= MultiplayerService Events =============================================================================

        public event EventHandler<PlayerDatasChangedEventArgs> OnPlayerDatasChanged;
        public event EventHandler<OnPlayerSpawnEventArgs> OnPlayerReadyToSpawn;
        public event EventHandler<OnClientEventArgs> OnClientConnected;
        public event EventHandler<OnDisconnectedEventArgs> OnDisconnected;
        public event EventHandler<OnClientEventArgs> OnHostDisconnected;
        public event EventHandler<OnPlayerDataEventArgs> OnInGamePlayerDisconnected;

        private void HandlePlayerDatasChanged(object sender, PlayerDatasChangedEventArgs e) => OnPlayerDatasChanged?.Invoke(this, e);
        private void HandlePlayerReadyToSpawn(object sender, OnPlayerSpawnEventArgs e) => OnPlayerReadyToSpawn?.Invoke(this, e);
        private void HandleHostDisconnected(object sender, OnClientEventArgs e) => OnHostDisconnected?.Invoke(this, e);
        private void HandleInGamePlayerDisconnected(object sender, OnPlayerDataEventArgs e) => OnInGamePlayerDisconnected?.Invoke(this, e);

        /// <summary> 내가 세션에 접속하면 보이스 로스터 추적을 시작합니다. </summary>
        private void HandleOnClientConnected(object sender, OnClientEventArgs e)
        {
            if (e.clientId == multiplayerService.LocalClientId && voiceService.IsVoiceAvailable && !voiceService.IsStartedSession)
            {
                voiceService.StartSession();
            }

            OnClientConnected?.Invoke(this, e);
        }

        /// <summary> 내 연결이 끊김(시작 실패 포함): 보이스 정리, 로비 퇴장, 클라이언트 콜백 해제 </summary>
        private void HandleDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            OnDisconnected?.Invoke(this, e);

            try
            {
                if (voiceService.IsStartedSession) voiceService.StopSession();
                if (lobbyService.JoinedLobby.HasValue) lobbyService.LeaveLobby();
            }
            catch (Exception ex) { Dev.LogWarn($"[HEXITEngine] 접속 종료 정리 중 예외 (무시): {ex.Message}"); }
            finally { multiplayerService.StopClient(); }
        }

        #endregion
    }
}