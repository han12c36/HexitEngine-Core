using System.IO;
using UnityEditor;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// Build 탭: Steam App ID를 한 곳(BuildConfig)에서 관리합니다.
    /// - Mode: Test / Release 중 하나를 고르고 Apply를 누르면 Config, NetworkManager 프리팹, 부트스트랩 씬에 배치된 FacepunchTransport, 테스트 모드 디파인이 함께 적용됩니다.
    /// - SteamAuthService는 실행 시 BuildConfig.EffectiveAppId를 읽으므로 따로 맞출 필요가 없습니다.
    /// - steam_appid.txt는 상태만 표시합니다. (Facepunch는 txt 없이 동작)
    /// </summary>
    public class BuildTab : HexitEngineTab<BuildConfig>
    {
        private const int MODE_TEST = 0;
        private const int MODE_RELEASE = 1;

        private static readonly string[] MODE_LABELS = { "Test", "Release" };
        private static readonly Color MODIFIED_COLOR = new(1f, 0.8f, 0.3f);
        private static readonly Color APPLY_BUTTON_COLOR = new(0.55f, 0.9f, 0.55f);

        public override bool ShowCodeArea => false;

        private bool showMode = true;
        private bool showStatus = true;

        private GUIContent statusFoldoutContent;
        private GUIContent okIcon;
        private GUIContent failIcon;
        private GUIContent warnIcon;
        private GUIStyle modifiedLabelStyle;

        /// <summary> Apply 전 선택 값. Config가 바뀌면 다시 읽습니다. </summary>
        private BuildConfig pendingSource;
        private int pendingMode;
        private uint pendingReleaseAppId;

        /// <summary> 닫힌 부트스트랩 씬을 마지막으로 읽은 결과 (매 프레임 씬을 열지 않도록 저장해 둠) </summary>
        private SteamBuildSync.SceneStatus cachedSceneStatus = SteamBuildSync.SceneStatus.SceneClosed;
        private long cachedSceneAppId = -1;

        public BuildTab()
        {
            TabName = "Build";
            HeaderTitle = "Build Settings";
            Description = "Steam App ID를 한 곳에서 관리합니다. Mode에서 Test / Release를 고르고 Apply를 누르면 NetworkManager 프리팹, 부트스트랩 씬의 FacepunchTransport와 " +
                          "테스트 모드 디파인이 함께 맞춰지고, SteamAuthService는 실행 시 이 값을 읽습니다.";
        }

        public override void OnEnable()
        {
            base.OnEnable();
            statusFoldoutContent = new GUIContent("Status");
            okIcon = EditorGUIUtility.IconContent("TestPassed");
            failIcon = EditorGUIUtility.IconContent("TestFailed");
            warnIcon = EditorGUIUtility.IconContent("console.warnicon.sml");

            pendingSource = null;
            EditorApplication.delayCall += RefreshSceneCache;
        }

        public override void DrawContent()
        {
            modifiedLabelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = MODIFIED_COLOR } };

            DrawConfigSettings("Build Config Asset");

            GUILayout.Space(10);

            if (_config == null) return;

            if (pendingSource != _config) LoadPending();

            DrawConfigProperties("Settings");

            GUILayout.Space(10);

            DrawMode();

            GUILayout.Space(10);

            DrawStatus();
        }

        public override string GetCodeSnippet() => string.Empty;

        #region Mode

        private void LoadPending()
        {
            pendingSource = _config;
            pendingMode = _config.TestMode ? MODE_TEST : MODE_RELEASE;
            pendingReleaseAppId = _config.ReleaseAppId;
        }

        private bool IsPendingModified =>
            (pendingMode == MODE_TEST) != _config.TestMode || pendingReleaseAppId != _config.ReleaseAppId;

        private void DrawMode()
        {
            string current = _config.TestMode ? "Test" : "Release";
            showMode = DrawFoldoutBar(showMode, new GUIContent($"Mode  ({current})"));
            if (!showMode) return;

            BeginSection();

            pendingMode = GUILayout.Toolbar(pendingMode, MODE_LABELS, GUILayout.Height(24));

            GUILayout.Space(8);

            if (pendingMode == MODE_TEST) DrawTestMode();
            else DrawReleaseMode();

            GUILayout.Space(8);

            DrawApplyRow();

            EndSection();
        }

        private void DrawTestMode()
        {
            EditorGUILayout.HelpBox(
                "Steam 테스트용 App ID를 사용합니다. (코드에 고정되어 있어 입력할 필요가 없습니다)\n" +
                "출시용 코드(RestartAppIfNecessary)가 잠기므로 Steam을 거치지 않고 실행해도 됩니다.\n" +
                "출시 빌드 전에는 반드시 Release로 바꾸세요.",
                MessageType.Info);
        }

        private void DrawReleaseMode()
        {
            long input = EditorGUILayout.LongField(
                new GUIContent("Release App Id", "Steamworks에서 발급받은 이 게임의 App ID"),
                BuildConfig.IsReleaseAppId(pendingReleaseAppId) ? pendingReleaseAppId : 0);

            pendingReleaseAppId = (uint)Mathf.Clamp(input, 0, uint.MaxValue);

            GUILayout.Space(4);

            if (!BuildConfig.IsReleaseAppId(pendingReleaseAppId))
            {
                EditorGUILayout.HelpBox("Steamworks에서 발급받은 App ID를 입력하세요.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "발급받은 App ID를 사용합니다.\n" +
                    "빌드에서 Steam을 거치지 않고 실행하면 Steam으로 다시 실행합니다. (출시용 코드 활성)",
                    MessageType.Info);
            }
        }

        private void DrawApplyRow()
        {
            bool canApply = pendingMode == MODE_TEST || BuildConfig.IsReleaseAppId(pendingReleaseAppId);
            bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;

            GUILayout.BeginHorizontal();

            if (IsPendingModified) GUILayout.Label("● 적용 안 됨", modifiedLabelStyle);
            else if (busy) GUILayout.Label(EditorApplication.isCompiling ? "컴파일 중..." : "플레이 중에는 적용할 수 없습니다.", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!canApply || busy))
            {
                var previousColor = GUI.backgroundColor;
                if (IsPendingModified) GUI.backgroundColor = APPLY_BUTTON_COLOR;

                if (GUILayout.Button("Apply", GUILayout.Width(120), GUILayout.Height(24)))
                {
                    EditorApplication.delayCall += Apply;
                }

                GUI.backgroundColor = previousColor;
            }

            GUILayout.EndHorizontal();
        }

        /// <summary> 선택한 모드를 Config에 저장하고 프리팹 / 디파인을 맞춥니다. </summary>
        private void Apply()
        {
            if (_config == null) return;

            bool isTestMode = pendingMode == MODE_TEST;

            // 테스트 모드로 바꿀 때도 입력해 둔 출시용 App ID는 유지
            uint releaseAppId = BuildConfig.IsReleaseAppId(pendingReleaseAppId) ? pendingReleaseAppId : _config.ReleaseAppId;

            Undo.RecordObject(_config, "Apply Build Mode");
            _config.SetMode(isTestMode, releaseAppId);

            SteamBuildSync.SyncAll(_config);

            LoadPending();
            RefreshSceneCache();
            EditorWindow.focusedWindow?.ShowNotification(new GUIContent($"{(isTestMode ? "Test" : "Release")} 모드 적용"), 1.5f);
        }

        #endregion

        #region Status

        /// <summary> 부트스트랩 씬이 닫혀 있어도 잠깐 열어서 값을 읽어 둡니다. (탭을 열 때 / Apply 후) </summary>
        private void RefreshSceneCache()
        {
            var config = _config != null ? _config : SteamBuildSync.FindConfig();
            if (config == null) return;

            cachedSceneStatus = SteamBuildSync.CheckBootstrapScene(config, openIfClosed: true, out cachedSceneAppId);
        }

        private void DrawStatus()
        {
            showStatus = DrawFoldoutBar(showStatus, statusFoldoutContent);
            if (!showStatus) return;

            BeginSection();

            uint appId = _config.EffectiveAppId;

            DrawStatusRow(okIcon, "적용 모드", _config.TestMode ? "Test Mode" : "Release Mode");
            DrawStatusRow(okIcon, "적용 App ID", appId.ToString());

            DrawPrefabRow(appId);
            DrawSceneRow(appId);

            // SteamAuthService의 실제 코드 값을 그대로 읽어 표시
            uint authAppId = SteamAuthService.AppId;
            DrawStatusRow(authAppId == appId ? okIcon : failIcon, "SteamAuthService App ID", authAppId.ToString());

            DrawReleaseCodeRow();
            DrawAppIdFileRow();

            EndSection();
        }

        private void DrawPrefabRow(uint appId)
        {
            const string label = "NetworkManager Prefab";
            var status = SteamBuildSync.CheckTransport(_config, out long transportAppId);

            switch (status)
            {
                case SteamBuildSync.TransportStatus.Matched:
                case SteamBuildSync.TransportStatus.Mismatched:
                    DrawStatusRow(transportAppId == appId ? okIcon : failIcon, label, $"FacepunchTransport {transportAppId}");
                    break;
                case SteamBuildSync.TransportStatus.NoPrefab:
                    DrawStatusRow(failIcon, label, "프리팹 없음 (Settings에서 지정)");
                    break;
                case SteamBuildSync.TransportStatus.NotPrefabAsset:
                    DrawStatusRow(failIcon, label, "프리팹 에셋이 아님");
                    break;
                case SteamBuildSync.TransportStatus.NoTransport:
                    DrawStatusRow(failIcon, label, "FacepunchTransport 없음");
                    break;
                case SteamBuildSync.TransportStatus.NoProperty:
                    DrawStatusRow(failIcon, label, "steamAppId 필드 없음 (콘솔 확인)");
                    break;
            }
        }

        private void DrawSceneRow(uint appId)
        {
            const string label = "Bootstrap NetworkManager";

            // 씬이 열려 있으면 실시간으로, 닫혀 있으면 마지막으로 읽은 값으로 표시
            var status = SteamBuildSync.CheckBootstrapScene(_config, openIfClosed: false, out long sceneAppId);
            if (status == SteamBuildSync.SceneStatus.SceneClosed)
            {
                status = cachedSceneStatus;
                sceneAppId = cachedSceneAppId;
            }

            switch (status)
            {
                case SteamBuildSync.SceneStatus.Matched:
                case SteamBuildSync.SceneStatus.Mismatched:
                    DrawStatusRow(sceneAppId == appId ? okIcon : failIcon, label, $"FacepunchTransport {sceneAppId}");
                    break;
                case SteamBuildSync.SceneStatus.NoTransport:
                    DrawStatusRow(okIcon, label, "씬에 배치된 FacepunchTransport 없음");
                    break;
                case SteamBuildSync.SceneStatus.NoBootstrapScene:
                    DrawStatusRow(warnIcon, label, "부트스트랩 씬 없음 (Bootstrap 탭에서 지정)");
                    break;
                default:
                    DrawStatusRow(warnIcon, label, "확인 전");
                    break;
            }
        }

        /// <summary> SteamAuthService가 지금 컴파일된 상태 그대로 (HEXIT_TEST_MODE 반영 여부) </summary>
        private void DrawReleaseCodeRow()
        {
            const string label = "SteamAuthService 출시 코드";
            bool releaseCodeActive = !SteamAuthService.IsTestMode;
            bool expected = !_config.TestMode;

            if (releaseCodeActive == expected)
                DrawStatusRow(okIcon, label, releaseCodeActive ? "활성화" : "비활성화");
            else if (EditorApplication.isCompiling)
                DrawStatusRow(warnIcon, label, "컴파일 중...");
            else
                DrawStatusRow(failIcon, label, $"{(releaseCodeActive ? "활성화" : "비활성화")} (모드와 다름, Apply 필요)");
        }

        /// <summary> Test: 있어야 함 (없으면 생성 버튼) / Release: 없어야 함 (있으면 삭제 버튼) </summary>
        private void DrawAppIdFileRow()
        {
            string label = SteamBuildSync.STEAM_APPID_FILE;
            var status = SteamBuildSync.CheckAppIdFile(_config, out bool exists);

            GUIContent icon = status == SteamBuildSync.AppIdFileStatus.Ok ? okIcon : failIcon;
            string detail = status switch
            {
                SteamBuildSync.AppIdFileStatus.WrongContent => "있음 (내용이 테스트 App ID가 아님)",
                SteamBuildSync.AppIdFileStatus.ShouldNotExist => "있음 (Release Mode에서는 없어야 함)",
                _ => exists ? "있음" : "없음"
            };

            string buttonText = status switch
            {
                SteamBuildSync.AppIdFileStatus.Missing => "생성",
                SteamBuildSync.AppIdFileStatus.WrongContent => "수정",
                SteamBuildSync.AppIdFileStatus.ShouldNotExist => "삭제",
                _ => null
            };

            GUILayout.BeginHorizontal();
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);

            if (buttonText != null && GUILayout.Button(buttonText, EditorStyles.miniButton, GUILayout.Width(60)))
            {
                var config = _config;
                EditorApplication.delayCall += () => SteamBuildSync.SyncAppIdFile(config);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawStatusRow(GUIContent icon, string label, string detail)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        #endregion

        #region Layout

        private static void BeginSection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(18);
            GUILayout.BeginVertical();
            GUILayout.Space(5);
        }

        private static void EndSection()
        {
            GUILayout.Space(5);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        #endregion
    }
}