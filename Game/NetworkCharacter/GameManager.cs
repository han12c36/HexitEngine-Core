using System;
using Unity.Netcode;

namespace HexitEngine
{
    /// <summary>
    /// Game씬에 배치해야합니다.
    /// 서버가 GameStarted_OnlyServer를 호출하면 게임이 시작되며, 모두에게 OnGameStarted를 알립니다.
    /// 서버가 GameEnded_OnlyServer를 호출하면 게임이 끝나며, 모두에게 OnGameEnded를 알립니다.
    /// 게임이 시작되면 로비가 잠겨 새로운 참가(처음이든 다시든)를 막고, 게임이 끝나면 로비를 다시 엽니다.
    /// </summary>
    public class GameManager : ServerSingleton<GameManager>
    {
        private NetworkVariable<bool> n_IsGameStarted = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary> [모두] 게임이 시작되었는지 </summary>
        public bool IsGameStarted => n_IsGameStarted.Value;

        /// <summary> [모두] 서버가 GameStarted_OnlyServer를 호출함. 즉 게임이 시작됨! </summary>
        public event EventHandler OnGameStarted;

        /// <summary> [모두] 서버가 GameEnded_OnlyServer를 호출함. 즉 게임이 끝남! </summary>
        public event EventHandler OnGameEnded;

        #region Lifecycle

        protected override void OnNetSpawn()
        {
            n_IsGameStarted.OnValueChanged -= HandleGameStartedChanged;
            n_IsGameStarted.OnValueChanged += HandleGameStartedChanged;
        }

        protected override void OnNetDespawn()
        {
            n_IsGameStarted.OnValueChanged -= HandleGameStartedChanged;
        }

        #endregion

        #region Game Flow

        /// <summary> [서버만] 게임을 시작합니다. 이미 시작되었으면 무시합니다. </summary>
        public void GameStarted_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 시작할 수 있습니다.");
                return;
            }

            if (IsGameStarted) return;

            HEXITEngine.Singleton.LockLobby();

            // 값이 바뀌면 모든 컴퓨터에서 OnGameStarted가 발생합니다. (호스트 포함)
            n_IsGameStarted.Value = true;
        }

        /// <summary> [서버만] 게임을 끝냅니다. 시작되지 않았으면 무시합니다. </summary>
        public void GameEnded_OnlyServer()
        {
            if (!IsServer)
            {
                Dev.LogWarn("서버에서만 게임을 끝낼 수 있습니다.");
                return;
            }

            if (!IsGameStarted) return;

            // 값이 바뀌면 모든 컴퓨터에서 OnGameEnded가 발생합니다. (호스트 포함)
            n_IsGameStarted.Value = false;

            HEXITEngine.Singleton.UnlockLobby();
        }

        private void HandleGameStartedChanged(bool previousValue, bool newValue)
        {
            if (previousValue == newValue) return;

            if (newValue) OnGameStarted?.Invoke(this, EventArgs.Empty);
            else OnGameEnded?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}