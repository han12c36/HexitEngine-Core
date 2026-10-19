using Steamworks;
using System;

namespace HexitEngine
{
    public static class SteamAuthService
    {
        /// <summary>
        /// 테스트 모드인지 (HEXIT_TEST_MODE 디파인 기준). Settings > Build의 Mode를 따릅니다.
        /// 테스트 모드면 테스트 App ID를 쓰고 출시용 코드(RestartAppIfNecessary)가 컴파일에서 빠집니다.
        /// </summary>
        public static bool IsTestMode =>
#if HEXIT_TEST_MODE
            true;
#else
            false;
#endif

        /// <summary> 이 게임의 Steam App ID. Settings > Build에서 설정합니다. (Config가 없으면 테스트 App ID) </summary>
        public static uint AppId
        {
            get
            {
                var config = BuildConfig.Instance;
                return config != null ? config.EffectiveAppId : BuildConfig.TEST_APP_ID;
            }
        }

        /// <summary> 스팀 API가 정상적으로 초기화되었는지 여부 </summary>
        public static bool IsInitialized => SteamClient.IsValid;

        /// <summary>
        /// 유저가 스팀 서버에 정상적으로 연결되어 있는지 여부.
        /// (멀티플레이 버튼을 누를 때 이 값이 false라면 "인터넷 연결을 확인하세요" 팝업을 띄우면 됩니다)
        /// </summary>
        public static bool IsLoggedOn => SteamClient.IsValid && SteamClient.IsLoggedOn;

        /// <summary> 유저의 SteamID64. 초기화 전이면 0 </summary>
        public static ulong SteamId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0;

        /// <summary> 유저의 고유 스팀 번호 (SteamID64) </summary>
        public static string AuthId => SteamClient.IsValid ? SteamClient.SteamId.ToString() : string.Empty;

        /// <summary >유저의 스팀 프로필 닉네임 </summary>
        public static string Nickname => SteamClient.IsValid ? SteamClient.Name : "Unknown";

        /// <summary> 유저의 현재 스팀 접속 상태 (온라인, 자리 비움, 오프라인 등) </summary>
        public static FriendState UserState => SteamClient.IsValid ? SteamClient.State : FriendState.Offline;

        /// <summary> 현재 실행 중인 게임의 스팀 App ID </summary>
        public static uint CurrentAppId => SteamClient.IsValid ? SteamClient.AppId.Value : 0;



        /// <summary> [호출 시점] HEXITEngine.OnAwake에서 1회 호출. 부트스트랩이 다른 서비스를 초기화하기 전에 실행됩니다. </summary>
        public static void Initialize()
        {
            // 이미 초기화되어 있다면 중복 실행 방지
            if (IsInitialized) return;

            try { SteamClient.Shutdown(); }
            catch { }

            try
            {
                //[출시용 필수 코드] 불법 복제 방지 및 강제 스팀 연동
                //유저가 스팀을 통하지 않고 exe 파일을 직접 실행했을 경우 작동합니다.
#if !UNITY_EDITOR && !HEXIT_TEST_MODE
                // 출시 빌드 전용. 에디터와 테스트 모드(테스트 App ID로 부르면 다른 앱으로 재실행됨)에서는 컴파일에서 빠집니다.
                if (SteamClient.RestartAppIfNecessary(AppId))
                {
                    Dev.LogWarn("Non-Steam launch detected. Invoking Steam and terminating the application.");
                    UnityEngine.Application.Quit();
                    return;
                }
#endif

                SteamClient.Init(AppId);

                SteamNetworkingUtils.InitRelayNetworkAccess();

#if UNITY_EDITOR
                Debug();   // 초기화 완료 + 접속자 정보를 로그 하나로
#else
                Dev.Log($"[Steam Auth] 시스템 초기화 완료! (App ID {AppId}{(IsTestMode ? ", 테스트 모드" : "")})");
#endif
            }
            catch (Exception e)
            {
                Dev.LogError($"[Steam Auth] 스팀 초기화 실패. PC에 스팀이 켜져 있는지 확인하세요.\n오류: {e.Message}");
            }
        }

        /// <summary> [호출 시점] HEXITEngine.OnApplicationQuit에서 다른 정리가 끝난 뒤 호출 </summary>
        public static void Shutdown()
        {
            try
            {
                SteamClient.Shutdown();
                Dev.Log("스팀 연결 정상 종료 완료.");
            }
            catch (Exception e)
            {
                Dev.LogError($"스팀 연결 종료 중 오류 발생: {e.Message}");
            }
        }

#if UNITY_EDITOR
        /// <summary> 현재 로그인된 스팀 유저의 모든 상태를 콘솔에 출력하여 확인합니다. </summary>
        private static void Debug()
        {
            if (!IsInitialized)
            {
                Dev.LogError("스팀이 초기화되지 않아 유저 정보를 가져올 수 없습니다.");
                return;
            }

            Dev.Log(
                "Steam Auth Report\n" +
                $"- 닉네임: {Nickname}\n" +
                $"- 고유 ID: {AuthId}\n" +
                $"- 서버 연결: {(IsLoggedOn ? "온라인 상태 (멀티 가능)" : "오프라인 상태 (멀티 불가)")}\n" +
                $"- 프로필 상태: {UserState}\n" +
                $"- 현재 AppID: {CurrentAppId} ({(IsTestMode ? "테스트 모드" : "출시 모드")})");
        }
#endif
    }
}