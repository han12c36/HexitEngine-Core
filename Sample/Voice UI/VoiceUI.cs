using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HexitEngine.Sample
{
    public class VoiceUI : MonoBehaviour
    {
        private readonly Dictionary<ulong, VoiceRosterItem> items = new();

        [Header("Voice Info")]
        [SerializeField] private TMP_Text voiceStateText;

        [Header("Voice List")]
        [SerializeField] private Transform listContent;
        [SerializeField] private VoiceRosterItem itemPrefab;

        private VoiceService voice;

        private void Start()
        {
            voice = VoiceService.Singleton;

            if (voice != null)
            {
                voice.OnParticipantJoined += HandleParticipantJoined;
                voice.OnParticipantLeft += HandleParticipantLeft;
                voice.OnParticipantUpdated += HandleParticipantChanged;
                voice.OnSpeakingStarted += HandleParticipantChanged;
                voice.OnSpeakingStopped += HandleParticipantChanged;
            }

            RebuildParticipants();
        }

        private void OnDestroy()
        {
            if (voice == null) return;

            voice.OnParticipantJoined -= HandleParticipantJoined;
            voice.OnParticipantLeft -= HandleParticipantLeft;
            voice.OnParticipantUpdated -= HandleParticipantChanged;
            voice.OnSpeakingStarted -= HandleParticipantChanged;
            voice.OnSpeakingStopped -= HandleParticipantChanged;
        }

        private void RebuildParticipants()
        {
            foreach (var item in items.Values)
            {
                if (item != null) Destroy(item.gameObject);
            }

            items.Clear();

            if (voice != null)
            {
                foreach (var participant in voice.Participants) AddItem(participant);
            }

            UpdateState();
        }

        private void AddItem(VoiceParticipant participant)
        {
            if (listContent == null || itemPrefab == null) return;
            if (items.ContainsKey(participant.SteamId)) return;

            var item = Instantiate(itemPrefab, listContent);

            item.Bind(participant);

            // 내 줄을 맨 위에
            if (participant.IsLocal) item.transform.SetAsFirstSibling();

            items.Add(participant.SteamId, item);
        }

        private void RemoveItem(VoiceParticipant participant)
        {
            if (!items.Remove(participant.SteamId, out var item)) return;
            if (item != null) Destroy(item.gameObject);
        }

        private void HandleParticipantJoined(VoiceParticipant participant)
        {
            AddItem(participant);
            UpdateState();
        }

        private void HandleParticipantLeft(VoiceParticipant participant)
        {
            RemoveItem(participant);
            UpdateState();
        }

        /// <summary> 이름 변경, 말하기 시작/멈춤 </summary>
        private void HandleParticipantChanged(VoiceParticipant participant)
        {
            if (items.TryGetValue(participant.SteamId, out var item) && item != null) item.Refresh();
        }

        private void UpdateState()
        {
            if (voiceStateText == null) return;

            if (voice == null || !voice.IsVoiceAvailable) voiceStateText.text = "Voice unavailable";
            else if (!voice.IsStartedSession) voiceStateText.text = "Voice connecting...";
            else voiceStateText.text = $"Voice ({items.Count})";
        }
    }
}