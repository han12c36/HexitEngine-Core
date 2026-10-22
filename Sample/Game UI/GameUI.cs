using System;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class GameUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button endButton;
        [SerializeField] private Button leaveButton;

        [Header("Leave Confirm")]
        [SerializeField] private ConfirmPopupUI confirmPopup;
        [SerializeField] private string leaveConfirmKey = "SYS_Confirm_LeaveGame";
        [SerializeField] private string hostLeaveConfirmKey = "SYS_Confirm_LeaveGameHost";

        private HEXITEngine hexit;
        private GameManagerBase gameManager;

        private bool isLeaving;
        private bool isEnding;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (endButton != null) endButton.onClick.AddListener(EndGame);
            if (leaveButton != null) leaveButton.onClick.AddListener(Leave);
        }

        private void Start()
        {
            hexit = HEXITEngine.Singleton;
            gameManager = GameManagerBase.Singleton;

            if (hexit == null) Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
            if (gameManager == null) Dev.LogError("GameManagerBase를 상속한 매니저가 Game 씬에 없습니다.");

            if (gameManager != null)
            {
                gameManager.OnGameStarted += HandleGameStateChanged;
                gameManager.OnGameEnded += HandleGameStateChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartGame);
            if (endButton != null) endButton.onClick.RemoveListener(EndGame);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(Leave);

            if (gameManager != null)
            {
                gameManager.OnGameStarted -= HandleGameStateChanged;
                gameManager.OnGameEnded -= HandleGameStateChanged;
            }
        }

        public void StartGame()
        {
            if (!CanControl()) return;

            gameManager.GameStarted_OnlyServer();
            Refresh();
        }

        public void EndGame()
        {
            if (!CanControl()) return;

            isEnding = true;
            Refresh();

            gameManager.GameEnded_OnlyServer();

            // Ready 씬 로드를 시작하지 못했으면 다시 누를 수 있게
            if (hexit.IsSessionLocked)
            {
                isEnding = false;
                Refresh();
            }
        }

        public void Leave()
        {
            if (hexit == null || isLeaving) return;

            string key = hexit.IsServer ? hostLeaveConfirmKey : leaveConfirmKey;

            if (confirmPopup == null)
            {
                LeaveNow();
                return;
            }

            confirmPopup.Show(hexit.GetSystemText(key), LeaveNow);
        }

        private void LeaveNow()
        {
            if (hexit == null || isLeaving) return;

            isLeaving = true;
            Refresh();

            hexit.LeaveLobby();
        }

        private bool CanControl() => hexit != null && gameManager != null && hexit.IsServer && !isLeaving && !isEnding;

        private void HandleGameStateChanged(object sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            bool isHost = hexit != null && hexit.IsServer;
            bool isStarted = gameManager != null && gameManager.IsGameStarted;
            bool isBusy = isLeaving || isEnding;

            if (startButton != null)
            {
                startButton.gameObject.SetActive(isHost);
                startButton.interactable = gameManager != null && !isStarted && !isBusy;
            }

            if (endButton != null)
            {
                endButton.gameObject.SetActive(isHost);
                endButton.interactable = gameManager != null && !isBusy;
            }

            if (leaveButton != null) leaveButton.interactable = !isLeaving;
        }
    }
}