using System;
using System.Collections.Generic;
using System.Threading;
using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    /// <summary> 로비 목록 검색과 필터 </summary>
    public partial class LobbyService
    {
        public enum LobbyState { Playing, Waiting }

        public enum LobbySlotFilter { All, Available, Full }
        public enum LobbyStateFilter { All, Playing, Waiting }
        public enum LobbyDistanceFilter { Worldwide, Far, Close }

        private const int LOBBY_LIST_MAX_RESULTS = 25;

        [Serializable]
        public class LobbyFilterOptions
        {
            public LobbySlotFilter slot;

            /// <summary> 현재는 사용하지 않습니다. 목록에는 항상 잠기지 않은(대기 중) 방만 표시됩니다. </summary>
            public LobbyStateFilter state;

            public LobbyDistanceFilter distance;

            public static LobbyFilterOptions Default => new()
            {
                slot = LobbySlotFilter.All,
                state = LobbyStateFilter.All,
                distance = LobbyDistanceFilter.Worldwide,
            };
        }

        private CancellationTokenSource filterCts;

        public LobbyFilterOptions FilterOptions { get; private set; } = LobbyFilterOptions.Default;

        private async void FilterLobbyList(LobbySlotFilter slotFilter, LobbyDistanceFilter distanceFilter, CancellationToken ct = default)
        {
            // 이전 검색 취소
            filterCts?.Cancel();
            filterCts?.Dispose();

            filterCts = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var linkedToken = filterCts.Token;

            try
            {
                var query = SteamMatchmaking.LobbyList
                    .WithMaxResults(LOBBY_LIST_MAX_RESULTS)
                    .WithKeyValue(LobbyKeys.IsLocked, LobbyKeys.False)
                    .WithKeyValue(LobbyKeys.IsPrivate, LobbyKeys.False);

                if (slotFilter == LobbySlotFilter.Available) query = query.WithSlotsAvailable(1);

                query = distanceFilter switch
                {
                    LobbyDistanceFilter.Close => query.FilterDistanceClose(),
                    LobbyDistanceFilter.Far => query.FilterDistanceFar(),
                    _ => query.FilterDistanceWorldwide(),
                };

                // Steam은 가까운 순으로 정렬해서 반환
                var lobbies = await query.RequestAsync();

                linkedToken.ThrowIfCancellationRequested();

                var filteredLobbies = new List<Lobby>();

                if (lobbies != null)
                {
                    foreach (var lobby in lobbies)
                    {
                        // 꽉 찬 방만 보기
                        if (slotFilter == LobbySlotFilter.Full && lobby.MemberCount < lobby.MaxMembers) continue;

                        // 유령 방 제외
                        if (lobby.MemberCount > 0) filteredLobbies.Add(lobby);
                    }
                }

                if (this == null) return;

                OnLobbyListChanged?.Invoke(this, new OnLobbyListChangedEventArgs(filteredLobbies));
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Dev.LogError($"[Lobby] 목록 검색 오류: {e.Message}"); }
        }

        public void SetFilter(LobbyFilterOptions newOptions)
        {
            FilterOptions = newOptions;
            RefreshLobbyList();
        }

        public void SetSlotFilter(LobbySlotFilter slot)
        {
            if (FilterOptions.slot == slot) return;
            FilterOptions.slot = slot;
            RefreshLobbyList();
        }

        public void SetStateFilter(LobbyStateFilter state)
        {
            if (FilterOptions.state == state) return;
            FilterOptions.state = state;
            RefreshLobbyList();
        }

        public void SetDistanceFilter(LobbyDistanceFilter distance)
        {
            if (FilterOptions.distance == distance) return;
            FilterOptions.distance = distance;
            RefreshLobbyList();
        }
    }
}