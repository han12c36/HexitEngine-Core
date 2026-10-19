using Dissonance;

namespace HexitEngine
{
    /// <summary>
    /// 보이스 로스터의 한 줄.
    /// VoiceService가 "Dissonance 입장 + PlayerData 존재"를 모두 확인한 뒤에만 생성하므로 PlayerData는 항상 유효합니다.
    /// </summary>
    public sealed class VoiceParticipant
    {
        public ulong SteamId { get; }

        /// <summary> 최신 PlayerData. 바뀌면 VoiceService가 갱신 후 OnParticipantUpdated를 발생시킵니다. </summary>
        public PlayerData PlayerData { get; internal set; }

        /// <summary> Dissonance 참가자 상태 </summary>
        public VoicePlayerState VoiceState { get; }

        public bool IsLocal => VoiceState.IsLocalPlayer;
        public float Amplitude => VoiceState.Amplitude;
        public bool IsSpeaking { get; internal set; }
        internal float LastSpokenTime { get; set; }

        // 자주 쓰는 값 바로가기
        public ulong ClientId => PlayerData.clientId;
        public int PlayerId => PlayerData.playerId;
        /// <summary> 화면 표시용 이름 (인게임 이름이 있으면 그것, 없으면 Steam 닉네임) </summary>
        public string DisplayName => PlayerData.PlayerName;

        /// <summary> 이 사람의 목소리를 나만 듣지 않기 </summary>
        public bool IsLocallyMuted
        {
            get => VoiceState.IsLocallyMuted;
            set => VoiceState.IsLocallyMuted = value;
        }

        /// <summary> 이 사람의 목소리 볼륨 (0~1) </summary>
        public float Volume
        {
            get => VoiceState.Volume;
            set => VoiceState.Volume = value;
        }

        internal VoiceParticipant(ulong steamId, VoicePlayerState voiceState, PlayerData playerData)
        {
            SteamId = steamId;
            VoiceState = voiceState;
            PlayerData = playerData;
        }

        /// <summary> 로스터에서 빠질 때 호출됩니다. 기존 VoiceParticipant.Cleanup의 내용이 있다면 여기로 옮기세요. </summary>
        internal void Cleanup()
        {
            IsSpeaking = false;
        }
    }
}