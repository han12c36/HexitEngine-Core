using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    public partial class LobbyService
    {
        /// <summary> 로비 생성 시작 시점 </summary>
        public event EventHandler OnCreateLobbyStartedToLobby;
        /// <summary> 로비 참가 시작 시점 </summary>
        public event EventHandler OnJoinStartedToLobby;

        /// <summary> 로비 생성 성공 시점 </summary>
        public event EventHandler OnCreateLobbySucceededToLobby;
        /// <summary> 로비 참가 성공 시점 </summary>
        public event EventHandler OnJoinSucceededToLobby;

        /// <summary> 로비 생성 실패 시점 </summary>
        public event EventHandler<OnLobbyCreateFailedEventArgs> OnCreateLobbyFailedToLobby;
        /// <summary> 로비 참가 실패 시점 </summary>
        public event EventHandler<OnLobbyJoinFailedEventArgs> OnJoinFailedToLobby;
        /// <summary> 빠른 로비 참가 실패 시점 (방이 없을 때 포함) </summary>
        public event EventHandler<OnLobbyQuickJoinFailedEventArgs> OnQuickJoinFailedToLobby;

        /// <summary> 로비 리스트 변경 시점 (UI 갱신용) </summary>
        public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

        /// <summary> 호스트에게 강퇴됨 </summary>
        public event EventHandler OnKickedFromLobby;

        /// <summary> Steam 오버레이에서 초대를 수락함 </summary>
        public event EventHandler<Lobby> OnJoinRequestedViaOverlay;

        /// <summary> 로비 생성 실패 상세 정보 </summary>
        public class OnLobbyCreateFailedEventArgs : EventArgs
        {
            public string Message { get; }
            public OnLobbyCreateFailedEventArgs(string message) => Message = message;
        }

        /// <summary> 로비 입장 실패 상세 정보 </summary>
        public class OnLobbyJoinFailedEventArgs : EventArgs
        {
            public RoomEnter? Reason { get; }
            public string Message { get; }

            public OnLobbyJoinFailedEventArgs(string message, RoomEnter? reason = null)
            {
                Message = message;
                Reason = reason;
            }
        }

        /// <summary> 빠른 로비 참가 실패 상세 정보 </summary>
        public class OnLobbyQuickJoinFailedEventArgs : EventArgs
        {
            public string Message { get; }
            public OnLobbyQuickJoinFailedEventArgs(string message) => Message = message;
        }

        /// <summary> 검색된 로비 리스트 전달 </summary>
        public class OnLobbyListChangedEventArgs : EventArgs
        {
            public List<Lobby> lobbyList;
            public OnLobbyListChangedEventArgs(List<Lobby> lobbyList) => this.lobbyList = lobbyList;
        }
    }
}