using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    /// <summary> 재사용 UI를 기반으로 제작. </summary>
    public class ChatUI : MonoBehaviour
    {
        [Tooltip("비워 두면 씬의 ChatManager를 찾습니다.")]
        [SerializeField] private ChatManager chatManager;

        [Tooltip("이동/크기 조절 창 (비우면 같은 오브젝트에서 찾음). 크기가 바뀔 때 맨 아래 유지에 사용")]
        [SerializeField] private ChatWindow window;

        [Header("List")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private ChatMessageItem itemPrefab;

        [Header("Input")]
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button sendButton;
        [Tooltip("보낸 뒤 입력칸에 다시 포커스 (연속 입력)")]
        [SerializeField] private bool keepFocusAfterSend = true;

        [Header("Colors")]
        [SerializeField] private Color localNameColor = new(0.45f, 0.85f, 1f);
        [SerializeField] private Color otherNameColor = new(1f, 0.85f, 0.4f);
        [SerializeField] private Color systemColor = new(0.7f, 0.7f, 0.7f);

        /// <summary> 맨 아래로 판단하는 여유 (정규화 값) </summary>
        private const float BOTTOM_THRESHOLD = 0.001f;

        private ChatMessageItem[] items;
        private int itemCount;

        /// <summary> 다음에 재사용할 가장 오래된 줄 (items가 가득 찬 뒤 순환) </summary>
        private int oldestIndex;

        private bool isScrollToBottomPending;

        /// <summary> 크기 조절을 시작할 때 맨 아래를 보고 있었는지 </summary>
        private bool keepBottomOnResize;

        private void Awake()
        {
            if (chatManager == null) chatManager = ChatManager.Current != null ? ChatManager.Current : FindAnyObjectByType<ChatManager>();

            if (chatManager == null)
            {
                Dev.LogError("ChatManager가 없습니다. 같은 프리팹이나 씬에 배치하세요.");
                enabled = false;
                return;
            }

            items = new ChatMessageItem[chatManager.MaxHistory];

            if (input != null)
            {
                input.characterLimit = chatManager.MaxMessageLength;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.onSubmit.AddListener(HandleSubmit);
            }

            if (sendButton != null) sendButton.onClick.AddListener(SendInput);

            chatManager.OnMessageReceived += HandleMessageReceived;

            if (window == null) window = GetComponent<ChatWindow>();
            if (window != null)
            {
                window.OnResizeStarted += HandleResizeStarted;
                window.OnResized += HandleResized;
            }
        }

        private void Start()
        {
            // 이 UI보다 먼저 받은 메시지 표시
            foreach (var message in chatManager.History) AddItem(message);

            RequestScrollToBottom();
        }

        private void OnDestroy()
        {
            if (input != null) input.onSubmit.RemoveListener(HandleSubmit);
            if (sendButton != null) sendButton.onClick.RemoveListener(SendInput);
            if (chatManager != null) chatManager.OnMessageReceived -= HandleMessageReceived;

            if (window != null)
            {
                window.OnResizeStarted -= HandleResizeStarted;
                window.OnResized -= HandleResized;
            }
        }

        private void LateUpdate()
        {
            if (!isScrollToBottomPending) return;
            isScrollToBottomPending = false;

            // 이번 프레임에 바뀐 줄 높이를 먼저 계산한 뒤 맨 아래로 (프레임당 한 번)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.verticalNormalizedPosition = 0f;
        }

        #region Send

        /// <summary> [나만] 입력칸의 내용을 보냅니다. </summary>
        public void SendInput()
        {
            if (input == null) return;

            if (chatManager.Send(input.text))
            {
                input.SetTextWithoutNotify(string.Empty);
                RequestScrollToBottom();
            }

            if (keepFocusAfterSend) input.ActivateInputField();
        }

        private void HandleSubmit(string _) => SendInput();

        #endregion

        #region List

        private void HandleMessageReceived(ChatMessage message)
        {
            // 위로 올려 읽는 중이면 위치를 유지, 맨 아래를 보고 있었으면 따라 내려감
            bool wasAtBottom = IsAtBottom();

            AddItem(message);

            if (wasAtBottom || IsLocalMessage(message)) RequestScrollToBottom();
        }

        private void AddItem(ChatMessage message)
        {
            if (content == null || itemPrefab == null) return;

            ChatMessageItem item;

            if (itemCount < items.Length)
            {
                item = Instantiate(itemPrefab, content);
                items[itemCount++] = item;
            }
            else
            {
                // 가장 오래된 줄을 재사용해 맨 아래로
                item = items[oldestIndex];
                oldestIndex = (oldestIndex + 1) % items.Length;
                item.transform.SetAsLastSibling();
            }

            item.Bind(message, GetNameColor(message), systemColor);
        }

        private Color GetNameColor(ChatMessage message) => IsLocalMessage(message) ? localNameColor : otherNameColor;

        private static bool IsLocalMessage(ChatMessage message)
        {
            var networkManager = Unity.Netcode.NetworkManager.Singleton;
            return !message.IsSystem && networkManager != null && message.SenderClientId == networkManager.LocalClientId;
        }

        #endregion

        #region Scroll

        /// <summary> 맨 아래를 보고 있는지 (내용이 창보다 짧아도 true) </summary>
        public bool IsAtBottom()
        {
            if (scrollRect == null || content == null) return true;

            var viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
            if (content.rect.height <= viewport.rect.height) return true;

            return scrollRect.verticalNormalizedPosition <= BOTTOM_THRESHOLD;
        }

        private void HandleResizeStarted() => keepBottomOnResize = IsAtBottom();

        private void HandleResized()
        {
            if (keepBottomOnResize) RequestScrollToBottom();
        }

        /// <summary> 이번 프레임 끝에 맨 아래로 스크롤합니다. 여러 번 불러도 한 번만 실행됩니다. </summary>
        public void RequestScrollToBottom()
        {
            if (scrollRect != null) isScrollToBottomPending = true;
        }

        #endregion
    }
}