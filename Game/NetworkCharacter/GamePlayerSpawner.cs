using System.Collections.Generic;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// [서버] Game 씬에 배치. OnPlayerReadyToSpawn을 받아 게임 플레이어(ClientGamePlayer)를 생성하고 해당 플레이어 소유로 스폰합니다.
    /// - 스폰 시점(접속 + 씬 준비 + PlayerData)은 MultiplayerService가 판단합니다.
    /// - 위치는 playerId 번째 스폰 지점을 사용합니다. (지점이 부족하면 순환, 없으면 이 오브젝트 위치)
    /// - 주인이 나가면 게임 플레이어도 함께 사라집니다. (재접속 없음)
    /// </summary>
    public class GamePlayerSpawner : MonoBehaviour
    {
        [Tooltip("ClientGamePlayer가 붙은 플레이어 프리팹")]
        [SerializeField] private Transform gamePlayerPrefab;

        [Tooltip("playerId 순서의 스폰 지점")]
        [SerializeField] private List<Transform> playerSpawnTransforms;

        private HEXITEngine HEXIT;

        private void Awake()
        {
            HEXIT = HEXITEngine.Singleton;

            if (HEXIT == null) return;

            HEXIT.OnPlayerReadyToSpawn -= OnGamePlayerReadyToSpawn;
            HEXIT.OnPlayerReadyToSpawn += OnGamePlayerReadyToSpawn;
        }

        private void OnDestroy()
        {
            if (HEXIT == null) return;

            HEXIT.OnPlayerReadyToSpawn -= OnGamePlayerReadyToSpawn;
        }

        private void OnGamePlayerReadyToSpawn(object sender, MultiplayerService.OnPlayerSpawnEventArgs e)
        {
            if (gamePlayerPrefab == null)
            {
                Dev.LogError("Game Player Prefab이 지정되지 않았습니다.");
                return;
            }

            var spawnPoint = GetSpawnPoint(e.playerData.playerId);

            var player = Instantiate(gamePlayerPrefab, spawnPoint.position, spawnPoint.rotation);

            if (!player.TryGetComponent<ClientGamePlayer>(out var gamePlayer))
            {
                Dev.LogError($"Game Player Prefab에 ClientGamePlayer가 없습니다: {gamePlayerPrefab.name}");
                Destroy(player.gameObject);
                return;
            }

            gamePlayer.SpawnGamePlayer_OnlyServer(e.playerData);
        }

        /// <summary> playerId 번째 스폰 지점. 지점이 부족하면 순환하고, 없으면 이 오브젝트 위치를 사용합니다. </summary>
        private Transform GetSpawnPoint(int playerId)
        {
            if (playerSpawnTransforms == null || playerSpawnTransforms.Count == 0)
            {
                Dev.LogWarn("스폰 지점이 없어 Spawner 위치에 생성합니다.");
                return transform;
            }

            if (playerId >= playerSpawnTransforms.Count)
            {
                Dev.LogWarn($"스폰 지점({playerSpawnTransforms.Count}개)보다 playerId({playerId})가 커서 순환 배치합니다.");
            }

            int index = Mathf.Abs(playerId) % playerSpawnTransforms.Count;
            var point = playerSpawnTransforms[index];

            return point != null ? point : transform;
        }
    }
}