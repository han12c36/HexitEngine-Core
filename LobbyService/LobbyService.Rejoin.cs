//using H_Framework;
//using Steamworks;
//using Steamworks.Data;
//using System;
//using UnityEngine;
//using H_Netcode;
//
//#if UNITY_EDITOR
//using ParrelSync;
//#endif
//
//public partial class LobbyService : LocalSingleton<LobbyService>
//{
//    private const string REJOIN_TICKET_KEY = "MULTIPLAYER_REJOIN_TICKET";
//    private const int REJOIN_TICKET_EXPIRE_MINUTES = 30;
//
//    [Serializable]
//    public struct RejoinTicket
//    {
//        public string authId;        // 내 스팀 고유 ID (SteamId.ToString)
//        public string lobbyId;       // 스팀 로비 ID (Lobby.Id.ToString)
//        public long createdAtTicks;  // 생성 시각
//
//        public bool IsExpired(TimeSpan maxAge)
//        {
//            var created = new DateTime(createdAtTicks, DateTimeKind.Utc);
//            return (DateTime.UtcNow - created) > maxAge;
//        }
//    }
//
//    private bool isRejoiningLobby = false;
//
//    public bool TryRaiseRejoinTicket(out RejoinTicket ticket)
//    {
//        ticket = default;
//
//        // 1) 티켓 로드 시도
//        if (!TryLoadRejoinTicket(out ticket)) return false;
//
//        // 2) 만료 체크
//        var maxAge = TimeSpan.FromMinutes(REJOIN_TICKET_EXPIRE_MINUTES);
//        if (ticket.IsExpired(maxAge))
//        {
//            Dev.Log("재접속 티켓 만료 - 폐기 처리");
//            ClearRejoinTicket();
//            return false;
//        }
//
//        // 3) 계정 일치 검증: 현재 로그인된 스팀 계정과 티켓의 소유자가 같은지 확인
//        if (ticket.authId != SteamClient.SteamId.ToString())
//        {
//            Dev.Log("스팀 계정 변경 감지 - 남의 티켓이므로 폐기 처리");
//            ClearRejoinTicket();
//            return false;
//        }
//
//        Dev.Log($"유효한 재접속 티켓 확인! 로비 ID: {ticket.lobbyId}");
//        return true;
//    }
//
//    private bool TryLoadRejoinTicket(out RejoinTicket ticket)
//    {
//        ticket = default;
//        var key = GetRejoinTicketKey();
//
//        if (!PlayerPrefs.HasKey(key)) return false;
//
//        var json = PlayerPrefs.GetString(key, string.Empty);
//
//        if (string.IsNullOrEmpty(json))
//        {
//            Dev.Log("저장된 티켓 Json이 비어있습니다.");
//            return false;
//        }
//
//        try
//        {
//            ticket = JsonUtility.FromJson<RejoinTicket>(json);
//            return true;
//        }
//        catch (Exception e)
//        {
//            Dev.LogError($"Json 파싱 실패, key = {key}, json = {json}\n예외: {e.Message}");
//            PlayerPrefs.DeleteKey(key);
//            return false;
//        }
//    }
//
//    public void IssueRejoinTicket(Lobby lobby)
//    {
//        Dev.Log("IssueRejoinTicket - 스팀 재접속 티켓 발급");
//
//        // 스팀 로비 ID는 ulong이므로 문자열로 변환하여 저장합니다.
//        string lobbyIdString = lobby.Id.ToString();
//
//        // 티켓 생성 (릴레이 코드를 찾는 복잡한 과정이 완전히 사라짐)
//        var ticket = new RejoinTicket
//        {
//            authId = SteamClient.SteamId.ToString(),
//            lobbyId = lobbyIdString,
//            createdAtTicks = DateTime.UtcNow.Ticks,
//        };
//
//        SaveRejoinTicket(ticket);
//    }
//
//    private void SaveRejoinTicket(RejoinTicket ticket)
//    {
//        var json = JsonUtility.ToJson(ticket);
//        var key = GetRejoinTicketKey();
//
//        PlayerPrefs.SetString(key, json);
//        PlayerPrefs.Save();
//
//        Dev.Log($"[SaveRejoinTicket] 티켓 저장 완료: {json}");
//    }
//
//    public void ClearRejoinTicket()
//    {
//        var key = GetRejoinTicketKey();
//
//        if (PlayerPrefs.HasKey(key))
//        {
//            PlayerPrefs.DeleteKey(key);
//            PlayerPrefs.Save();
//            Dev.Log("재접속 티켓 초기화 완료");
//        }
//    }
//
//    private string GetRejoinTicketKey()
//    {
//        var key = REJOIN_TICKET_KEY;
//
//#if UNITY_EDITOR
//        // 에디터에서 ParrelSync로 다중 클라이언트 테스트 시 티켓 충돌 방지
//        if (ClonesManager.IsClone())
//        {
//            var arg = ClonesManager.GetArgument();
//            if (!string.IsNullOrEmpty(arg))
//            {
//                key += $"_clone_{arg}";
//            }
//        }
//#endif
//
//        return key;
//    }
//
//    public bool CanRejoinLobby(string lobbyIdString, string steamId)
//    {
//        if (!ulong.TryParse(lobbyIdString, out ulong lobbyId)) return false;
//
//        var lobby = new Lobby(lobbyId);
//
//        // 1. 상태 검증: 대기 중인 방은 일반 참가 대상이지 '재접속' 대상이 아님
//        if (GetLobbyState(lobby) == LobbyState.Waiting)
//        {
//            Dev.Log("재접속 중단: 로비가 아직 대기(Waiting) 상태입니다. (일반 참가를 이용하세요)");
//            return false;
//        }
//
//        // 2. 권한 검증: 호스트가 작성한 '확정 명단(WhiteList)'에 포함되어 있는지 확인
//        if (!IsMatchedMember(lobby, steamId))
//        {
//            Dev.Log($"재접속 실패: 플레이어({steamId})가 게임 시작 당시의 확정 멤버 명단에 없습니다.");
//            return false;
//        }
//
//        // 3. 고스트 로비 검증: 스팀은 호스트 자동 승격이 되지만, 혹시 모를 로비 파괴 상태를 체크
//        if (lobby.Owner.Id.Value == 0)
//        {
//            Dev.Log("재접속 실패: 유효한 로비 호스트가 존재하지 않습니다.");
//            return false;
//        }
//
//        return true;
//    }
//
//    private bool IsMatchedMember(Lobby lobby, string steamId)
//    {
//        // 스팀 GetData는 데이터가 없으면 빈 문자열("")을 반환합니다.
//        string raw = lobby.GetData(MATCHED_AUTH_IDS_KEY);
//
//        if (string.IsNullOrEmpty(raw))
//        {
//            Dev.LogWarn("확정 멤버 데이터가 로비에 존재하지 않습니다.");
//            return false;
//        }
//
//        Dev.Log($"전체 확정 명단: {raw}");
//
//        var ids = raw.Split(',');
//        foreach (var id in ids)
//        {
//            if (id.Trim() == steamId)
//            {
//                Dev.Log($"일치하는 멤버 확인: {steamId}");
//                return true;
//            }
//        }
//
//        Dev.LogWarn($"명단에서 {steamId}를 찾지 못했습니다.");
//        return false;
//    }
//}