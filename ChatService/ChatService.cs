//using H_Netcode;
//using System.Threading.Tasks;
//using UnityEngine;
//
//public partial class ChatService : LocalSingleton<ChatService>
//{
//    protected override async void OnAwake()
//    {
//        while (!AuthService.IsInitialized) { await Task.Yield(); }
//    
//        BindVivoxEvents();
//    }
//
//    protected override void OnDestroyed()
//    {
//        //UnbindVivoxEvents();
//    }
//
//    private void BindVivoxEvents()
//    {
//        VivoxService.Instance.ChannelMessageReceived -= HandleChannelMessageReceived;
//        VivoxService.Instance.ChannelMessageReceived += HandleChannelMessageReceived;
//    
//        VivoxService.Instance.DirectedMessageReceived -= HandleDirectedMessageReceived;
//        VivoxService.Instance.DirectedMessageReceived += HandleDirectedMessageReceived;
//    
//        VivoxService.Instance.ChannelMessageEdited -= HandleChannelMessageEdited;
//        VivoxService.Instance.ChannelMessageEdited += HandleChannelMessageEdited;
//    
//        VivoxService.Instance.ChannelMessageDeleted -= HandleChannelMessageDeleted;
//        VivoxService.Instance.ChannelMessageDeleted += HandleChannelMessageDeleted;
//    
//    }
//    
//    private void UnbindVivoxEvents()
//    {
//        VivoxService.Instance.ChannelMessageReceived -= HandleChannelMessageReceived;
//        VivoxService.Instance.DirectedMessageReceived -= HandleDirectedMessageReceived;
//        VivoxService.Instance.ChannelMessageEdited -= HandleChannelMessageEdited;
//        VivoxService.Instance.ChannelMessageDeleted -= HandleChannelMessageDeleted;
//    }
//    
//    private void HandleChannelMessageReceived(VivoxMessage m) => OnChannelMessageReceived?.Invoke(m);
//    private void HandleDirectedMessageReceived(VivoxMessage m) => OnDirectedMessageReceived?.Invoke(m);
//    private void HandleChannelMessageEdited(VivoxMessage m) => OnChannelMessageEdited?.Invoke(m);
//    private void HandleChannelMessageDeleted(VivoxMessage m) => OnChannelMessageDeleted?.Invoke(m);
//    
//    
//    /// <summary> Vivox SDK를 통해 특정 채널로 텍스트 메시지를 전송합니다 </summary>
//    public async Task SendChannelMessageAsync(string channelName, string message)
//    {
//        if (string.IsNullOrEmpty(channelName))
//        {
//            Debug.LogWarning("메세지를 보낼 채널에 참가하지 않았습니다.");
//            return;
//        }
//    
//        // 안전 장치: 로그인이 안 되어 있거나 Vivox가 없으면 리턴
//        if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
//        {
//            Debug.LogWarning("[ChatService] Vivox에 연결되어 있지 않아 메시지를 보낼 수 없습니다.");
//            return;
//        }
//    
//        // 실제 SDK 호출 (래핑)
//        await VivoxService.Instance.SendChannelTextMessageAsync(channelName, message);
//    }
//}