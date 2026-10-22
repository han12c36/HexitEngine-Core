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
    /// 고정 사이클(메인메뉴 → 방 생성/참가 → Ready ⇄ Game → 나가기 → 종료)의 지휘자.
    /// 각 서비스를 연결하고, UI가 구독할 이벤트를 한곳으로 모아서 다시 내보냅니다.
    ///
    /// - 모든 서비스를 참조하므로 부트스트랩 초기화 순서의 맨 마지막에 둡니다.
    /// - 콜드부트 초대(+connect_lobby)는 부트스트랩이 끝나고 첫 씬에 도착한 뒤 처리합니다.
    /// - 앱 종료 정리 순서(네트워크 → 로비 → Steam)를 지휘합니다.
    /// </summary>
    public partial class HEXITEngine : LocalSingleton<HEXITEngine>
    {
        private const float SHUTDOWN_WAIT_TIMEOUT_SEC = 1.5f;

        [Header("Voice")]
        [Tooltip("내가 세션에 접속해 보이스 세션이 열리면 기본 채널에 자동으로 참가합니다.")]
        [SerializeField] private bool autoJoinVoiceChannel = true;
        [SerializeField] private VoiceChannel defaultVoiceChannel = VoiceChannel.Channel1;

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

        /// <summary> 시스템 문구 번역용 (선택). 없으면 키를 그대로 전달합니다. </summary>
        private LocalizationService localizationService;

        /// <summary> 콜드부트 초대 처리를 이미 했는지 </summary>
        private bool hasStartedProcess;

        /// <summary> 방 생성/참가 요청이 진행 중인지. 시작 이벤트부터 결과(성공/실패 이벤트)까지 true </summary>
        public bool IsSessionRequestInProgress { get; private set; }

        /// <summary> 세션 퇴장(네트워크 종료 대기 → 메인메뉴 로드) 진행 중인지 </summary>
        private bool isLeavingSession;

        /// <summary>
        /// [서버만] 세션 잠금 여부. 방장이 Play를 누른 순간 true, Ready로 돌아오면 false.
        /// Steam 로비 데이터는 반영이 늦을 수 있으므로, 접속 승인은 이 값(호스트 메모리)으로 즉시 판단합니다.
        /// </summary>
        public bool IsSessionLocked { get; private set; }

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
            localizationService = LocalizationService.Singleton;

            if (lobbyService == null || multiplayerService == null || voiceService == null)
            {
                throw new InvalidOperationException("[HEXITEngine] 필수 서비스(Lobby, Multiplayer, Voice)가 BootstrapScene에 없습니다.");
            }

            // 2. 서비스 간 연결 (서비스끼리는 서로를 모르고, 필요한 정보는 HEXITEngine이 전달)
            multiplayerService.SetGameStartedResolver(IsGameStarted);
            voiceService.SetPlayerDataResolver(multiplayerService.TryGetPlayerDataBySteamId);

            // 3. 이전 세션 잔여물 정리
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
            {
                Dev.LogWarn("[HEXITEngine] 이전 세션의 네트워크 잔여물을 감지하여 종료합니다.");
                networkManager.Shutdown();
            }

            // 4. 이벤트 연결
            BindEvents();

            // 5. 부트스트랩 씬이 아닌 곳에서 시작한 경우(씬 라우팅 끔) 바로 시작 처리
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
                if (multiplayerService != null && IsServer) multiplayerService.DisconnectAllClients_OnlyServer(SystemKeys.HostLeftSession);
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
                JoinAsClientProcess(ct => lobbyService.JoinLobbyAsync(inviteLobbyId, ct), RaiseJoinFailed, SystemKeys.JoinCanceled);
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
        public void JoinLobby(Lobby lobby) => JoinAsClientProcess(ct => lobbyService.JoinLobbyAsync(lobby.Id.ToString(), ct), RaiseJoinFailed, SystemKeys.JoinCanceled);

        /// <summary> 유저가 입력한 코드로 참가를 시도합니다. </summary>
        public void JoinLobbyByCode(string joinCode) => JoinAsClientProcess(ct => lobbyService.JoinLobbyByCodeAsync(joinCode, ct), RaiseJoinFailed, SystemKeys.JoinCanceled);

        /// <summary> 유효한 로비에 빠른 참가를 시도합니다. </summary>
        public void QuickJoinLobby() => JoinAsClientProcess(ct => lobbyService.QuickJoinLobbyAsync(ct), RaiseQuickJoinFailed, SystemKeys.QuickJoinCanceled);

        /// <summary> [나만] 현재 참가한 로비에서 나가고, 네트워크 종료가 끝난 뒤 메인메뉴로 돌아갑니다. (Ready, Game 어디서든 같은 경로) </summary>
        public void LeaveLobby() => LeaveSessionAsync(loadMainMenu: true).Forget();

        /// <summary>
        /// [서버만] Ready → Game. 방장이 Play를 누른 순간 호출합니다. (플레이 가능 검증은 ReadyManager가 먼저 합니다)
        /// 1. 세션 잠금: 이 순간부터 새 접속은 승인 단계에서 거절됩니다.
        /// 2. Steam 로비 잠금: 목록에서 숨겨 새 시도 자체를 줄입니다. (보조 수단)
        /// 3. Game 씬 네트워크 로드: 모든 클라이언트가 함께 이동합니다.
        /// 씬 로드를 시작하지 못하면 1, 2를 되돌려 Ready에 그대로 남습니다. (방장은 다시 Play를 누를 수 있음)
        /// </summary>
        public void StartGame_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 시작할 수 있습니다.");
                return;
            }

            if (IsSessionLocked) return;

            IsSessionLocked = true;
            lobbyService.LockLobby();

            if (!Loader.LoadNetwork(Loader.Scene.Game))
            {
                IsSessionLocked = false;
                lobbyService.UnlockLobby();

                Dev.LogWarn("Game 씬 로드를 시작하지 못해 세션 잠금을 되돌렸습니다.");
            }
        }

        /// <summary>
        /// [서버만] Game → Ready. GameManager.GameEnded_OnlyServer에서 호출합니다.
        /// 1. 게임 중 나간 사람의 빈 슬롯 정리
        /// 2. Ready 씬 네트워크 로드: ReadyManager가 새로 생성되어 레디 상태도 처음부터 시작합니다.
        /// 3. 세션 잠금 해제 + Steam 로비 잠금 해제: 다시 새 참가를 받습니다.
        /// 씬 로드를 시작하지 못하면 잠긴 채로 Game에 남습니다. (잠금 해제를 로드 성공 뒤에 하므로 되돌릴 것이 없음)
        /// </summary>
        public void ReturnToReady_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 Ready로 돌아갈 수 있습니다.");
                return;
            }

            if (!IsSessionLocked) return;

            multiplayerService.PurgeDisconnectedPlayers();

            if (!Loader.LoadNetwork(Loader.Scene.Ready))
            {
                Dev.LogWarn("Ready 씬 로드를 시작하지 못했습니다. 세션은 잠긴 상태로 유지됩니다.");
                return;
            }

            IsSessionLocked = false;
            lobbyService.UnlockLobby();
        }

        /// <summary>
        /// [호스트] 플레이어를 강퇴합니다. 강퇴는 Ready에서 Play를 누르기 전(세션이 잠기기 전)까지만 가능합니다.
        /// 로비에 강퇴 대상을 기록한 뒤(대상은 이를 감지하고 스스로 나감) 네트워크 연결을 끊습니다.
        /// </summary>
        public void KickPlayer(ulong targetClientId)
        {
            if (!IsServer) return;

            if (IsSessionLocked)
            {
                Dev.LogWarn("게임이 시작된 뒤에는 강퇴할 수 없습니다.");
                return;
            }
            if (!multiplayerService.TryGetPlayerDataByClientId(targetClientId, out var playerData)) return;

            lobbyService.KickPlayerFromLobby(playerData.AuthId);
            multiplayerService.KickPlayer(targetClientId);
        }

        /// <summary>
        /// 게임 진행 중(세션 잠김) 여부. (MultiplayerService에 전달)
        /// - 서버: 호스트 메모리의 IsSessionLocked로 즉시 판단 (접속 승인, 이탈자 슬롯 유지에 사용)
        /// - 클라이언트: 서버 값을 알 수 없으므로 참가한 로비의 잠금 상태로 판단 (연결 끊김 사유 선택에 사용)
        /// </summary>
        private bool IsGameStarted()
        {
            if (IsServer) return IsSessionLocked;

            return lobbyService.JoinedLobby.HasValue && lobbyService.GetLobbyState(lobbyService.JoinedLobby.Value) == LobbyState.Playing;
        }

        #endregion

        #region Session Flows

        /// <summary>
        /// 로비 생성 → 호스트 시작 → Ready 씬 로드. 호스트 시작에 실패하면 구독 해제 후 로비를 나갑니다.
        /// Ready 씬 로드를 시작하지 못하면 보이스, 로비, 네트워크를 정리하고 생성 실패 이벤트를 보냅니다. (Connecting...에 멈추지 않도록)
        /// 시작 이벤트를 보낸 뒤에는 반드시 성공(씬 이동) 또는 실패 이벤트(OnCreateLobbyFailedToLobby / OnDisconnected)로 끝납니다.
        /// </summary>
        private async void CreateLobbyProcess(string lobbyName, int maxCapacity, bool isPrivate)
        {
            // 진행 중이면 무시 (진행 중인 요청의 UI를 건드리지 않도록 이벤트도 보내지 않음)
            if (!TryBeginSessionRequest()) return;

            // 시작 이벤트는 여기서 직접 발생 (네트워크 종료 대기보다 먼저)
            OnCreateLobbyStartedToLobby?.Invoke(this, EventArgs.Empty);

            var ct = destroyCancellationToken;

            try
            {
                if (!await WaitForNetworkShutdownAsync(ct))
                {
                    RaiseCreateFailed(SystemKeys.PreviousSessionNotEnded);
                    return;
                }

                var result = await lobbyService.CreateLobbyAsync(lobbyName, maxCapacity, isPrivate, ct);

                // 실패 이벤트는 LobbyService가 이미 발생시킴
                if (!result.IsSuccess) return;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                // 새 세션은 항상 잠금 해제 상태로 시작
                IsSessionLocked = false;

                // 실패 이벤트(OnDisconnected)는 MultiplayerService가 이미 발생시킴
                if (!multiplayerService.StartHost(result))
                {
                    multiplayerService.StopHost();
                    lobbyService.LeaveLobby();
                    return;
                }

                // 호스트는 이미 시작됐으므로 퇴장과 같은 순서로 정리한 뒤 실패 이벤트를 보냄
                if (!Loader.LoadNetwork(Loader.Scene.Ready))
                {
                    if (voiceService.IsStartedSession) voiceService.StopSession();
                    lobbyService.LeaveLobby();
                    multiplayerService.ShutdownNetwork();

                    RaiseCreateFailed(SystemKeys.ReadySceneLoadFailed);
                }
            }
            catch (OperationCanceledException)
            {
                RaiseCreateFailed(SystemKeys.CreateCanceled);
            }
            catch (Exception e)
            {
                Dev.LogError($"[HEXITEngine] CreateLobby 예외: {e.Message}");
                RaiseCreateFailed(SystemKeys.LobbyCreateFailed);
            }
            finally { IsSessionRequestInProgress = false; }
        }

        /// <summary>
        /// 로비 참가(일반/코드/빠른/초대) → 클라이언트 시작. 네 가지 참가 방식이 공유합니다.
        /// 클라이언트 시작에 실패하면 구독 해제 후 로비를 나갑니다.
        /// 시작 이벤트를 보낸 뒤에는 반드시 성공(접속 후 씬 이동) 또는 실패 이벤트(참가 실패 / OnDisconnected)로 끝납니다.
        /// </summary>
        private async void JoinAsClientProcess(Func<CancellationToken, Task<LobbyResult>> joinLobbyAsync, Action<string> raiseFailed, string canceledKey)
        {
            // 진행 중이면 무시 (진행 중인 요청의 UI를 건드리지 않도록 이벤트도 보내지 않음)
            if (!TryBeginSessionRequest()) return;

            // 시작 이벤트는 여기서 직접 발생 (코드/빠른 참가의 검색, 네트워크 종료 대기보다 먼저)
            OnJoinStartedToLobby?.Invoke(this, EventArgs.Empty);

            var ct = destroyCancellationToken;

            try
            {
                if (!await WaitForNetworkShutdownAsync(ct))
                {
                    raiseFailed(SystemKeys.PreviousSessionNotEnded);
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
                raiseFailed(canceledKey);
            }
            catch (Exception e)
            {
                Dev.LogError($"[HEXITEngine] JoinLobby 예외: {e.Message}");
                raiseFailed(SystemKeys.LobbyJoinFailed);
            }
            finally { IsSessionRequestInProgress = false; }
        }

        /// <summary> 방 생성/참가 요청을 시작합니다. 이미 진행 중이면 false </summary>
        private bool TryBeginSessionRequest()
        {
            if (IsSessionRequestInProgress)
            {
                Dev.LogWarn("[HEXITEngine] 이미 진행 중인 요청이 있어 무시합니다.");
                return false;
            }

            IsSessionRequestInProgress = true;
            return true;
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

        /// <summary>
        /// 보이스 → 로비 → 네트워크 순으로 세션을 나갑니다.
        /// NGO의 Shutdown은 호출 즉시 끝나지 않으므로, 종료가 끝날 때까지 기다린 뒤 메인메뉴를 로드합니다.
        /// (메인메뉴에 도착했을 때 이전 세션이 항상 정리되어 있도록 보장)
        /// </summary>
        private async UniTaskVoid LeaveSessionAsync(bool loadMainMenu)
        {
            if (isLeavingSession) return;
            isLeavingSession = true;

            // 나가기 가림막 등이 즉시 반응할 수 있도록 맨 앞에서 알림
            OnLeaveSessionStarted?.Invoke(this, EventArgs.Empty);

            try
            {
                IsSessionLocked = false;

                if (voiceService.IsStartedSession) voiceService.StopSession();

                // [서버만] 방장이 나가면 방이 사라지므로, 연결을 끊기 전에 모두에게 사유(방장 퇴장)를 보냄
                // NGO의 DisconnectClient(사유)를 사용하므로 받은 쪽은 OnDisconnected의 reasonKey로 받습니다.
                if (IsServer) multiplayerService.DisconnectAllClients_OnlyServer(SystemKeys.HostLeftSession);

                lobbyService.LeaveLobby();
                multiplayerService.ShutdownNetwork();

                if (!await WaitForNetworkShutdownAsync(destroyCancellationToken))
                {
                    Dev.LogWarn("[HEXITEngine] 네트워크 종료가 제한 시간 안에 끝나지 않았습니다.");
                }

                if (loadMainMenu) Loader.Load(Loader.Scene.MainMenu);
            }
            catch (OperationCanceledException) { }
            finally { isLeavingSession = false; }
        }

        // HEXITEngine이 직접 보내는 실패 이벤트. LobbyService에서 온 것과 같은 경로(Handle*)로 번역해서 전달합니다.
        private void RaiseCreateFailed(string reasonKey) => HandleCreateLobbyFailedToLobby(this, new OnLobbyCreateFailedEventArgs(reasonKey));
        private void RaiseJoinFailed(string reasonKey) => HandleJoinFailedToLobby(this, new OnLobbyJoinFailedEventArgs(reasonKey));
        private void RaiseQuickJoinFailed(string reasonKey) => HandleQuickJoinFailedToLobby(this, new OnLobbyQuickJoinFailedEventArgs(reasonKey));

        #endregion

        #region Events

        private void BindEvents()
        {
            UnbindEvents();

            lobbyService.OnCreateLobbySucceededToLobby += HandleCreateLobbySucceededToLobby;
            lobbyService.OnJoinSucceededToLobby += HandleJoinSucceededToLobby;
            lobbyService.OnCreateLobbyFailedToLobby += HandleCreateLobbyFailedToLobby;
            lobbyService.OnQuickJoinFailedToLobby += HandleQuickJoinFailedToLobby;
            lobbyService.OnJoinFailedToLobby += HandleJoinFailedToLobby;
            lobbyService.OnLobbyListChanged += HandleLobbyListChanged;
            lobbyService.OnKickedFromLobby += HandleKickedFromLobby;
            lobbyService.OnLobbyMemberLeft += HandleLobbyMemberLeft;
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
                lobbyService.OnCreateLobbySucceededToLobby -= HandleCreateLobbySucceededToLobby;
                lobbyService.OnJoinSucceededToLobby -= HandleJoinSucceededToLobby;
                lobbyService.OnCreateLobbyFailedToLobby -= HandleCreateLobbyFailedToLobby;
                lobbyService.OnQuickJoinFailedToLobby -= HandleQuickJoinFailedToLobby;
                lobbyService.OnJoinFailedToLobby -= HandleJoinFailedToLobby;
                lobbyService.OnLobbyListChanged -= HandleLobbyListChanged;
                lobbyService.OnKickedFromLobby -= HandleKickedFromLobby;
                lobbyService.OnLobbyMemberLeft -= HandleLobbyMemberLeft;
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

        /// <summary> 방 생성 요청 시작 (HEXITEngine이 직접 발생). 이후 반드시 성공 또는 실패로 끝납니다. </summary>
        public event EventHandler OnCreateLobbyStartedToLobby;
        /// <summary> 방 참가 요청 시작 (HEXITEngine이 직접 발생, 네 가지 참가 방식 공통). 이후 반드시 성공 또는 실패로 끝납니다. </summary>
        public event EventHandler OnJoinStartedToLobby;
        public event EventHandler OnCreateLobbySucceededToLobby;
        public event EventHandler OnJoinSucceededToLobby;
        public event EventHandler<OnLobbyQuickJoinFailedEventArgs> OnQuickJoinFailedToLobby;
        public event EventHandler<OnLobbyCreateFailedEventArgs> OnCreateLobbyFailedToLobby;
        public event EventHandler<OnLobbyJoinFailedEventArgs> OnJoinFailedToLobby;
        public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

        /// <summary> 호스트에게 강퇴됨 (LobbyService의 KickTarget 감지) </summary>
        public event EventHandler OnKickedFromLobby;

        /// <summary> [나만] 세션 퇴장 시작 (LeaveLobby 호출 직후, 네트워크 종료 대기 전). 나가기 가림막용 </summary>
        public event EventHandler OnLeaveSessionStarted;

        private void HandleCreateLobbySucceededToLobby(object sender, EventArgs e) => OnCreateLobbySucceededToLobby?.Invoke(this, e);
        private void HandleJoinSucceededToLobby(object sender, EventArgs e) => OnJoinSucceededToLobby?.Invoke(this, e);
        /// <summary> 실패 이벤트: 사유 키를 번역해서 Message에 채운 뒤 전달합니다. </summary>
        private void HandleCreateLobbyFailedToLobby(object sender, OnLobbyCreateFailedEventArgs e)
        {
            e.Message = GetSystemText(e.ReasonKey);
            OnCreateLobbyFailedToLobby?.Invoke(this, e);
        }
        private void HandleQuickJoinFailedToLobby(object sender, OnLobbyQuickJoinFailedEventArgs e)
        {
            e.Message = GetSystemText(e.ReasonKey);
            OnQuickJoinFailedToLobby?.Invoke(this, e);
        }
        private void HandleJoinFailedToLobby(object sender, OnLobbyJoinFailedEventArgs e)
        {
            e.Message = GetSystemText(e.ReasonKey);
            OnJoinFailedToLobby?.Invoke(this, e);
        }
        private void HandleLobbyListChanged(object sender, OnLobbyListChangedEventArgs e) => OnLobbyListChanged?.Invoke(this, e);

        /// <summary> 강퇴됨: 로비 퇴장은 LobbyService가 이미 했으므로 보이스와 네트워크를 정리하고 전달합니다. </summary>
        private void HandleKickedFromLobby(object sender, EventArgs e)
        {
            IsSessionLocked = false;

            if (voiceService.IsStartedSession) voiceService.StopSession();
            multiplayerService.ShutdownNetwork();

            OnKickedFromLobby?.Invoke(this, e);
        }

        /// <summary>
        /// Steam 로비에서 멤버가 나감. 게임 종료/강제 종료로 연결 종료 신호가 오지 못해도, 시간 초과를 기다리지 않고 즉시 처리하기 위함입니다.
        /// - [서버만] 나간 멤버의 네트워크 연결이 아직 남아 있으면 바로 끊습니다. 이후 처리(슬롯 제거, 레디 목록 갱신 등)는 일반 이탈과 같은 경로를 탑니다.
        /// - [클라이언트] 나간 멤버가 방장이면 방이 사라진 것이므로 "방장 퇴장" 사유로 연결 끊김을 처리합니다.
        /// </summary>
        private void HandleLobbyMemberLeft(object sender, OnLobbyMemberLeftEventArgs e)
        {
            if (!IsServer)
            {
                if (IsHostSteamId(e.SteamId)) HandleHostLeftLobby();
                return;
            }

            // 그 사이 같은 유저가 다시 들어왔다면 새 연결을 끊지 않음
            if (lobbyService.IsLobbyMember(e.SteamId)) return;

            if (!multiplayerService.TryGetPlayerDataBySteamId(e.SteamId, out var playerData) || !playerData.isConnected) return;

            Dev.Log($"로비에서 나간 플레이어의 연결을 끊습니다. (SteamID {e.SteamId}, ClientId {playerData.clientId})");
            multiplayerService.DisconnectClient_OnlyServer(playerData.clientId);
        }

        /// <summary> [클라이언트] 이 SteamID가 지금 세션의 방장인지 (PlayerData의 서버 슬롯 기준) </summary>
        private bool IsHostSteamId(ulong steamId)
        {
            return multiplayerService.TryGetPlayerDataByClientId(multiplayerService.ServerClientId, out var hostData)
                && !hostData.IsEmpty()
                && hostData.steamId == steamId;
        }

        /// <summary>
        /// [클라이언트] 방장이 Steam 로비에서 나감(정상 퇴장 신호보다 먼저 감지되었거나, 방장이 강제 종료됨).
        /// NGO의 연결 끊김과 같은 경로(HandleDisconnected)로 처리한 뒤 네트워크를 종료합니다.
        /// HandleDisconnected가 로비 퇴장과 클라이언트 콜백 해제를 하므로, 뒤늦게 도착하는 NGO 끊김 신호는 중복 처리되지 않습니다.
        /// </summary>
        private void HandleHostLeftLobby()
        {
            if (isLeavingSession || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            Dev.Log("방장이 로비에서 나가 세션을 종료합니다.");

            HandleDisconnected(this, new OnDisconnectedEventArgs { reasonKey = SystemKeys.HostLeftSession });
            multiplayerService.ShutdownNetwork();
        }

        /// <summary> 오버레이 초대 수락: 다른 방에 있으면 메인메뉴로 가지 않고 세션만 나간 뒤 바로 새 방에 참가 </summary>
        private void HandleJoinRequestedViaOverlay(object sender, Lobby targetLobby)
        {
            // 다른 요청 진행 중이거나 퇴장(메인메뉴 이동) 중이면, 기존 세션을 건드리기 전에 무시
            if (IsSessionRequestInProgress || isLeavingSession)
            {
                Dev.LogWarn("[HEXITEngine] 진행 중인 요청이 있어 초대 수락을 무시합니다.");
                return;
            }

            if (lobbyService.JoinedLobby.HasValue)
            {
                if (lobbyService.JoinedLobby.Value.Id == targetLobby.Id) return;

                Dev.Log("[HEXITEngine] 초대를 수락하여 기존 로비에서 나갑니다.");

                // 네트워크 종료 대기는 JoinAsClientProcess가 함
                LeaveSessionAsync(loadMainMenu: false).Forget();
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

        /// <summary> PlayerData 변경: 보이스 로스터를 먼저 맞춘 뒤 UI에 전달합니다. </summary>
        private void HandlePlayerDatasChanged(object sender, PlayerDatasChangedEventArgs e)
        {
            voiceService.NotifyPlayerDatasChanged();
            OnPlayerDatasChanged?.Invoke(this, e);
        }
        private void HandlePlayerReadyToSpawn(object sender, OnPlayerSpawnEventArgs e) => OnPlayerReadyToSpawn?.Invoke(this, e);
        private void HandleHostDisconnected(object sender, OnClientEventArgs e) => OnHostDisconnected?.Invoke(this, e);
        private void HandleInGamePlayerDisconnected(object sender, OnPlayerDataEventArgs e) => OnInGamePlayerDisconnected?.Invoke(this, e);

        /// <summary>
        /// 내가 세션에 접속하면 보이스 로스터 추적을 시작하고 기본 채널에 참가합니다.
        /// VoiceService는 부트스트랩에 남아 있으므로 Ready ⇄ Game을 오가도 채널이 유지되고, 나갈 때 StopSession이 모든 채널에서 나갑니다.
        /// </summary>
        private void HandleOnClientConnected(object sender, OnClientEventArgs e)
        {
            if (e.clientId == multiplayerService.LocalClientId && voiceService.IsVoiceAvailable && !voiceService.IsStartedSession)
            {
                voiceService.StartSession();

                if (autoJoinVoiceChannel && voiceService.IsStartedSession) voiceService.JoinChannel(defaultVoiceChannel);
            }

            OnClientConnected?.Invoke(this, e);
        }

        /// <summary>
        /// 내 연결이 끊김(시작 실패 포함): 사유 키를 번역해서 전달한 뒤 보이스 정리, 로비 퇴장, 클라이언트 콜백 해제.
        /// UI는 e.reason(번역된 문구)을, 분기가 필요하면 e.reasonKey를 사용합니다.
        /// </summary>
        private void HandleDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            IsSessionLocked = false;

            e.reason = GetSystemText(e.reasonKey);
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

        #region System Text

        /// <summary>
        /// 시스템 키(SYS_)를 System_Table에서 현재 언어로 번역합니다. 엔진 사유 키와 샘플 UI의 제목 키 모두 여기서 번역합니다.
        /// - arguments가 있으면 문구의 {0}, {1} 자리에 채웁니다. (예: "{0}님이 나갔습니다.")
        /// - SYS_로 시작하지 않는 문자열(트랜스포트 원문 등)은 그대로 반환합니다.
        /// - 테이블에 키가 없거나 비어 있으면 경고를 남기고 키를 그대로 반환합니다. (빠진 항목을 화면에서 바로 알아볼 수 있도록)
        /// </summary>
        public string GetSystemText(string key, params object[] arguments)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (!SystemKeys.IsSystemKey(key)) return key;

            if (localizationService == null)
            {
                Dev.LogWarn($"LocalizationService가 없어 '{key}'를 번역하지 않고 표시합니다.");
                return key;
            }

            if (localizationService.TryGetLocalizedString(SystemKeys.TABLE, key, out string text, arguments)) return text;

            Dev.LogWarn($"{SystemKeys.TABLE}에 '{key}' 번역이 없습니다. 테이블에 항목을 추가하세요.");
            return key;
        }

        #endregion
    }
}