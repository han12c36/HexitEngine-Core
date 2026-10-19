using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace HexitEngine
{
    public class LocalizationService : LocalSingleton<LocalizationService>
    {
        private const string LANGUAGE_CODE_KEY = "LanguageCode";
        private const string FALLBACK_LANGUAGE_CODE = "en";

        protected override async UniTask OnInitializeAsync(CancellationToken ct)
        {
            await LocalizationSettings.InitializationOperation.Task.AsUniTask().AttachExternalCancellation(ct);

            // 저장된 언어가 있으면 적용
            string savedCode = PlayerPrefs.GetString(LANGUAGE_CODE_KEY, string.Empty);

            if (!string.IsNullOrEmpty(savedCode)) TryApplyLanguage(savedCode, save: false);

            // 그래도 선택된 언어가 없으면 첫 번째 언어로 강제 지정
            if (LocalizationSettings.SelectedLocale == null && LocalizationSettings.AvailableLocales.Locales.Count > 0)
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales[0];
            }
        }

        /// <summary> 언어를 변경하고 저장합니다. </summary>
        public void SetLanguage(string languageCode)
        {
            if (!IsInitialized)
            {
                Dev.LogWarn("[Localization] 아직 초기화되지 않았습니다.");
                return;
            }

            TryApplyLanguage(languageCode, save: true);
        }

        /// <summary> 현재 언어 코드. 초기화 전이면 기본값을 반환합니다. </summary>
        public string GetCurrentLanguageCode()
        {
            return LocalizationSettings.SelectedLocale?.Identifier.Code ?? FALLBACK_LANGUAGE_CODE;
        }

        /// <summary> 번역 문자열. 초기화 전이면 key를 그대로 반환합니다. </summary>
        public string GetLocalizedString(string table, string key, params object[] arguments)
        {
            if (!IsInitialized || LocalizationSettings.SelectedLocale == null) return key;

            if (arguments != null && arguments.Length > 0)
            {
                return LocalizationSettings.StringDatabase.GetLocalizedString(table, key, arguments: arguments);
            }

            return LocalizationSettings.StringDatabase.GetLocalizedString(table, key);
        }

        private bool TryApplyLanguage(string languageCode, bool save)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;

            var target = LocalizationSettings.AvailableLocales.Locales.Find(l =>
                l.Identifier.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Dev.LogWarn($"[Localization] '{languageCode}' 언어를 찾을 수 없습니다.");
                return false;
            }

            LocalizationSettings.SelectedLocale = target;

            if (save)
            {
                PlayerPrefs.SetString(LANGUAGE_CODE_KEY, languageCode);
                PlayerPrefs.Save();
            }

            Dev.Log($"[Localization] 언어 변경: {languageCode}");
            return true;
        }
    }
}