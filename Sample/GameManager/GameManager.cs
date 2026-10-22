namespace HexitEngine.Sample
{

    public class GameManager : GameManagerBase
    {
        protected override void OnServerGameStarted()
        {
            Dev.Log("게임 시작 (샘플: 아무 동작 없음)");
        }

        protected override void OnServerGameEnded()
        {
            Dev.Log("게임 종료 → Ready 씬으로 이동");
        }

        protected override void OnGamePlayerReadyHook(ClientGamePlayer gamePlayer)
        {
            Dev.Log($"게임 플레이어 준비: {gamePlayer.PlayerName} (playerId {gamePlayer.PlayerId}{(gamePlayer.IsLocal ? ", 나" : "")})");
        }

        protected override void OnGamePlayerDespawnedHook(ClientGamePlayer gamePlayer)
        {
            Dev.Log($"게임 플레이어 사라짐: {gamePlayer.PlayerName}");
        }
    }
}