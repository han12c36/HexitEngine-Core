using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class ReadyListItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject readyMark;
        [SerializeField] private GameObject hostMark;
        [SerializeField] private GameObject localMark;
        [SerializeField] private Button kickButton;

        private ulong clientId;
        private Action<ulong> onKick;

        private void Awake()
        {
            if (kickButton != null) kickButton.onClick.AddListener(HandleKickClicked);
        }

        private void OnDestroy()
        {
            if (kickButton != null) kickButton.onClick.RemoveListener(HandleKickClicked);
        }

        public void Bind(ulong clientId, string playerName, bool isReady, bool isHost, bool isLocal, Action<ulong> onKick)
        {
            this.clientId = clientId;
            this.onKick = onKick;

            if (nameText != null) nameText.text = string.IsNullOrEmpty(playerName) ? "Player" : playerName;
            if (readyMark != null) readyMark.SetActive(isReady);
            if (hostMark != null) hostMark.SetActive(isHost);
            if (localMark != null) localMark.SetActive(isLocal);

            if (kickButton != null)
            {
                // 내 줄에는 강퇴 버튼을 그리지 않음 (자기 자신은 강퇴 불가)
                kickButton.gameObject.SetActive(!isLocal);

                // 다른 사람 줄: 방장 화면 + Play 전에만 누를 수 있음
                kickButton.interactable = onKick != null;
            }
        }

        private void HandleKickClicked()
        {
            if (kickButton != null && !kickButton.interactable) return;
            onKick?.Invoke(clientId);
        }
    }
}