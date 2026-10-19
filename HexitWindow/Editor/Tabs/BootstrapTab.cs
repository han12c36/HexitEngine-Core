using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexitEngine
{
    public class BootstrapTab : HexitEngineTab<BootstrapConfig>
    {
        private const string SERVICE_ORDER_PROPERTY = "serviceOrder";

        public override bool ShowCodeArea => false;

        private bool showServiceOrder = true;
        private GUIContent serviceOrderFoldoutContent;
        private GUIContent refreshButtonContent;

        /// <summary> 폴드아웃 바 위 새로고침 버튼 위치 (Repaint 때 갱신) </summary>
        private Rect refreshButtonRect;

        private SerializedObject orderSerializedObject;
        private ReorderableList orderList;

        /// <summary> 마지막으로 불러온 BootstrapScene의 타입 → 오브젝트 이름 (표시용) </summary>
        private readonly Dictionary<string, string> scannedObjectNames = new();
        private bool hasScanned;
        private bool hasNetworkManagerInScene;
        private bool hasHexitEngineInScene;

        public BootstrapTab()
        {
            TabName = "Bootstrap";
            HeaderTitle = "Bootstrap System";
            Description = "부트스트랩 시스템은 게임 시작 시 BootstrapScene에 배치된 서비스들을 정해진 순서로 초기화하고, " +
                          "초기화 무결성을 검사한 뒤 지정된 씬으로 라우팅을 수행합니다.";
        }

        public override void OnEnable()
        {
            base.OnEnable();
            serviceOrderFoldoutContent = new GUIContent("Service Initialization Order");
            refreshButtonContent = EditorGUIUtility.TrIconContent("Refresh", "부트스트랩 씬에서 서비스 다시 불러오기");

            // 부트스트랩 씬이 이미 열려 있으면 바로 동기화 (열려 있지 않으면 새로고침 버튼으로)
            if (!EditorApplication.isPlayingOrWillChangePlaymode && TryGetLoadedBootstrapScene(out _)) SyncFromBootstrapScene();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            orderSerializedObject = null;
            orderList = null;
        }

        public override void DrawContent()
        {
            DrawConfigSettings("Bootstrap Config Asset");

            GUILayout.Space(10);

            EditorGUILayout.HelpBox("Force Scene Routing을 끄면 에디터에서 현재 열려있는 씬에서 바로 테스트가 가능합니다.", MessageType.Info);
            GUILayout.Space(5);

            // 기본 프로퍼티 (Next Scene, Force Scene Routing). 초기화 순서는 HideInInspector라 아래에서 따로 그립니다.
            DrawConfigProperties("Settings");

            GUILayout.Space(10);

            DrawServiceOrder();
        }

        public override string GetCodeSnippet() => string.Empty;

        #region Service Order

        private void DrawServiceOrder()
        {
            if (_config == null) return;

            var bootstrapScene = _config.BootstrapSceneAsset;
            bool canRefresh = bootstrapScene != null && !EditorApplication.isPlayingOrWillChangePlaymode;

            // 새로고침 클릭은 폴드아웃 바보다 먼저 처리 (바가 클릭을 가져가 접히지 않도록)
            Event e = Event.current;
            if (canRefresh && e.type == EventType.MouseDown && e.button == 0 && refreshButtonRect.Contains(e.mousePosition))
            {
                e.Use();
                // 씬 열기/닫기는 GUI 그리기 도중에 하지 않도록 다음 에디터 틱으로 미룸
                EditorApplication.delayCall += SyncFromBootstrapScene;
            }

            showServiceOrder = DrawFoldoutBar(showServiceOrder, serviceOrderFoldoutContent);
            DrawRefreshButton(canRefresh);

            if (!showServiceOrder) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(18);
            GUILayout.BeginVertical();
            GUILayout.Space(5);

            if (bootstrapScene == null)
            {
                EditorGUILayout.HelpBox("Settings에서 Bootstrap Scene을 지정해야 서비스를 불러올 수 있습니다.", MessageType.Warning);
            }

            if (hasScanned && !hasNetworkManagerInScene)
                EditorGUILayout.HelpBox("부트스트랩 씬에 NetworkManager가 없습니다.", MessageType.Error);
            if (hasScanned && !hasHexitEngineInScene)
                EditorGUILayout.HelpBox("부트스트랩 씬에 HEXITEngine이 없습니다.", MessageType.Error);

            EnsureOrderList();

            // [고정] 맨 처음
            DrawFixedRow(0, BootstrapConfig.FIRST_FIXED_NAME, "");

            // [변경 가능] 중간
            orderSerializedObject.Update();
            orderList.DoLayoutList();
            orderSerializedObject.ApplyModifiedProperties();

            // [고정] 맨 마지막
            DrawFixedRow(orderList.count + 1, nameof(HEXITEngine), "");

            GUILayout.Space(5);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary> 폴드아웃 바 오른쪽 끝에 새로고침 아이콘을 그립니다. (클릭 처리는 DrawServiceOrder 앞부분) </summary>
        private void DrawRefreshButton(bool canRefresh)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rect barRect = GUILayoutUtility.GetLastRect();
                refreshButtonRect = new Rect(barRect.xMax - 26f, barRect.y + 3f, 20f, 18f);
            }

            using (new EditorGUI.DisabledScope(!canRefresh))
            {
                GUI.Label(refreshButtonRect, refreshButtonContent, EditorStyles.iconButton);
            }
        }

        /// <summary> Config가 바뀌었거나 처음이면 ReorderableList를 다시 만듭니다. </summary>
        private void EnsureOrderList()
        {
            if (orderSerializedObject != null && orderSerializedObject.targetObject == _config && orderList != null) return;

            orderSerializedObject = new SerializedObject(_config);
            var property = orderSerializedObject.FindProperty(SERVICE_ORDER_PROPERTY);

            orderList = new ReorderableList(orderSerializedObject, property, draggable: true, displayHeader: false, displayAddButton: false, displayRemoveButton: false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                footerHeight = 0f,
                drawFooterCallback = _ => { },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    string typeName = property.GetArrayElementAtIndex(index).stringValue;
                    DrawRowContent(rect, index + 1, ShortName(typeName), GetObjectLabel(typeName));
                },
            };
        }

        private void DrawFixedRow(int index, string name, string note)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
            rect.xMin += 20f; // ReorderableList 드래그 핸들 자리만큼 맞춤

            using (new EditorGUI.DisabledScope(true))
            {
                DrawRowContent(rect, index, name, $"[고정] {note}");
            }
        }

        private static void DrawRowContent(Rect rect, int index, string name, string detail)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var indexRect = new Rect(rect.x, rect.y, 28f, rect.height);
            var nameRect = new Rect(indexRect.xMax, rect.y, rect.width * 0.45f, rect.height);
            var detailRect = new Rect(nameRect.xMax, rect.y, rect.xMax - nameRect.xMax, rect.height);

            EditorGUI.LabelField(indexRect, index.ToString(), EditorStyles.boldLabel);
            EditorGUI.LabelField(nameRect, name);
            EditorGUI.LabelField(detailRect, detail, EditorStyles.miniLabel);
        }

        private string GetObjectLabel(string typeName)
        {
            if (!hasScanned) return string.Empty;
            return scannedObjectNames.TryGetValue(typeName, out var objectName) ? objectName : "(씬에 없음)";
        }

        private static string ShortName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "(비어 있음)";
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        #endregion

        #region Scene Sync

        /// <summary>
        /// BootstrapScene에 배치된 ISingleton을 읽어 초기화 순서를 동기화합니다.
        /// 기존 순서는 유지하고, 새 서비스는 뒤에 추가, 씬에서 사라진 서비스는 제거합니다. (NetworkManager, HEXITEngine 제외)
        /// </summary>
        private void SyncFromBootstrapScene()
        {
            if (_config == null) return;

            bool openedHere = false;

            if (!TryGetLoadedBootstrapScene(out Scene scene))
            {
                string path = GetBootstrapScenePath();
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogError("[Bootstrap] BootstrapConfig에 Bootstrap Scene이 지정되지 않았습니다.");
                    return;
                }

                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                var foundInSceneOrder = ScanScene(scene);
                ApplyScanResult(foundInSceneOrder);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary> 씬의 활성 ISingleton 타입을 하이어라키 순서로 수집합니다. </summary>
        private List<string> ScanScene(Scene scene)
        {
            scannedObjectNames.Clear();
            hasNetworkManagerInScene = false;
            hasHexitEngineInScene = false;

            var result = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<NetworkManager>(true) != null) hasNetworkManagerInScene = true;

                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour is not ISingleton) continue;

                    string typeName = behaviour.GetType().FullName;

                    if (typeName == BootstrapConfig.LastFixedTypeName)
                    {
                        hasHexitEngineInScene = true;
                        continue;
                    }

                    if (scannedObjectNames.ContainsKey(typeName)) continue;

                    scannedObjectNames.Add(typeName, behaviour.gameObject.activeInHierarchy ? behaviour.gameObject.name : $"{behaviour.gameObject.name} (비활성: 초기화 안 됨)");
                    result.Add(typeName);
                }
            }

            hasScanned = true;
            return result;
        }

        private void ApplyScanResult(List<string> foundInSceneOrder)
        {
            EnsureOrderList();
            orderSerializedObject.Update();

            var property = orderSerializedObject.FindProperty(SERVICE_ORDER_PROPERTY);

            // 1. 기존 순서 중 씬에 남아 있는 것만 유지
            var merged = new List<string>();
            for (int i = 0; i < property.arraySize; i++)
            {
                string typeName = property.GetArrayElementAtIndex(i).stringValue;
                if (foundInSceneOrder.Contains(typeName) && !merged.Contains(typeName)) merged.Add(typeName);
            }

            // 2. 새로 발견된 서비스는 뒤에 추가
            foreach (string typeName in foundInSceneOrder)
            {
                if (!merged.Contains(typeName)) merged.Add(typeName);
            }

            // 3. 기록 (Undo 지원)
            property.arraySize = merged.Count;
            for (int i = 0; i < merged.Count; i++) property.GetArrayElementAtIndex(i).stringValue = merged[i];

            orderSerializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(_config);
        }

        /// <summary> Config에 지정된 부트스트랩 씬 에셋 경로. 미지정이면 null </summary>
        private string GetBootstrapScenePath()
        {
            if (_config == null || _config.BootstrapSceneAsset == null) return null;
            return AssetDatabase.GetAssetPath(_config.BootstrapSceneAsset);
        }

        /// <summary> 부트스트랩 씬이 에디터에 이미 열려 있으면 그 씬을 돌려줍니다. </summary>
        private bool TryGetLoadedBootstrapScene(out Scene scene)
        {
            scene = default;

            string path = GetBootstrapScenePath();
            if (string.IsNullOrEmpty(path)) return false;

            scene = SceneManager.GetSceneByPath(path);
            return scene.IsValid() && scene.isLoaded;
        }

        #endregion
    }
}