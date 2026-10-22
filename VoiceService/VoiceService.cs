using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Dissonance;
using Dissonance.Audio.Capture;
using Dissonance.Config;
using Steamworks;
using UnityEngine;

namespace HexitEngine
{
    public enum VoiceChannel
    {
        Channel1, // 예: 생존자 채널
        Channel2, // 예: 사망자 채널
        Channel3  // 예: 옵저버 또는 시스템 채널 등
    }

    /// <summary>
    /// Dissonance 기반 보이스 서비스. (설정, 채널)
    /// 참가자 로스터는 VoiceService.Roster.cs에 있습니다.
    ///
    /// - DissonanceComms 컴포넌트는 인스펙터에서 반드시 비활성화 상태여야 합니다. 초기화 시 SteamID를 등록한 뒤 켭니다.
    /// - 보이스 식별자는 닉네임이 아니라 SteamID입니다. 닉네임은 중복될 수 있고, 불러오는 데 시간이 걸리기 때문입니다.
    /// - Steam이 초기화되어 있어야 하므로 부트스트랩 순서상 SteamAuthService보다 뒤에 배치합니다.
    /// </summary>
    public partial class VoiceService : LocalSingleton<VoiceService>
    {
        // VAD 임계값 (마이크 노이즈에 따라 0.01f ~ 0.05f 사이로 조절)
        private const float VAD_THRESHOLD = 0.01f;
        private const float VAD_HANGOVER_TIME = 0.5f;

        private const int CHANNEL_COUNT = 3;

        [SerializeField] private DissonanceComms dissonanceComms;

        private readonly RoomMembership?[] listeningMemberships = new RoomMembership?[CHANNEL_COUNT];
        private readonly RoomChannel?[] speakingChannels = new RoomChannel?[CHANNEL_COUNT];

        public DissonanceComms DissonanceComms => dissonanceComms;

        /// <summary> 입력 장치가 바뀌었을 때 </summary>
        public event EventHandler OnCurrentInputDeviceChanged;

        /// <summary> 보이스 사용 가능 여부. Steam이 없어서 Dissonance를 켜지 못했다면 false. </summary>
        public bool IsVoiceAvailable { get; private set; }

        #region Lifecycle

        /// <summary>
        /// Dissonance 세션 이벤트를 구독하고, 로컬 SteamID를 보이스 식별자로 등록한 뒤 Dissonance를 켭니다.
        /// 기다릴 작업이 없으므로 즉시 완료됩니다.
        /// </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            if (dissonanceComms == null)
            {
                throw new InvalidOperationException("[Voice] DissonanceComms가 연결되지 않았습니다.");
            }

            // 1. 세션 입장/퇴장 이벤트 구독 (로스터 관리용)
            dissonanceComms.OnPlayerJoinedSession -= HandleVoiceJoined;
            dissonanceComms.OnPlayerJoinedSession += HandleVoiceJoined;

            dissonanceComms.OnPlayerLeftSession -= HandleVoiceLeft;
            dissonanceComms.OnPlayerLeftSession += HandleVoiceLeft;

            // 2. SteamID를 식별자로 등록하고 Dissonance 활성화
            //    Steam이 없으면(에디터 단독 테스트 등) 보이스 없이 부팅은 계속합니다.
            if (SteamClient.IsValid)
            {
                dissonanceComms.LocalPlayerName = SteamClient.SteamId.ToString();
                dissonanceComms.enabled = true;
                IsVoiceAvailable = true;
            }
            else
            {
                Dev.LogWarn("[Voice] Steam이 초기화되지 않아 보이스를 비활성화합니다.");
            }

            // 3. 메서드 종료 → 베이스가 IsInitialized = true 설정
            return UniTask.CompletedTask;
        }

        protected override void OnDestroyed()
        {
            if (dissonanceComms != null)
            {
                dissonanceComms.OnPlayerJoinedSession -= HandleVoiceJoined;
                dissonanceComms.OnPlayerLeftSession -= HandleVoiceLeft;
            }
        }

        #endregion

        #region Settings

        /// <summary> 내 마이크 끄기 </summary>
        public bool IsMuted
        {
            get => dissonanceComms.IsMuted;
            set => dissonanceComms.IsMuted = value;
        }

        /// <summary> 다른 사람 소리 듣지 않기 </summary>
        public bool IsDeafened
        {
            get => dissonanceComms.IsDeafened;
            set => dissonanceComms.IsDeafened = value;
        }

        /// <summary> 사용 가능한 마이크 목록 </summary>
        public IEnumerable<string> AvailableInputDevices
        {
            get
            {
                var devices = new List<string>();
                dissonanceComms.GetMicrophoneDevices(devices);
                return devices;
            }
        }

        /// <summary> Dissonance는 출력 장치 목록을 제공하지 않으므로 시스템 기본값만 노출 </summary>
        public IEnumerable<string> AvailableOutputDevices => new List<string> { "System Default" };

        /// <summary> 현재 마이크 이름 </summary>
        public string CurrentInputDevice => dissonanceComms.MicrophoneName;

        /// <summary> VAD 감도 (0~4 단계). 이름은 기존 호환을 위해 유지합니다. </summary>
        public int InputVolume
        {
            get => (int)VoiceSettings.Instance.VadSensitivity;
            set => VoiceSettings.Instance.VadSensitivity = (VadSensitivityLevels)Mathf.Clamp(value, 0, 4);
        }

        /// <summary> 타인 목소리 전체 볼륨 (0~100) </summary>
        public int OutputVolume
        {
            get => (int)(dissonanceComms.RemoteVoiceVolume * 100);
            set => dissonanceComms.RemoteVoiceVolume = Mathf.Clamp01(value / 100f);
        }

        /// <summary> 마이크 장치 변경 </summary>
        public void SetInputDevice(string deviceName)
        {
            dissonanceComms.MicrophoneName = deviceName;
            OnCurrentInputDeviceChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary> 내 마이크 진폭. 세션 밖(설정 화면 마이크 테스트 등)에서도 동작합니다. </summary>
        public float GetLocalAudioEnergy()
        {
            if (!IsVoiceAvailable) return 0f;

            var local = dissonanceComms.FindPlayer(dissonanceComms.LocalPlayerName);
            return local != null ? local.Amplitude : 0f;
        }

        #endregion

        #region Channels

        /// <summary> 채널 참가. 이미 참가 중이면 듣기/말하기 상태만 갱신합니다. </summary>
        public void JoinChannel(VoiceChannel channel, bool listen = true, bool speak = true)
        {
            if (!IsVoiceAvailable) return;

            int index = (int)channel;
            string channelName = channel.ToString();

            bool isListening = listeningMemberships[index].HasValue;
            bool isSpeaking = speakingChannels[index].HasValue;

            // 듣기 상태 반영
            if (listen && !isListening)
            {
                listeningMemberships[index] = dissonanceComms.Rooms.Join(channelName);
            }
            else if (!listen && isListening)
            {
                dissonanceComms.Rooms.Leave(listeningMemberships[index].Value);
                listeningMemberships[index] = null;
            }

            // 말하기 상태 반영
            if (speak && !isSpeaking)
            {
                speakingChannels[index] = dissonanceComms.RoomChannels.Open(channelName);
            }
            else if (!speak && isSpeaking)
            {
                speakingChannels[index].Value.Dispose();
                speakingChannels[index] = null;
            }
        }

        /// <summary> 채널에서 듣기/말하기 모두 중단 </summary>
        public void LeaveChannel(VoiceChannel channel)
        {
            if (dissonanceComms == null) return;

            int index = (int)channel;

            if (listeningMemberships[index].HasValue)
            {
                dissonanceComms.Rooms.Leave(listeningMemberships[index].Value);
                listeningMemberships[index] = null;
            }

            if (speakingChannels[index].HasValue)
            {
                speakingChannels[index].Value.Dispose();
                speakingChannels[index] = null;
            }
        }

        /// <summary> 모든 채널에서 나가기 </summary>
        public void LeaveAllChannels()
        {
            for (int i = 0; i < CHANNEL_COUNT; i++) LeaveChannel((VoiceChannel)i);
        }

        #endregion
    }
}