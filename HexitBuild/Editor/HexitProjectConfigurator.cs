#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace HexitEngine.EditorTools
{
    /// <summary>
    /// [에디터 전용] 프로젝트 설정 연결. HexitEngineSetupWindow의 Setup이 Core 설치 후 호출합니다. (여러 번 실행해도 안전)
    /// Setup 창은 패키지/Core 없이도 컴파일되어야 하므로 패키지 API를 직접 쓰지 못합니다. 그래서 패키지 API가 필요한 설정은 Core 안의 이 스크립트가 맡습니다.
    ///
    /// 1. Addressables: 설정이 없으면 생성, 플레이어 빌드 시 Addressables도 함께 빌드, 에디터 Play Mode는 Use Asset Database
    /// 2. Localization: Project의 Localization Settings를 활성 설정으로 지정, 모든 Locale("Locale" 라벨)과 문자열 테이블을 Addressables에 등록
    /// 3. TMP: Essential Resources가 없으면 가져오고, 기본 폰트(NotoSans)와 Fallback(JP → SC → TC → KR) 지정
    /// </summary>
    public static class HexitProjectConfigurator
    {
        /// <summary> Localization Settings를 찾을 때 우선하는 위치 (템플릿에서 복사된 Project) </summary>
        private const string PROJECT_LOCALIZATION_FOLDER = "Assets/Project/Localization";

        private const string DEFAULT_FONT_NAME = "NotoSans-Regular SDF";

        /// <summary> 한자는 위에 있는 폰트의 모양으로 그려지므로 JP → SC → TC 순, 한글은 KR에만 있어 맨 아래 </summary>
        private static readonly string[] FALLBACK_FONT_NAMES =
        {
            "NotoSansJP-Regular SDF",
            "NotoSansSC-Regular SDF",
            "NotoSansTC-Regular SDF",
            "NotoSansKR-Regular SDF",
        };

        private const string TMP_IMPORTING_KEY = "HexitEngine.Configurator.TmpImporting";

        /// <summary> Setup이 호출합니다. 단계마다 실패해도 다음 단계는 계속 진행합니다. </summary>
        public static void Run()
        {
            Dev.Log("프로젝트 설정 연결 시작 (Addressables / Localization / TMP)");

            TryStep("Addressables", ConfigureAddressables);
            TryStep("Localization", ConfigureLocalization);
            TryStep("TMP", ConfigureTmp);

            AssetDatabase.SaveAssets();
        }

        private static void TryStep(string name, System.Action step)
        {
            try { step(); }
            catch (System.Exception e) { Dev.LogError($"{name} 설정 중 오류: {e.Message}"); }
        }

        #region Addressables

        private static void ConfigureAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Dev.LogError("Addressables 설정을 만들지 못했습니다.");
                return;
            }

            // 빌드할 때 Addressables도 함께 빌드 (빠지면 빌드에서 문구가 키로 보임)
            if (settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            {
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
                EditorUtility.SetDirty(settings);
                Dev.Log("Addressables: Build Addressables on Player Build 켬");
            }

            // 에디터 Play Mode: Use Asset Database (빌드 없이 바로 사용, "No Locales" 방지)
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                var builder = settings.DataBuilders[i];
                if (builder == null || builder.GetType().Name != "BuildScriptFastMode") continue;

                if (settings.ActivePlayModeDataBuilderIndex != i)
                {
                    settings.ActivePlayModeDataBuilderIndex = i;
                    EditorUtility.SetDirty(settings);
                    Dev.Log("Addressables: Play Mode Script를 Use Asset Database로 지정");
                }
                break;
            }
        }

        #endregion

        #region Localization

        private static void ConfigureLocalization()
        {
            // 1. 활성 Localization Settings (Project 안의 것을 우선)
            var localizationSettings = FindAsset<LocalizationSettings>(PROJECT_LOCALIZATION_FOLDER);

            if (localizationSettings == null)
            {
                Dev.LogWarn($"Localization Settings가 없습니다. ({PROJECT_LOCALIZATION_FOLDER}) 템플릿을 확인하세요.");
            }
            else if (LocalizationEditorSettings.ActiveLocalizationSettings != localizationSettings)
            {
                LocalizationEditorSettings.ActiveLocalizationSettings = localizationSettings;
                Dev.Log($"Localization: 활성 설정 지정 → {AssetDatabase.GetAssetPath(localizationSettings)}");
            }

            // 2. Locale 등록 (Core의 기본 14개 + 게임이 Project에 추가한 Locale). "Locale" 라벨은 등록 과정에서 붙음
            int localeCount = 0;
            foreach (var locale in FindAllAssets<Locale>())
            {
                LocalizationEditorSettings.AddLocale(locale, false);
                localeCount++;
            }

            // 3. 문자열 테이블 등록 (Core의 System_Table, Project의 UI_Table 등)
            int tableCount = 0;
            foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
            {
                collection.RefreshAddressables();
                tableCount++;
            }

            Dev.Log($"Localization: Locale {localeCount}개, 문자열 테이블 {tableCount}개를 Addressables에 등록");
        }

        #endregion

        #region TMP

        private static void ConfigureTmp()
        {
            var tmpSettings = FindTmpSettings();

            if (tmpSettings != null)
            {
                ApplyFonts(tmpSettings);
                return;
            }

            // Essential Resources가 없음: 가져온 뒤(비동기) 폰트 지정
            if (SessionState.GetBool(TMP_IMPORTING_KEY, false)) return;
            SessionState.SetBool(TMP_IMPORTING_KEY, true);

            AssetDatabase.importPackageCompleted -= HandleTmpImported;
            AssetDatabase.importPackageCompleted += HandleTmpImported;
            AssetDatabase.importPackageFailed -= HandleTmpImportFailed;
            AssetDatabase.importPackageFailed += HandleTmpImportFailed;

            Dev.Log("TMP: Essential Resources 가져오는 중...");
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        private static void HandleTmpImported(string packageName)
        {
            EndTmpImport();

            // 가져온 에셋이 인식된 다음 프레임에 폰트 지정
            EditorApplication.delayCall += () =>
            {
                var tmpSettings = FindTmpSettings();

                if (tmpSettings != null) ApplyFonts(tmpSettings);
                else Dev.LogWarn("TMP: Essential Resources를 가져왔지만 TMP Settings를 찾지 못했습니다. Setup을 다시 눌러 주세요.");
            };
        }

        private static void HandleTmpImportFailed(string packageName, string errorMessage)
        {
            EndTmpImport();
            Dev.LogError($"TMP: Essential Resources 가져오기 실패: {errorMessage}");
        }

        private static void EndTmpImport()
        {
            SessionState.SetBool(TMP_IMPORTING_KEY, false);
            AssetDatabase.importPackageCompleted -= HandleTmpImported;
            AssetDatabase.importPackageFailed -= HandleTmpImportFailed;
        }

        /// <summary> TMP Settings에 기본 폰트와 Fallback을 지정합니다. 폰트는 경로가 아니라 에셋 이름으로 찾습니다. </summary>
        private static void ApplyFonts(TMP_Settings tmpSettings)
        {
            var defaultFont = FindFontByName(DEFAULT_FONT_NAME);

            var fallbacks = new List<TMP_FontAsset>();
            foreach (string fontName in FALLBACK_FONT_NAMES)
            {
                var font = FindFontByName(fontName);
                if (font != null) fallbacks.Add(font);
            }

            var serialized = new SerializedObject(tmpSettings);
            var defaultProperty = serialized.FindProperty("m_defaultFontAsset");
            var fallbackProperty = serialized.FindProperty("m_fallbackFontAssets");

            if (defaultProperty == null || fallbackProperty == null)
            {
                Dev.LogWarn("TMP: TMP Settings의 폰트 항목을 찾지 못했습니다. (TMP 버전 차이) Project Settings > TextMesh Pro에서 직접 지정하세요.");
                return;
            }

            if (defaultFont != null) defaultProperty.objectReferenceValue = defaultFont;

            fallbackProperty.arraySize = fallbacks.Count;
            for (int i = 0; i < fallbacks.Count; i++) fallbackProperty.GetArrayElementAtIndex(i).objectReferenceValue = fallbacks[i];

            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(tmpSettings);
                AssetDatabase.SaveAssetIfDirty(tmpSettings);
                Dev.Log($"TMP: 기본 폰트 {DEFAULT_FONT_NAME}, Fallback {fallbacks.Count}개 지정");
            }
        }

        private static TMP_Settings FindTmpSettings() => FindAsset<TMP_Settings>(null);

        private static TMP_FontAsset FindFontByName(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:TMP_FontAsset {assetName}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null && font.name == assetName) return font;
            }

            Dev.LogWarn($"TMP: 폰트 에셋 '{assetName}'을 찾지 못했습니다.");
            return null;
        }

        #endregion

        #region Helpers

        /// <summary> 종류로 에셋을 찾습니다. preferFolder 안에 있는 것을 우선합니다. </summary>
        private static T FindAsset<T>(string preferFolder) where T : Object
        {
            T fallback = null;

            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null) continue;

                if (!string.IsNullOrEmpty(preferFolder) && path.StartsWith(preferFolder)) return asset;
                if (fallback == null) fallback = asset;
            }

            return fallback;
        }

        private static IEnumerable<T> FindAllAssets<T>() where T : Object
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) yield return asset;
            }
        }

        #endregion
    }
}
#endif