using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    // 다른 UI보다 먼저 Awake를 실행해서, 패널들이 초기화되기 전에 시작 상태(메인만 켜짐)를 확정
    [DefaultExecutionOrder(-100)]
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button createRoomBtn;
        [SerializeField] private Button joinRoomBtn;
        //[SerializeField] private Button settingBtn;
        [SerializeField] private Button exitBtn;

        [SerializeField] private GameObject mainPanel;

        [SerializeField] private GameObject createRoomUI;
        [SerializeField] private GameObject joinRoomUI;
        //[SerializeField] private GameObject settingUI;

        private GameObject currentPanel;

        private void Awake()
        {
            if (createRoomBtn != null)
            {
                createRoomBtn.onClick.AddListener(OnClickCreateRoomBtn);
            }

            if (joinRoomBtn != null)
            {
                joinRoomBtn.onClick.AddListener(OnClickJoinRoomBtn);
            }

            //if (settingBtn != null)
            //{
            //    settingBtn.onClick.AddListener(OnClickSettingBtn);
            //}

            if (exitBtn != null)
            {
                exitBtn.onClick.AddListener(OnClickExitBtn);
            }

            ShowMainMenu();
        }

        private void OnDestroy()
        {
            if (createRoomBtn != null) createRoomBtn.onClick.RemoveListener(OnClickCreateRoomBtn);
            if (joinRoomBtn != null) joinRoomBtn.onClick.RemoveListener(OnClickJoinRoomBtn);
            //if (settingBtn != null) settingBtn.onClick.RemoveListener(OnClickSettingBtn);
            if (exitBtn != null) exitBtn.onClick.RemoveListener(OnClickExitBtn);
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (currentPanel != null && Input.GetKeyDown(KeyCode.Escape)) ShowMainMenu();
#endif
        }

        private void OnClickCreateRoomBtn()
        {
            OpenPanel(createRoomUI);
        }

        private void OnClickJoinRoomBtn()
        {
            OpenPanel(joinRoomUI);
        }

        //private void OnClickSettingBtn()
        //{
        //    OpenPanel(settingUI);
        //}

        private void OnClickExitBtn()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void ShowMainMenu()
        {
            SetPanelActive(createRoomUI, false);
            SetPanelActive(joinRoomUI, false);
            //SetPanelActive(settingUI, false);
            SetPanelActive(mainPanel, true);

            currentPanel = null;
        }

        private void OpenPanel(GameObject panel)
        {
            if (panel == null) return;

            SetPanelActive(createRoomUI, panel == createRoomUI);
            SetPanelActive(joinRoomUI, panel == joinRoomUI);
            //SetPanelActive(settingUI, panel == settingUI);
            SetPanelActive(mainPanel, false);

            currentPanel = panel;
        }

        private static void SetPanelActive(GameObject panel, bool value)
        {
            if (panel != null && panel.activeSelf != value) panel.SetActive(value);
        }
    }
}