using System.Collections.Generic;
using HexitEngine;
using UnityEngine;

public class GameManager : GameManagerBase
{
    [SerializeField] private ClientGamePlayer gamePlayerPrefab;
    [SerializeField] private List<Transform> playerSpawnPoints = new();

    /// <summary> [서버만] 게임 플레이어를 생성해 그 플레이어 소유로 스폰합니다. </summary>
    protected override void SpawnGamePlayer_OnlyServer(PlayerData playerData)
    {
        if (gamePlayerPrefab == null)
        {
            Dev.LogError("Game Player Prefab이 지정되지 않았습니다.");
            return;
        }

        GetSpawnPose(playerData, out var position, out var rotation);

        var gamePlayer = Instantiate(gamePlayerPrefab, position, rotation);
        gamePlayer.SpawnGamePlayer_OnlyServer(playerData);
    }

    /// <summary> [서버만] 생성 위치. playerId 번째 스폰 지점 (부족하면 순환, 없으면 이 오브젝트 위치) </summary>
    private void GetSpawnPose(PlayerData playerData, out Vector3 position, out Quaternion rotation)
    {
        var point = GetSpawnPoint(playerData.playerId);

        position = point.position;
        rotation = point.rotation;
    }

    private Transform GetSpawnPoint(int playerId)
    {
        if (playerSpawnPoints == null || playerSpawnPoints.Count == 0)
        {
            Dev.LogWarn("스폰 지점이 없어 GameManager 위치에 생성합니다.");
            return transform;
        }

        if (playerId >= playerSpawnPoints.Count)
        {
            Dev.LogWarn($"스폰 지점({playerSpawnPoints.Count}개)보다 playerId({playerId})가 커서 순환 배치합니다.");
        }

        int index = Mathf.Abs(playerId) % playerSpawnPoints.Count;
        var point = playerSpawnPoints[index];

        return point != null ? point : transform;
    }

    /// <summary> 매니저 스폰보다 플레이어 준비가 먼저 끝났을 수도 있으므로 스폰 시점에도 확인 </summary>
    protected override void OnGameManagerSpawn() => TryReleaseLoading_OnlyServer();

    protected override void OnGamePlayerReadyHook(ClientGamePlayer gamePlayer)
    {
        Dev.Log($"게임 플레이어 준비: {gamePlayer.PlayerName} (playerId {gamePlayer.PlayerId}{(gamePlayer.IsLocal ? ", 나" : "")})");

        TryReleaseLoading_OnlyServer();
    }

    /// <summary> 기다리던 사람이 나가면 남은 인원으로 다시 확인 </summary>
    protected override void OnServerClientDisconnectedHook(ulong clientId) => TryReleaseLoading_OnlyServer();

    /// <summary> [서버만] 준비가 끝났으면 모든 컴퓨터의 로딩을 동시에 걷습니다. </summary>
    private void TryReleaseLoading_OnlyServer()
    {
        if (!IsServer || IsAllGamePlayersLoaded) return;
        if (!IsReadyToReleaseLoading_OnlyServer()) return;

        ReleaseLoadingForAll_OnlyServer();
    }

    /// <summary> [서버만] 로딩을 걷을 조건. 게임별로 더 기다릴 것(맵 생성 등)이 있으면 여기에 추가합니다. </summary>
    private bool IsReadyToReleaseLoading_OnlyServer()
    {
        return AreAllGamePlayersReady_OnlyServer();
    }

    protected override void OnAllGamePlayersLoadedHook()
    {
        Dev.Log("모든 플레이어 준비 완료: 로딩 화면이 걷혔습니다.");
    }

    protected override void OnServerGameStarted()
    {
        Dev.Log("게임 시작");
    }

    protected override void OnServerGameEnded()
    {
        Dev.Log("게임 종료 → Ready 씬으로 이동");
    }

    protected override void OnGamePlayerDespawnedHook(ClientGamePlayer gamePlayer)
    {
        Dev.Log($"게임 플레이어 사라짐: {gamePlayer.PlayerName}");
    }
}