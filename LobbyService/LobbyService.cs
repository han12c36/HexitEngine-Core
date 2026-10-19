using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    /// <summary> Steam 로비 데이터 키와 값. LobbyService와 MultiplayerService가 공유합니다. </summary>
    public static class LobbyKeys
    {
        public const string JoinCode = "JoinCode";
        public const string Name = "Name";
        public const string IsLocked = "IsLocked";
        public const string IsPrivate = "IsPrivate";
        public const string KickTarget = "KickTarget";

        public const string True = "true";
        public const string False = "false";
    }

    /// <summary>
    /// Steam 로비 생성/참가/퇴장, 참가 코드, 잠금.
    /// 실패 시 Steam에 로비가 남아 있는 상태(유령 로비)가 되지 않도록, 입장 후 실패하면 반드시 나갑니다.
    /// </summary>
    public partial class LobbyService : LocalSingleton<LobbyService>
    {
        private const int JOIN_DELAY_MS = 1500;
        private const int JOIN_CODE_LENGTH = 8;
        private const int JOIN_CODE_MAX_ATTEMPTS = 5;
        private const string JOIN_CODE_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary> 참가 코드 생성용. 호출마다 새로 만들면 시간 기반 시드가 같아 같은 코드가 반복될 수 있어 하나만 사용합니다. </summary>
        private static readonly Random codeRandom = new();

        public Lobby? JoinedLobby { get; private set; } = null;

        /// <summary> 참가한 로비의 인원. 로비가 없으면 0 </summary>
        public int JoinedMemberCount => JoinedLobby?.MemberCount ?? 0;

        /// <summary> 참가한 로비의 최대 인원. 로비가 없으면 0 </summary>
        public int MaxCapacity => JoinedLobby?.MaxMembers ?? 0;

        /// <summary> 생성/참가 진행 중 또는 직후 쿨타임 중 </summary>
        private bool isJoiningLobby;

        #region Lifecycle

        /// <summary> 로비 데이터 변경(강퇴)과 Steam 오버레이 초대 수락 이벤트를 구독합니다. 기다릴 작업이 없으므로 즉시 완료됩니다. </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChangedCallback;
            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChangedCallback;

            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequestedCallback;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequestedCallback;

            return UniTask.CompletedTask;
        }

        protected override void OnDestroyed()
        {
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChangedCallback;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequestedCallback;
        }

        #endregion

        #region Create / Join

        public async Task<LobbyResult> CreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (isJoiningLobby) return LobbyResult.Fail(0, "System", "Already joining lobby.");
            if (!SteamAuthService.IsInitialized) return LobbyResult.Fail(0, "System", "Steam login required.");

            isJoiningLobby = true;

            OnCreateLobbyStartedToLobby?.Invoke(this, EventArgs.Empty);

            Lobby? createdLobby = null;

            try
            {
                createdLobby = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

                if (!createdLobby.HasValue) throw new Exception("Lobby creation was denied by Steam.");

                ct.ThrowIfCancellationRequested();

                var lobby = createdLobby.Value;

                lobby.SetJoinable(true);
                lobby.SetPublic();

                string joinCode = await GenerateUniqueLobbyCodeAsync(ct);

                lobby.SetData(LobbyKeys.JoinCode, joinCode);
                lobby.SetData(LobbyKeys.Name, lobbyName);
                lobby.SetData(LobbyKeys.IsLocked, LobbyKeys.False);
                lobby.SetData(LobbyKeys.IsPrivate, isPrivate ? LobbyKeys.True : LobbyKeys.False);

                JoinedLobby = lobby;

                OnCreateLobbySucceededToLobby?.Invoke(this, EventArgs.Empty);

                return LobbyResult.Success(lobby);
            }
            catch (OperationCanceledException)
            {
                LeaveIfNotRecorded(createdLobby);
                throw;
            }
            catch (Exception e)
            {
                LeaveIfNotRecorded(createdLobby);
                OnCreateLobbyFailedToLobby?.Invoke(this, new OnLobbyCreateFailedEventArgs(e.Message));
                return LobbyResult.Fail(0, "System", e.Message);
            }
            finally { _ = StartJoinLobbyDelayAsync(ct); }
        }

        public async Task<LobbyResult> JoinLobbyAsync(string lobbyIdString, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (isJoiningLobby) return LobbyResult.Fail(0, "System", "Already joining lobby.");
            if (!ulong.TryParse(lobbyIdString, out ulong lobbyId)) return LobbyResult.Fail(0, "System", "Invalid lobby ID.");

            isJoiningLobby = true;

            OnJoinStartedToLobby?.Invoke(this, EventArgs.Empty);

            var targetLobby = new Lobby(lobbyId);
            bool hasEntered = false;

            try
            {
                var joinResult = await targetLobby.Join();

                if (joinResult != RoomEnter.Success)
                {
                    FailJoin($"{joinResult}", joinResult);
                    return LobbyResult.Fail(0, "System", $"{joinResult}");
                }

                hasEntered = true;

                ct.ThrowIfCancellationRequested();

                JoinedLobby = targetLobby;

                OnJoinSucceededToLobby?.Invoke(this, EventArgs.Empty);

                return LobbyResult.Success(targetLobby);
            }
            catch (OperationCanceledException)
            {
                // 입장은 했는데 취소된 경우: Steam에서 나가야 유령 로비가 남지 않음
                if (hasEntered) targetLobby.Leave();
                throw;
            }
            catch (Exception e)
            {
                if (hasEntered) targetLobby.Leave();
                FailJoin(e.Message, null);
                return LobbyResult.Fail(0, "System", e.Message);
            }
            finally
            {
                // 실패로 이미 잠금이 풀렸다면 쿨타임을 주지 않음
                if (isJoiningLobby) _ = StartJoinLobbyDelayAsync(ct);
            }
        }

        public async Task<LobbyResult> JoinLobbyByCodeAsync(string joinCode, CancellationToken ct = default)
        {
            if (isJoiningLobby) return LobbyResult.Fail(0, "System", "Already joining lobby.");

            try
            {
                // 소문자로 입력해도 대문자로 통일해서 검색
                joinCode = (joinCode ?? string.Empty).ToUpper().Trim();

                var lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(LobbyKeys.JoinCode, joinCode)
                    .RequestAsync();

                ct.ThrowIfCancellationRequested();

                var target = FindFirstActiveLobby(lobbies);

                if (!target.HasValue)
                {
                    const string failMessage = "Invalid code.";
                    OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(failMessage));
                    return LobbyResult.Fail(0, "Lobby", failMessage);
                }

                // 이후 입장 과정의 실패는 JoinLobbyAsync가 OnJoinFailedToLobby를 발생시킴
                return await JoinLobbyAsync(target.Value.Id.ToString(), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(e.Message));
                return LobbyResult.Fail(0, "System", e.Message);
            }
        }

        public async Task<LobbyResult> QuickJoinLobbyAsync(CancellationToken ct = default)
        {
            if (isJoiningLobby) return LobbyResult.Fail(0, "System", "Already joining lobby.");

            try
            {
                var lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithSlotsAvailable(1)
                    .WithKeyValue(LobbyKeys.IsLocked, LobbyKeys.False)
                    .WithKeyValue(LobbyKeys.IsPrivate, LobbyKeys.False)
                    .RequestAsync();

                ct.ThrowIfCancellationRequested();

                var target = FindFirstActiveLobby(lobbies);

                if (!target.HasValue)
                {
                    const string failMessage = "No available rooms found.";
                    OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(failMessage));
                    return LobbyResult.Fail(0, "Lobby", failMessage);
                }

                return await JoinLobbyAsync(target.Value.Id.ToString(), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(e.Message));
                return LobbyResult.Fail(0, "System", e.Message);
            }
        }

        #endregion

        #region Leave / Lock

        public void LeaveLobby()
        {
            if (!JoinedLobby.HasValue) return;

            try { JoinedLobby.Value.Leave(); }
            catch (Exception e) { Dev.LogError($"[Lobby] {e.Message}"); }
            finally { ResetData(); }
        }

        /// <summary> [호스트] 강퇴 대상을 로비 데이터에 기록합니다. 대상 클라이언트는 OnLobbyDataChanged에서 감지하고 나갑니다. </summary>
        public void KickPlayerFromLobby(string targetSteamId)
        {
            if (!JoinedLobby.HasValue) return;
            if (!IsLobbyHost) return;

            try
            {
                var lobby = JoinedLobby.Value;
                lobby.SetData(LobbyKeys.KickTarget, targetSteamId);
            }
            catch (Exception e) { Dev.LogError($"[Lobby] {e.Message}"); }
        }

        public void OpenInviteOverlay()
        {
            if (!JoinedLobby.HasValue) return;

            SteamFriends.OpenGameInviteOverlay(JoinedLobby.Value.Id);
        }

        /// <summary> [호스트] 게임 시작: 로비를 잠그고 검색에서 숨깁니다. </summary>
        public void LockLobby()
        {
            if (!IsLobbyHost || !JoinedLobby.HasValue) return;

            try
            {
                var lobby = JoinedLobby.Value;
                lobby.SetData(LobbyKeys.IsLocked, LobbyKeys.True);
                lobby.SetInvisible();
            }
            catch (Exception e) { Dev.LogError($"[Lobby] {e.Message}"); }
        }

        /// <summary> [호스트] 로비로 복귀: 잠금을 풀고 다시 검색에 노출합니다. </summary>
        public void UnlockLobby()
        {
            if (!IsLobbyHost || !JoinedLobby.HasValue) return;

            try
            {
                var lobby = JoinedLobby.Value;
                lobby.SetData(LobbyKeys.IsLocked, LobbyKeys.False);
                lobby.SetPublic();
            }
            catch (Exception e) { Dev.LogError($"[Lobby] {e.Message}"); }
        }

        /// <summary> Steam 초대로 게임을 실행한 경우(+connect_lobby) 대상 로비 ID를 얻습니다. </summary>
        public bool TryGetColdBootInviteLobbyId(out string lobbyId)
        {
            lobbyId = string.Empty;
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "+connect_lobby") continue;

                lobbyId = args[i + 1];
                return true;
            }

            return false;
        }

        #endregion

        #region Helpers

        private void OnGameLobbyJoinRequestedCallback(Lobby lobby, SteamId friendId)
        {
            OnJoinRequestedViaOverlay?.Invoke(this, lobby);
        }

        private void OnLobbyDataChangedCallback(Lobby lobby)
        {
            // 1. 내가 참가한 방이 없거나, 데이터가 바뀐 방이 우리 방이 아니라면 무시
            if (!JoinedLobby.HasValue || JoinedLobby.Value.Id != lobby.Id) return;

            // 2. 강퇴 타겟 데이터 읽기
            string kickedId = lobby.GetData(LobbyKeys.KickTarget);

            // 3. 타겟이 비어있지 않고, 그 타겟이 '나'라면
            if (!string.IsNullOrEmpty(kickedId) && kickedId == SteamAuthService.SteamId.ToString())
            {
                LeaveLobby();

                if (MultiplayerService.Singleton != null)
                {
                    MultiplayerService.Singleton.ShutdownNetwork();
                }

                OnKickedFromLobby?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary> 입장 실패 처리: 이벤트 발생, 쿨타임 없이 잠금 해제, 유령 방일 가능성이 높으므로 목록 새로고침 </summary>
        private void FailJoin(string message, RoomEnter? reason)
        {
            OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(message, reason));

            isJoiningLobby = false;
            RefreshLobbyList();
        }

        /// <summary> Steam에 들어가 있지만 JoinedLobby로 기록되지 않은 로비가 있으면 나갑니다. (유령 로비 방지) </summary>
        private void LeaveIfNotRecorded(Lobby? lobby)
        {
            if (!lobby.HasValue) return;
            if (JoinedLobby.HasValue && JoinedLobby.Value.Id == lobby.Value.Id) return;

            try { lobby.Value.Leave(); }
            catch (Exception e) { Dev.LogWarn($"[Lobby] 미완성 로비 퇴장 실패: {e.Message}"); }
        }

        /// <summary> 검색 결과에서 멤버가 있는 첫 로비. 없으면 null </summary>
        private static Lobby? FindFirstActiveLobby(Lobby[] lobbies)
        {
            if (lobbies == null) return null;

            foreach (var lobby in lobbies)
            {
                if (lobby.MemberCount > 0) return lobby;
            }

            return null;
        }

        private static string GenerateLobbyCode()
        {
            var chars = new char[JOIN_CODE_LENGTH];

            lock (codeRandom)
            {
                for (int i = 0; i < JOIN_CODE_LENGTH; i++) chars[i] = JOIN_CODE_CHARS[codeRandom.Next(JOIN_CODE_CHARS.Length)];
            }

            return new string(chars);
        }

        /// <summary> 전 세계 로비에서 중복되지 않는 참가 코드를 만듭니다. 연속 실패 시 마지막 코드를 그대로 사용합니다. </summary>
        private async Task<string> GenerateUniqueLobbyCodeAsync(CancellationToken ct)
        {
            string code = GenerateLobbyCode();

            for (int attempt = 1; attempt <= JOIN_CODE_MAX_ATTEMPTS; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(LobbyKeys.JoinCode, code)
                    .RequestAsync();

                if (lobbies == null || lobbies.Length == 0) return code;

                Dev.LogWarn($"[Lobby] 참가 코드({code})가 이미 존재하여 다시 생성합니다. (시도 {attempt})");
                code = GenerateLobbyCode();
            }

            Dev.LogError("[Lobby] 고유 코드 검증에 연속 실패하여 마지막 코드를 사용합니다.");
            return code;
        }

        /// <summary> 생성/참가 직후 연타 방지 쿨타임 </summary>
        private async Task StartJoinLobbyDelayAsync(CancellationToken ct = default)
        {
            try { await Task.Delay(JOIN_DELAY_MS, ct); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Dev.LogError($"[Lobby] 참가 쿨타임 중 오류: {e.Message}"); }
            finally
            {
                if (this != null) isJoiningLobby = false;
            }
        }

        #endregion
    }
}