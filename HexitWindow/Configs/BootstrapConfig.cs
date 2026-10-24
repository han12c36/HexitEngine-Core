using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HexitEngine
{
    /// <summary>
    /// 부트스트랩 설정: 부트스트랩 씬, 다음 씬, 강제 라우팅, 서비스 초기화 순서, 로딩 화면.
    ///
    /// 초기화 순서
    /// - [고정] 맨 처음: NetworkManager (존재 확인만 합니다. ISingleton이 아닙니다.)
    /// - [변경 가능] 중간: serviceOrder에 기록된 타입 순서 (BootstrapScene에서 불러와 Settings 창에서 드래그로 정렬)
    /// - [고정] 맨 마지막: HEXITEngine
    /// 씬 오브젝트는 에셋에서 참조할 수 없으므로 타입 이름(FullName)으로 기록합니다.
    /// </summary>
    public class BootstrapConfig : HexitEngineTapConfig<BootstrapConfig>
    {
        /// <summary> 맨 처음 고정 항목 표시 이름 </summary>
        public const string FIRST_FIXED_NAME = "NetworkManager";

        /// <summary> 맨 마지막 고정 항목 타입 이름 </summary>
        public static string LastFixedTypeName => typeof(HEXITEngine).FullName;

#if UNITY_EDITOR
        /// <summary> 서비스들이 배치된 씬. 게임 시작 시 이 씬에서 서비스를 초기화합니다. </summary>
        [SerializeField] private SceneAsset bootstrapScene;
        [SerializeField] private SceneAsset nextScene;
#endif
        [SerializeField, HideInInspector] private string bootstrapSceneName;
        [SerializeField, HideInInspector] private string nextSceneName;

        /// <summary> 고정 항목(처음/마지막)을 제외한 서비스 타입 이름. 이 순서대로 초기화됩니다. </summary>
        [SerializeField, HideInInspector] private List<string> serviceOrder = new();

        public bool ForceSceneRouting = true;

        [Header("Loading")]
        [Tooltip("앱 시작 시 Bootstrap → 다음 씬(MainMenu)으로 넘어갈 때도 로딩 화면을 보입니다. (그 외 씬 전환은 항상 보입니다)")]
        [SerializeField] private bool showLoadingOnBoot = false;

        [Tooltip("도착 씬별 로딩 화면을 지정하지 않았을 때 사용하는 로딩 화면 (Assets/Project/Loading의 프리팹)")]
        [SerializeField] private LoadingCanvasBase defaultLoadingCanvas;

        [Tooltip("MainMenu로 갈 때(앱 시작, 세션 퇴장)의 로딩 화면. 비우면 Default")]
        [SerializeField] private LoadingCanvasBase mainMenuLoadingCanvas;

        [Tooltip("Ready로 갈 때(방 만들기, 참가, 게임 종료)의 로딩 화면. 비우면 Default")]
        [SerializeField] private LoadingCanvasBase readyLoadingCanvas;

        [Tooltip("Game으로 갈 때(게임 시작)의 로딩 화면. 비우면 Default")]
        [SerializeField] private LoadingCanvasBase gameLoadingCanvas;

        [Tooltip("로딩 화면 최대 표시 시간(초). 엔진 안전장치: 매니저가 로딩을 놓지 못해도 이 시간이 지나면 경고 후 숨깁니다.\n(최소 표시 시간과 나타남/사라짐 연출은 각 로딩 화면 프리팹에서 정합니다)")]
        [SerializeField, Min(1f)] private float loadingTimeoutSeconds = 30f;

        public string BootstrapSceneName => bootstrapSceneName;
        public string NextSceneName => nextSceneName;
        public IReadOnlyList<string> ServiceOrder => serviceOrder;

        /// <summary> 앱 시작(Bootstrap → 다음 씬) 때 로딩 화면을 보일지 </summary>
        public bool ShowLoadingOnBoot => showLoadingOnBoot;

        /// <summary> 로딩 화면 최대 표시 시간(초) </summary>
        public float LoadingTimeoutSeconds => loadingTimeoutSeconds;

        /// <summary> 지정된 모든 로딩 화면 프리팹 (중복, 빈 칸 제외). 게임 시작 시 미리 생성하는 데 사용합니다. </summary>
        public IEnumerable<LoadingCanvasBase> LoadingCanvasPrefabs
        {
            get
            {
                var prefabs = new List<LoadingCanvasBase>();

                foreach (var prefab in new[] { defaultLoadingCanvas, mainMenuLoadingCanvas, readyLoadingCanvas, gameLoadingCanvas })
                {
                    if (prefab != null && !prefabs.Contains(prefab)) prefabs.Add(prefab);
                }

                return prefabs;
            }
        }

        /// <summary> 도착 씬에 맞는 로딩 화면 프리팹. 지정되지 않았으면 Default, 그것도 없으면 null </summary>
        public LoadingCanvasBase GetLoadingCanvasPrefab(string sceneName)
        {
            LoadingCanvasBase prefab = null;

            if (sceneName == nameof(Loader.Scene.MainMenu)) prefab = mainMenuLoadingCanvas;
            else if (sceneName == nameof(Loader.Scene.Ready)) prefab = readyLoadingCanvas;
            else if (sceneName == nameof(Loader.Scene.Game)) prefab = gameLoadingCanvas;

            return prefab != null ? prefab : defaultLoadingCanvas;
        }

        /// <summary> 해당 씬이 부트스트랩 씬인지 </summary>
        public bool IsBootstrapScene(string sceneName) => !string.IsNullOrEmpty(bootstrapSceneName) && sceneName == bootstrapSceneName;

#if UNITY_EDITOR
        /// <summary> [에디터] 부트스트랩 씬 에셋 (Settings 창에서 서비스 목록을 읽을 때 사용) </summary>
        public SceneAsset BootstrapSceneAsset => bootstrapScene;

        protected override void OnValidate()
        {
            base.OnValidate();

            if (bootstrapScene != null) bootstrapSceneName = bootstrapScene.name;
            else bootstrapSceneName = string.Empty;

            if (nextScene != null) nextSceneName = nextScene.name;
            else nextSceneName = string.Empty;
        }
#endif
    }
}