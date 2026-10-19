using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Pool;

namespace HexitEngine
{
    /// <summary>
    /// BGM / SFX 재생과 볼륨을 담당합니다.
    /// SoundLibrary, SFX Emitter, Audio Mixer는 SoundConfig에서 읽습니다. (Settings > Sound)
    /// BGM Source는 씬에 배치된 컴포넌트라 서비스에서 직접 참조합니다.
    /// </summary>
    public class SoundService : LocalSingleton<SoundService>
    {
        private const float BGM_FADE_DURATION = 3f;
        private const string SETTINGS_SAVE_KEY = "UserSettings";

        private const string MIXER_MASTER = "Master";
        private const string MIXER_BGM = "BGM";
        private const string MIXER_SFX = "SFX";
        private const string MIXER_VOICE = "Mic";

        private const int VOLUME_LEVEL_MIN = 0;
        private const int VOLUME_LEVEL_MAX = 10;
        private const float MUTE_DECIBEL = -80f;

        [Header("Audio References")]
        [SerializeField] private AudioSource bgmSource;

        [Header("SFX Pool")]
        [SerializeField] private int sfxPoolDefaultCapacity = 5;
        [SerializeField] private int sfxPoolMaxSize = 30;

        /// <summary> 초기화 시 SoundConfig에서 가져온 참조 </summary>
        private SoundLibrary soundLibrary;
        private AudioSource sfxEmitter;
        private AudioMixer audioMixer;

        private ObjectPool<AudioSource> sfxPool;

        /// <summary> 진행 중인 BGM 페이드 취소용 </summary>
        private CancellationTokenSource bgmFadeCts;

        #region Lifecycle

        /// <summary> SoundConfig 참조를 가져오고, SFX 풀을 생성, 한 프레임 대기 후 저장된 유저 설정의 볼륨을 믹서에 적용. </summary>
        protected override async UniTask OnInitializeAsync(CancellationToken ct)
        {
            // 0. SoundConfig 참조 (하나라도 비어 있으면 부팅 실패로 처리)
            LoadConfig();

            // 1. SFX 풀 생성
            sfxPool = new ObjectPool<AudioSource>(
                createFunc: () => Instantiate(sfxEmitter, transform),
                actionOnGet: source => source.gameObject.SetActive(true),
                actionOnRelease: source => source.gameObject.SetActive(false),
                actionOnDestroy: source => { if (source != null) Destroy(source.gameObject); },
                collectionCheck: false,
                defaultCapacity: sfxPoolDefaultCapacity,
                maxSize: sfxPoolMaxSize
            );

            // 2. 한 프레임 대기
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            // 3. 저장된 볼륨 적용
            ApplySavedVolumes();
        }

        protected override void OnDestroyed() { CancelBgmFade(); }

        /// <summary> SoundConfig에서 SoundLibrary / SFX Emitter / Audio Mixer를 가져옵니다. 비어 있으면 예외를 던집니다. </summary>
        private void LoadConfig()
        {
            var config = SoundConfig.Instance;
            if (config == null) throw new InvalidOperationException("SoundConfig를 찾을 수 없습니다.");

            soundLibrary = config.SoundLibrary;
            sfxEmitter = config.SfxEmitter;
            audioMixer = config.AudioMixer;

            if (soundLibrary == null) throw new InvalidOperationException("SoundConfig에 SoundLibrary가 지정되지 않았습니다.");
            if (sfxEmitter == null) throw new InvalidOperationException("SoundConfig에 SFX Emitter가 지정되지 않았습니다.");
            if (audioMixer == null) throw new InvalidOperationException("SoundConfig에 Audio Mixer가 지정되지 않았습니다.");
            if (bgmSource == null) throw new InvalidOperationException("SoundService에 BGM Source가 지정되지 않았습니다.");
        }

        #endregion

        #region SFX

        /// <summary> 위치와 무관한 2D 효과음 재생 </summary>
        public void PlaySfx2D(string sfxId) => PlaySfx(sfxId, null);

        /// <summary> 지정 위치에서 3D 효과음 재생 </summary>
        public void PlaySfx3D(string sfxId, Vector3 position) => PlaySfx(sfxId, position);

        /// <summary> position이 null이면 2D, 값이 있으면 해당 위치에서 3D로 재생합니다. </summary>
        private void PlaySfx(string sfxId, Vector3? position)
        {
            if (!IsInitialized)
            {
                Dev.LogWarn($"[Sound] 초기화 전 SFX 재생 요청 무시: '{sfxId}'");
                return;
            }

            if (!TryGetSound(sfxId, out var soundData)) return;

            AudioSource source = sfxPool.Get();
            source.clip = soundData.audioClip;
            source.volume = soundData.volume;
            source.pitch = ResolvePitch(soundData);
            source.loop = false;

            if (position.HasValue)
            {
                source.spatialBlend = 1f;
                source.transform.position = position.Value;
            }
            else
            {
                source.spatialBlend = 0f;
                source.transform.localPosition = Vector3.zero;
            }

            source.Play();

            ReleaseWhenDoneAsync(source, destroyCancellationToken).Forget();
        }

        /// <summary> 재생이 끝나면 풀에 반납. 서비스가 파괴되면 대기를 중단. </summary>
        private async UniTaskVoid ReleaseWhenDoneAsync(AudioSource source, CancellationToken ct)
        {
            try
            {
                await UniTask.WaitWhile(() => source != null && source.isPlaying, cancellationToken: ct);
            }
            catch (OperationCanceledException) { return; }

            if (source != null) sfxPool.Release(source);
        }

        #endregion

        #region BGM

        /// <summary> BGM을 즉시 재생. 진행 중인 페이드는 취소. </summary>
        public void PlayBgm(string bgmId, bool loop = true)
        {
            if (!TryGetSound(bgmId, out var soundData)) return;

            CancelBgmFade();
            SetupBgm(soundData, loop, soundData.volume);
            bgmSource.Play();
        }

        /// <summary> BGM을 볼륨 0에서 시작해 fadeDuration 동안 서서히 키움. </summary>
        public void PlayBgmFadeIn(string bgmId, bool loop = true, float fadeDuration = BGM_FADE_DURATION)
        {
            if (!TryGetSound(bgmId, out var soundData)) return;

            SetupBgm(soundData, loop, 0f);
            bgmSource.Play();

            StartBgmFade(0f, soundData.volume, fadeDuration, stopOnComplete: false);
        }

        /// <summary> BGM을 즉시 정지. </summary>
        public void StopBgm()
        {
            CancelBgmFade();

            if (bgmSource.isPlaying) bgmSource.Stop();
            bgmSource.volume = 0f;
        }

        /// <summary> BGM을 fadeDuration 동안 서서히 줄인 뒤 정지. </summary>
        public void StopBgmFadeOut(float fadeDuration = BGM_FADE_DURATION)
        {
            if (!bgmSource.isPlaying) return;

            StartBgmFade(bgmSource.volume, 0f, fadeDuration, stopOnComplete: true);
        }

        private void SetupBgm(SoundData soundData, bool loop, float volume)
        {
            bgmSource.clip = soundData.audioClip;
            bgmSource.pitch = ResolvePitch(soundData);
            bgmSource.loop = loop;
            bgmSource.volume = volume;
        }

        /// <summary> 기존 페이드를 취소하고 새 페이드를 시작. 서비스가 파괴되면 함께 취소. </summary>
        private void StartBgmFade(float from, float to, float duration, bool stopOnComplete)
        {
            CancelBgmFade();

            bgmFadeCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            FadeBgmAsync(from, to, duration, stopOnComplete, bgmFadeCts.Token).Forget();
        }

        private void CancelBgmFade()
        {
            if (bgmFadeCts == null) return;

            bgmFadeCts.Cancel();
            bgmFadeCts.Dispose();
            bgmFadeCts = null;
        }

        /// <summary> from → to로 볼륨을 보간합니다. duration이 0 이하면 즉시 적용. 일시정지 영향 없도록 unscaled 시간 사용. </summary>
        private async UniTaskVoid FadeBgmAsync(float from, float to, float duration, bool stopOnComplete, CancellationToken ct)
        {
            try
            {
                if (duration > 0f)
                {
                    float timer = 0f;

                    while (timer < duration)
                    {
                        timer += Time.unscaledDeltaTime;
                        bgmSource.volume = Mathf.Lerp(from, to, timer / duration);
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }

                bgmSource.volume = to;

                if (stopOnComplete) bgmSource.Stop();
            }
            catch (OperationCanceledException) { }
        }

        #endregion

        #region Volume

        public void SetMasterVolume(int level) => SetMixerVolume(MIXER_MASTER, level);
        public void SetBgmVolume(int level) => SetMixerVolume(MIXER_BGM, level);
        public void SetSfxVolume(int level) => SetMixerVolume(MIXER_SFX, level);
        public void SetVoiceVolume(int level) => SetMixerVolume(MIXER_VOICE, level);

        /// <summary> 저장된 유저 설정을 불러와 믹서에 적용합니다. 파일이 손상되었으면 기본값을 사용합니다. </summary>
        private void ApplySavedVolumes()
        {
            UserSettingData settings;

            try
            {
                settings = ES3.Load(SETTINGS_SAVE_KEY, new UserSettingData());
            }
            catch (Exception e)
            {
                Dev.LogWarn($"[Sound] 유저 설정을 불러오지 못해 기본값을 사용합니다: {e.Message}");
                settings = new UserSettingData();
            }

            SetMasterVolume(settings.masterVolumeIndex);
            SetBgmVolume(settings.bgmVolumeIndex);
            SetSfxVolume(settings.sfxVolumeIndex);
        }

        private void SetMixerVolume(string parameter, int level)
        {
            level = Mathf.Clamp(level, VOLUME_LEVEL_MIN, VOLUME_LEVEL_MAX);

            if (!audioMixer.SetFloat(parameter, LevelToDecibel(level)))
            {
                Dev.LogWarn($"[Sound] 믹서에 '{parameter}' Exposed Parameter가 없습니다.");
            }
        }

        /// <summary>
        /// 볼륨 단계를 데시벨로 변환.
        /// level 5 = 0dB(원음), level 10 = 약 +6dB, level 0 = 음소거(-80dB)
        /// </summary>
        private static float LevelToDecibel(int level)
        {
            float linearVolume = level / 5f;

            if (linearVolume <= 0.0001f) return MUTE_DECIBEL;

            return Mathf.Log10(linearVolume) * 20f;
        }

        #endregion

        #region Helpers

        /// <summary> 사운드 데이터를 찾고, 클립이 비어 있으면 실패 처리. </summary>
        private bool TryGetSound(string soundId, out SoundData soundData)
        {
            soundData = soundLibrary != null ? soundLibrary.GetSoundData(soundId) : null;

            if (soundData == null || soundData.audioClip == null)
            {
                Dev.LogWarn($"[Sound] 사운드를 찾을 수 없거나 클립이 비어 있습니다: '{soundId}'");
                return false;
            }

            return true;
        }

        /// <summary> 랜덤 피치 설정이면 범위 내 랜덤 값, 아니면 고정 피치를 반환합니다. </summary>
        private static float ResolvePitch(SoundData soundData)
        {
            return soundData.isRandomPitch
                ? UnityEngine.Random.Range(soundData.minPitch, soundData.maxPitch)
                : soundData.pitch;
        }

        #endregion
    }
}