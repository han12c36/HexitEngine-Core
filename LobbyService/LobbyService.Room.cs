using Steamworks.Data;
using UnityEngine;

namespace HexitEngine
{
    /// <summary> 로비 공통 조회와 상태 초기화 </summary>
    public partial class LobbyService
    {
        /// <summary> 로비 검색 연타 방지 쿨타임 (초) </summary>
        private const float QUERY_LOBBY_INTERVAL_SEC = 1.0f;

        private float lastLobbyRefreshAt = float.NegativeInfinity;

        /// <summary> 방장(로비 소유자)이 나인지 여부 </summary>
        public bool IsLobbyHost => JoinedLobby.HasValue && JoinedLobby.Value.Owner.Id.Value == SteamAuthService.SteamId;

        /// <summary> 로비의 대기/게임 중 상태 </summary>
        public LobbyState GetLobbyState(Lobby lobby) => lobby.GetData(LobbyKeys.IsLocked) == LobbyKeys.True ? LobbyState.Playing : LobbyState.Waiting;

        /// <summary> 로비 목록을 새로고침합니다. 1초 안에 다시 부르면 무시합니다. </summary>
        public void RefreshLobbyList()
        {
            if (!SteamAuthService.IsInitialized) return;

            if (Time.realtimeSinceStartup - lastLobbyRefreshAt < QUERY_LOBBY_INTERVAL_SEC) return;

            lastLobbyRefreshAt = Time.realtimeSinceStartup;

            FilterLobbyList(FilterOptions.slot, FilterOptions.distance, destroyCancellationToken);
        }

        private void ResetData()
        {
            isJoiningLobby = false;
            JoinedLobby = null;

            RefreshLobbyList();
        }
    }
}