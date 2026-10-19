using System;
using System.Collections.Generic;

namespace HexitEngine
{
    public enum ListEventType
    {
        Add = 0,
        Remove = 1,
        Clear = 2,
    }

    public partial class MultiplayerService : ServerSingleton<MultiplayerService>
    {
        //public event EventHandler<PlayerDatasChangedEventArgs> OnPlayerDatasChanged;
        public EventHandler<PlayerDatasChangedEventArgs> OnPlayerDatasChanged { get; set; }

        /// <summary> 플레이어 생성 가능 시점 </summary>
        //public event EventHandler<OnPlayerSpawnEventArgs> OnPlayerReadyToSpawn;
        public EventHandler<OnPlayerSpawnEventArgs> OnPlayerReadyToSpawn { get; set; }

        /// <summary> 서버를 포함한 클라이언트가 서버에 접속했을 때 호스트를 포함한 모든 로컬에 호출되는 이벤트  </summary>
        //public event EventHandler<OnClientEventArgs> OnClientConnected;
        public EventHandler<OnClientEventArgs> OnClientConnected { get; set; }

        /// <summary> 서버를 포함한 클라이언트가 서버에서 끊어졌을 때 호스트를 포함한 모든 로컬에 호출되는 이벤트  </summary>
        //public event EventHandler<OnClientDisconnectedEventArgs> OnClientDisconnected;
        public EventHandler<OnClientEventArgs> OnClientDisconnected { get; set; }

        /// <summary> 본인이 서버 연결 끊킴을 인지 시 호출</summary>
        public EventHandler<OnDisconnectedEventArgs> OnDisconnected { get; set; }

        /// <summary> 호스트가 타인의 서버 연결 끊킴을 인지 시 호출 </summary>
        public EventHandler<OnClientEventArgs> OnHostDisconnected { get; set; }





        /// <summary> 인게임 중 어떤 플레이어가 나갔을 때(연결 끊김), 모든 클라이언트에서 브로드캐스트 되는 이벤트. </summary>
        //public event EventHandler<OnPlayerDataEventArgs> OnInGamePlayerDisconnected;
        public EventHandler<OnPlayerDataEventArgs> OnInGamePlayerDisconnected { get; set; }

        public class PlayerDatasChangedEventArgs : EventArgs
        {
            public List<ulong> joinedClientIds;
        }

        public class OnDisconnectedEventArgs : EventArgs
        {
            public string reason;
        }

        public class OnClientEventArgs : EventArgs
        {
            public ulong clientId;
            public string reason;
        }

        public class OnPlayerDataEventArgs : EventArgs
        {
            public ListEventType eventType;
            public PlayerData playerData;
        }

        public class OnPlayerSpawnEventArgs : EventArgs
        {
            public string sceneName;
            public PlayerData playerData;
        }
    }
}