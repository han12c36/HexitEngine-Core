using UnityEngine;
using UnityEditor;

namespace HexitEngine
{
    public abstract class HexitEngineTab<T> : HexitEngineTabBase where T : HexitEngineTapConfig<T>
    {
        protected T _config;
        protected Editor _configEditor;

        protected bool _showSettings = true;
        protected bool _showProperties = true;

        // 최적화: 제네릭 타입별 정적 캐싱 (제네릭의 특성상 타입마다 독립적으로 안전하게 캐싱됩니다)
        protected static readonly GUIContent _configAssetContent = new GUIContent("Config Asset");
        protected static readonly GUIContent _newBtnContent = new GUIContent("New");
        protected static readonly GUIContent _cloneBtnContent = new GUIContent("Clone");

        // FoldoutBar에 넘길 제목 GUIContent 인스턴스 (문자열 생성 방지)
        private GUIContent _settingsFoldoutContent;
        private GUIContent _propertiesFoldoutContent;

        public override void OnEnable() => LoadConfig();
        public override void OnDisable() { if (_configEditor != null) Object.DestroyImmediate(_configEditor); }

        public abstract override void DrawContent();

        protected void DrawConfigSettings(string name)
        {
            if (_settingsFoldoutContent == null || _settingsFoldoutContent.text != name)
                _settingsFoldoutContent = new GUIContent(name);

            _showSettings = DrawFoldoutBar(_showSettings, _settingsFoldoutContent);

            if (_showSettings)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(18);
                GUILayout.BeginVertical();
                GUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();

                // 최적화: 캐싱된 라벨 콘텐츠 전달
                T newConfig = (T)EditorGUILayout.ObjectField(_configAssetContent, _config, typeof(T), false);
                if (EditorGUI.EndChangeCheck()) UpdateConfig(newConfig);

                if (GUILayout.Button(_newBtnContent, EditorStyles.miniButtonLeft, GUILayout.Width(45))) CreateNewConfig();
                GUI.enabled = _config != null;
                if (GUILayout.Button(_cloneBtnContent, EditorStyles.miniButtonRight, GUILayout.Width(45))) CloneConfig();
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        protected void DrawConfigProperties(string name)
        {
            if (_config == null) return;

            if (_propertiesFoldoutContent == null || _propertiesFoldoutContent.text != name)
                _propertiesFoldoutContent = new GUIContent(name);

            _showProperties = DrawFoldoutBar(_showProperties, _propertiesFoldoutContent);

            if (_showProperties)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(18);
                GUILayout.BeginVertical();
                GUILayout.Space(5);

                if (_configEditor == null) _configEditor = Editor.CreateEditor(_config);
                _configEditor.serializedObject.Update();

                SerializedProperty prop = _configEditor.serializedObject.GetIterator();
                if (prop.NextVisible(true))
                {
                    // 핵심 최적화: m_Script인지 비교하는 string 연산을 단 1회로 제한
                    // Unity Object의 최상단 첫 번째 프로퍼티는 구조상 대부분 m_Script입니다.
                    if (prop.name == "m_Script")
                    {
                        while (prop.NextVisible(false))
                        {
                            EditorGUILayout.PropertyField(prop, true);
                        }
                    }
                    else
                    {
                        do
                        {
                            EditorGUILayout.PropertyField(prop, true);
                        }
                        while (prop.NextVisible(false));
                    }
                }
                _configEditor.serializedObject.ApplyModifiedProperties();

                GUILayout.Space(5);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private void LoadConfig()
        {
            // 2. T.Instance가 아니라 H_PreloadedConfig<T>.Instance 로 수정!
            T instance = HexitEngineTapConfig<T>.Instance;

            // 만약 프로젝트 최초 세팅이라서 아직 엔진에 인스턴스가 없다면 그때만 백업으로 검색합니다.
            if (instance == null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
                if (guids.Length > 0)
                    instance = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            UpdateConfig(instance);
        }

        private void UpdateConfig(T newConfig)
        {
            _config = newConfig;
            if (_configEditor != null) { Object.DestroyImmediate(_configEditor); _configEditor = null; }
        }

        private void CreateNewConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject($"새 {typeof(T).Name} 생성", $"New_{typeof(T).Name}", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            UpdateConfig(asset);
        }

        private void CloneConfig()
        {
            if (_config == null) return;
            string path = EditorUtility.SaveFilePanelInProject("에셋 복제", _config.name + "_Clone", "asset", "");
            if (!string.IsNullOrEmpty(path) && AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(_config), path))
            {
                AssetDatabase.Refresh();
                UpdateConfig(AssetDatabase.LoadAssetAtPath<T>(path));
            }
        }
    }
}