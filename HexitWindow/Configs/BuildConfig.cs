using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HexitEngine
{
    /// <summary>
    /// 빌드와 Steam 연동 설정. Settings > Build 탭에서 관리합니다.
    ///
    /// - Mode: Test / Release. Build 탭의 Mode에서 고르고 Apply로 적용합니다.
    ///   Test    : 코드에 고정된 Steam 테스트 App ID를 쓰고, 출시용 코드(RestartAppIfNecessary)를 잠급니다.
    ///   Release : Steam에서 발급받은 App ID(Release App ID)를 씁니다.
    ///   잠금은 디파인 심볼(HEXIT_TEST_MODE)로 하며, Apply 시 Build 탭이 자동으로 넣고 뺍니다.
    /// - NetworkManager Prefab: FacepunchTransport가 붙은 NetworkManager 프리팹. Apply 시 App ID가 자동으로 맞춰집니다.
    ///
    /// SteamAuthService와 FacepunchTransport 모두 EffectiveAppId 하나만 따릅니다.
    /// </summary>
    public class BuildConfig : HexitEngineTapConfig<BuildConfig>
    {
        /// <summary> Steam 테스트용 App ID. 모든 개발자가 함께 쓰는 공용 App ID이며, 기본값으로도 쓰입니다. </summary>
        public const uint TEST_APP_ID = 480;

        // Mode 영역에서 직접 그리므로 일반 속성 목록에서는 숨김
        [FormerlySerializedAs("steamAppId")]
        [SerializeField, HideInInspector] private uint releaseAppId = TEST_APP_ID;

        [SerializeField, HideInInspector] private bool testMode = true;

#if UNITY_EDITOR
        [Tooltip("FacepunchTransport가 붙은 NetworkManager 프리팹. Apply 시 App ID가 자동으로 적용됩니다.")]
        [SerializeField] private GameObject networkManagerPrefab;
#endif

        /// <summary> Steam에서 발급받은 출시용 App ID. 아직 입력하지 않았으면 테스트 App ID </summary>
        public uint ReleaseAppId => releaseAppId;

        public bool TestMode => testMode;

        /// <summary> 출시용 App ID가 입력되었는지 (기본값인 테스트 App ID가 아닌지) </summary>
        public bool HasReleaseAppId => IsReleaseAppId(releaseAppId);

        /// <summary> 실제로 쓰는 App ID. 테스트 모드면 테스트 App ID, 아니면 출시용 </summary>
        public uint EffectiveAppId => testMode ? TEST_APP_ID : releaseAppId;

        /// <summary> 출시용으로 쓸 수 있는 App ID인지 (0이나 테스트 App ID가 아닌지) </summary>
        public static bool IsReleaseAppId(uint appId) => appId != 0 && appId != TEST_APP_ID;

        /// <summary> 표시용 App ID. 테스트 App ID는 숫자 대신 이름으로 보여줍니다. </summary>
        public static string DescribeAppId(long appId) => appId == TEST_APP_ID ? "테스트 App ID" : appId.ToString();

#if UNITY_EDITOR
        /// <summary> [에디터] NetworkManager 프리팹 </summary>
        public GameObject NetworkManagerPrefab => networkManagerPrefab;

        /// <summary> [에디터] 모드와 출시용 App ID를 바꿉니다. (Build 탭 Apply에서 사용) </summary>
        public void SetMode(bool isTestMode, uint newReleaseAppId)
        {
            testMode = isTestMode;
            releaseAppId = newReleaseAppId;
            EditorUtility.SetDirty(this);
        }
#endif
    }
}