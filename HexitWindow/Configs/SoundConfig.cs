using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HexitEngine
{
    /// <summary>
    /// SoundService 설정: SoundService가 실행 중에 참조하는 에셋과 SoundData 저장 폴더.
    ///
    /// - SoundLibrary: SoundService가 id로 SoundData를 찾는 라이브러리. 에셋 위치는 상관없습니다.
    /// - SFX Emitter: SFX 풀이 복제해서 쓰는 AudioSource 프리팹. (Output을 믹서의 SFX 그룹으로 지정해 두세요)
    /// - Audio Mixer: 볼륨 조절에 쓰는 믹서. Master / BGM / SFX / Mic Exposed Parameter가 있어야 합니다.
    /// - SFX / BGM Data Folder: Sound 탭에서 새 SoundData를 만들 때 저장할 폴더. 지정한 폴더에 그대로 저장합니다. (하위 폴더를 만들지 않음)
    ///   두 칸에 같은 폴더를 지정해도 됩니다. 폴더를 직접 참조(GUID)하므로 이름을 바꾸거나 옮겨도 유지되며, 코드에서는 경로 문자열로 씁니다.
    ///   실행 중인 게임은 폴더와 무관합니다. (SoundService는 SoundLibrary만 사용)
    /// </summary>
    public class SoundConfig : HexitEngineTapConfig<SoundConfig>
    {
        [Tooltip("SoundService가 사용할 SoundLibrary")]
        [SerializeField] private SoundLibrary soundLibrary;

        [Tooltip("SFX 풀이 복제해서 쓰는 AudioSource 프리팹")]
        [SerializeField] private AudioSource sfxEmitter;

        [Tooltip("볼륨 조절에 쓰는 AudioMixer (Master / BGM / SFX / Mic Exposed Parameter 필요)")]
        [SerializeField] private AudioMixer audioMixer;

#if UNITY_EDITOR
        [Tooltip("새 SFX SoundData를 저장할 폴더")]
        [SerializeField] private DefaultAsset sfxDataFolder;

        [Tooltip("새 BGM SoundData를 저장할 폴더")]
        [SerializeField] private DefaultAsset bgmDataFolder;
#endif
        [SerializeField, HideInInspector] private string sfxDataPath;
        [SerializeField, HideInInspector] private string bgmDataPath;

        public SoundLibrary SoundLibrary => soundLibrary;
        public AudioSource SfxEmitter => sfxEmitter;
        public AudioMixer AudioMixer => audioMixer;

        /// <summary> SFX SoundData 저장 폴더 경로. 지정되지 않았으면 빈 문자열 </summary>
        public string SfxDataPath => sfxDataPath;

        /// <summary> BGM SoundData 저장 폴더 경로. 지정되지 않았으면 빈 문자열 </summary>
        public string BgmDataPath => bgmDataPath;

        /// <summary> 타입에 맞는 SoundData 저장 폴더 경로 </summary>
        public string GetDataPath(SoundType type) => type == SoundType.BGM ? bgmDataPath : sfxDataPath;

#if UNITY_EDITOR
        /// <summary> [에디터] 참조된 SFX SoundData 폴더 </summary>
        public DefaultAsset SfxDataFolder => sfxDataFolder;

        /// <summary> [에디터] 참조된 BGM SoundData 폴더 </summary>
        public DefaultAsset BgmDataFolder => bgmDataFolder;

        /// <summary> [에디터] SFX / BGM SoundData 폴더를 지정합니다. (Setup에서 사용) </summary>
        public void SetDataFolders(DefaultAsset sfxFolder, DefaultAsset bgmFolder)
        {
            sfxDataFolder = sfxFolder;
            bgmDataFolder = bgmFolder;
            SyncDataPaths();
            EditorUtility.SetDirty(this);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            SyncDataPaths();
        }

        private void SyncDataPaths()
        {
            sfxDataPath = ResolveFolderPath(ref sfxDataFolder);
            bgmDataPath = ResolveFolderPath(ref bgmDataFolder);
        }

        /// <summary> 폴더 참조를 경로로 바꿉니다. 폴더가 아닌 에셋이면 참조를 비웁니다. </summary>
        private static string ResolveFolderPath(ref DefaultAsset folder)
        {
            if (folder == null) return string.Empty;

            string path = AssetDatabase.GetAssetPath(folder);

            // DefaultAsset은 폴더가 아닌 파일도 들어올 수 있으므로 폴더만 허용
            if (!AssetDatabase.IsValidFolder(path))
            {
                Dev.LogWarn($"폴더만 지정할 수 있습니다: {path}");
                folder = null;
                return string.Empty;
            }

            return path;
        }
#endif
    }
}