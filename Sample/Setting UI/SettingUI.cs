using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    /// <summary>
    /// [샘플] 설정 패널 (MainMenuUI의 settingUI). 사운드, 보이스, 언어 설정을 HexitEngine 서비스로 바꿉니다.
    ///
    /// 사운드 (SoundService)
    /// - 마스터/BGM/SFX 볼륨 0~10 단계. 5 = 원음, 0 = 음소거
    /// - 슬라이더를 움직이면 바로 적용되고, 패널을 닫을 때 UserSettingData(ES3 "UserSettings")에 저장됩니다.
    /// - SoundService는 부팅 시 같은 키에서 볼륨을 불러옵니다.
    ///
    /// 보이스 (VoiceService)
    /// - 마이크 장치, 감도(VAD 0~4), 다른 사람 목소리 볼륨(0~100), 마이크 테스트 미터
    /// - 엔진에 보이스 설정 저장 기능이 없어 이번 실행 동안만 유지됩니다.
    ///
    /// 언어 (LocalizationService)
    /// - Localization 설정에 등록된 언어 목록을 그대로 보여줍니다. 선택하면 엔진이 저장합니다.
    ///
    /// 모든 UI 슬롯은 선택입니다. 연결하지 않은 항목이나 해당 서비스가 없는 항목은 무시됩니다.
    /// </summary>
    public class SettingUI : MonoBehaviour
    {
        /// <summary> SoundService가 읽는 저장 키와 같아야 합니다. </summary>
        private const string SETTINGS_SAVE_KEY = "UserSettings";

        private const int VOLUME_LEVEL_MIN = 0;
        private const int VOLUME_LEVEL_MAX = 10;

        private const int VAD_LEVEL_MIN = 0;
        private const int VAD_LEVEL_MAX = 4;

        private const string DEFAULT_DEVICE_LABEL = "기본 장치";

        [Header("Sound")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private TMP_Text masterVolumeText;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private TMP_Text bgmVolumeText;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private TMP_Text sfxVolumeText;

        [Header("Voice")]
        [SerializeField] private TMP_Dropdown inputDeviceDropdown;
        [Tooltip("VAD 감도 (0~4)")]
        [SerializeField] private Slider micSensitivitySlider;
        [SerializeField] private TMP_Text micSensitivityText;
        [Tooltip("다른 사람 목소리 볼륨 (0~100)")]
        [SerializeField] private Slider voiceOutputSlider;
        [SerializeField] private TMP_Text voiceOutputText;
        [Tooltip("마이크 테스트 미터 (0~1). 비워두면 꺼짐")]
        [SerializeField] private Image micLevelFill;
        [Tooltip("미터 증폭 배율. 진폭은 보통 작게 나오므로 키워서 보여줍니다.")]
        [SerializeField] private float micLevelGain = 5f;
        [SerializeField] private GameObject voiceUnavailableNotice;

        [Header("Language")]
        [SerializeField] private TMP_Dropdown languageDropdown;

        [Header("Navigation")]
        [SerializeField] private Button backButton;
        [SerializeField] private MainMenuUI mainMenu;

        private readonly List<string> inputDevices = new();
        private readonly List<string> languageCodes = new();

        private SoundService sound;
        private VoiceService voice;
        private LocalizationService localization;

        private UserSettingData settings;
        private bool isSoundDirty;

        #region Lifecycle

        private void Awake()
        {
            SetupSlider(masterVolumeSlider, VOLUME_LEVEL_MIN, VOLUME_LEVEL_MAX, HandleMasterVolumeChanged);
            SetupSlider(bgmVolumeSlider, VOLUME_LEVEL_MIN, VOLUME_LEVEL_MAX, HandleBgmVolumeChanged);
            SetupSlider(sfxVolumeSlider, VOLUME_LEVEL_MIN, VOLUME_LEVEL_MAX, HandleSfxVolumeChanged);
            SetupSlider(micSensitivitySlider, VAD_LEVEL_MIN, VAD_LEVEL_MAX, HandleMicSensitivityChanged);
            SetupSlider(voiceOutputSlider, 0, 100, HandleVoiceOutputChanged);

            if (inputDeviceDropdown != null) inputDeviceDropdown.onValueChanged.AddListener(HandleInputDeviceChanged);
            if (languageDropdown != null) languageDropdown.onValueChanged.AddListener(HandleLanguageChanged);
            if (backButton != null) backButton.onClick.AddListener(Back);
        }

        private void OnDestroy()
        {
            if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.RemoveListener(HandleMasterVolumeChanged);
            if (bgmVolumeSlider != null) bgmVolumeSlider.onValueChanged.RemoveListener(HandleBgmVolumeChanged);
            if (sfxVolumeSlider != null) sfxVolumeSlider.onValueChanged.RemoveListener(HandleSfxVolumeChanged);
            if (micSensitivitySlider != null) micSensitivitySlider.onValueChanged.RemoveListener(HandleMicSensitivityChanged);
            if (voiceOutputSlider != null) voiceOutputSlider.onValueChanged.RemoveListener(HandleVoiceOutputChanged);

            if (inputDeviceDropdown != null) inputDeviceDropdown.onValueChanged.RemoveListener(HandleInputDeviceChanged);
            if (languageDropdown != null) languageDropdown.onValueChanged.RemoveListener(HandleLanguageChanged);
            if (backButton != null) backButton.onClick.RemoveListener(Back);
        }

        /// <summary> 패널이 열릴 때마다 현재 값으로 UI를 채웁니다. </summary>
        private void OnEnable()
        {
            sound = SoundService.Singleton;
            voice = VoiceService.Singleton;
            localization = LocalizationService.Singleton;

            LoadSoundSettings();
            LoadVoiceSettings();
            LoadLanguages();
        }

        /// <summary> 닫힐 때(뒤로가기, ESC, 씬 전환) 사운드 설정을 저장합니다. </summary>
        private void OnDisable()
        {
            SaveSoundSettings();
        }

        private void Update()
        {
            UpdateMicLevel();
        }

        #endregion

        #region Sound

        private void LoadSoundSettings()
        {
            try
            {
                settings = ES3.Load(SETTINGS_SAVE_KEY, new UserSettingData());
            }
            catch (Exception e)
            {
                Dev.LogWarn($"유저 설정을 불러오지 못해 기본값을 사용합니다: {e.Message}");
                settings = new UserSettingData();
            }

            isSoundDirty = false;

            bool available = sound != null;

            SetSliderWithoutNotify(masterVolumeSlider, settings.masterVolumeIndex, available);
            SetSliderWithoutNotify(bgmVolumeSlider, settings.bgmVolumeIndex, available);
            SetSliderWithoutNotify(sfxVolumeSlider, settings.sfxVolumeIndex, available);

            SetText(masterVolumeText, settings.masterVolumeIndex.ToString());
            SetText(bgmVolumeText, settings.bgmVolumeIndex.ToString());
            SetText(sfxVolumeText, settings.sfxVolumeIndex.ToString());
        }

        private void SaveSoundSettings()
        {
            if (!isSoundDirty || settings == null) return;

            try
            {
                ES3.Save(SETTINGS_SAVE_KEY, settings);
                isSoundDirty = false;
                Dev.Log("사운드 설정 저장");
            }
            catch (Exception e)
            {
                Dev.LogError($"사운드 설정 저장 실패: {e.Message}");
            }
        }

        private void HandleMasterVolumeChanged(float value)
        {
            int level = ToLevel(value);

            settings.masterVolumeIndex = level;
            sound?.SetMasterVolume(level);

            OnSoundChanged(masterVolumeText, level);
        }

        private void HandleBgmVolumeChanged(float value)
        {
            int level = ToLevel(value);

            settings.bgmVolumeIndex = level;
            sound?.SetBgmVolume(level);

            OnSoundChanged(bgmVolumeText, level);
        }

        private void HandleSfxVolumeChanged(float value)
        {
            int level = ToLevel(value);

            settings.sfxVolumeIndex = level;
            sound?.SetSfxVolume(level);

            OnSoundChanged(sfxVolumeText, level);
        }

        private void OnSoundChanged(TMP_Text label, int level)
        {
            isSoundDirty = true;
            SetText(label, level.ToString());
        }

        #endregion

        #region Voice

        private void LoadVoiceSettings()
        {
            bool available = voice != null && voice.IsVoiceAvailable;

            if (voiceUnavailableNotice != null) voiceUnavailableNotice.SetActive(!available);

            if (inputDeviceDropdown != null) inputDeviceDropdown.interactable = available;
            if (micSensitivitySlider != null) micSensitivitySlider.interactable = available;
            if (voiceOutputSlider != null) voiceOutputSlider.interactable = available;
            if (micLevelFill != null) micLevelFill.fillAmount = 0f;

            if (!available) return;

            LoadInputDevices();

            SetSliderWithoutNotify(micSensitivitySlider, voice.InputVolume, true);
            SetText(micSensitivityText, voice.InputVolume.ToString());

            SetSliderWithoutNotify(voiceOutputSlider, voice.OutputVolume, true);
            SetText(voiceOutputText, voice.OutputVolume.ToString());
        }

        /// <summary> 마이크 목록을 채웁니다. 첫 항목은 시스템 기본 장치(null)입니다. </summary>
        private void LoadInputDevices()
        {
            if (inputDeviceDropdown == null) return;

            inputDevices.Clear();
            inputDevices.Add(null);
            inputDevices.AddRange(voice.AvailableInputDevices);

            var options = new List<string> { DEFAULT_DEVICE_LABEL };
            for (int i = 1; i < inputDevices.Count; i++) options.Add(inputDevices[i]);

            inputDeviceDropdown.ClearOptions();
            inputDeviceDropdown.AddOptions(options);

            string current = voice.CurrentInputDevice;
            int index = string.IsNullOrEmpty(current) ? 0 : Mathf.Max(0, inputDevices.IndexOf(current));

            inputDeviceDropdown.SetValueWithoutNotify(index);
        }

        private void HandleInputDeviceChanged(int index)
        {
            if (voice == null || index < 0 || index >= inputDevices.Count) return;

            voice.SetInputDevice(inputDevices[index]);
            Dev.Log($"마이크 변경: {inputDevices[index] ?? DEFAULT_DEVICE_LABEL}");
        }

        private void HandleMicSensitivityChanged(float value)
        {
            if (voice == null) return;

            voice.InputVolume = Mathf.RoundToInt(value);
            SetText(micSensitivityText, voice.InputVolume.ToString());
        }

        private void HandleVoiceOutputChanged(float value)
        {
            if (voice == null) return;

            voice.OutputVolume = Mathf.RoundToInt(value);
            SetText(voiceOutputText, voice.OutputVolume.ToString());
        }

        /// <summary> 내 마이크 입력 크기를 미터로 보여줍니다. (방 밖에서도 동작) </summary>
        private void UpdateMicLevel()
        {
            if (micLevelFill == null || voice == null || !voice.IsVoiceAvailable) return;

            float target = Mathf.Clamp01(voice.GetLocalAudioEnergy() * micLevelGain);

            // 깜빡임 없이 부드럽게
            micLevelFill.fillAmount = Mathf.Lerp(micLevelFill.fillAmount, target, Time.unscaledDeltaTime * 15f);
        }

        #endregion

        #region Language

        /// <summary> Localization 설정에 등록된 언어로 드롭다운을 채웁니다. </summary>
        private void LoadLanguages()
        {
            if (languageDropdown == null) return;

            bool available = localization != null && localization.IsInitialized;
            languageDropdown.interactable = available;

            if (!available) return;

            languageCodes.Clear();
            var options = new List<string>();

            foreach (var locale in LocalizationSettings.AvailableLocales.Locales)
            {
                languageCodes.Add(locale.Identifier.Code);
                options.Add(GetLanguageLabel(locale.Identifier.CultureInfo, locale.LocaleName));
            }

            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(options);

            int index = languageCodes.FindIndex(code =>
                code.Equals(localization.GetCurrentLanguageCode(), StringComparison.OrdinalIgnoreCase));

            languageDropdown.SetValueWithoutNotify(Mathf.Max(0, index));
        }

        private void HandleLanguageChanged(int index)
        {
            if (localization == null || index < 0 || index >= languageCodes.Count) return;
            localization.SetLanguage(languageCodes[index]);
        }

        /// <summary> 언어 이름은 그 언어로 표시합니다. (예: 한국어, English, 日本語) </summary>
        private static string GetLanguageLabel(System.Globalization.CultureInfo culture, string fallback)
        {
            if (culture == null) return fallback;

            string name = culture.NativeName;
            return string.IsNullOrEmpty(name) ? fallback : char.ToUpper(name[0]) + name.Substring(1);
        }

        #endregion

        #region Navigation

        /// <summary> 메인메뉴로 돌아갑니다. 저장은 OnDisable에서 합니다. </summary>
        public void Back()
        {
            if (mainMenu != null) mainMenu.ShowMainMenu();
            else gameObject.SetActive(false);
        }

        #endregion

        #region Utils

        private static void SetupSlider(Slider slider, int min, int max, UnityEngine.Events.UnityAction<float> onChanged)
        {
            if (slider == null) return;

            slider.wholeNumbers = true;
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(onChanged);
        }

        private static void SetSliderWithoutNotify(Slider slider, int value, bool interactable)
        {
            if (slider == null) return;

            slider.SetValueWithoutNotify(value);
            slider.interactable = interactable;
        }

        private static int ToLevel(float value) => Mathf.Clamp(Mathf.RoundToInt(value), VOLUME_LEVEL_MIN, VOLUME_LEVEL_MAX);

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null) label.text = value;
        }

        #endregion
    }
}