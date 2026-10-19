using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary> 원형 버퍼로 처리된 채팅  </summary>
public class ChatBehaviour : MonoBehaviour
{
    public static ChatBehaviour Singleton;

    [SerializeField] private ChatMessage chatMessagePrefab;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private int allowedLines = 3;
    [SerializeField] private int maxHistoryCount = 100;

    private string connectedChannelId;

    // 데이터 저장
    private readonly List<ChatLogEntry> chatHistory = new();
    private readonly List<ChatMessage> itemPool = new();
    private readonly List<float> cumulativeHeights = new();

    private RectTransform viewportRect;
    private RectTransform contentRect;

    private float itemHeight;
    private float maxWidth;
    private float heightOffset;
    private int visibleCount;
    private int extraBufferCount = 2;
    private float initViewportHeight;
    private int firstVisibleIndex = 0;

    private bool isChatMode = false;

    private void Awake()
    {
        if (Singleton != null)
        {
            if (Singleton != this) Destroy(this);
        }

        Singleton = this;

        viewportRect = scrollRect.viewport;
        contentRect = scrollRect.content;
        maxWidth = chatMessagePrefab.TextField.rectTransform.rect.width;
    }

    private void Start()
    {
        initViewportHeight = viewportRect.rect.height;

        cumulativeHeights.Clear();
        heightOffset = 0f;

        itemHeight = ((RectTransform)chatMessagePrefab.transform).rect.height;

        CreateChatMessageItem();

        UpdateContentHeight();
        UpdateVisibleCount();
        UpdateVisibleItems();

        scrollRect.onValueChanged.RemoveAllListeners();
        scrollRect.onValueChanged.AddListener(OnScrollChanged);

        //VoiceService.Singleton.OnChannelJoined -= OnChannelJoined;
        //VoiceService.Singleton.OnChannelJoined += OnChannelJoined;
        //
        //ChatService.Singleton.OnChannelMessageReceived -= OnMessageReceived;
        //ChatService.Singleton.OnChannelMessageReceived += OnMessageReceived;
        //
        //if (VoiceService.Singleton.IsLoggedIn && VoiceService.Singleton.IsConnectedToChannel)
        //{
        //    OnChannelJoined(VoiceService.Singleton.JoinedChannelName);
        //}
    }

    public void OnDestroy()
    {
        //if (VoiceService.Singleton != null)
        //{
        //    VoiceService.Singleton.OnChannelJoined -= OnChannelJoined;
        //}
        //
        //if (ChatService.Singleton != null)
        //{
        //    ChatService.Singleton.OnChannelMessageReceived -= OnMessageReceived;
        //}

        Singleton = null;
    }

    private void Update()
    {
        HandleInput();
    }

    private void OnChannelJoined(string channelName)
    {
        connectedChannelId = channelName;
    }

    //private void OnMessageReceived(VivoxMessage vMsg)
    //{
    //    if (vMsg.SenderPlayerId == VoiceService.Singleton.SignedInPlayerId) return;
    //
    //    AddMessage(vMsg.ReceivedTime, vMsg.SenderDisplayName, vMsg.MessageText);
    //}

    private void CreateChatMessageItem()
    {
        if (itemHeight <= 0f) return;

        int maxVisibleCount = Mathf.CeilToInt(initViewportHeight / itemHeight);
        int maxPoolCount = maxVisibleCount + extraBufferCount;

        while (itemPool.Count < maxPoolCount)
        {
            var msg = Instantiate(chatMessagePrefab, contentRect);
            msg.gameObject.SetActive(false);
            itemPool.Add(msg);
        }
    }

    // 전체 컨텐츠 높이 = (마지막 누적 절대값) - (삭제된 높이 오프셋)
    private float GetTotalHeight()
    {
        if (cumulativeHeights.Count == 0) return 0f;

        return cumulativeHeights[cumulativeHeights.Count - 1] - heightOffset;
    }

    // 특정 인덱스 Y좌표 = -((이전까지의 누적 절대값) - (삭제된 높이 오프셋))
    private float GetYForIndex(int index)
    {
        if (index <= 0) return 0f;

        float prevAbsSum = cumulativeHeights[index - 1];
        return -(prevAbsSum - heightOffset);
    }

    //맨 아래가 보이도록 firstVisibleIndex 재계산
    private void RecalculateFirstVisibleIndexForBottom()
    {
        float total = GetTotalHeight();
        float viewH = initViewportHeight;

        if (total <= viewH || cumulativeHeights.Count == 0)
        {
            firstVisibleIndex = 0;
            return;
        }

        // "맨 아래를 보여준다" = 위에서 (total - viewH)만큼 내려온 위치
        float offsetFromTop = total - viewH;

        // 여기서도 같은 이진 탐색 사용
        firstVisibleIndex = FindIndexByOffsetBinary(offsetFromTop);
    }

    private void UpdateContentHeight()
    {
        contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, GetTotalHeight());
    }

    private void UpdateVisibleCount()
    {
        visibleCount = Mathf.Max(1, Mathf.CeilToInt(initViewportHeight / itemHeight));
    }

    private void UpdateVisibleItems()
    {
        int total = chatHistory.Count;

        // 데이터 자체가 없으면 전부 비활성화
        if (total == 0)
        {
            foreach (var item in itemPool)
            {
                item.ClearMessage();
            }

            return;
        }

        // 실제로 이번 프레임에 "시도할" 최대 슬롯 수 (뷰포트 높이 기준)
        int maxSlotsToUse = Mathf.Min(visibleCount, itemPool.Count);

        for (int i = 0; i < itemPool.Count; i++)
        {
            var item = itemPool[i];

            // 1) 이번 프레임에 쓰고 싶은 슬롯 범위 밖이면 무조건 비활성화
            if (i >= maxSlotsToUse)
            {
                if (item.IsVisible) item.ClearMessage();

                continue;
            }

            // 2) 이 슬롯이 담당해야 할 데이터 인덱스
            int dataIndex = firstVisibleIndex + i;

            // 데이터 범위 밖이면 비활성화
            if (dataIndex < 0 || dataIndex >= total)
            {
                if (item.IsVisible) item.ClearMessage();

                continue;
            }

            var entry = chatHistory[dataIndex];

            // 3) 이미 올바른 인덱스를 표시 중이면 스킵 (효율)
            if (item.IsVisible && item.Index == dataIndex) continue;

            // 4) 실제로 갱신이 필요할 때만 위치/데이터 설정
            var rt = (RectTransform)item.transform;

            float expectedY = GetYForIndex(dataIndex);

            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, expectedY);

            item.SetMessage(entry);
        }
    }

    private void ScrollToBottom()
    {
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private void AddMessage(DateTime time, string senderName, string messageText)
    {
        // 1. 인덱스 결정 (현재 리스트의 크기)
        int index = chatHistory.Count;

        // 2. 텍스트 포맷팅 및 줄 수 계산
        string displayMessageText = ChatTextFormatter.BuildWrappedMessage(
            chatMessagePrefab.TextField,
            maxWidth,
            allowedLines,
            time,
            senderName,
            messageText,
            out int lineCount
        );

        // 3. 로그 엔트리 생성
        var entry = new ChatLogEntry(
            index,
            time,
            senderName,
            messageText,
            displayMessageText,
            lineCount
        );

        chatHistory.Add(entry);

        // 4. 높이 누적 계산 (절대 좌표 방식)
        // 리스트가 비어있다면 heightOffset이 시작 기준점이고,
        // 데이터가 있다면 마지막 아이템의 누적 높이가 기준점입니다.
        float addedHeight = lineCount * itemHeight;

        float prevAbsTotal = cumulativeHeights.Count > 0
            ? cumulativeHeights[cumulativeHeights.Count - 1]
            : heightOffset;

        cumulativeHeights.Add(prevAbsTotal + addedHeight);

        // 5. 최대 개수 초과 시 오래된 메시지 삭제 및 오프셋 보정
        if (chatHistory.Count > maxHistoryCount)
        {
            // [중요 수정] For문 루프 삭제됨

            // 0번 아이템이 가지고 있던 '누적 절대 높이'를 가져옵니다.
            // 이 값이 곧, 앞으로의 0점(기준점)이 됩니다.
            float removedAbsoluteHeight = cumulativeHeights[0];

            // 오프셋을 갱신하여, 이후 계산(GetTotalHeight 등)에서 이만큼을 빼주도록 합니다.
            heightOffset = removedAbsoluteHeight;

            // 실제 리스트에서 데이터 삭제
            // (List의 RemoveAt은 내부 배열 복사가 일어나지만, 단순 값 수정을 위한 for문보다는 훨씬 효율적입니다)
            chatHistory.RemoveAt(0);
            cumulativeHeights.RemoveAt(0);
        }

        // 6. UI 갱신
        UpdateContentHeight();
        //RecalculateFirstVisibleIndexForBottom();
        UpdateVisibleItems();
        //ScrollToBottom();
    }

    private void OnScrollChanged(Vector2 normalizedPos)
    {
        if (chatHistory.Count == 0 || cumulativeHeights.Count == 0)
            return;

        // 전체 컨텐츠(모든 채팅 메시지)의 총 높이
        float total = GetTotalHeight();

        // 현재 뷰포트(ScrollRect의 보이는 영역)의 높이
        float viewH = initViewportHeight;

        // 전체 높이가 뷰포트보다 작거나 같으면 스크롤이 의미 없음 (항상 맨 위만 보임)
        if (total <= viewH)
        {
            // 첫 번째 메시지부터 보이도록 인덱스 설정
            firstVisibleIndex = 0;

            // 그에 맞춰 실제로 풀에서 꺼낸 ChatMessage들을 갱신
            UpdateVisibleItems();
            return;
        }

        // 스크롤될 수 있는 전체 거리 (컨텐츠 총 높이 - 뷰포트 높이)
        float scrollable = total - viewH;

        //ScrollRect의 normalizedPosition.y는
        //y = 1 이 맨 위(스크롤바 최상단), y = 0 이 맨 아래(스크롤바 최하단)
        //우리가 원하는 것은 "위에서부터 얼마나 내려왔는지" 이므로 1 - y 사용
        float t = 1f - normalizedPos.y;

        //t(0~1)를 실제 스크롤 가능 거리(scrollable)에 곱해서
        //컨텐츠 최상단으로부터 얼만큼 내려왔는지(픽셀 단위 오프셋)를 구함
        float offsetFromTop = scrollable * t;

        int idx = FindIndexByOffsetBinary(offsetFromTop);

        if (idx != firstVisibleIndex)
        {
            firstVisibleIndex = idx;
            UpdateVisibleItems();
        }
    }

    /// <summary>
    /// offsetFromTop(컨텐츠의 위에서부터 떨어진 픽셀 위치)에
    /// 해당하는 메시지 인덱스를 이진 탐색으로 찾는다.
    ///
    /// cumulativeHeights[i] = 0~i 번째 메시지까지의 총 높이
    /// i 번째 메시지의 구간: 
    ///   [ (i==0 ? 0 : cumulativeHeights[i-1]) , cumulativeHeights[i] )
    /// </summary>
    private int FindIndexByOffsetBinary(float offsetFromTop)
    {
        int count = cumulativeHeights.Count;
        if (count == 0) return 0;

        // [수정] 찾으려는 위치(상대값)에 오프셋을 더해 '절대 좌표'로 변환 후 비교
        float targetAbsoluteY = offsetFromTop + heightOffset;

        int lo = 0;
        int hi = count - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;

            // mid 아이템의 시작 절대 높이 (0번이면 오프셋이 시작점)
            float prevAbs = (mid == 0) ? heightOffset : cumulativeHeights[mid - 1];
            float curAbs = cumulativeHeights[mid];

            if (targetAbsoluteY < prevAbs)
                hi = mid - 1;
            else if (targetAbsoluteY >= curAbs)
                lo = mid + 1;
            else
                return mid;
        }

        return 0;
    }

    private void HandleInput()
    {
        if (!Input.GetKeyDown(KeyCode.Return)) return; // GetKeyUp 말고 Down이 UX도 더 자연스러움

        // 1) 채팅 모드가 아니면 → 채팅 모드 진입 + 포커싱
        if (!isChatMode)
        {
            isChatMode = true;

            chatInputField.Select();
            chatInputField.ActivateInputField();
            return;
        }

        // 2) 이미 채팅 모드인 상태
        string text = chatInputField.text;

        // 2-1) 내용이 있으면 → 전송 (쿨타임 체크) + 채팅 모드 유지
        if (!string.IsNullOrWhiteSpace(text))
        {
            TrySendLocalMessage(); // 여기서 포커스 유지 (바로 다음 채팅 가능)

            chatInputField.Select();
            chatInputField.ActivateInputField();
            return;
        }

        // 2-2) 내용이 없으면 → 채팅 모드 종료 + 포커스 해제
        isChatMode = false;
        chatInputField.DeactivateInputField(clearSelection: true);
    }

    private void TrySendLocalMessage()
    {
        //if (!VoiceService.Singleton.IsLoggedIn) 
        //{
        //    Debug.LogWarning("Vivox에 로그인되어 있지 않아 메시지를 보낼 수 없습니다.");
        //    return;
        //}

        string message = chatInputField.text;
        chatInputField.text = ""; // 입력창 즉시 비움

        if (string.IsNullOrWhiteSpace(message)) return;

        //string myName = VoiceService.Singleton.LocalParticipant.DisplayName;
        string myName = "";

        // 내 화면에 먼저 그리기
        AddMessage(DateTime.Now, myName, message);

        RecalculateFirstVisibleIndexForBottom();
        UpdateVisibleItems();
        ScrollToBottom();

        // 그 다음 서버로 전송 (비동기, 결과 안 기다림)
        SendMessageToVivoxAsync(message).Forget();

        // 포커스 유지
        chatInputField.Select();
        chatInputField.ActivateInputField();
    }

    private async UniTaskVoid SendMessageToVivoxAsync(string message)
    {
        try
        {
            await UniTask.CompletedTask;
            //await ChatService.Singleton.SendChannelMessageAsync(connectedChannelId, message);
        }
        catch (Exception e)
        {
            Debug.LogError($"Message Send Failed: {e.Message}");
        }
    }

    public static void Log(string message, string sender)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // [New] 로그성 메시지는 로컬에만 띄울지, 서버로 보낼지 결정 필요.
        // 여기서는 편의상 로컬 히스토리에만 추가하는 예시 (필요시 Vivox 전송으로 변경 가능)
        // 하지만 VivoxMessage 구조체가 필요하므로 가짜 데이터를 만들거나 로직 분리 필요.
        // 여기서는 일단 주석 처리.
    }
}