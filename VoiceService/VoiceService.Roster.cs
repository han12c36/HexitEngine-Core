using System;
using System.Collections.Generic;
using Dissonance;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// 보이스 로스터 (참가자 목록과 말하기 감지).
    ///
    /// 계약: 보이스 참가자는 "Dissonance 세션 입장" + "PlayerData 존재(isConnected)" 두 조건이 모두 충족될 때만 생성됩니다.
    /// 둘 중 무엇이 먼저 도착하든 대기열에서 기다렸다가, 나머지가 도착하는 순간 OnParticipantJoined가 발생합니다.
    /// 따라서 UI는 이벤트를 받는 시점에 항상 유효한 PlayerData를 가지고 있습니다. (캐릭터 스폰과 같은 계약)
    /// </summary>
    public partial class VoiceService
    {
        /// <summary> 로스터에 올라간 참가자 (SteamID 기준) </summary>
        private readonly Dictionary<ulong, VoiceParticipant> participants = new();

        /// <summary> Dissonance에는 들어왔지만 PlayerData가 아직 없는 참가자 </summary>
        private readonly Dictionary<ulong, VoicePlayerState> pendingStates = new();

        // 순회 중 컬렉션 변경을 피하기 위한 재사용 버퍼
        private readonly List<ulong> syncBuffer = new();
        private readonly List<ulong> promoteBuffer = new();

        private MultiplayerService boundMultiplayer;

        /// <summary> 로스터 추적 중인지 여부 </summary>
        public bool IsStartedSession { get; private set; }

        // 주의: 아래 이벤트 핸들러 안에서 StartSession/StopSession을 호출하지 마세요.

        /// <summary> 로스터에 참가자 추가 (PlayerData 보장) </summary>
        public event Action<VoiceParticipant> OnParticipantJoined;
        /// <summary> 로스터에서 참가자 제거 </summary>
        public event Action<VoiceParticipant> OnParticipantLeft;
        /// <summary> 참가자의 PlayerData가 바뀜 (이름 변경 등) </summary>
        public event Action<VoiceParticipant> OnParticipantUpdated;
        /// <summary> 실제로 말하기 시작 </summary>
        public event Action<VoiceParticipant> OnSpeakingStarted;
        /// <summary> 말하기 멈춤 (퇴장 시에도 반드시 발생) </summary>
        public event Action<VoiceParticipant> OnSpeakingStopped;

        /// <summary> UI 초기 구성용 현재 참가자 목록 </summary>
        public IReadOnlyCollection<VoiceParticipant> Participants => participants.Values;

        public bool TryGetParticipant(ulong steamId, out VoiceParticipant participant)
            => participants.TryGetValue(steamId, out participant);

        #region Session

        /// <summary> 네트워크 접속 후 호출. 로스터 추적을 시작합니다. </summary>
        public void StartSession()
        {
            if (!IsInitialized || !IsVoiceAvailable)
            {
                Dev.LogWarn("[Voice] 보이스를 사용할 수 없어 세션을 시작하지 않습니다.");
                return;
            }

            if (IsStartedSession)
            {
                Dev.LogWarn("[Voice] 이미 보이스 세션이 열려 있습니다.");
                return;
            }

            IsStartedSession = true;

            BindPlayerDatas();

            // 이미 Dissonance 세션에 있는 사람들(나 포함)을 대기열에 올리고 승격 시도
            foreach (var state in dissonanceComms.Players) HandleVoiceJoined(state);
        }

        /// <summary> 네트워크 종료 시 호출. 로스터를 비우고 채널에서 나갑니다. </summary>
        public void StopSession()
        {
            if (!IsStartedSession)
            {
                Dev.LogWarn("[Voice] 이미 보이스 세션이 닫혀 있습니다.");
                return;
            }

            IsStartedSession = false;

            UnbindPlayerDatas();

            syncBuffer.Clear();
            syncBuffer.AddRange(participants.Keys);
            foreach (var id in syncBuffer) RemoveParticipant(id, backToPending: false);

            pendingStates.Clear();

            LeaveAllChannels();
        }

        #endregion

        #region Dissonance Events

        private void HandleVoiceJoined(VoicePlayerState state)
        {
            if (!IsStartedSession) return;

            if (!TryParseSteamId(state, out ulong steamId))
            {
                Dev.LogWarn($"[Voice] SteamID 형식이 아닌 보이스 참가자 무시: {state?.Name}");
                return;
            }

            if (participants.ContainsKey(steamId)) return;

            pendingStates[steamId] = state;
            TryPromote(steamId);
        }

        private void HandleVoiceLeft(VoicePlayerState state)
        {
            if (!TryParseSteamId(state, out ulong steamId)) return;

            pendingStates.Remove(steamId);
            RemoveParticipant(steamId, backToPending: false);
        }

        #endregion

        #region PlayerData Sync

        private void BindPlayerDatas()
        {
            UnbindPlayerDatas();

            boundMultiplayer = MultiplayerService.Singleton;
            if (boundMultiplayer == null) return;

            boundMultiplayer.OnPlayerDatasChanged += HandlePlayerDatasChanged;
        }

        private void UnbindPlayerDatas()
        {
            if (boundMultiplayer != null) boundMultiplayer.OnPlayerDatasChanged -= HandlePlayerDatasChanged;
            boundMultiplayer = null;
        }

        private void HandlePlayerDatasChanged(object sender, MultiplayerService.PlayerDatasChangedEventArgs e)
        {
            if (!IsStartedSession) return;

            // 1. 기존 참가자: PlayerData 갱신 또는 제거
            syncBuffer.Clear();
            syncBuffer.AddRange(participants.Keys);

            foreach (var steamId in syncBuffer)
            {
                var participant = participants[steamId];

                if (TryGetConnectedPlayerData(steamId, out var latest))
                {
                    if (!participant.PlayerData.Equals(latest))
                    {
                        participant.PlayerData = latest;
                        OnParticipantUpdated?.Invoke(participant);
                    }
                }
                else
                {
                    // PlayerData는 사라졌지만 보이스에는 남아 있을 수 있으므로 대기열로 되돌림
                    RemoveParticipant(steamId, backToPending: true);
                }
            }

            // 2. 대기 중인 보이스 참가자 승격 시도
            PromotePending();
        }

        #endregion

        #region Roster Core

        private void PromotePending()
        {
            if (pendingStates.Count == 0) return;

            promoteBuffer.Clear();
            promoteBuffer.AddRange(pendingStates.Keys);

            foreach (var steamId in promoteBuffer) TryPromote(steamId);
        }

        /// <summary> 대기 중인 참가자의 PlayerData가 있으면 로스터에 올립니다. </summary>
        private void TryPromote(ulong steamId)
        {
            if (!IsStartedSession) return;

            if (participants.ContainsKey(steamId))
            {
                pendingStates.Remove(steamId);
                return;
            }

            if (!pendingStates.TryGetValue(steamId, out var state)) return;
            if (!TryGetConnectedPlayerData(steamId, out var playerData)) return;

            pendingStates.Remove(steamId);

            var participant = new VoiceParticipant(steamId, state, playerData);
            participants.Add(steamId, participant);

            OnParticipantJoined?.Invoke(participant);
        }

        /// <summary> 로스터에서 제거합니다. backToPending이면 대기열로 되돌려 PlayerData가 다시 오면 재승격됩니다. </summary>
        private void RemoveParticipant(ulong steamId, bool backToPending)
        {
            if (!participants.Remove(steamId, out var participant)) return;

            // 말하던 도중 나가도 UI의 "말하는 중" 표시가 남지 않도록 반드시 정지 이벤트 발생
            if (participant.IsSpeaking)
            {
                participant.IsSpeaking = false;
                OnSpeakingStopped?.Invoke(participant);
            }

            if (backToPending && IsStartedSession) pendingStates[steamId] = participant.VoiceState;

            OnParticipantLeft?.Invoke(participant);

            participant.Cleanup();
        }

        private static bool TryGetConnectedPlayerData(ulong steamId, out PlayerData playerData)
        {
            playerData = PlayerData.EmptyData;

            var multiplayer = MultiplayerService.Singleton;
            if (multiplayer == null) return false;

            return multiplayer.TryGetPlayerDataBySteamId(steamId, out playerData) && playerData.isConnected;
        }

        /// <summary> Dissonance 참가자 이름(SteamID 문자열)을 ulong으로 변환합니다. </summary>
        private static bool TryParseSteamId(VoicePlayerState state, out ulong steamId)
        {
            steamId = 0;
            return state != null && ulong.TryParse(state.Name, out steamId);
        }

        #endregion

        #region Speaking Detection

        private void Update()
        {
            if (!IsStartedSession || participants.Count == 0) return;

            float now = Time.time;

            foreach (var participant in participants.Values)
            {
                bool isMuted = participant.IsLocal ? IsMuted : participant.IsLocallyMuted;
                bool isMakingNoise = !isMuted && participant.Amplitude > VAD_THRESHOLD;

                if (isMakingNoise)
                {
                    participant.LastSpokenTime = now;

                    if (!participant.IsSpeaking)
                    {
                        participant.IsSpeaking = true;
                        OnSpeakingStarted?.Invoke(participant);
                    }
                }
                else if (participant.IsSpeaking && now - participant.LastSpokenTime > VAD_HANGOVER_TIME)
                {
                    // 소리가 멈춘 뒤 HANGOVER_TIME이 지나야 정지로 판정 (말 사이 짧은 공백에 깜빡이지 않도록)
                    participant.IsSpeaking = false;
                    OnSpeakingStopped?.Invoke(participant);
                }
            }
        }

        #endregion
    }
}