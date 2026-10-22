using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    public enum ChatMessageType : byte
    {
        Player = 0,
        System = 1,
    }

    /// <summary> 받은 채팅 한 줄. System이면 Text는 이미 내 언어로 번역된 문구입니다. </summary>
    public readonly struct ChatMessage
    {
        public readonly ChatMessageType Type;
        public readonly ulong SenderClientId;
        public readonly string SenderName;
        public readonly string Text;

        public bool IsSystem => Type == ChatMessageType.System;

        public ChatMessage(ChatMessageType type, ulong senderClientId, string senderName, string text)
        {
            Type = type;
            SenderClientId = senderClientId;
            SenderName = senderName;
            Text = text;
        }
    }

    /// <summary>
    /// 씬 채팅 매니저. 씬마다 하나 배치합니다. (Game 씬, 이후 프리팹으로 만들어 Ready 씬에도 배치 가능)
    ///
    /// - NetworkObject가 필요 없습니다. NGO의 CustomMessagingManager(Named Message)로 주고받으므로, 프리팹을 어느 씬에 두어도 바로 동작합니다.
    /// - 흐름: [나만] Send → [서버만] 검증(글자 수, 도배 방지, 보낸 사람 확인) → [모두] 수신(OnMessageReceived)
    /// - 보낸 사람은 클라이언트가 적은 값이 아니라 NGO가 알려주는 senderClientId로 서버가 판단합니다. (사칭 방지)
    /// - 시스템 메시지(입장/퇴장)는 키와 이름만 보내고, 받는 쪽이 자기 언어로 번역합니다.
    /// - 기록은 최근 maxHistory개만 보관합니다. 씬이 바뀌면 이 매니저와 함께 사라집니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChatManager : MonoBehaviour
    {
        private const string MESSAGE_SEND = "HEXIT_Chat_Send";
        private const string MESSAGE_BROADCAST = "HEXIT_Chat_Broadcast";

        /// <summary> 메시지 버퍼 크기. 넘치면 maxSize까지 자동으로 늘어납니다. </summary>
        private const int WRITER_INITIAL_SIZE = 512;
        private const int WRITER_MAX_SIZE = 4096;

        [Header("Rule")]
        [Tooltip("메시지 최대 글자 수 (서버가 자릅니다. UI 입력칸 제한도 이 값을 따릅니다.)")]
        [SerializeField, Min(1)] private int maxMessageLength = 120;

        [Tooltip("같은 사람이 다시 보낼 수 있기까지의 최소 간격 (초)")]
        [SerializeField, Min(0f)] private float sendInterval = 0.3f;

        [Tooltip("보관할 최근 메시지 수 (UI 목록 크기도 이 값을 따릅니다.)")]
        [SerializeField, Min(1)] private int maxHistory = 100;

        [Header("System Message")]
        [SerializeField] private bool showJoinMessage = true;
        [SerializeField] private bool showLeaveMessage = true;

        /// <summary> 씬에 있는 채팅 매니저 (없으면 null) </summary>
        public static ChatManager Current { get; private set; }

        public int MaxMessageLength => maxMessageLength;
        public int MaxHistory => maxHistory;

        /// <summary> [모두] 최근 메시지 (오래된 순) </summary>
        public IReadOnlyList<ChatMessage> History => history;

        /// <summary> [모두] 메시지 수신 (내가 보낸 메시지 포함) </summary>
        public event Action<ChatMessage> OnMessageReceived;

        private readonly List<ChatMessage> history = new();

        /// <summary> [서버만] 보낸 사람별 마지막 전송 시각 </summary>
        private readonly Dictionary<ulong, float> lastSendTimes = new();

        /// <summary> [서버만] 퇴장 메시지용 이름 기억 (이탈 시점에는 PlayerData가 이미 비었을 수 있음) </summary>
        private readonly Dictionary<ulong, string> knownNames = new();

        /// <summary> [서버만] 브로드캐스트 대상 재사용 버퍼 </summary>
        private readonly List<ulong> targetBuffer = new();

        /// <summary>
        /// 메시지 핸들러를 등록한 매니저. 핸들러는 이름당 하나라서, 씬 전환 중 새 씬의 매니저가 먼저 등록한 뒤
        /// 이전 씬의 매니저가 해제하면 새 핸들러까지 지워집니다. 그래서 마지막으로 등록한 매니저만 해제할 수 있게 합니다.
        /// </summary>
        private static ChatManager handlerOwner;

        private HEXITEngine hexit;
        private NetworkManager networkManager;
        private bool isRegistered;
        private float localLastSendTime = float.NegativeInfinity;

        #region Lifecycle

        private void Awake()
        {
            if (Current != null && Current != this) Dev.LogWarn("씬에 ChatManager가 둘 이상 있습니다. 마지막 것을 사용합니다.");
            Current = this;

            hexit = HEXITEngine.Singleton;

            if (hexit == null)
            {
                Dev.LogError("HEXITEngine이 없습니다. 부트스트랩을 거쳐서 실행하세요.");
                return;
            }

            hexit.OnClientConnected += HandleClientConnected;
            hexit.OnHostDisconnected += HandleClientDisconnected;
            hexit.OnPlayerDatasChanged += HandlePlayerDatasChanged;
        }

        private void OnEnable() => RegisterHandlers();

        private void OnDisable() => UnregisterHandlers();

        private void OnDestroy()
        {
            if (Current == this) Current = null;

            if (hexit == null) return;

            hexit.OnClientConnected -= HandleClientConnected;
            hexit.OnHostDisconnected -= HandleClientDisconnected;
            hexit.OnPlayerDatasChanged -= HandlePlayerDatasChanged;
        }

        /// <summary> 네트워크가 켜진 상태에서만 메시지 핸들러를 등록합니다. (Ready/Game 씬은 항상 접속 후에 로드됨) </summary>
        private void RegisterHandlers()
        {
            networkManager = NetworkManager.Singleton;

            if (isRegistered || networkManager == null || !networkManager.IsListening || networkManager.CustomMessagingManager == null) return;

            var messaging = networkManager.CustomMessagingManager;

            if (networkManager.IsServer) messaging.RegisterNamedMessageHandler(MESSAGE_SEND, HandleSendMessage_OnlyServer);
            messaging.RegisterNamedMessageHandler(MESSAGE_BROADCAST, HandleBroadcastMessage);

            isRegistered = true;
            handlerOwner = this;

            if (networkManager.IsServer) RefreshKnownNames_OnlyServer();
        }

        private void UnregisterHandlers()
        {
            if (!isRegistered) return;
            isRegistered = false;

            // 다른 매니저(새 씬)가 이미 등록했다면 그 핸들러를 지우지 않음
            if (handlerOwner != this) return;
            handlerOwner = null;

            // 네트워크가 먼저 종료되었으면 메시징도 이미 사라짐
            var messaging = networkManager != null ? networkManager.CustomMessagingManager : null;
            if (messaging == null) return;

            messaging.UnregisterNamedMessageHandler(MESSAGE_SEND);
            messaging.UnregisterNamedMessageHandler(MESSAGE_BROADCAST);
        }

        #endregion

        #region Send [나만]

        /// <summary> [나만] 다음 메시지를 보낼 수 있기까지 남은 시간 (초) </summary>
        public float RemainingCooldown => Mathf.Max(0f, localLastSendTime + sendInterval - Time.unscaledTime);

        /// <summary> [나만] 메시지를 보냅니다. 비어 있거나 너무 빠르면 보내지 않고 false </summary>
        public bool Send(string text)
        {
            if (!isRegistered) RegisterHandlers();
            if (!isRegistered) return false;

            text = Sanitize(text);
            if (string.IsNullOrEmpty(text)) return false;

            if (RemainingCooldown > 0f) return false;
            localLastSendTime = Time.unscaledTime;

            // 호스트는 자기 자신에게 보낼 필요 없이 바로 서버 처리
            if (networkManager.IsServer)
            {
                ProcessPlayerMessage_OnlyServer(NetworkManager.ServerClientId, text);
                return true;
            }

            using var writer = new FastBufferWriter(WRITER_INITIAL_SIZE, Allocator.Temp, WRITER_MAX_SIZE);
            writer.WriteValueSafe(text);
            networkManager.CustomMessagingManager.SendNamedMessage(MESSAGE_SEND, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return true;
        }

        #endregion

        #region Server [서버만]

        private void HandleSendMessage_OnlyServer(ulong senderClientId, FastBufferReader reader)
        {
            if (!networkManager.IsServer) return;

            reader.ReadValueSafe(out string text);
            ProcessPlayerMessage_OnlyServer(senderClientId, text);
        }

        /// <summary> [서버만] 검증 후 모두에게 전달합니다. 보낸 사람 이름은 서버의 PlayerData 기준입니다. </summary>
        private void ProcessPlayerMessage_OnlyServer(ulong senderClientId, string text)
        {
            text = Sanitize(text);
            if (string.IsNullOrEmpty(text)) return;

            float now = Time.unscaledTime;
            if (lastSendTimes.TryGetValue(senderClientId, out float last) && now - last < sendInterval) return;
            lastSendTimes[senderClientId] = now;

            Broadcast_OnlyServer(ChatMessageType.Player, senderClientId, GetName_OnlyServer(senderClientId), text);
        }

        /// <summary> [서버만] 시스템 메시지. key는 System_Table 키이며 {0}에 이름이 들어갑니다. 받는 쪽이 번역합니다. </summary>
        public void SendSystemMessage_OnlyServer(string key, string argument = "")
        {
            if (networkManager == null || !networkManager.IsServer) return;

            Broadcast_OnlyServer(ChatMessageType.System, NetworkManager.ServerClientId, argument ?? string.Empty, key);
        }

        private void Broadcast_OnlyServer(ChatMessageType type, ulong senderClientId, string senderName, string text)
        {
            // 호스트 자신은 네트워크를 거치지 않고 바로 수신
            Receive(type, senderClientId, senderName, text);

            targetBuffer.Clear();
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId) targetBuffer.Add(clientId);
            }

            if (targetBuffer.Count == 0) return;

            using var writer = new FastBufferWriter(WRITER_INITIAL_SIZE, Allocator.Temp, WRITER_MAX_SIZE);
            writer.WriteValueSafe((byte)type);
            writer.WriteValueSafe(senderClientId);
            writer.WriteValueSafe(senderName);
            writer.WriteValueSafe(text);

            networkManager.CustomMessagingManager.SendNamedMessage(MESSAGE_BROADCAST, targetBuffer, writer, NetworkDelivery.ReliableSequenced);
        }

        private string GetName_OnlyServer(ulong clientId)
        {
            var multiplayer = MultiplayerService.Singleton;

            if (multiplayer != null && multiplayer.TryGetPlayerDataByClientId(clientId, out var data) && !data.IsEmpty())
            {
                knownNames[clientId] = data.PlayerName;
                return data.PlayerName;
            }

            return knownNames.TryGetValue(clientId, out string name) ? name : "Player";
        }

        private void RefreshKnownNames_OnlyServer()
        {
            var multiplayer = MultiplayerService.Singleton;
            if (multiplayer == null) return;

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (multiplayer.TryGetPlayerDataByClientId(clientId, out var data) && !data.IsEmpty()) knownNames[clientId] = data.PlayerName;
            }
        }

        private void HandlePlayerDatasChanged(object sender, MultiplayerService.PlayerDatasChangedEventArgs e)
        {
            if (networkManager != null && networkManager.IsServer) RefreshKnownNames_OnlyServer();
        }

        /// <summary> [서버만] 새 플레이어 입장 (호스트 자신 제외) </summary>
        private void HandleClientConnected(object sender, MultiplayerService.OnClientEventArgs e)
        {
            if (!showJoinMessage || !isRegistered || !networkManager.IsServer || e.clientId == NetworkManager.ServerClientId) return;

            SendSystemMessage_OnlyServer(SystemKeys.ChatPlayerJoined, GetName_OnlyServer(e.clientId));
        }

        /// <summary> [서버만] 플레이어 퇴장 </summary>
        private void HandleClientDisconnected(object sender, MultiplayerService.OnClientEventArgs e)
        {
            if (!isRegistered || !networkManager.IsServer) return;

            string name = GetName_OnlyServer(e.clientId);

            lastSendTimes.Remove(e.clientId);
            knownNames.Remove(e.clientId);

            if (showLeaveMessage) SendSystemMessage_OnlyServer(SystemKeys.ChatPlayerLeft, name);
        }

        #endregion

        #region Receive [모두]

        private void HandleBroadcastMessage(ulong senderClientId, FastBufferReader reader)
        {
            // 서버가 보낸 것만 받음
            if (senderClientId != NetworkManager.ServerClientId) return;

            reader.ReadValueSafe(out byte type);
            reader.ReadValueSafe(out ulong chatSenderClientId);
            reader.ReadValueSafe(out string senderName);
            reader.ReadValueSafe(out string text);

            Receive((ChatMessageType)type, chatSenderClientId, senderName, text);
        }

        private void Receive(ChatMessageType type, ulong senderClientId, string senderName, string text)
        {
            // 시스템 메시지는 받은 쪽 언어로 번역 (키 + 이름)
            if (type == ChatMessageType.System) text = hexit != null ? hexit.GetSystemText(text, senderName) : text;

            var message = new ChatMessage(type, senderClientId, senderName, text);

            if (history.Count >= maxHistory) history.RemoveAt(0);
            history.Add(message);

            OnMessageReceived?.Invoke(message);
        }

        #endregion

        /// <summary> 앞뒤 공백 제거, 줄바꿈을 공백으로, 최대 글자 수로 자르기 </summary>
        private string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length > maxMessageLength) text = text.Substring(0, maxMessageLength);

            return text;
        }
    }
}