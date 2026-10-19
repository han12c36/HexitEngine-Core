using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HexitEngine
{
    /// <summary>
    /// 부트스트랩 설정: 부트스트랩 씬, 다음 씬, 강제 라우팅, 서비스 초기화 순서.
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

        public string BootstrapSceneName => bootstrapSceneName;
        public string NextSceneName => nextSceneName;
        public IReadOnlyList<string> ServiceOrder => serviceOrder;

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