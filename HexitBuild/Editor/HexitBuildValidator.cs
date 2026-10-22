using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace HexitEngine
{
    /// <summary>
    /// [에디터] 빌드 전후 검사. (PC 빌드만)
    ///
    /// 빌드 전
    /// - Release 모드인데 발급받은 App ID가 없으면 빌드를 중단합니다.
    /// - 디파인 심볼이 Mode와 다르면 맞춘 뒤 빌드를 중단합니다. (컴파일이 끝나야 반영되므로, 끝나면 다시 빌드)
    /// - NetworkManager 프리팹의 App ID가 다르면 자동으로 맞춥니다.
    /// - 부트스트랩 씬에 배치된 FacepunchTransport가 App ID를 덮어쓰고 있으면 되돌립니다.
    /// - 테스트 모드로 빌드하면 경고합니다.
    ///
    /// 빌드 후
    /// - 출시 모드인데 exe 옆에 steam_appid.txt가 있으면 경고합니다. (있으면 Steam 실행 확인이 꺼짐)
    /// </summary>
    public class HexitBuildValidator : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            var config = SteamBuildSync.FindConfig();

            if (config == null)
            {
                Dev.LogWarn("BuildConfig를 찾을 수 없어 테스트 App ID로 빌드합니다.");
                return;
            }

            // 0. 출시 모드인데 발급받은 App ID가 없으면 중단
            if (!config.TestMode && !config.HasReleaseAppId)
            {
                throw new BuildFailedException("Release 모드인데 Release App Id가 입력되지 않았습니다. Settings > Build의 Mode에서 입력 후 Apply하세요.");
            }

            // 1. 디파인 (출시용 코드 잠금 여부가 여기서 결정됨)
            if (!SteamBuildSync.IsDefineSynced(config))
            {
                SteamBuildSync.SyncDefine(config.TestMode);
                throw new BuildFailedException(
                    $"Mode({(config.TestMode ? "Test" : "Release")})와 디파인 심볼이 달라 맞췄습니다. 컴파일이 끝난 뒤 다시 빌드하세요.");
            }

            // 2. NetworkManager 프리팹
            var status = SteamBuildSync.CheckTransport(config, out long transportAppId);

            switch (status)
            {
                case SteamBuildSync.TransportStatus.Matched:
                    break;

                case SteamBuildSync.TransportStatus.Mismatched:
                    SteamBuildSync.ApplyTransport(config);
                    Dev.Log($"FacepunchTransport App ID를 {BuildConfig.DescribeAppId(transportAppId)} → {BuildConfig.DescribeAppId(config.EffectiveAppId)}로 맞췄습니다.");
                    break;

                case SteamBuildSync.TransportStatus.NoPrefab:
                    Dev.LogWarn("Build 탭에 NetworkManager 프리팹이 지정되지 않아 FacepunchTransport App ID를 확인하지 못했습니다.");
                    break;

                default:
                    throw new BuildFailedException($"FacepunchTransport App ID를 확인할 수 없습니다. ({status}) Settings > Build를 확인하세요.");
            }

            // 3. 부트스트랩 씬 (배치된 인스턴스가 프리팹 값을 덮어쓰고 있으면 되돌림)
            switch (SteamBuildSync.ApplyBootstrapScene(config))
            {
                case SteamBuildSync.SceneStatus.NeedsSave:
                    throw new BuildFailedException("부트스트랩 씬의 FacepunchTransport App ID를 맞췄지만 씬에 저장되지 않은 다른 변경이 있습니다. 씬을 저장한 뒤 다시 빌드하세요.");

                case SteamBuildSync.SceneStatus.NoBootstrapScene:
                    Dev.LogWarn("부트스트랩 씬이 지정되지 않아 씬의 FacepunchTransport App ID를 확인하지 못했습니다.");
                    break;
            }

            // 4. 모드 알림
            if (config.TestMode)
                Dev.LogWarn("테스트 빌드입니다. (테스트 App ID, Steam 실행 확인 잠김) 출시 빌드라면 Settings > Build의 Mode를 Release로 바꾸고 Apply하세요.");
            else
                Dev.Log($"출시 빌드: App ID {config.EffectiveAppId}");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            var config = SteamBuildSync.FindConfig();
            if (config == null || config.TestMode) return;

            string buildDirectory = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(buildDirectory)) return;

            if (SteamBuildSync.TryReadAppIdFile(buildDirectory, out _))
            {
                Dev.LogWarn($"출시 빌드 폴더에 {SteamBuildSync.STEAM_APPID_FILE}가 있습니다. 이 파일이 있으면 Steam 실행 확인이 꺼지므로 배포 전에 삭제하세요.\n{buildDirectory}");
            }
        }
    }
}