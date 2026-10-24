using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexitEngine
{
    public enum ListEventType
    {
        Add = 0,
        Remove = 1,
        Clear = 2,
    }

    // 연결 종료/거절 사유 키는 SystemKeys(System_Table)로 옮겼습니다. MultiplayerService는 번역하지 않은 키만 보내고, HEXITEngine이 번역합니다.

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

        /// <summary> 본인이 서버 연결 끊킴을 인지 시 호출. reasonKey만 채워서 보내며, 번역은 HEXITEngine이 합니다. </summary>
        public EventHandler<OnDisconnectedEventArgs> OnDisconnected { get; set; }

        /// <summary> 호스트가 타인의 서버 연결 끊킴을 인지 시 호출 </summary>
        public EventHandler<OnClientEventArgs> OnHostDisconnected { get; set; }

        /// <summary> [나만] 내 컴퓨터에서 네트워크 씬 로드 시작 (클라이언트의 첫 접속은 씬 동기화 시작). 로딩 화면용 </summary>
        public EventHandler<OnSceneLoadEventArgs> OnLocalSceneLoadStarted { get; set; }

        /// <summary> [나만] 내 컴퓨터에서 네트워크 씬 로드 완료 (클라이언트의 첫 접속은 씬 동기화 완료). 로딩 화면용 </summary>
        public EventHandler<OnSceneLoadEventArgs> OnLocalSceneLoadCompleted { get; set; }



        /// <summary> 인게임 중 어떤 플레이어가 나갔을 때(연결 끊김), 모든 클라이언트에서 브로드캐스트 되는 이벤트. </summary>
        //public event EventHandler<OnPlayerDataEventArgs> OnInGamePlayerDisconnected;
        public EventHandler<OnPlayerDataEventArgs> OnInGamePlayerDisconnected { get; set; }

        public class PlayerDatasChangedEventArgs : EventArgs
        {
            public List<ulong> joinedClientIds;
        }

        public class OnDisconnectedEventArgs : EventArgs
        {
            /// <summary> 화면 표시용 문구. HEXITEngine이 reasonKey를 번역해서 채웁니다. </summary>
            public string reason;

            /// <summary> 사유 키 (SystemKeys). 엔진이 정의하지 않은 사유면 원문이 그대로 들어 있습니다. </summary>
            public string reasonKey;
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

        public class OnSceneLoadEventArgs : EventArgs
        {
            /// <summary> 도착 씬 이름. 씬 동기화(참가자의 첫 접속)에서는 비어 있을 수 있습니다. </summary>
            public string sceneName;

            /// <summary> 참가자의 첫 접속 씬 동기화인지 </summary>
            public bool isSynchronize;

            /// <summary> 진행률을 읽을 씬 로드 작업. 씬 동기화, 완료 이벤트에서는 null </summary>
            public AsyncOperation asyncOperation;
        }
    }
}