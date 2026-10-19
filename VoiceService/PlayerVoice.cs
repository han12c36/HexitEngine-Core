//using H_Framework;
//using H_Netcode;
//using Mono.CSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Unity.Services.Core;
//using Unity.Services.Vivox;
//using UnityEngine;

//!!!!!!!!!!!!!!!!!!!
// 디바이스의 명칭이 영어가 아닌 이상은 음성채널에 접속이 안되던데 이거 해결해야할껄
// 음성 변조 아이템 테스트

///// <summary> Vivox를 활용한 VoiceService </summary>
//public partial class VoiceService : LocalSingleton<VoiceService>
//{
//    [SerializeField] private string key;
//    [SerializeField] private string issuer;
//    [SerializeField] private string domain;
//    [SerializeField] private string server;
//    
//    public VivoxParticipant LocalParticipant { get; private set; }
//    public string JoinedChannelName { get; private set; }
//    public int JoinedChannelCount => VivoxService.Instance.ActiveChannels.Count;
//    public bool IsLoggedIn => VivoxService.Instance.IsLoggedIn;
//    public bool IsConnectedToChannel => VivoxService.Instance.ActiveChannels.Count > 0;
//    public string SignedInPlayerId => VivoxService.Instance.SignedInPlayerId;
//    
//    protected override async void OnAwake()
//    {
//        //while (!AuthService.IsInitialized) { await Task.Yield(); }
//        while (VivoxService.Instance == null) { await Task.Yield(); }
//    
//        BindVivoxEvents();
//        BindDeviceEvents();
//        BindVoiceEffectEvents();
//    }
//    
//    //protected override void OnAwake()
//    //{
//    //    BindVivoxEvents();
//    //    BindDeviceEvents();
//    //    BindVoiceEffectEvents();
//    //}
//    
//    protected override void OnDestroyed()
//    {
//        UnbindVivoxEvents();
//        UnbindDeviceEvents();
//        UnbindVoiceEffectEvents();
//    }
//    
//    private void BindVivoxEvents() 
//    {
//        // 1) 로그인/연결 관련
//        VivoxService.Instance.LoggedIn -= HandleLoggedIn;
//        VivoxService.Instance.LoggedIn += HandleLoggedIn;
//    
//        VivoxService.Instance.LoggedOut -= HandleLoggedOut;
//        VivoxService.Instance.LoggedOut += HandleLoggedOut;
//    
//        VivoxService.Instance.ConnectionRecovering -= HandleConnectionRecovering;
//        VivoxService.Instance.ConnectionRecovering += HandleConnectionRecovering;
//    
//        VivoxService.Instance.ConnectionRecovered -= HandleConnectionRecovered;
//        VivoxService.Instance.ConnectionRecovered += HandleConnectionRecovered;
//    
//        VivoxService.Instance.ConnectionFailedToRecover -= HandleConnectionFailedToRecover;
//        VivoxService.Instance.ConnectionFailedToRecover += HandleConnectionFailedToRecover;
//    
//        // 2) 채널 관련
//        VivoxService.Instance.ChannelJoined -= HandleChannelJoined;
//        VivoxService.Instance.ChannelJoined += HandleChannelJoined;
//    
//        VivoxService.Instance.ChannelLeft -= HandleChannelLeft;
//        VivoxService.Instance.ChannelLeft += HandleChannelLeft;
//    
//        VivoxService.Instance.ParticipantAddedToChannel -= HandleParticipantAdded;
//        VivoxService.Instance.ParticipantAddedToChannel += HandleParticipantAdded;
//    
//        VivoxService.Instance.ParticipantRemovedFromChannel -= HandleParticipantRemoved;
//        VivoxService.Instance.ParticipantRemovedFromChannel += HandleParticipantRemoved;
//    
//        //// 3) 메시지 관련
//        //VivoxService.Instance.ChannelMessageReceived -= HandleChannelMessageReceived;
//        //VivoxService.Instance.ChannelMessageReceived += HandleChannelMessageReceived;
//        //
//        //VivoxService.Instance.DirectedMessageReceived -= HandleDirectedMessageReceived;
//        //VivoxService.Instance.DirectedMessageReceived += HandleDirectedMessageReceived;
//        //
//        //VivoxService.Instance.ChannelMessageEdited -= HandleChannelMessageEdited;
//        //VivoxService.Instance.ChannelMessageEdited += HandleChannelMessageEdited;
//        //
//        //VivoxService.Instance.ChannelMessageDeleted -= HandleChannelMessageDeleted;
//        //VivoxService.Instance.ChannelMessageDeleted += HandleChannelMessageDeleted;
//    
//        // 4) 장치 관련
//        VivoxService.Instance.AvailableInputDevicesChanged -= HandleAvailableInputDevicesChanged;
//        VivoxService.Instance.AvailableInputDevicesChanged += HandleAvailableInputDevicesChanged;
//    
//        VivoxService.Instance.AvailableOutputDevicesChanged -= HandleAvailableOutputDevicesChanged;
//        VivoxService.Instance.AvailableOutputDevicesChanged += HandleAvailableOutputDevicesChanged;
//    
//        VivoxService.Instance.EffectiveInputDeviceChanged -= HandleInputDeviceChanged;
//        VivoxService.Instance.EffectiveInputDeviceChanged += HandleInputDeviceChanged;
//    
//        VivoxService.Instance.EffectiveOutputDeviceChanged -= HandleOutputDeviceChanged;
//        VivoxService.Instance.EffectiveOutputDeviceChanged += HandleOutputDeviceChanged;
//    }
//    
//    private void UnbindVivoxEvents() 
//    {
//        VivoxService.Instance.LoggedIn -= HandleLoggedIn;
//        VivoxService.Instance.LoggedOut -= HandleLoggedOut;
//        VivoxService.Instance.ConnectionRecovering -= HandleConnectionRecovering;
//        VivoxService.Instance.ConnectionRecovered -= HandleConnectionRecovered;
//        VivoxService.Instance.ConnectionFailedToRecover -= HandleConnectionFailedToRecover;
//    
//        VivoxService.Instance.ChannelJoined -= HandleChannelJoined;
//        VivoxService.Instance.ChannelLeft -= HandleChannelLeft;
//    
//        VivoxService.Instance.ParticipantAddedToChannel -= HandleParticipantAdded;
//        VivoxService.Instance.ParticipantRemovedFromChannel -= HandleParticipantRemoved;
//    
//        //VivoxService.Instance.ChannelMessageReceived -= HandleChannelMessageReceived;
//        //VivoxService.Instance.DirectedMessageReceived -= HandleDirectedMessageReceived;
//        //VivoxService.Instance.ChannelMessageEdited -= HandleChannelMessageEdited;
//        //VivoxService.Instance.ChannelMessageDeleted -= HandleChannelMessageDeleted;
//    
//        VivoxService.Instance.AvailableInputDevicesChanged -= HandleAvailableInputDevicesChanged;
//        VivoxService.Instance.AvailableOutputDevicesChanged -= HandleAvailableOutputDevicesChanged;
//        VivoxService.Instance.EffectiveInputDeviceChanged -= HandleInputDeviceChanged;
//        VivoxService.Instance.EffectiveOutputDeviceChanged -= HandleOutputDeviceChanged;
//    }
//    
//    public static void SetVivoxCredentials(InitializationOptions options)
//    {
//        if (s_Singleton == null) return;
//    
//        if (Singleton.CheckManualCredentials())
//        {
//            options.SetVivoxCredentials(Singleton.server, Singleton.domain, Singleton.issuer, Singleton.key);
//        }
//    }
//    
//    public static async Task SignIn(string displayName)
//    {
//        await Singleton.SignInInternal(displayName);
//    }
//    
//    public static async Task SignOut()
//    {
//        if (VivoxService.Instance != null)
//        {
//            await VivoxService.Instance.LogoutAsync();
//        }
//    }
//    
//    /// <summary>채널 이름은 Lobby.Id를 사용합니다</summary>
//    public async Task JoinChannelAsync(LobbyResult result)
//    {
//        //var channelKey = result.LobbyData.Id;
//        var channelKey = "";
//    
//        if (string.IsNullOrEmpty(channelKey)) return;
//    
//        if (VivoxService.Instance == null) return;
//    
//        if (VivoxService.Instance.ActiveChannels.ContainsKey(channelKey)) return;
//    
//        try
//        {
//            Dev.Log("JoinVoiceChannel");
//    
//            //ChatCapability.TextAndAudio를 사용해야 마이크가 켜집니다.
//            await VivoxService.Instance.JoinGroupChannelAsync(channelKey, ChatCapability.TextAndAudio);
//        }
//        catch (Exception e)
//        {
//            Debug.LogError($"[Vivox] 채널 입장 실패: {e.Message}");
//        }
//    }
//    
//    public async Task LeaveChannelAsync(LobbyResult result)
//    {
//        //var channelKey = result.LobbyData.Id;
//        var channelKey = "";
//    
//        if (string.IsNullOrEmpty(channelKey)) return;
//    
//        if (VivoxService.Instance == null) return;
//    
//        if (!VivoxService.Instance.ActiveChannels.ContainsKey(channelKey)) return;
//    
//        try
//        {
//            Dev.Log("LeaveVoiceChannel");
//    
//            await VivoxService.Instance.LeaveChannelAsync(channelKey);
//        }
//        catch (Exception e)
//        {
//            Debug.LogError($"[Vivox] 채널 입장 실패: {e.Message}");
//        }
//    }
//    
//    private async Task SignInInternal(string displayName)
//    {
//        try
//        {
//            await VivoxService.Instance.InitializeAsync();
//    
//            var options = new LoginOptions
//            {
//                DisplayName = displayName,
//                ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
//            };
//    
//            await VivoxService.Instance.LoginAsync(options);
//        }
//        catch (Exception e)
//        {
//            Debug.LogError($"[Vivox] 로그인 실패: {e.Message}");
//        }
//    }
//    
//    private bool CheckManualCredentials()
//    {
//        return !(string.IsNullOrEmpty(issuer) && string.IsNullOrEmpty(domain) && string.IsNullOrEmpty(server));
//    }
//    
//    
//    private void RefreshLocalParticipantCache()
//    {
//        if (VivoxService.Instance.ActiveChannels.Count > 0)
//        {
//            var channel = VivoxService.Instance.ActiveChannels.FirstOrDefault();
//    
//            if (channel.Value != null)
//            {
//                LocalParticipant = channel.Value.FirstOrDefault(p => p.IsSelf);
//            }
//        }
//        else
//        {
//            LocalParticipant = null;
//        }
//    }
//    
//    public VivoxParticipant GetParticipantByAuthId(string authId)
//    {
//        foreach (var participant in VivoxService.Instance.ActiveChannels.FirstOrDefault().Value)
//        {
//            if (participant.PlayerId == authId)
//            {
//                return participant;
//            }
//        }
//        return null;
//    }
//    
//    
//    private void HandleLoggedIn() => OnLoggedIn?.Invoke();
//    private void HandleLoggedOut() => OnLoggedOut?.Invoke();
//    private void HandleConnectionRecovering() => OnConnectionRecovering?.Invoke();
//    private void HandleConnectionRecovered() => OnConnectionRecovered?.Invoke();
//    private void HandleConnectionFailedToRecover() => OnConnectionFailedToRecover?.Invoke();
//    
//    private void HandleChannelJoined(string channelName) 
//    {
//        JoinedChannelName = channelName;
//    
//        RefreshLocalParticipantCache();
//    
//        OnChannelJoined?.Invoke(channelName);
//    }
//    private void HandleChannelLeft(string channelName)
//    {
//        JoinedChannelName = null;
//    
//        LocalParticipant = null;
//        OnChannelLeft?.Invoke(channelName);
//    }
//    
//    private void HandleParticipantAdded(VivoxParticipant p)
//    {
//        if (p.IsSelf) LocalParticipant = p;
//    
//        OnParticipantAdded?.Invoke(p);
//    }
//    
//    private void HandleParticipantRemoved(VivoxParticipant p)
//    {
//        if (p.IsSelf) LocalParticipant = null;
//    
//        OnParticipantRemoved?.Invoke(p);
//    }
//    
//    //private void HandleChannelMessageReceived(VivoxMessage m) => OnChannelMessageReceived?.Invoke(m);
//    //private void HandleDirectedMessageReceived(VivoxMessage m) => OnDirectedMessageReceived?.Invoke(m);
//    //private void HandleChannelMessageEdited(VivoxMessage m) => OnChannelMessageEdited?.Invoke(m);
//    //private void HandleChannelMessageDeleted(VivoxMessage m) => OnChannelMessageDeleted?.Invoke(m);
//    
//    private void HandleAvailableInputDevicesChanged() => OnAvailableInputDevicesChanged?.Invoke();
//    private void HandleAvailableOutputDevicesChanged() => OnAvailableOutputDevicesChanged?.Invoke();
//    private void HandleInputDeviceChanged() => OnCurrentInputDeviceChanged?.Invoke();
//    private void HandleOutputDeviceChanged() => OnCurrentOutputDeviceChanged?.Invoke();
//    
//    
//    private class ParticipantSpeechTracker
//    {
//        public VivoxParticipant Participant { get; }
//        private readonly Action<VivoxParticipant> onSpeechAction;
//    
//        public ParticipantSpeechTracker(VivoxParticipant p, Action<VivoxParticipant> onSpeechAction)
//        {
//            Participant = p;
//            this.onSpeechAction = onSpeechAction;
//            // 메서드 참조를 사용하여 구독 (람다 아님)
//            Participant.ParticipantSpeechDetected += RelaySpeech;
//        }
//    
//        private void RelaySpeech() => onSpeechAction?.Invoke(Participant);
//    
//        public void Dispose()
//        {
//            // 확실하게 구독 해제
//            Participant.ParticipantSpeechDetected -= RelaySpeech;
//        }
//    }
//}