using System.IO;
using System.Linq;
using Netcode.Transports.Facepunch;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace HexitEngine
{
    /// <summary>
    /// [에디터] BuildConfig 값을 프로젝트에 맞춥니다.
    /// 
    /// - 디파인 심볼: Mode(Test / Release)에 맞춰 HEXIT_TEST_MODE를 Standalone에 넣고 뺍니다. (에디터가 켜질 때도 자동 확인)
    /// - NetworkManager 프리팹: FacepunchTransport의 steamAppId를 EffectiveAppId로 맞춥니다.
    /// - 부트스트랩 씬: 배치된 FacepunchTransport의 steamAppId 오버라이드를 되돌려 프리팹 값을 따르게 합니다.
    ///   (씬에 배치된 프리팹 인스턴스가 값을 덮어쓰고 있으면 프리팹을 바꿔도 적용되지 않기 때문)
    /// - steam_appid.txt: Test면 프로젝트 루트에 만들고(테스트 App ID), Release면 지웁니다.
    /// </summary>
    [InitializeOnLoad]
    public static class SteamBuildSync
    {
        /// <summary> 테스트 모드 디파인 심볼. 있으면 출시용 코드가 컴파일에서 빠집니다. </summary>
        public const string TEST_MODE_DEFINE = "HEXIT_TEST_MODE";

        /// <summary> FacepunchTransport의 App ID 직렬화 필드 이름 </summary>
        private const string TRANSPORT_APP_ID_PROPERTY = "steamAppId";

        public const string STEAM_APPID_FILE = "steam_appid.txt";

        public enum TransportStatus { NoPrefab, NotPrefabAsset, NoTransport, NoProperty, Matched, Mismatched }

        public enum SceneStatus
        {
            /// <summary> BootstrapConfig에 부트스트랩 씬이 없음 </summary>
            NoBootstrapScene,
            /// <summary> 씬이 닫혀 있어 확인하지 않음 </summary>
            SceneClosed,
            /// <summary> 씬에 FacepunchTransport가 없음 (프리팹으로만 생성하는 경우) </summary>
            NoTransport,
            Matched,
            Mismatched,
            /// <summary> 적용했지만 씬에 다른 변경이 있어 저장하지 않음 </summary>
            NeedsSave
        }

        /// <summary> Steam 빌드 대상 </summary>
        private static NamedBuildTarget Target => NamedBuildTarget.Standalone;

        static SteamBuildSync()
        {
            // 새 프로젝트로 옮겨도 Config 기준으로 디파인이 맞춰지도록 (AssetDatabase 준비 후 실행)
            EditorApplication.delayCall += () =>
            {
                var config = FindConfig();
                if (config != null) SyncDefine(config.TestMode);
            };
        }

        #region Config

        /// <summary> 프로젝트의 BuildConfig를 찾습니다. (Instance가 아직 로드되지 않은 경우 대비) </summary>
        public static BuildConfig FindConfig()
        {
            var instance = BuildConfig.Instance;
            if (instance != null) return instance;

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(BuildConfig)}");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<BuildConfig>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        /// <summary> 프리팹, 부트스트랩 씬, steam_appid.txt, 디파인을 모두 맞춥니다. </summary>
        public static void SyncAll(BuildConfig config)
        {
            if (config == null) return;

            // 플레이 중에 디파인이 바뀌면 다시 컴파일되며 플레이가 깨지므로 건너뜀 (Status에 불일치로 표시됨)
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Dev.LogWarn("플레이 중에는 적용하지 않습니다. 플레이를 끝낸 뒤 Build 탭 Mode의 Apply를 누르세요.");
                return;
            }

            // 디파인 변경으로 다시 컴파일되기 전에 Config 저장
            AssetDatabase.SaveAssetIfDirty(config);

            ApplyTransport(config);
            ApplyBootstrapScene(config);
            SyncAppIdFile(config);
            SyncDefine(config.TestMode);
        }

        #endregion

        #region Define

        public static bool HasTestDefine()
        {
            PlayerSettings.GetScriptingDefineSymbols(Target, out string[] defines);
            return defines.Contains(TEST_MODE_DEFINE);
        }

        /// <summary> 디파인이 Mode와 일치하는지 </summary>
        public static bool IsDefineSynced(BuildConfig config) => config != null && HasTestDefine() == config.TestMode;

        /// <summary> Mode에 맞춰 디파인을 넣거나 뺍니다. 바뀌면 다시 컴파일됩니다. </summary>
        public static void SyncDefine(bool testMode)
        {
            PlayerSettings.GetScriptingDefineSymbols(Target, out string[] defines);

            bool has = defines.Contains(TEST_MODE_DEFINE);
            if (has == testMode) return;

            var next = testMode
                ? defines.Append(TEST_MODE_DEFINE).ToArray()
                : defines.Where(define => define != TEST_MODE_DEFINE).ToArray();

            PlayerSettings.SetScriptingDefineSymbols(Target, next);
            Dev.Log($"{TEST_MODE_DEFINE} {(testMode ? "추가 (테스트 모드)" : "제거 (출시 모드)")}. 다시 컴파일합니다.");
        }

        #endregion

        #region Transport

        /// <summary> 프리팹의 FacepunchTransport App ID를 EffectiveAppId와 비교합니다. </summary>
        public static TransportStatus CheckTransport(BuildConfig config, out long transportAppId)
        {
            transportAppId = -1;

            if (!TryGetTransportProperty(config, out var serializedObject, out var property, out var failStatus)) return failStatus;

            transportAppId = property.longValue;
            serializedObject.Dispose();

            return transportAppId == config.EffectiveAppId ? TransportStatus.Matched : TransportStatus.Mismatched;
        }

        /// <summary> 프리팹의 FacepunchTransport App ID를 EffectiveAppId로 바꾸고 저장합니다. </summary>
        public static TransportStatus ApplyTransport(BuildConfig config)
        {
            if (!TryGetTransportProperty(config, out var serializedObject, out var property, out var failStatus)) return failStatus;

            uint appId = config.EffectiveAppId;

            if (property.longValue != appId)
            {
                long previous = property.longValue;
                property.longValue = appId;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(config.NetworkManagerPrefab);
                AssetDatabase.SaveAssetIfDirty(config.NetworkManagerPrefab);

                Dev.Log($"FacepunchTransport Steam App Id: {BuildConfig.DescribeAppId(previous)} → {BuildConfig.DescribeAppId(appId)}");
            }

            serializedObject.Dispose();
            return TransportStatus.Matched;
        }

        private static bool TryGetTransportProperty(BuildConfig config, out SerializedObject serializedObject, out SerializedProperty property, out TransportStatus failStatus)
        {
            serializedObject = null;
            property = null;
            failStatus = TransportStatus.NoPrefab;

            var prefab = config != null ? config.NetworkManagerPrefab : null;
            if (prefab == null) return false;

            // 씬의 오브젝트가 아닌 프리팹 에셋만 허용
            if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                failStatus = TransportStatus.NotPrefabAsset;
                return false;
            }

            var transport = prefab.GetComponentInChildren<FacepunchTransport>(true);
            if (transport == null)
            {
                failStatus = TransportStatus.NoTransport;
                return false;
            }

            serializedObject = new SerializedObject(transport);
            property = serializedObject.FindProperty(TRANSPORT_APP_ID_PROPERTY);

            if (property == null)
            {
                Dev.LogError($"FacepunchTransport에서 '{TRANSPORT_APP_ID_PROPERTY}' 필드를 찾을 수 없습니다. 사용 중인 Transport 버전의 필드 이름을 확인하세요.");
                serializedObject.Dispose();
                serializedObject = null;
                failStatus = TransportStatus.NoProperty;
                return false;
            }

            return true;
        }

        #endregion

        #region Bootstrap Scene

        /// <summary>
        /// 부트스트랩 씬의 FacepunchTransport App ID를 확인합니다.
        /// openIfClosed가 false면 씬이 열려 있을 때만 확인하고, 닫혀 있으면 SceneClosed를 돌려줍니다. (Status를 그릴 때마다 씬을 열지 않도록)
        /// openIfClosed가 true면 잠깐 열어서 읽고 저장 없이 닫습니다.
        /// </summary>
        public static SceneStatus CheckBootstrapScene(BuildConfig config, bool openIfClosed, out long sceneAppId)
        {
            sceneAppId = -1;

            if (!TryGetBootstrapScenePath(out string path)) return SceneStatus.NoBootstrapScene;

            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!openIfClosed || EditorApplication.isPlayingOrWillChangePlaymode) return SceneStatus.SceneClosed;

                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                var transports = FindTransports(scene);
                if (transports.Length == 0) return SceneStatus.NoTransport;

                foreach (var transport in transports)
                {
                    using var serializedObject = new SerializedObject(transport);
                    var property = serializedObject.FindProperty(TRANSPORT_APP_ID_PROPERTY);
                    if (property == null) continue;

                    sceneAppId = property.longValue;
                    if (sceneAppId != config.EffectiveAppId) return SceneStatus.Mismatched;
                }

                return SceneStatus.Matched;
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// 부트스트랩 씬의 FacepunchTransport App ID를 맞춥니다.
        /// - 프리팹 인스턴스면 steamAppId 오버라이드를 되돌려 프리팹 값을 따르게 합니다.
        /// - 프리팹을 풀어서(Unpack) 배치한 경우엔 값을 직접 씁니다.
        /// - 씬이 닫혀 있으면 열어서 적용 후 저장하고 닫습니다.
        /// - 열려 있는 씬은, 원래 변경이 없던 경우에만 저장합니다. (작업 중인 다른 변경까지 저장하지 않도록)
        /// </summary>
        public static SceneStatus ApplyBootstrapScene(BuildConfig config)
        {
            if (!TryGetBootstrapScenePath(out string path)) return SceneStatus.NoBootstrapScene;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return SceneStatus.SceneClosed;

            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                var transports = FindTransports(scene);
                if (transports.Length == 0) return SceneStatus.NoTransport;

                bool wasDirty = scene.isDirty;
                bool changed = false;
                uint appId = config.EffectiveAppId;

                foreach (var transport in transports)
                {
                    changed |= ApplyToSceneTransport(transport, appId);
                }

                if (!changed) return SceneStatus.Matched;

                EditorSceneManager.MarkSceneDirty(scene);

                if (openedHere || !wasDirty)
                {
                    EditorSceneManager.SaveScene(scene);
                    Dev.Log($"부트스트랩 씬 FacepunchTransport Steam App Id → {BuildConfig.DescribeAppId(appId)}");
                    return SceneStatus.Matched;
                }

                Dev.LogWarn($"부트스트랩 씬 FacepunchTransport Steam App Id → {BuildConfig.DescribeAppId(appId)}. 씬에 다른 변경이 있어 저장하지 않았습니다. 씬을 저장하세요.");
                return SceneStatus.NeedsSave;
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary> 씬 오브젝트 하나에 적용합니다. 바뀌었으면 true </summary>
        private static bool ApplyToSceneTransport(FacepunchTransport transport, uint appId)
        {
            using var serializedObject = new SerializedObject(transport);
            var property = serializedObject.FindProperty(TRANSPORT_APP_ID_PROPERTY);
            if (property == null) return false;

            bool changed = false;

            // 프리팹 인스턴스가 값을 덮어쓰고 있으면 되돌려서 프리팹 값을 따르게 함
            if (property.prefabOverride)
            {
                PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                serializedObject.Update();
                property = serializedObject.FindProperty(TRANSPORT_APP_ID_PROPERTY);
                changed = true;
            }

            // 프리팹을 풀어서 배치했거나 프리팹 값이 아직 다르면 직접 씀
            if (property.longValue != appId)
            {
                property.longValue = appId;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            return changed;
        }

        private static FacepunchTransport[] FindTransports(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FacepunchTransport>(true))
                .ToArray();
        }

        private static bool TryGetBootstrapScenePath(out string path)
        {
            path = null;

            var bootstrapConfig = BootstrapConfig.Instance;
            if (bootstrapConfig == null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{nameof(BootstrapConfig)}");
                if (guids.Length > 0) bootstrapConfig = AssetDatabase.LoadAssetAtPath<BootstrapConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (bootstrapConfig == null || bootstrapConfig.BootstrapSceneAsset == null) return false;

            path = AssetDatabase.GetAssetPath(bootstrapConfig.BootstrapSceneAsset);
            return !string.IsNullOrEmpty(path);
        }

        #endregion

        #region steam_appid.txt

        /// <summary> 프로젝트 루트의 steam_appid.txt 경로 (에디터 실행 시 Steam이 읽는 위치) </summary>
        public static string ProjectAppIdFilePath => Path.Combine(Directory.GetCurrentDirectory(), STEAM_APPID_FILE);

        /// <summary> 폴더에 steam_appid.txt가 있으면 내용을 읽습니다. </summary>
        public static bool TryReadAppIdFile(string directory, out string content)
        {
            content = string.Empty;

            string path = Path.Combine(directory, STEAM_APPID_FILE);
            if (!File.Exists(path)) return false;

            try { content = File.ReadAllText(path).Trim(); }
            catch { content = "(읽기 실패)"; }

            return true;
        }

        public enum AppIdFileStatus { Ok, Missing, WrongContent, ShouldNotExist }

        /// <summary>
        /// 프로젝트 루트의 steam_appid.txt 상태.
        /// Test    : 테스트 App ID가 적힌 파일이 있어야 함
        /// Release : 없어야 함 (있으면 Steam 실행 확인을 건너뜀)
        /// </summary>
        public static AppIdFileStatus CheckAppIdFile(BuildConfig config, out bool exists)
        {
            exists = TryReadAppIdFile(Path.GetDirectoryName(ProjectAppIdFilePath), out string content);

            if (config.TestMode)
            {
                if (!exists) return AppIdFileStatus.Missing;
                return content == BuildConfig.TEST_APP_ID.ToString() ? AppIdFileStatus.Ok : AppIdFileStatus.WrongContent;
            }

            return exists ? AppIdFileStatus.ShouldNotExist : AppIdFileStatus.Ok;
        }

        /// <summary> 모드에 맞춰 프로젝트 루트의 steam_appid.txt를 만들거나(Test) 지웁니다(Release). </summary>
        public static void SyncAppIdFile(BuildConfig config)
        {
            if (config == null) return;

            string path = ProjectAppIdFilePath;

            try
            {
                switch (CheckAppIdFile(config, out _))
                {
                    case AppIdFileStatus.Missing:
                    case AppIdFileStatus.WrongContent:
                        File.WriteAllText(path, BuildConfig.TEST_APP_ID.ToString());
                        Dev.Log($"{STEAM_APPID_FILE} 생성 (테스트 App ID)");
                        break;

                    case AppIdFileStatus.ShouldNotExist:
                        File.Delete(path);
                        Dev.Log($"{STEAM_APPID_FILE} 삭제 (Release Mode)");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Dev.LogError($"{STEAM_APPID_FILE} 처리 실패: {e.Message}\n{path}");
            }
        }

        #endregion
    }
}