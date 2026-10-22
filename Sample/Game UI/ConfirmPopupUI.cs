using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    [RequireComponent(typeof(CanvasGroup))]
    public class ConfirmPopupUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        private CanvasGroup canvasGroup;
        private Action onConfirm;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            Hide();

            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
        }

        private void OnDestroy()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(Hide);
        }

        public void Show(string message, Action onConfirm)
        {
            this.onConfirm = onConfirm;

            if (messageText != null) messageText.text = message;
            if (confirmButton != null) confirmButton.interactable = true;

            IsOpen = true;
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton != null ? cancelButton.gameObject : null);
        }

        public void Hide()
        {
            IsOpen = false;
            onConfirm = null;

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void Confirm()
        {
            var action = onConfirm;

            // 연타 방지
            if (confirmButton != null) confirmButton.interactable = false;

            Hide();
            action?.Invoke();
        }
    }
}