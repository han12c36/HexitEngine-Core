using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class VoiceRosterItem : MonoBehaviour
    {
        [Header("Info")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject hostMark;
        [SerializeField] private GameObject localMark;

        [Header("Speaking Indicator")]
        [SerializeField] private TMP_Text indicatorText;
        [SerializeField] private Color speakingColor = Color.green;
        [SerializeField] private Color idleColor = Color.white;

        [Header("Volume")]
        [SerializeField] private Slider volumeSlider;

        [Header("Speak")]
        [SerializeField] private Button speakButton;
        [SerializeField] private Image speakIcon;
        [SerializeField] private Sprite speakOnSprite;
        [SerializeField] private Sprite speakOffSprite;

        [Header("Mic")]
        [SerializeField] private Button micButton;
        [SerializeField] private Image micIcon;
        [SerializeField] private Sprite micOnSprite;
        [SerializeField] private Sprite micOffSprite;

        private VoiceParticipant participant;

        public VoiceParticipant Participant => participant;

        private void Awake()
        {
            if (speakButton != null) speakButton.onClick.AddListener(HandleSpeakerClicked);
            if (micButton != null) micButton.onClick.AddListener(HandleMicClicked);

            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
                volumeSlider.onValueChanged.AddListener(HandleVolumeChanged);
            }
        }

        private void OnDestroy()
        {
            if (speakButton != null) speakButton.onClick.RemoveListener(HandleSpeakerClicked);
            if (micButton != null) micButton.onClick.RemoveListener(HandleMicClicked);
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(HandleVolumeChanged);
        }

        /// <summary> 참가자를 연결합니다. 내 줄이면 마이크 버튼이 보이고 볼륨 슬라이더는 비활성화됩니다. </summary>
        public void Bind(VoiceParticipant participant)
        {
            this.participant = participant;

            bool isLocal = participant.IsLocal;

            if (micButton != null) micButton.gameObject.SetActive(isLocal);

            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(isLocal ? 1f : participant.Volume);
                volumeSlider.interactable = !isLocal;
            }

            Refresh();
        }


        /// <summary> 이름, 방장 표시, 말하는 중 표시, 아이콘을 다시 그립니다. </summary>
        public void Refresh()
        {
            if (participant == null) return;

            var voice = VoiceService.Singleton;
            bool isAvailable = voice != null && voice.IsVoiceAvailable;
            bool isLocal = participant.IsLocal;

            if (nameText != null) nameText.text = string.IsNullOrEmpty(participant.DisplayName) ? "Player" : participant.DisplayName;
            if (localMark != null) localMark.SetActive(isLocal);
            if (hostMark != null) hostMark.SetActive(IsHost(participant));

            // 스피커: 내 줄은 내 스피커, 다른 사람 줄은 그 사람 음소거
            bool isSpeakerOff = isLocal ? isAvailable && voice.IsDeafened : participant.IsLocallyMuted;

            // 마이크: 내 줄에만
            bool isMicOff = isLocal && isAvailable && voice.IsMuted;

            // 내 줄은 마이크가 꺼져 있으면, 다른 사람 줄은 내가 음소거했으면 "말하는 중"을 표시하지 않음
            bool isSilenced = isLocal ? isMicOff : participant.IsLocallyMuted;
            if (indicatorText != null) indicatorText.color = participant.IsSpeaking && !isSilenced ? speakingColor : idleColor;

            SetIcon(speakIcon, isSpeakerOff ? speakOffSprite : speakOnSprite);
            SetIcon(micIcon, isMicOff ? micOffSprite : micOnSprite);

            if (speakButton != null) speakButton.interactable = isAvailable;
            if (micButton != null) micButton.interactable = isAvailable;
        }

        /// <summary> [나만] 내 줄이면 내 스피커를, 다른 사람 줄이면 그 사람 목소리를 끄고 켭니다. </summary>
        private void HandleSpeakerClicked()
        {
            if (participant == null) return;

            if (participant.IsLocal)
            {
                var voice = VoiceService.Singleton;
                if (voice == null || !voice.IsVoiceAvailable) return;

                voice.IsDeafened = !voice.IsDeafened;
            }
            else
            {
                participant.IsLocallyMuted = !participant.IsLocallyMuted;
            }

            Refresh();
        }

        /// <summary> [나만] 내 마이크를 끄고 켭니다. (내 줄에만 있음) </summary>
        private void HandleMicClicked()
        {
            if (participant == null || !participant.IsLocal) return;

            var voice = VoiceService.Singleton;
            if (voice == null || !voice.IsVoiceAvailable) return;

            voice.IsMuted = !voice.IsMuted;
            Refresh();
        }

        /// <summary> [나만] 이 사람의 목소리 볼륨 (0~1) </summary>
        private void HandleVolumeChanged(float value)
        {
            if (participant == null || participant.IsLocal) return;
            participant.Volume = value;
        }

        private static void SetIcon(Image icon, Sprite sprite)
        {
            if (icon != null && sprite != null) icon.sprite = sprite;
        }

        private static bool IsHost(VoiceParticipant participant)
        {
            var multiplayer = MultiplayerService.Singleton;
            return multiplayer != null && participant.ClientId == multiplayer.ServerClientId;
        }
    }
}