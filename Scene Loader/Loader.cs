using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace HexitEngine 
{
    public static class Loader
    {
        public enum Scene { MainMenu, Game }

        public static string GetActiveSceneName => SceneManager.GetActiveScene().name;

        public static void Load(Scene targetScene) => SceneManager.LoadScene(targetScene.ToString());

        public static void LoadNetwork(Scene targetScene)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Dev.Log($"서버가 아니거나 NetworkManager가 없습니다.");
                return;
            }

            // Netcode의 SceneManager를 통해 모든 클라이언트를 함께 이동시킵니다.
            NetworkManager.Singleton.SceneManager.LoadScene(targetScene.ToString(), LoadSceneMode.Single);
        }
    }
}