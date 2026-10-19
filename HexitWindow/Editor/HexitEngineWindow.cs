using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

namespace HexitEngine
{
    public class HexitEngineWindow : EditorWindow
    {
        private List<HexitEngineTabBase> _tabs;
        private int _selectedTabIndex = 0;
        private string _searchString = "";

        private Vector2 _leftScrollPos, _rightScrollPos;
        private float _leftPanelWidth = 180f;
        private bool _isResizingLeftPanel = false;

        // 최적화: 스타일 객체 캐싱
        private GUIStyle _searchToolbarStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _toolbarCancelEmptyStyle;
        private GUIStyle _toolbarCancelStyle;
        private bool _isStylesInitialized = false;

        private readonly Color _darkBgColorPro = new Color(0.13f, 0.13f, 0.13f, 1f);
        private readonly Color _darkBgColorLight = new Color(0.8f, 0.8f, 0.8f, 1f);
        private readonly Color _lineColorPro = new Color(0.12f, 0.12f, 0.12f);
        private readonly Color _lineColorLight = new Color(0.5f, 0.5f, 0.5f);
        private readonly Color _headerLineColor = new Color(1f, 1f, 1f, 0.15f);

        // 최적화: 빈 문자열 버튼에 대한 GUIContent 캐싱
        private static readonly GUIContent _emptyContent = new GUIContent("");

        [MenuItem("HexitEngine/Settings")]
        public static void ShowWindow()
        {
            HexitEngineWindow window = GetWindow<HexitEngineWindow>("HexitEngine");

            window.minSize = new Vector2(800f, 500f);

            window.Show();
        }

        private void OnEnable()
        {
            _tabs = new List<HexitEngineTabBase>()
            {
                new BootstrapTab(),
                new DevTab(),
                new LocalPoolTab(),
                new NetworkPoolTab(),
                new UIToolsTab(),
                new SoundTab(),
                new SteamAchievementTab(),
                new BuildTab(),
            };

            foreach (var tab in _tabs) tab.OnEnable();
        }

        private void OnDisable()
        {
            if (_tabs != null) foreach (var tab in _tabs) tab.OnDisable();
        }

        private void InitializeStyles()
        {
            if (_isStylesInitialized) return;

            _searchToolbarStyle = new GUIStyle(EditorStyles.toolbar) { padding = new RectOffset(0, 0, 0, 0) };
            _searchFieldStyle = new GUIStyle(EditorStyles.toolbarSearchField);
            _searchFieldStyle.margin = new RectOffset(0, 0, _searchFieldStyle.margin.top, _searchFieldStyle.margin.bottom);

            // 최적화: 암묵적 string 캐스팅 방지를 위해 GUIStyle 이름도 미리 캐싱
            _toolbarCancelEmptyStyle = new GUIStyle("ToolbarSearchCancelButtonEmpty");
            _toolbarCancelStyle = new GUIStyle("ToolbarSearchCancelButton");

            _isStylesInitialized = true;
        }

        private void OnGUI()
        {
            if (_tabs == null || _tabs.Count == 0) return;

            InitializeStyles();

            EditorGUILayout.BeginHorizontal();
            DrawLeftMenuPanel();
            DrawLeftPanelSplitter();

            EditorGUILayout.BeginVertical();

            Rect rightAreaRect = EditorGUILayout.BeginVertical();

            Color darkBgColor = EditorGUIUtility.isProSkin ? _darkBgColorPro : _darkBgColorLight;
            EditorGUI.DrawRect(new Rect(rightAreaRect.x, 0, position.width, position.height), darkBgColor);

            _rightScrollPos = EditorGUILayout.BeginScrollView(_rightScrollPos, GUILayout.ExpandHeight(true));

            GUILayout.BeginVertical();
            GUILayout.Space(15);

            _tabs[_selectedTabIndex].DrawHeader();

            Rect lineRect = EditorGUILayout.GetControlRect(false, 1f);
            float margin = 14f;
            lineRect.x += margin;
            lineRect.width -= margin * 2;

            EditorGUI.DrawRect(lineRect, _headerLineColor);

            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUILayout.BeginVertical();

            _tabs[_selectedTabIndex].DrawContent();

            GUILayout.EndVertical();
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(30);
            GUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (_tabs[_selectedTabIndex].ShowCodeArea)
            {
                DrawHorizontalLine();
                _tabs[_selectedTabIndex].DrawCodeArea(this);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftMenuPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));

            EditorGUILayout.BeginHorizontal(_searchToolbarStyle);
            GUILayout.Space(10);
            _searchString = EditorGUILayout.TextField(_searchString, _searchFieldStyle, GUILayout.ExpandWidth(true));

            GUIStyle cancelBtnStyle = string.IsNullOrEmpty(_searchString) ? _toolbarCancelEmptyStyle : _toolbarCancelStyle;
            if (GUILayout.Button(_emptyContent, cancelBtnStyle))
            {
                _searchString = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos);

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (!string.IsNullOrEmpty(_searchString) &&
                    _tabs[i].TabName.IndexOf(_searchString, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool isSelected = (_selectedTabIndex == i);

                // 핵심 최적화: "  " + TabName 으로 인한 매 프레임 문자열 할당을 TabNameContent 캐싱으로 해결
                if (GUILayout.Toggle(isSelected, _tabs[i].TabNameContent, EditorStyles.toolbarButton, GUILayout.Height(24)))
                {
                    _selectedTabIndex = i;
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftPanelSplitter()
        {
            Rect lineRect = GUILayoutUtility.GetRect(1f, position.height, GUILayout.ExpandHeight(true));
            Color lineColor = EditorGUIUtility.isProSkin ? _lineColorPro : _lineColorLight;
            EditorGUI.DrawRect(lineRect, lineColor);

            Rect dragRect = new Rect(lineRect.x - 2f, lineRect.y, 5f, lineRect.height);
            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeHorizontal);

            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && dragRect.Contains(currentEvent.mousePosition))
            {
                _isResizingLeftPanel = true;
                currentEvent.Use();
            }

            if (_isResizingLeftPanel && currentEvent.type == EventType.MouseDrag)
            {
                _leftPanelWidth = Mathf.Clamp(_leftPanelWidth + currentEvent.delta.x, 100f, position.width / 2f);
                Repaint();
                currentEvent.Use();
            }

            if (currentEvent.type == EventType.MouseUp && _isResizingLeftPanel)
            {
                _isResizingLeftPanel = false;
                currentEvent.Use();
            }
        }

        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            Color lineColor = EditorGUIUtility.isProSkin ? _lineColorPro : _lineColorLight;
            EditorGUI.DrawRect(rect, lineColor);
        }
    }
}