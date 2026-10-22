using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    public partial class LobbyService
    {
        // 생성/참가 시작 이벤트는 HEXITEngine이 요청을 받는 시점에 직접 발생시킵니다. (OnCreateLobbyStartedToLobby / OnJoinStartedToLobby)
        // 실패 이벤트는 번역하지 않은 사유 키(SystemKeys)만 담아 보냅니다. 번역은 HEXITEngine이 Message에 채웁니다.

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

        /// <summary> Steam 오버레이에서 초대를 수락함 </summary>
        public event EventHandler<Lobby> OnJoinRequestedViaOverlay;

        /// <summary> 호스트에게 강퇴됨 (KickTarget 감지 후 로비에서 나간 직후). 네트워크 종료는 HEXITEngine이 처리합니다. </summary>
        public event EventHandler OnKickedFromLobby;

        /// <summary> 참가한 로비에서 다른 멤버가 나감 (나 자신 제외). 호스트의 빠른 이탈 감지용 </summary>
        public event EventHandler<OnLobbyMemberLeftEventArgs> OnLobbyMemberLeft;

        /// <summary> 로비 생성 실패 상세 정보 </summary>
        public class OnLobbyCreateFailedEventArgs : EventArgs
        {
            /// <summary> 사유 키 (SystemKeys) </summary>
            public string ReasonKey { get; }
            /// <summary> 화면 표시용 문구. HEXITEngine이 ReasonKey를 번역해서 채웁니다. (번역 전에는 키) </summary>
            public string Message { get; set; }

            public OnLobbyCreateFailedEventArgs(string reasonKey)
            {
                ReasonKey = reasonKey;
                Message = reasonKey;
            }
        }

        /// <summary> 로비 입장 실패 상세 정보 </summary>
        public class OnLobbyJoinFailedEventArgs : EventArgs
        {
            /// <summary> Steam 입장 결과 (Steam 입장 단계에서 실패한 경우만) </summary>
            public RoomEnter? Reason { get; }
            /// <summary> 사유 키 (SystemKeys) </summary>
            public string ReasonKey { get; }
            /// <summary> 화면 표시용 문구. HEXITEngine이 ReasonKey를 번역해서 채웁니다. (번역 전에는 키) </summary>
            public string Message { get; set; }

            public OnLobbyJoinFailedEventArgs(string reasonKey, RoomEnter? reason = null)
            {
                ReasonKey = reasonKey;
                Message = reasonKey;
                Reason = reason;
            }
        }

        /// <summary> 빠른 로비 참가 실패 상세 정보 </summary>
        public class OnLobbyQuickJoinFailedEventArgs : EventArgs
        {
            /// <summary> 사유 키 (SystemKeys) </summary>
            public string ReasonKey { get; }
            /// <summary> 화면 표시용 문구. HEXITEngine이 ReasonKey를 번역해서 채웁니다. (번역 전에는 키) </summary>
            public string Message { get; set; }

            public OnLobbyQuickJoinFailedEventArgs(string reasonKey)
            {
                ReasonKey = reasonKey;
                Message = reasonKey;
            }
        }

        /// <summary> 로비에서 나간 멤버 </summary>
        public class OnLobbyMemberLeftEventArgs : EventArgs
        {
            public ulong SteamId { get; }
            public OnLobbyMemberLeftEventArgs(ulong steamId) => SteamId = steamId;
        }

        /// <summary> 검색된 로비 리스트 전달 </summary>
        public class OnLobbyListChangedEventArgs : EventArgs
        {
            public List<Lobby> lobbyList;
            public OnLobbyListChangedEventArgs(List<Lobby> lobbyList) => this.lobbyList = lobbyList;
        }
    }
}