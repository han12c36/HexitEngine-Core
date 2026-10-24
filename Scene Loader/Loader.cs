using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexitEngine
{
    public static class Loader
    {
        public enum Scene { MainMenu, Ready, Game }

        public static string GetActiveSceneName => SceneManager.GetActiveScene().name;

        public static void Load(Scene targetScene) => SceneManager.LoadScene(targetScene.ToString());

        /// <summary> 비동기로 씬을 로드합니다. (로딩 화면 진행률 표시용) 빌드 목록에 없는 씬이면 null </summary>
        public static AsyncOperation LoadAsync(Scene targetScene) => SceneManager.LoadSceneAsync(targetScene.ToString());

        /// <summary>
        /// [서버만] Netcode의 SceneManager로 모든 클라이언트를 함께 이동시킵니다.
        /// 씬 로드를 시작했으면 true, 시작하지 못했으면(서버 아님, 씬 관리 꺼짐, 빌드 목록에 없는 씬, 다른 씬 이벤트 진행 중 등) false
        /// </summary>
        public static bool LoadNetwork(Scene targetScene)
        {
            var networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsServer)
            {
                Dev.LogWarn($"서버가 아니거나 NetworkManager가 없습니다.");
                return false;
            }

            if (networkManager.SceneManager == null)
            {
                Dev.LogError("NetworkManager의 Enable Scene Management가 꺼져 있습니다.");
                return false;
            }

            var status = networkManager.SceneManager.LoadScene(targetScene.ToString(), LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Dev.LogError($"{targetScene} 씬 로드를 시작하지 못했습니다. ({status})");
                return false;
            }

            return true;
        }
    }
}