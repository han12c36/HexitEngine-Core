using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    /// <summary>
    /// [샘플] 채팅 창. ChatManager와 같은 프리팹에 두면 어느 씬에서든 그대로 사용할 수 있습니다.
    ///
    /// 성능
    /// - 줄 오브젝트를 ChatManager.MaxHistory개까지만 만들고, 넘치면 가장 오래된 줄을 재사용해 맨 아래로 옮깁니다. (생성/파괴 없음)
    /// - 맨 아래로 스크롤은 메시지가 몇 개 오든 한 프레임에 한 번만 합니다.
    ///
    /// 크기 변화 (ChatResizeHandle로 위아래로 늘이고 줄임)
    /// - 줄 높이는 레이아웃이 자동으로 계산합니다. 창 폭이 바뀌어도 줄바꿈과 높이가 다시 맞춰집니다.
    /// - 맨 아래를 보고 있었다면 크기를 바꾼 뒤에도 맨 아래를 유지하고, 위로 올려 읽는 중이면 위치를 건드리지 않습니다.
    ///
    /// 프리팹 구성
    /// - ScrollRect (Vertical만) / Viewport (Mask) / Content
    ///   Content: Vertical Layout Group (Control Child Size: Width, Height 체크 / Child Force Expand: Width만 체크)
    ///            + Content Size Fitter (Vertical Fit: Preferred Size), 위쪽 기준(Pivot Y = 1, 위로 stretch)
    /// - itemPrefab: TextMeshProUGUI(줄바꿈 켜짐) + ChatMessageItem이 붙은 오브젝트
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        [Tooltip("비워 두면 씬의 ChatManager를 찾습니다.")]
        [SerializeField] private ChatManager chatManager;

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

        private void Awake()
        {
            if (chatManager == null) chatManager = ChatManager.Current != null ? ChatManager.Current : FindFirstObjectByType<ChatManager>();

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

        /// <summary> 이번 프레임 끝에 맨 아래로 스크롤합니다. 여러 번 불러도 한 번만 실행됩니다. </summary>
        public void RequestScrollToBottom()
        {
            if (scrollRect != null) isScrollToBottomPending = true;
        }

        #endregion
    }
}
