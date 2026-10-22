using Steamworks.Data;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static HexitEngine.LobbyService;

namespace HexitEngine.Sample
{
    public class JoinRoomUI : MonoBehaviour
    {
        [SerializeField] private Transform listContent;
        [SerializeField] private RoomListItem itemPrefab;
        [SerializeField] private GameObject emptyState;

        [SerializeField] private Button refreshButton;
        [SerializeField] private float autoRefreshInterval = 5f;

        [SerializeField] private Button quickJoinButton;
        [SerializeField] private Button codeButton;
        [SerializeField] private JoinByCodeUI joinByCodeUI;

        [SerializeField] private Button backButton;
        [SerializeField] private MainMenuUI mainMenu;

        private readonly List<RoomListItem> items = new();

        private HEXITEngine hexit;
        private float nextAutoRefreshAt;

        private void Awake()
        {
            if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
            if (quickJoinButton != null) quickJoinButton.onClick.AddListener(QuickJoin);
            if (codeButton != null) codeButton.onClick.AddListener(OpenJoinByCode);
            if (backButton != null) backButton.onClick.AddListener(Back);

            hexit = HEXITEngine.Singleton;

            if (hexit == null)
            {
                Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
                return;
            }

            hexit.OnLobbyListChanged += HandleLobbyListChanged;
        }

        private void OnDestroy()
        {
            if (refreshButton != null) refreshButton.onClick.RemoveListener(Refresh);
            if (quickJoinButton != null) quickJoinButton.onClick.RemoveListener(QuickJoin);
            if (codeButton != null) codeButton.onClick.RemoveListener(OpenJoinByCode);
            if (backButton != null) backButton.onClick.RemoveListener(Back);

            if (hexit != null) hexit.OnLobbyListChanged -= HandleLobbyListChanged;
        }

        /// <summary> 열 때마다 코드 팝업을 닫고 목록을 새로 검색합니다. </summary>
        private void OnEnable()
        {   
            if (joinByCodeUI != null) joinByCodeUI.Close();

            if (quickJoinButton != null) quickJoinButton.interactable = hexit != null;
            if (codeButton != null) codeButton.interactable = hexit != null && joinByCodeUI != null;

            Refresh();
        }

        private void Update()
        {
            if (hexit == null || autoRefreshInterval <= 0f) return;

            if (Time.realtimeSinceStartup >= nextAutoRefreshAt) Refresh();
        }

        /// <summary> 방 목록을 다시 검색합니다. 결과는 OnLobbyListChanged로 옵니다. </summary>
        public void Refresh()
        {
            if (hexit == null) return;

            nextAutoRefreshAt = Time.realtimeSinceStartup + Mathf.Max(autoRefreshInterval, 1f);
            hexit.RefreshLobbyList();
        }

        /// <summary> 목록에서 고른 방에 참가합니다. </summary>
        private void Join(Lobby lobby)
        {
            if (hexit == null) return;
            hexit.JoinLobby(lobby);
        }

        /// <summary> 들어갈 수 있는 아무 방에나 참가합니다. </summary>
        public void QuickJoin()
        {
            if (hexit == null) return;
            hexit.QuickJoinLobby();
        }

        /// <summary> 참가 코드 입력 팝업을 엽니다. </summary>
        public void OpenJoinByCode()
        {
            if (joinByCodeUI != null) joinByCodeUI.Open();
        }

        /// <summary> 메인메뉴로 돌아갑니다. </summary>
        public void Back()
        {
            if (mainMenu != null) mainMenu.ShowMainMenu();
            else gameObject.SetActive(false);
        }

        private void HandleLobbyListChanged(object sender, OnLobbyListChangedEventArgs e)
        {
            if (!isActiveAndEnabled) return;

            Rebuild(e.lobbyList);
        }

        /// <summary> 받은 목록으로 항목을 다시 그립니다. 항목 오브젝트는 재사용합니다. </summary>
        private void Rebuild(List<Lobby> lobbies)
        {
            int count = lobbies?.Count ?? 0;

            if (listContent != null && itemPrefab != null)
            {
                while (items.Count < count) items.Add(Instantiate(itemPrefab, listContent));
            }

            int shown = Mathf.Min(count, items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                bool active = i < shown;
                items[i].gameObject.SetActive(active);

                if (active) items[i].Bind(lobbies[i], Join);
            }

            if (emptyState != null) emptyState.SetActive(shown == 0);
        }
    }
}