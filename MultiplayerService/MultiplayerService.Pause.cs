//using System;
//using UnityEngine;
//using Unity.Netcode;
//using System.Collections.Generic;
//using H_Netcode;
//
//public partial class Multiplayer : ServerSingleton<Multiplayer>
//{
//    //public event EventHandler OnLocalGamePaused;
//    //public event EventHandler OnLocalGameUnpaused;
//    //public event EventHandler OnMultiplayerGamePaused;
//    //public event EventHandler OnMultiplayerGameUnpaused;
//
//    public bool IsLocalGamePaused { get; private set; } = false;
//    
//    private NetworkVariable<bool> isMultiplayerGamePaused = new NetworkVariable<bool>(false);
//    private bool AutoEvaluateMultiplayerGamePauseState;
//    private Dictionary<ulong, bool> playerPausedDic;
//    
//    
//    private void Awake_Pause() 
//    {
//        playerPausedDic = new();
//    }
//    
//    private void Reset_Pause() 
//    {
//        if (IsServer)
//        {
//            isMultiplayerGamePaused.Value = false;
//            AutoEvaluateMultiplayerGamePauseState = false;
//            playerPausedDic.Clear();
//        }
//    
//        IsLocalGamePaused = false;
//    
//        isMultiplayerGamePaused.OnValueChanged -= IsMultiplayerGamePaused_OnValueChanged;
//    
//        if (IsServer)
//            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
//    }
//    
//    private void OnNetworkSpawn_Pause()
//    {
//        isMultiplayerGamePaused.OnValueChanged += IsMultiplayerGamePaused_OnValueChanged;
//    
//        if(IsServer)
//            NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
//    }
//    
//    private void LateUpdate()
//    {
//        if (!IsServer) return;
//    
//        if (AutoEvaluateMultiplayerGamePauseState)
//        {
//            AutoEvaluateMultiplayerGamePauseState = false;
//    
//            EvaluateMultiplayerGamePauseState();
//        }
//    }
//    
//    private void IsMultiplayerGamePaused_OnValueChanged(bool previousValue, bool newValue)
//    {
//        Time.timeScale = newValue ? 0f : 1f;
//    
//        if (newValue)
//            OnMultiplayerGamePaused?.Invoke(this, EventArgs.Empty);
//        else
//            OnMultiplayerGameUnpaused?.Invoke(this, EventArgs.Empty);
//    }
//    
//    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
//    {
//        AutoEvaluateMultiplayerGamePauseState = true;
//    }
//    
//    public void Pause()
//    {
//        if (IsLocalGamePaused) return;
//    
//        IsLocalGamePaused = true;
//        PauseGameServerRpc();
//        OnLocalGamePaused?.Invoke(this, EventArgs.Empty);
//    }
//    
//    public void Resume()
//    {
//        if (!IsLocalGamePaused) return;
//    
//        IsLocalGamePaused = false;
//        UnpauseGameServerRpc();
//        OnLocalGameUnpaused?.Invoke(this, EventArgs.Empty);
//    }
//    
//    [ServerRpc(RequireOwnership = false)]
//    private void PauseGameServerRpc(ServerRpcParams serverRpcParams = default)
//    {
//        playerPausedDic[serverRpcParams.Receive.SenderClientId] = true;
//        EvaluateMultiplayerGamePauseState();
//    }
//    
//    [ServerRpc(RequireOwnership = false)]
//    private void UnpauseGameServerRpc(ServerRpcParams serverRpcParams = default)
//    {
//        playerPausedDic[serverRpcParams.Receive.SenderClientId] = false;
//        EvaluateMultiplayerGamePauseState();
//    }
//    
//    private void EvaluateMultiplayerGamePauseState()
//    {
//        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
//        {
//            if (playerPausedDic.TryGetValue(clientId, out var isPaused) && isPaused)
//            {
//                //한명이라도 퍼즈를 검
//                isMultiplayerGamePaused.Value = true;
//                return;
//            }
//        }
//    
//        //한명도 퍼즈를 안검.
//        isMultiplayerGamePaused.Value = false;
//    }
//}