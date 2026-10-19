using UnityEngine;
using UnityEditor;
using System.IO;

namespace HexitEngine
{
    public abstract class HexitEngineTabBase
    {
        private string _tabName;
        public string TabName
        {
            get => _tabName;
            protected set
            {
                _tabName = value;
                // 최적화: 좌측 메뉴에 그릴 "공백 + 탭 이름"을 GUIContent로 미리 생성해 둡니다.
                TabNameContent = new GUIContent("  " + _tabName);
            }
        }

        public GUIContent TabNameContent { get; private set; } // H_FrameworkWindow에서 사용

        public string HeaderTitle { get; protected set; }
        public string Description { get; protected set; }

        protected Vector2 _codeScrollPos;
        protected float _codeAreaHeight = 150f;
        protected bool _isResizingCodeArea = false;

        protected string _cachedCodeSnippet;

        public virtual bool ShowCodeArea => true;

        // 최적화: DrawCodeArea에서 사용되는 라벨/버튼 텍스트를 정적 GUIContent로 캐싱
        protected static readonly GUIContent _viewCodeLabelContent = new GUIContent(" View Component Code");
        protected static readonly GUIContent _copyViewBtnContent = new GUIContent("Copy View");
        protected static readonly GUIContent _saveScriptBtnContent = new GUIContent("Save Component Script");

        protected static class Styles
        {
            public static GUIStyle TitleStyle;
            public static GUIStyle DescStyle;
            public static GUIStyle FoldoutStyle;
            public static GUIStyle TextAreaStyle;
            private static bool _isInitialized = false;

            public static void Init()
            {
                if (_isInitialized) return;

                TitleStyle = new GUIStyle(EditorStyles.largeLabel)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Normal
                };
                TitleStyle.margin = new RectOffset(0, 0, TitleStyle.margin.top, TitleStyle.margin.bottom);
                TitleStyle.padding = new RectOffset(0, 0, TitleStyle.padding.top, TitleStyle.padding.bottom);

                DescStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true
                };
                DescStyle.margin = new RectOffset(0, 0, DescStyle.margin.top, DescStyle.margin.bottom);
                DescStyle.padding = new RectOffset(0, 0, DescStyle.padding.top, DescStyle.padding.bottom);

                FoldoutStyle = new GUIStyle(EditorStyles.foldout)
                {
                    fontStyle = FontStyle.Bold
                };

                TextAreaStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false
                };

                _isInitialized = true;
            }
        }

        public virtual void OnEnable()
        {
            _cachedCodeSnippet = GetCodeSnippet();
        }

        public abstract void OnDisable();
        public abstract void DrawContent();
        public abstract string GetCodeSnippet();

        public virtual void DrawHeader()
        {
            Styles.Init();

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            if (!string.IsNullOrEmpty(HeaderTitle))
            {
                GUILayout.Label(HeaderTitle, Styles.TitleStyle);
                GUILayout.Space(5);
            }

            if (!string.IsNullOrEmpty(Description))
            {
                Styles.DescStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.3f, 0.3f);
                GUILayout.Label(Description, Styles.DescStyle);
            }

            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
        }

        // 최적화: string title 대신 GUIContent를 받도록 수정하여 묵시적 할당 방지
        protected bool DrawFoldoutBar(bool isExpanded, GUIContent titleContent)
        {
            Styles.Init();

            Rect rect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));

            Color bgColor = EditorGUIUtility.isProSkin ? new Color(0.24f, 0.24f, 0.24f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
            Color borderColor = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);

            EditorGUI.DrawRect(rect, borderColor);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), bgColor);

            Event e = Event.current;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
            {
                isExpanded = !isExpanded;
                e.Use();
            }

            Rect foldoutRect = new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 16);
            return EditorGUI.Foldout(foldoutRect, isExpanded, titleContent, true, Styles.FoldoutStyle);
        }

        public virtual void DrawCodeArea(EditorWindow window)
        {
            if (!ShowCodeArea) return;

            Styles.Init();

            EditorGUILayout.BeginVertical(GUILayout.Height(_codeAreaHeight));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 최적화: 캐싱된 GUIContent 사용
            GUILayout.Label(_viewCodeLabelContent, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(_copyViewBtnContent, EditorStyles.toolbarButton))
                GUIUtility.systemCopyBuffer = _cachedCodeSnippet;

            if (GUILayout.Button(_saveScriptBtnContent, EditorStyles.toolbarButton))
            {
                string path = EditorUtility.SaveFilePanelInProject("저장", $"Example_{TabName}", "cs", "");

                if (!string.IsNullOrEmpty(path))
                {
                    File.WriteAllText(path, _cachedCodeSnippet);
                    AssetDatabase.Refresh();
                }
            }
            EditorGUILayout.EndHorizontal();

            Rect dragRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeVertical);
            if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) _isResizingCodeArea = true;
            if (_isResizingCodeArea && Event.current.type == EventType.MouseDrag)
            {
                _codeAreaHeight = Mathf.Clamp(_codeAreaHeight - Event.current.delta.y, 50f, window.position.height - 100f);
                window.Repaint();
            }
            if (Event.current.type == EventType.MouseUp) _isResizingCodeArea = false;

            _codeScrollPos = EditorGUILayout.BeginScrollView(_codeScrollPos);
            EditorGUILayout.TextArea(_cachedCodeSnippet, Styles.TextAreaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
    }
}