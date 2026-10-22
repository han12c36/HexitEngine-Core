using HexitEngine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class CreateRoomUI : MonoBehaviour
    {
        /// <summary> 생성 룸 이름의 최대 글자 수 </summary>
        private const int ROOM_NAME_MAX_LENGTH = 20;

        [Header("Input")]
        [SerializeField] private TMP_InputField roomNameInput;
        [SerializeField] private Slider maxCapacitySlider;
        [SerializeField] private TMP_Text maxCapacityText;
        [SerializeField] private Toggle privateToggle;

        [Header("Action")]
        [SerializeField] private Button createButton;
        [SerializeField] private Button backButton;

        [Header("Navigation")]
        [SerializeField] private MainMenuUI mainMenu;

        [Header("Capacity")]
        [SerializeField] private int minCapacity = 2;
        [SerializeField] private int maxCapacity = 10;
        [SerializeField] private int defaultCapacity = 4;

        [Header("Reset")]
        [Tooltip("켜면 창을 열 때마다 입력값을 기본값 되돌릴 것인지.")]
        [SerializeField] private bool resetOnEnable = true;

        private HEXITEngine hexit;
        private bool isInitialized;

        private void Awake()
        {
            hexit = HEXITEngine.Singleton;

            if (hexit == null) Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");

            SetupInputs();

            if (createButton != null) createButton.onClick.AddListener(CreateRoom);
            if (backButton != null) backButton.onClick.AddListener(Back);
        }

        private void OnDestroy()
        {
            if (createButton != null) createButton.onClick.RemoveListener(CreateRoom);
            if (backButton != null) backButton.onClick.RemoveListener(Back);
            if (roomNameInput != null) roomNameInput.onValueChanged.RemoveListener(HandleRoomNameChanged);
            if (maxCapacitySlider != null) maxCapacitySlider.onValueChanged.RemoveListener(HandleCapacityChanged);
        }

        private void OnEnable()
        {
            if (!isInitialized || resetOnEnable)
            {
                ResetForm();
                isInitialized = true;
                return;
            }

            RefreshCreateButton();
        }

        public void CreateRoom()
        {
            if (hexit == null) return;

            string roomName = GetRoomName();
            if (string.IsNullOrEmpty(roomName)) return;

            int capacity = GetCapacity();
            bool isPrivate = privateToggle != null && privateToggle.isOn;

            hexit.CreateLobby(roomName, capacity, isPrivate);
        }

        public void Back()
        {
            if (mainMenu != null) mainMenu.ShowMainMenu();
            else gameObject.SetActive(false);
        }

        private string GetRoomName() => roomNameInput != null ? roomNameInput.text.Trim() : string.Empty;

        private int GetCapacity()
        {
            int value = maxCapacitySlider != null ? Mathf.RoundToInt(maxCapacitySlider.value) : defaultCapacity;
            return Mathf.Clamp(value, minCapacity, maxCapacity);
        }

        private void SetupInputs()
        {
            if (roomNameInput != null)
            {
                roomNameInput.characterLimit = ROOM_NAME_MAX_LENGTH;
                roomNameInput.onValueChanged.AddListener(HandleRoomNameChanged);
            }

            if (maxCapacitySlider != null)
            {
                maxCapacitySlider.wholeNumbers = true;
                maxCapacitySlider.minValue = minCapacity;
                maxCapacitySlider.maxValue = maxCapacity;
                maxCapacitySlider.onValueChanged.AddListener(HandleCapacityChanged);
            }
        }

        private void ResetForm()
        {
            if (roomNameInput != null)
            {
                string nickName = hexit != null ? hexit.SteamNickName : string.Empty;
                string defaultName = string.IsNullOrEmpty(nickName) ? string.Empty : $"{nickName}'s Room";

                if (defaultName.Length > ROOM_NAME_MAX_LENGTH) defaultName = defaultName.Substring(0, ROOM_NAME_MAX_LENGTH);

                roomNameInput.SetTextWithoutNotify(defaultName);
            }

            if (maxCapacitySlider != null) maxCapacitySlider.SetValueWithoutNotify(Mathf.Clamp(defaultCapacity, minCapacity, maxCapacity));
            if (privateToggle != null) privateToggle.SetIsOnWithoutNotify(false);

            HandleCapacityChanged(GetCapacity());
            RefreshCreateButton();
        }

        private void HandleRoomNameChanged(string _) => RefreshCreateButton();

        private void HandleCapacityChanged(float value)
        {
            if (maxCapacityText != null) maxCapacityText.text = $"{Mathf.RoundToInt(value)}";
        }

        private void RefreshCreateButton()
        {
            if (createButton != null) createButton.interactable = hexit != null && !string.IsNullOrEmpty(GetRoomName());
        }
    }
}