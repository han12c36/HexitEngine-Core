using UnityEngine;
using UnityEditor;
using TMPro;

namespace HexitEngine
{
    public class UIToolsTab : HexitEngineTabBase
    {
        private TMP_FontAsset _targetFont;
        private Color _targetColor = Color.white;
        private Color _targetOutlineColor = Color.black;
        private GameObject _targetPrefab; // 특정 프리팹 타겟 추가

        private bool _showTMPModifiers = true;
        private GUIContent _tmpModifierFoldoutContent;

        private static readonly GUIContent _targetPrefabLabelContent = new GUIContent("Target Prefab (Optional)", "특정 프리팹만 변경하고 싶을 때 에셋을 할당합니다.");
        private static readonly GUIContent _targetFontLabelContent = new GUIContent("Target TMP Font", "일괄 변경할 목표 폰트를 설정합니다.");
        private static readonly GUIContent _targetColorLabelContent = new GUIContent("Target TMP Color", "일괄 적용할 텍스트 색상을 설정합니다.");
        private static readonly GUIContent _targetOutlineColorLabelContent = new GUIContent("Target Outline Color", "일괄 적용할 UI 아웃라인 색상을 설정합니다.");

        public UIToolsTab()
        {
            TabName = "UI Tools";
            HeaderTitle = "UI & Font Tools";
            Description = "씬 및 프리팹의 UI 컴포넌트들을 일괄 제어합니다.";
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _tmpModifierFoldoutContent = new GUIContent("UI Modifiers (Scene & Prefabs)");
        }

        public override void OnDisable() { }

        public override void DrawContent()
        {
            _showTMPModifiers = DrawFoldoutBar(_showTMPModifiers, _tmpModifierFoldoutContent);

            if (_showTMPModifiers)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(18);
                GUILayout.BeginVertical();
                GUILayout.Space(10);

                // ==========================================
                // 0. 타겟 프리팹 설정
                // ==========================================
                EditorGUILayout.LabelField("Specific Prefab Setting", EditorStyles.boldLabel);
                _targetPrefab = (GameObject)EditorGUILayout.ObjectField(_targetPrefabLabelContent, _targetPrefab, typeof(GameObject), false);
                
                GUILayout.Space(15);
                DrawHorizontalLine();
                GUILayout.Space(15);

                // ==========================================
                // 1. 폰트 변경
                // ==========================================
                EditorGUILayout.LabelField("Font Settings", EditorStyles.boldLabel);
                _targetFont = (TMP_FontAsset)EditorGUILayout.ObjectField(_targetFontLabelContent, _targetFont, typeof(TMP_FontAsset), false);

                GUILayout.Space(5);
                GUI.enabled = _targetFont != null;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("씬 변경", GUILayout.Height(25))) ChangeAllFontsInScene();
                GUI.enabled = _targetFont != null && _targetPrefab != null;
                if (GUILayout.Button("선택 프리팹 변경", GUILayout.Height(25))) ChangeSpecificPrefabFont();
                GUI.enabled = _targetFont != null;
                if (GUILayout.Button("모든 프리팹 변경", GUILayout.Height(25))) ChangeAllFontsInPrefabs();
                GUILayout.EndHorizontal();
                GUI.enabled = true;

                GUILayout.Space(15);
                DrawHorizontalLine();
                GUILayout.Space(15);

                // ==========================================
                // 2. 텍스트 색상 변경
                // ==========================================
                EditorGUILayout.LabelField("Text Color Settings", EditorStyles.boldLabel);
                _targetColor = EditorGUILayout.ColorField(_targetColorLabelContent, _targetColor);

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("씬 변경", GUILayout.Height(25))) ChangeAllColorsInScene();
                GUI.enabled = _targetPrefab != null;
                if (GUILayout.Button("선택 프리팹 변경", GUILayout.Height(25))) ChangeSpecificPrefabColor();
                GUI.enabled = true;
                if (GUILayout.Button("모든 프리팹 변경", GUILayout.Height(25))) ChangeAllColorsInPrefabs();
                GUILayout.EndHorizontal();

                GUILayout.Space(15);
                DrawHorizontalLine();
                GUILayout.Space(15);

                // ==========================================
                // 3. 아웃라인 색상 변경
                // ==========================================
                EditorGUILayout.LabelField("Outline Color Settings", EditorStyles.boldLabel);
                _targetOutlineColor = EditorGUILayout.ColorField(_targetOutlineColorLabelContent, _targetOutlineColor);

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("씬 변경", GUILayout.Height(25))) ChangeAllOutlinesInScene();
                GUI.enabled = _targetPrefab != null;
                if (GUILayout.Button("선택 프리팹 변경", GUILayout.Height(25))) ChangeSpecificPrefabOutline();
                GUI.enabled = true;
                if (GUILayout.Button("모든 프리팹 변경", GUILayout.Height(25))) ChangeAllOutlinesInPrefabs();
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        // ==============================================
        // 씬(Scene) 변경 로직
        // ==============================================
        private void ChangeAllFontsInScene()
        {
            TMP_Text[] allTexts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var t in allTexts)
            {
                if (string.IsNullOrEmpty(t.gameObject.scene.name)) continue;
                if (t.font != _targetFont) { Undo.RecordObject(t, "Change Font"); t.font = _targetFont; EditorUtility.SetDirty(t); count++; }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"<b>[UI Tools]</b> 씬 내 <color=cyan>{count}</color>개 폰트 변경 완료.");
        }

        private void ChangeAllColorsInScene()
        {
            TMP_Text[] allTexts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var t in allTexts)
            {
                if (string.IsNullOrEmpty(t.gameObject.scene.name)) continue;
                if (t.color != _targetColor) { Undo.RecordObject(t, "Change Color"); t.color = _targetColor; EditorUtility.SetDirty(t); count++; }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"<b>[UI Tools]</b> 씬 내 <color=yellow>{count}</color>개 색상 변경 완료.");
        }

        private void ChangeAllOutlinesInScene()
        {
            UnityEngine.UI.Outline[] allOutlines = Object.FindObjectsByType<UnityEngine.UI.Outline>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var o in allOutlines)
            {
                if (string.IsNullOrEmpty(o.gameObject.scene.name)) continue;
                if (o.effectColor != _targetOutlineColor) { Undo.RecordObject(o, "Change Outline"); o.effectColor = _targetOutlineColor; EditorUtility.SetDirty(o); count++; }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"<b>[UI Tools]</b> 씬 내 <color=orange>{count}</color>개 아웃라인 변경 완료.");
        }

        // ==============================================
        // 특정 프리팹(Single Prefab) 변경 로직
        // ==============================================
        private void ChangeSpecificPrefabFont()
        {
            ProcessSinglePrefab<TMP_Text>((TMP_Text textComp, ref int count) =>
            {
                if (textComp.font != _targetFont) { textComp.font = _targetFont; count++; }
            });
        }

        private void ChangeSpecificPrefabColor()
        {
            ProcessSinglePrefab<TMP_Text>((TMP_Text textComp, ref int count) =>
            {
                if (textComp.color != _targetColor) { textComp.color = _targetColor; count++; }
            });
        }

        private void ChangeSpecificPrefabOutline()
        {
            ProcessSinglePrefab<UnityEngine.UI.Outline>((UnityEngine.UI.Outline outlineComp, ref int count) =>
            {
                if (outlineComp.effectColor != _targetOutlineColor) { outlineComp.effectColor = _targetOutlineColor; count++; }
            });
        }

        // ==============================================
        // 모든 프리팹(All Prefabs) 변경 로직
        // ==============================================
        private void ChangeAllFontsInPrefabs()
        {
            ProcessPrefabs<TMP_Text>("폰트 변경 중...", (TMP_Text textComp, ref int count) =>
            {
                if (textComp.font != _targetFont) { textComp.font = _targetFont; count++; }
            });
        }

        private void ChangeAllColorsInPrefabs()
        {
            ProcessPrefabs<TMP_Text>("색상 변경 중...", (TMP_Text textComp, ref int count) =>
            {
                if (textComp.color != _targetColor) { textComp.color = _targetColor; count++; }
            });
        }

        private void ChangeAllOutlinesInPrefabs()
        {
            ProcessPrefabs<UnityEngine.UI.Outline>("아웃라인 변경 중...", (UnityEngine.UI.Outline outlineComp, ref int count) =>
            {
                if (outlineComp.effectColor != _targetOutlineColor) { outlineComp.effectColor = _targetOutlineColor; count++; }
            });
        }

        // ==============================================
        // 프리팹 처리 코어 로직
        // ==============================================
        private delegate void delegateProcessAction<T>(T comp, ref int count);

        private void ProcessSinglePrefab<T>(delegateProcessAction<T> action) where T : Component
        {
            if (_targetPrefab == null) return;

            string path = AssetDatabase.GetAssetPath(_targetPrefab);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[UI Tools] 유효한 프리팹 에셋이 아닙니다.");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (prefabRoot == null) return;

            T[] components = prefabRoot.GetComponentsInChildren<T>(true);
            int changedCount = 0;

            foreach (T comp in components)
            {
                action(comp, ref changedCount);
            }

            if (changedCount > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                Debug.Log($"<b>[UI Tools]</b> <color=#00FF00>{_targetPrefab.name}</color> 프리팹에서 {changedCount}개의 속성 변경 완료.");
            }
            else
            {
                Debug.Log($"<b>[UI Tools]</b> {_targetPrefab.name} 프리팹에 변경할 대상이 없습니다.");
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        private void ProcessPrefabs<T>(string progressTitle, delegateProcessAction<T> action) where T : Component
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            int total = guids.Length;
            int changedComponentCount = 0;
            int changedPrefabCount = 0;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("UI Tools", $"{progressTitle} ({i}/{total})", (float)i / total);

                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    if (prefabRoot == null) continue;

                    T[] components = prefabRoot.GetComponentsInChildren<T>(true);
                    if (components.Length == 0)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                        continue;
                    }

                    int tempCount = changedComponentCount;
                    foreach (T comp in components)
                    {
                        action(comp, ref changedComponentCount);
                    }

                    if (changedComponentCount > tempCount)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                        changedPrefabCount++;
                    }

                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Debug.Log($"<b>[UI Tools]</b> 총 {changedPrefabCount}개 프리팹에서 <color=#00FF00>{changedComponentCount}</color>개 속성 변경 완료.");
            }
        }

        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            Color lineColor = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.5f, 0.5f, 0.5f);
            EditorGUI.DrawRect(rect, lineColor);
        }

        public override string GetCodeSnippet() { return ""; }
    }
}