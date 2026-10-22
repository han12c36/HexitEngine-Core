using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    public class JoinByCodeUI : MonoBehaviour
    {
        /// <summary> 참가 코드 길이. </summary>
        private const int JOIN_CODE_LENGTH = 8;

        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button closeButton;

        private HEXITEngine hexit;

        private void Awake()
        {
            hexit = HEXITEngine.Singleton;

            if (hexit == null) Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");

            if (joinCodeInput != null)
            {
                joinCodeInput.characterLimit = JOIN_CODE_LENGTH;
                joinCodeInput.onValidateInput = ValidateJoinCodeChar;
                joinCodeInput.onValueChanged.AddListener(HandleJoinCodeChanged);
                joinCodeInput.onSubmit.AddListener(HandleInputSubmit);
            }

            if (submitButton != null) submitButton.onClick.AddListener(Submit);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            if (joinCodeInput != null)
            {
                joinCodeInput.onValueChanged.RemoveListener(HandleJoinCodeChanged);
                joinCodeInput.onSubmit.RemoveListener(HandleInputSubmit);
            }

            if (submitButton != null) submitButton.onClick.RemoveListener(Submit);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }

        public void Open()
        {
            gameObject.SetActive(true);

            if (joinCodeInput != null)
            {
                joinCodeInput.SetTextWithoutNotify(string.Empty);
                joinCodeInput.ActivateInputField();
            }

            RefreshSubmitButton();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        public void Submit()
        {
            if (hexit == null || !IsValidJoinCode()) return;

            hexit.JoinLobbyByCode(joinCodeInput.text);
        }

        private static char ValidateJoinCodeChar(string text, int charIndex, char addedChar)
        {
            if (addedChar >= 'a' && addedChar <= 'z') return char.ToUpperInvariant(addedChar);
            if ((addedChar >= 'A' && addedChar <= 'Z') || (addedChar >= '0' && addedChar <= '9')) return addedChar;

            return '\0';
        }

        private void HandleJoinCodeChanged(string _) => RefreshSubmitButton();

        private void HandleInputSubmit(string _) => Submit();

        private bool IsValidJoinCode() => joinCodeInput != null && joinCodeInput.text.Length == JOIN_CODE_LENGTH;

        private void RefreshSubmitButton()
        {
            if (submitButton != null) submitButton.interactable = hexit != null && IsValidJoinCode();
        }
    }
}