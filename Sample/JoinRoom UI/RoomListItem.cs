using System;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    [RequireComponent(typeof(Button))]
    public class RoomListItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text roomNameText;
        [SerializeField] private TMP_Text memberCountText;
        [SerializeField] private GameObject fullMark;

        private Button button;
        private Lobby lobby;
        private Action<Lobby> onJoin;
        private bool isFull;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(HandleClicked);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClicked);
        }

        public void Bind(Lobby lobby, Action<Lobby> onJoin)
        {
            this.lobby = lobby;
            this.onJoin = onJoin;

            string roomName = lobby.GetData(LobbyKeys.Name);
            if (roomNameText != null) roomNameText.text = string.IsNullOrEmpty(roomName) ? "(No Name)" : roomName;

            isFull = lobby.MemberCount >= lobby.MaxMembers;

            if (memberCountText != null) memberCountText.text = $"{lobby.MemberCount} / {lobby.MaxMembers}";
            if (fullMark != null) fullMark.SetActive(isFull);

            button.interactable = !isFull;
        }

        private void HandleClicked()
        {
            if (isFull) return;
            onJoin?.Invoke(lobby);
        }
    }
}