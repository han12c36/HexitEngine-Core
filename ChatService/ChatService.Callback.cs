//using H_Framework;
//using H_Netcode;
//using System;
//using UnityEngine;
//
//public partial class ChatService : LocalSingleton<ChatService>, IAppLifecycleListener
//{
//    /// <summary>채팅(전체) 수신</summary>
//    public Action<VivoxMessage> OnChannelMessageReceived;
//    
//    /// <summary>귓속말(DM) 수신</summary>
//    public Action<VivoxMessage> OnDirectedMessageReceived;
//    
//    /// <summary>메시지 수정됨 (채팅창 갱신)</summary>
//    public Action<VivoxMessage> OnChannelMessageEdited;
//    
//    /// <summary>메시지 삭제됨 (채팅창 갱신)</summary>
//    public Action<VivoxMessage> OnChannelMessageDeleted;
//}