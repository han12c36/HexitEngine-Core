using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class SessionMessageUI : MonoBehaviour
    {
        protected enum MessageState { Hidden, Progress, Notice, Fatal }

        [SerializeField] private TMP_Text messageText;
        [Tooltip("Fatal 메시지용: 메인메뉴로 돌아가기 (Fatal을 쓰지 않는 씬에서는 비워도 됨)")]
        [SerializeField] private Button confirmButton;
        [Tooltip("Notice 메시지용: 메시지만 닫기 (Notice를 쓰지 않는 씬에서는 비워도 됨)")]
        [SerializeField] private Button closeButton;

        private CanvasGroup canvasGroup;

        protected HEXITEngine Hexit { get; private set; }

        /// <summary> 지금 표시 중인 메시지 종류 </summary>
        protected MessageState State { get; private set; } = MessageState.Hidden;

        private bool isLeaving;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            Hide();

            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            Hexit = HEXITEngine.Singleton;

            if (Hexit == null)
            {
                Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
                return;
            }

            Hexit.OnLeaveSessionStarted += HandleLeaveSessionStarted;

            BindEvents(Hexit);
        }

        private void OnDestroy()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);

            if (Hexit == null) return;

            Hexit.OnLeaveSessionStarted -= HandleLeaveSessionStarted;

            UnbindEvents(Hexit);
        }

        /// <summary> 씬별 엔진 이벤트 연결. Awake에서 한 번 호출됩니다. </summary>
        protected abstract void BindEvents(HEXITEngine hexit);

        /// <summary> 씬별 엔진 이벤트 해제. OnDestroy에서 한 번 호출됩니다. </summary>
        protected abstract void UnbindEvents(HEXITEngine hexit);

        /// <summary> [나만] Fatal의 확인 버튼 동작. 기본은 메인메뉴로 돌아가기 </summary>
        protected virtual void OnConfirm() => Hexit.LeaveLobby();

        /// <summary> 내가 나가는 중이면 안내를 띄우지 않습니다. </summary>
        private void HandleLeaveSessionStarted(object sender, EventArgs e)
        {
            isLeaving = true;
            Hide();
        }

        /// <summary> 진행 중 메시지. 버튼 없이 문구만 보이고 뒤쪽 입력을 막습니다. key는 System_Table 키입니다. </summary>
        protected void ShowProgress(string key)
        {
            if (isLeaving || State == MessageState.Fatal) return;

            State = MessageState.Progress;
            Show(Translate(key), button: null);
        }

        /// <summary> 진행 중 문구만 바꿉니다. Progress 상태가 아니면 무시합니다. </summary>
        protected void UpdateProgress(string key)
        {
            if (isLeaving || State != MessageState.Progress) return;

            SetText(Translate(key));
        }

        /// <summary> 알림 메시지. 닫기를 누르면 메시지만 닫힙니다. titleKey는 System_Table 키, detail은 이미 번역된 문구입니다. </summary>
        protected void ShowNotice(string titleKey, string detail)
        {
            if (isLeaving || IsBlocked(MessageState.Notice, titleKey, detail)) return;

            State = MessageState.Notice;
            Show(Compose(titleKey, detail), closeButton);
        }

        /// <summary> 치명적 메시지. 확인을 누르면 메인메뉴로 돌아갑니다. titleKey는 System_Table 키, detail은 이미 번역된 문구입니다. </summary>
        protected void ShowFatal(string titleKey, string detail)
        {
            if (isLeaving || IsBlocked(MessageState.Fatal, titleKey, detail)) return;

            State = MessageState.Fatal;
            Show(Compose(titleKey, detail), confirmButton);
        }

        /// <summary> 같은 종류 이상이 이미 떠 있으면 첫 메시지를 유지하고 로그만 남깁니다. </summary>
        private bool IsBlocked(MessageState incoming, string titleKey, string detail)
        {
            bool isBlocked = State == MessageState.Fatal || (State == MessageState.Notice && incoming == MessageState.Notice);

            if (isBlocked) Dev.LogWarn($"이미 메시지가 표시 중이라 무시합니다: {Compose(titleKey, detail)}");

            return isBlocked;
        }

        /// <summary> 보이는 버튼은 하나뿐입니다. button이 null이면 버튼 없이 표시합니다. </summary>
        private void Show(string text, Button button)
        {
            if (State != MessageState.Progress) Dev.LogWarn(text);

            SetText(text);

            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(button == confirmButton);
                confirmButton.interactable = true;
            }

            if (closeButton != null) closeButton.gameObject.SetActive(button == closeButton);

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            // 키보드/패드로도 바로 누를 수 있도록 보이는 버튼 선택 (버튼이 없으면 뒤쪽 선택을 해제해 입력을 막음)
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button != null ? button.gameObject : null);
        }

        /// <summary> 메시지를 닫습니다. (Progress를 이벤트로 닫을 때도 사용) </summary>
        protected void Hide()
        {
            State = MessageState.Hidden;

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private string Compose(string titleKey, string detail)
        {
            string title = Translate(titleKey);
            return string.IsNullOrEmpty(detail) ? title : $"{title}\n{detail}";
        }

        private void SetText(string text)
        {
            if (messageText != null) messageText.text = text;
        }

        protected string Translate(string key) => Hexit != null ? Hexit.GetSystemText(key) : key;

        /// <summary> [나만] Fatal 확인 </summary>
        private void Confirm()
        {
            if (Hexit == null || State != MessageState.Fatal) return;

            if (confirmButton != null) confirmButton.interactable = false;
            OnConfirm();
        }

        /// <summary> Notice만 닫을 수 있습니다. </summary>
        private void Close()
        {
            if (State != MessageState.Notice) return;
            Hide();
        }
    }
}