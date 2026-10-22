using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    /// <summary> Steam 로비 데이터 키와 값 </summary>
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
    /// 실패 이벤트에는 번역하지 않은 사유 키(SystemKeys)만 담습니다. 예외 원문은 로그로만 남깁니다.
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

        /// <summary> 로비 데이터 변경(강퇴), 멤버 퇴장, Steam 오버레이 초대 수락 이벤트를 구독합니다. 기다릴 작업이 없으므로 즉시 완료됩니다. </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChangedCallback;
            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChangedCallback;

            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeaveCallback;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeaveCallback;

            SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberLeaveCallback;
            SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberLeaveCallback;

            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequestedCallback;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequestedCallback;

            return UniTask.CompletedTask;
        }

        protected override void OnDestroyed()
        {
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChangedCallback;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeaveCallback;
            SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberLeaveCallback;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequestedCallback;
        }

        #endregion

        #region Create / Join

        /// <summary>
        /// 로비를 생성합니다. 실패하면 반드시 OnCreateLobbyFailedToLobby가 발생합니다. (취소는 예외로 전달)
        /// 시작 이벤트는 HEXITEngine이 발생시킵니다.
        /// </summary>
        public async Task<LobbyResult> CreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (isJoiningLobby) return FailCreate(SystemKeys.AlreadyJoiningLobby);
            if (!SteamAuthService.IsInitialized) return FailCreate(SystemKeys.SteamLoginRequired);

            isJoiningLobby = true;

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

                // 실패 직후 바로 다시 시도할 수 있도록 쿨타임 없이 잠금 해제
                isJoiningLobby = false;

                Dev.LogWarn($"로비 생성 실패: {e.Message}");
                return FailCreate(SystemKeys.LobbyCreateFailed);
            }
            finally
            {
                // 실패로 이미 잠금이 풀렸다면 쿨타임을 주지 않음
                if (isJoiningLobby) _ = StartJoinLobbyDelayAsync(ct);
            }
        }

        /// <summary>
        /// 로비 ID로 참가합니다. 실패하면 반드시 OnJoinFailedToLobby가 발생합니다. (취소는 예외로 전달)
        /// 시작 이벤트는 HEXITEngine이 발생시킵니다.
        /// </summary>
        public async Task<LobbyResult> JoinLobbyAsync(string lobbyIdString, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (isJoiningLobby) return FailJoinEarly(SystemKeys.AlreadyJoiningLobby);
            if (!ulong.TryParse(lobbyIdString, out ulong lobbyId)) return FailJoinEarly(SystemKeys.InvalidLobbyId);

            isJoiningLobby = true;

            var targetLobby = new Lobby(lobbyId);
            bool hasEntered = false;

            try
            {
                var joinResult = await targetLobby.Join();

                if (joinResult != RoomEnter.Success)
                {
                    Dev.LogWarn($"로비 입장 실패: {joinResult}");
                    FailJoin(ToJoinFailedKey(joinResult), joinResult);
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

                Dev.LogWarn($"로비 입장 실패: {e.Message}");
                FailJoin(SystemKeys.LobbyJoinFailed, null);
                return LobbyResult.Fail(0, "System", e.Message);
            }
            finally
            {
                // 실패로 이미 잠금이 풀렸다면 쿨타임을 주지 않음
                if (isJoiningLobby) _ = StartJoinLobbyDelayAsync(ct);
            }
        }

        /// <summary> 참가 코드로 방을 찾아 참가합니다. 실패하면 반드시 OnJoinFailedToLobby가 발생합니다. (취소는 예외로 전달) </summary>
        public async Task<LobbyResult> JoinLobbyByCodeAsync(string joinCode, CancellationToken ct = default)
        {
            if (isJoiningLobby) return FailJoinEarly(SystemKeys.AlreadyJoiningLobby);

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
                    OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(SystemKeys.InvalidJoinCode));
                    return LobbyResult.Fail(0, "Lobby", SystemKeys.InvalidJoinCode);
                }

                // 이후 입장 과정의 실패는 JoinLobbyAsync가 OnJoinFailedToLobby를 발생시킴
                return await JoinLobbyAsync(target.Value.Id.ToString(), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Dev.LogWarn($"코드 참가 실패: {e.Message}");
                OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(SystemKeys.LobbyJoinFailed));
                return LobbyResult.Fail(0, "System", SystemKeys.LobbyJoinFailed);
            }
        }

        /// <summary>
        /// 들어갈 수 있는 방을 찾아 참가합니다. (취소는 예외로 전달)
        /// 방을 못 찾으면 OnQuickJoinFailedToLobby, 찾은 방 입장에 실패하면 OnJoinFailedToLobby가 발생합니다.
        /// </summary>
        public async Task<LobbyResult> QuickJoinLobbyAsync(CancellationToken ct = default)
        {
            if (isJoiningLobby)
            {
                OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(SystemKeys.AlreadyJoiningLobby));
                return LobbyResult.Fail(0, "System", SystemKeys.AlreadyJoiningLobby);
            }

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
                    OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(SystemKeys.NoAvailableRoom));
                    return LobbyResult.Fail(0, "Lobby", SystemKeys.NoAvailableRoom);
                }

                return await JoinLobbyAsync(target.Value.Id.ToString(), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Dev.LogWarn($"빠른 참가 실패: {e.Message}");
                OnQuickJoinFailedToLobby?.Invoke(this, new OnLobbyQuickJoinFailedEventArgs(SystemKeys.LobbyJoinFailed));
                return LobbyResult.Fail(0, "System", SystemKeys.LobbyJoinFailed);
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

        /// <summary> 참가한 로비의 현재 멤버인지 (Steam 로비 기준) </summary>
        public bool IsLobbyMember(ulong steamId)
        {
            if (!JoinedLobby.HasValue) return false;

            foreach (var member in JoinedLobby.Value.Members)
            {
                if (member.Id.Value == steamId) return true;
            }

            return false;
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
                // 네트워크 종료는 HEXITEngine이 OnKickedFromLobby를 받아서 처리
                LeaveLobby();

                OnKickedFromLobby?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 참가한 로비에서 다른 멤버가 나감(정상 퇴장, 게임 종료, 강제 종료 모두).
        /// 게임 프로세스가 끝나면 Steam 클라이언트가 로비에서 바로 빼 주므로, 네트워크 시간 초과보다 훨씬 빨리 알 수 있습니다.
        /// </summary>
        private void OnLobbyMemberLeaveCallback(Lobby lobby, Friend member)
        {
            if (!JoinedLobby.HasValue || JoinedLobby.Value.Id != lobby.Id) return;
            if (member.Id.Value == SteamAuthService.SteamId) return;

            OnLobbyMemberLeft?.Invoke(this, new OnLobbyMemberLeftEventArgs(member.Id.Value));
        }

        /// <summary> 생성 실패 처리: 이벤트 발생 후 실패 결과 반환 </summary>
        private LobbyResult FailCreate(string reasonKey)
        {
            OnCreateLobbyFailedToLobby?.Invoke(this, new OnLobbyCreateFailedEventArgs(reasonKey));
            return LobbyResult.Fail(0, "System", reasonKey);
        }

        /// <summary> 입장 시도 전 실패 처리(잠금을 걸기 전): 이벤트 발생 후 실패 결과 반환. 잠금은 건드리지 않습니다. </summary>
        private LobbyResult FailJoinEarly(string reasonKey)
        {
            OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(reasonKey));
            return LobbyResult.Fail(0, "System", reasonKey);
        }

        /// <summary> 입장 실패 처리: 이벤트 발생, 쿨타임 없이 잠금 해제, 유령 방일 가능성이 높으므로 목록 새로고침 </summary>
        private void FailJoin(string reasonKey, RoomEnter? reason)
        {
            OnJoinFailedToLobby?.Invoke(this, new OnLobbyJoinFailedEventArgs(reasonKey, reason));

            isJoiningLobby = false;
            RefreshLobbyList();
        }

        /// <summary> Steam 입장 결과를 사유 키로 바꿉니다. 자주 나오는 경우만 따로 나누고 나머지는 공통 실패 키 </summary>
        private static string ToJoinFailedKey(RoomEnter result) => result switch
        {
            RoomEnter.DoesntExist => SystemKeys.RoomNotFound,
            RoomEnter.Full => SystemKeys.RoomFull,
            _ => SystemKeys.LobbyJoinFailed,
        };

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