using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HexitEngine
{
    /// <summary>
    /// Sound 탭: SoundConfig(SoundLibrary, SFX Emitter, Audio Mixer, SFX / BGM Data Folder)를 설정하고, SoundLibrary의 SFX / BGM 데이터를 관리합니다.
    /// SoundService는 실행 시 SoundConfig에서 SoundLibrary / SFX Emitter / Audio Mixer를 읽어 씁니다.
    /// - 자동 수집: 프로젝트 안의 모든 SoundData를 SoundLibrary에 모읍니다. (SFX / BGM Data 바의 새로고침 아이콘)
    /// - 생성: Settings에서 지정한 SFX / BGM Data Folder에 SFX001_NewSFX 형식으로 만듭니다.
    /// - 편집: Edit → Editing 영역에서 수정 → Save로 저장하고 닫습니다. 저장 전에는 변경 표시가 나타납니다.
    /// </summary>
    public class SoundTab : HexitEngineTab<SoundConfig>
    {
        private const int PAGE_SIZE = 10;
        private const float ROW_HEIGHT = 22f;

        /// <summary> 하단 Editing 영역의 처음 높이 (상단 바를 드래그해 조절 가능) </summary>
        private const float EDITING_DEFAULT_HEIGHT = 240f;

        private static readonly SoundType[] SOUND_TYPES = { SoundType.SFX, SoundType.BGM };

        private static readonly Color ROW_EVEN_COLOR = new(1f, 1f, 1f, 0.02f);
        private static readonly Color ROW_ODD_COLOR = new(1f, 1f, 1f, 0.05f);
        private static readonly Color ROW_EDITING_COLOR = new(0.24f, 0.49f, 0.91f, 0.35f);
        private static readonly Color MODIFIED_COLOR = new(1f, 0.8f, 0.3f);
        private static readonly Color SAVE_BUTTON_COLOR = new(0.55f, 0.9f, 0.55f);

        /// <summary> 하단 고정 영역(코드 영역 자리)을 Editing 영역으로 사용 </summary>
        public override bool ShowCodeArea => true;

        private bool showStatus = true;
        private readonly Dictionary<SoundType, bool> showList = new() { { SoundType.SFX, true }, { SoundType.BGM, true } };
        private readonly Dictionary<SoundType, int> pageIndex = new() { { SoundType.SFX, 0 }, { SoundType.BGM, 0 } };

        /// <summary> 폴드아웃 바 위 새로고침 아이콘 위치 (Repaint 때 갱신) </summary>
        private readonly Dictionary<SoundType, Rect> syncButtonRect = new() { { SoundType.SFX, default }, { SoundType.BGM, default } };

        private GUIContent statusFoldoutContent;
        private GUIContent okIcon;
        private GUIContent failIcon;
        private GUIContent syncButtonContent;
        private GUIStyle modifiedToolbarLabelStyle;

        /// <summary> 마지막으로 자동 수집한 라이브러리 (Config의 라이브러리가 바뀌면 다시 수집) </summary>
        private SoundLibrary syncedLibrary;

        private SoundData editingData;
        private UnityEditor.Editor editingDataEditor;
        private Vector2 editingScroll;

        /// <summary> 편집 시작 시점의 데이터 (변경 여부 확인 / 되돌리기용) </summary>
        private string editingSnapshot;

        private SoundLibrary Library => _config != null ? _config.SoundLibrary : null;

        public SoundTab()
        {
            TabName = "Sound";
            HeaderTitle = "Sound Service";
            Description = "SoundService가 사용할 SoundLibrary, SFX Emitter, Audio Mixer를 지정하고 SoundData를 관리합니다. " +
                          "새 SoundData는 SFX / BGM Data Folder에 지정한 폴더에 저장됩니다.";

            _codeAreaHeight = EDITING_DEFAULT_HEIGHT;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            statusFoldoutContent = new GUIContent("Status");
            okIcon = EditorGUIUtility.IconContent("TestPassed");
            failIcon = EditorGUIUtility.IconContent("TestFailed");
            syncButtonContent = EditorGUIUtility.TrIconContent("Refresh", "프로젝트 안의 모든 SoundData 자동 수집");

            syncedLibrary = null;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            ClearEditing();
        }

        public override void DrawContent()
        {
            DrawConfigSettings("Sound Config Asset");

            GUILayout.Space(10);

            DrawConfigProperties("Settings");

            GUILayout.Space(10);

            if (_config == null) return;

            DrawStatus();

            if (Library == null) return;

            // 라이브러리가 처음 지정되었거나 바뀌었으면 자동 수집
            if (syncedLibrary != Library) SyncSoundData();

            Library.sounds.RemoveAll(sound => sound == null);

            // 편집 중이던 에셋이 밖에서 삭제되었으면 편집 종료
            if (editingData == null && editingDataEditor != null) ClearEditing();

            foreach (var type in SOUND_TYPES)
            {
                GUILayout.Space(10);
                DrawSoundList(type);
            }
        }

        public override string GetCodeSnippet() => string.Empty;

        #region Status

        private void DrawStatus()
        {
            showStatus = DrawFoldoutBar(showStatus, statusFoldoutContent);
            if (!showStatus) return;

            BeginSection();

            DrawStatusRow("SoundLibrary",
                Library != null,
                Library != null ? AssetDatabase.GetAssetPath(Library) : "없음");

            DrawStatusRow("SFX Emitter",
                _config.SfxEmitter != null,
                _config.SfxEmitter != null ? AssetDatabase.GetAssetPath(_config.SfxEmitter) : "없음");

            DrawStatusRow("Audio Mixer",
                _config.AudioMixer != null,
                _config.AudioMixer != null ? AssetDatabase.GetAssetPath(_config.AudioMixer) : "없음");

            bool hasSfxFolder = !string.IsNullOrEmpty(_config.SfxDataPath);
            DrawStatusRow("SFX Data 폴더", hasSfxFolder, hasSfxFolder ? _config.SfxDataPath : "없음");

            bool hasBgmFolder = !string.IsNullOrEmpty(_config.BgmDataPath);
            DrawStatusRow("BGM Data 폴더", hasBgmFolder, hasBgmFolder ? _config.BgmDataPath : "없음");

            EndSection();
        }

        private void DrawStatusRow(string label, bool ok, string detail)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(ok ? okIcon : failIcon, GUILayout.Width(20), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        #endregion

        #region Editing

        /// <summary>
        /// 창 하단 고정 영역(View Component Code 자리)에 Editing을 그립니다. 탭 내용 스크롤과 관계없이 항상 같은 자리에 있습니다.
        /// - 상단 바: 편집 중인 데이터 이름 / 변경 표시 / Save, Close. 바를 드래그하면 높이 조절
        /// - 본문: 편집 중이면 SoundData 편집 화면(내용이 길면 스크롤), 아니면 빈 안내
        /// </summary>
        public override void DrawCodeArea(EditorWindow window)
        {
            Styles.Init();
            modifiedToolbarLabelStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold, normal = { textColor = MODIFIED_COLOR } };

            bool isEditing = editingData != null;
            bool isModified = isEditing && IsEditingModified();

            EditorGUILayout.BeginVertical(GUILayout.Height(_codeAreaHeight));

            DrawEditingToolbar(window, isEditing, isModified);

            if (isEditing) DrawEditingContent();
            else DrawEditingEmpty();

            EditorGUILayout.EndVertical();
        }

        /// <summary> 상단 바 (코드 영역의 바와 같은 모양). 드래그로 높이 조절 </summary>
        private void DrawEditingToolbar(EditorWindow window, bool isEditing, bool isModified)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(isEditing ? $" Editing: {editingData.name}" : " Editing", EditorStyles.miniLabel);
            if (isModified) GUILayout.Label("● 저장 안 됨", modifiedToolbarLabelStyle);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!isEditing))
            {
                var previousColor = GUI.backgroundColor;
                if (isModified) GUI.backgroundColor = SAVE_BUTTON_COLOR;

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    GUI.backgroundColor = previousColor;
                    SaveEditing();
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = previousColor;

                if (GUILayout.Button("Close", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    if (TryCloseEditing()) GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();

            // 바를 드래그해 높이 조절 (코드 영역과 같은 방식)
            Rect dragRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeVertical);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && dragRect.Contains(e.mousePosition)) _isResizingCodeArea = true;
            if (_isResizingCodeArea && e.type == EventType.MouseDrag)
            {
                _codeAreaHeight = Mathf.Clamp(_codeAreaHeight - e.delta.y, 80f, window.position.height - 100f);
                window.Repaint();
            }
            if (e.type == EventType.MouseUp) _isResizingCodeArea = false;
        }

        /// <summary> 편집 화면. 내용이 영역보다 길면 스크롤됩니다. </summary>
        private void DrawEditingContent()
        {
            if (editingDataEditor == null || editingDataEditor.target != editingData)
            {
                if (editingDataEditor != null) Object.DestroyImmediate(editingDataEditor);
                editingDataEditor = UnityEditor.Editor.CreateEditor(editingData);
            }

            editingScroll = EditorGUILayout.BeginScrollView(editingScroll);

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            GUILayout.Space(5);

            editingDataEditor.OnInspectorGUI();

            GUILayout.Space(5);
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        /// <summary> 편집 중이 아닐 때: 같은 높이의 빈 영역에 안내만 표시 </summary>
        private void DrawEditingEmpty()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("편집 중인 SoundData가 없습니다.\n목록에서 Edit 또는 + 를 누르세요.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
        }

        private bool IsEditingModified()
        {
            return editingData != null && editingSnapshot != null && EditorJsonUtility.ToJson(editingData) != editingSnapshot;
        }

        /// <summary> 편집을 시작합니다. 다른 데이터를 편집 중이고 변경 사항이 있으면 먼저 확인합니다. </summary>
        private void StartEditing(SoundData data)
        {
            if (editingData == data) return;
            if (!TryCloseEditing()) return;

            editingData = data;
            editingSnapshot = EditorJsonUtility.ToJson(data);
            editingScroll = Vector2.zero;
            GUI.FocusControl(null);
        }

        /// <summary> 변경 사항을 저장하고 편집을 닫습니다. ID가 파일 이름과 다르면 파일 이름도 ID로 바꿉니다. </summary>
        private void SaveEditing()
        {
            if (editingData == null) return;

            var data = editingData;
            string path = AssetDatabase.GetAssetPath(data);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            if (!string.IsNullOrWhiteSpace(data.id) && data.id != data.name)
            {
                string error = AssetDatabase.RenameAsset(path, data.id);
                if (!string.IsNullOrEmpty(error)) Dev.LogWarn($"파일 이름을 ID로 바꾸지 못했습니다: {error}");
            }

            var library = Library;
            if (library != null)
            {
                library.sounds = library.sounds.OrderBy(sound => sound.name).ToList();
                SaveLibrary(library);
            }

            ClearEditing();
            ShowNotification($"저장됨: {data.name}");
            Dev.Log($"SoundData 저장: {AssetDatabase.GetAssetPath(data)}");
        }

        /// <summary> 편집을 닫습니다. 변경 사항이 있으면 저장 / 되돌리기 / 취소를 묻습니다. 닫혔으면 true. </summary>
        private bool TryCloseEditing()
        {
            if (editingData == null) return true;

            if (IsEditingModified())
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "저장되지 않은 변경 사항",
                    $"'{editingData.name}'에 저장되지 않은 변경 사항이 있습니다.",
                    "저장", "취소", "되돌리기");

                switch (choice)
                {
                    case 0: SaveEditing(); return true;
                    case 1: return false;
                    case 2: RevertEditing(); break;
                }
            }

            ClearEditing();
            return true;
        }

        /// <summary> 편집 시작 시점으로 되돌립니다. </summary>
        private void RevertEditing()
        {
            EditorJsonUtility.FromJsonOverwrite(editingSnapshot, editingData);
            EditorUtility.SetDirty(editingData);
            AssetDatabase.SaveAssetIfDirty(editingData);
            Dev.Log($"SoundData 되돌림: {editingData.name}");
        }

        private void ClearEditing()
        {
            editingData = null;
            editingSnapshot = null;

            if (editingDataEditor != null) Object.DestroyImmediate(editingDataEditor);
            editingDataEditor = null;
        }

        #endregion

        #region Sound List

        /// <summary> SFX Data / BGM Data 목록. 번호, 이름, Edit / Delete를 한 줄씩 그리고 페이지로 나눕니다. </summary>
        private void DrawSoundList(SoundType type)
        {
            var library = Library;
            var list = library.sounds.Where(sound => sound.soundType == type).ToList();

            bool canSync = !EditorApplication.isPlayingOrWillChangePlaymode;

            // 자동 수집 클릭은 폴드아웃 바보다 먼저 처리 (바가 클릭을 가져가 접히지 않도록)
            if (TryClickBarIcon(syncButtonRect[type], canSync))
            {
                SyncSoundData();
                ShowNotification("SoundData 자동 수집 완료");
            }

            showList[type] = DrawFoldoutBar(showList[type], new GUIContent($"{type} Data ({list.Count})"));

            var rect = syncButtonRect[type];
            DrawBarIcon(ref rect, syncButtonContent, canSync);
            syncButtonRect[type] = rect;

            if (!showList[type]) return;

            BeginSection();

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PAGE_SIZE));
            int page = Mathf.Clamp(pageIndex[type], 0, pageCount - 1);
            pageIndex[type] = page;

            GUILayout.BeginVertical(EditorStyles.helpBox);

            if (list.Count == 0)
            {
                GUILayout.Label("없음", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(ROW_HEIGHT));
            }
            else
            {
                int start = page * PAGE_SIZE;
                int end = Mathf.Min(start + PAGE_SIZE, list.Count);

                for (int i = start; i < end; i++)
                {
                    DrawSoundRow(library, list[i], i);
                }
            }

            GUILayout.EndVertical();

            GUILayout.Space(4);

            DrawListFooter(library, type, page, pageCount);

            EndSection();
        }

        private void DrawSoundRow(SoundLibrary library, SoundData sound, int index)
        {
            Rect rowRect = GUILayoutUtility.GetRect(0f, ROW_HEIGHT, GUILayout.ExpandWidth(true));

            Color background = sound == editingData ? ROW_EDITING_COLOR : (index % 2 == 0 ? ROW_EVEN_COLOR : ROW_ODD_COLOR);
            EditorGUI.DrawRect(rowRect, background);

            float y = rowRect.y + 2f;
            float height = ROW_HEIGHT - 4f;

            var indexRect = new Rect(rowRect.x + 4f, y, 32f, height);
            var deleteRect = new Rect(rowRect.xMax - 59f, y, 55f, height);
            var editRect = new Rect(deleteRect.x - 50f, y, 50f, height);
            var nameRect = new Rect(indexRect.xMax + 4f, y, editRect.x - indexRect.xMax - 12f, height);

            GUI.Label(indexRect, (index + 1).ToString(), EditorStyles.miniLabel);

            var nameStyle = sound == editingData ? EditorStyles.boldLabel : EditorStyles.label;
            if (GUI.Button(nameRect, sound.name, nameStyle))
            {
                EditorGUIUtility.PingObject(sound);
            }

            if (GUI.Button(editRect, "Edit", EditorStyles.miniButtonLeft))
            {
                StartEditing(sound);
            }

            if (GUI.Button(deleteRect, "Delete", EditorStyles.miniButtonRight))
            {
                DeleteSoundData(library, sound);
            }
        }

        /// <summary> 목록 아래: 왼쪽 새로 만들기 버튼, 오른쪽 페이지 이동 </summary>
        private void DrawListFooter(SoundLibrary library, SoundType type, int page, int pageCount)
        {
            bool hasFolder = !string.IsNullOrEmpty(_config.GetDataPath(type));

            GUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!hasFolder || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var content = new GUIContent("+", hasFolder ? null : $"Settings에서 {type} Data Folder를 먼저 지정하세요.");
                if (GUILayout.Button(content, GUILayout.Width(40), GUILayout.Height(20))) CreateSoundData(library, type);
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(page <= 0))
            {
                if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(28))) pageIndex[type] = page - 1;
            }

            GUILayout.Label($"{page + 1} / {pageCount}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50));

            using (new EditorGUI.DisabledScope(page >= pageCount - 1))
            {
                if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(28))) pageIndex[type] = page + 1;
            }

            GUILayout.EndHorizontal();

            if (!hasFolder)
            {
                EditorGUILayout.LabelField($"Settings에서 {type} Data Folder를 지정해야 새로 만들 수 있습니다.", EditorStyles.miniLabel);
            }
        }

        #endregion

        #region Actions

        /// <summary> 프로젝트 안의 모든 SoundData를 이름순으로 라이브러리에 모읍니다. 바뀐 게 있을 때만 저장합니다. </summary>
        private void SyncSoundData()
        {
            var library = Library;
            syncedLibrary = library;
            if (library == null) return;

            var collected = AssetDatabase.FindAssets($"t:{nameof(SoundData)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<SoundData>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(sound => sound != null)
                .OrderBy(sound => sound.name)
                .ToList();

            if (collected.SequenceEqual(library.sounds)) return;

            library.sounds = collected;
            SaveLibrary(library);
        }

        /// <summary> 타입별 Data Folder에 {타입}{번호 3자리}_New{타입} 이름으로 새 SoundData를 만들고 편집을 엽니다. (하위 폴더를 만들지 않음) </summary>
        private void CreateSoundData(SoundLibrary library, SoundType type)
        {
            string folderPath = _config.GetDataPath(type);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                Dev.LogError($"{type} Data Folder가 올바르지 않습니다: {folderPath}");
                return;
            }

            // 편집 중인 데이터에 변경 사항이 있으면 먼저 확인
            if (!TryCloseEditing()) return;

            string typeName = type.ToString();

            string fileName = $"{typeName}{GetNextSequenceNumber(library, type):D3}_New{typeName}";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{fileName}.asset");

            var data = ScriptableObject.CreateInstance<SoundData>();
            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            data.id = assetName;
            data.name = assetName;
            data.soundType = type;

            AssetDatabase.CreateAsset(data, assetPath);

            library.sounds.Add(data);
            library.sounds = library.sounds.OrderBy(sound => sound.name).ToList();
            SaveLibrary(library);

            // 새 데이터가 있는 페이지로 이동
            var list = library.sounds.Where(sound => sound.soundType == type).ToList();
            pageIndex[type] = list.IndexOf(data) / PAGE_SIZE;

            StartEditing(data);
            Dev.Log($"SoundData 생성: {assetPath}");
        }

        /// <summary> 확인 후 SoundData 에셋을 휴지통으로 옮기고 라이브러리에서 뺍니다. </summary>
        private void DeleteSoundData(SoundLibrary library, SoundData sound)
        {
            if (!EditorUtility.DisplayDialog("SoundData 삭제", $"'{sound.name}' 에셋을 삭제하시겠습니까?", "삭제", "취소")) return;

            if (editingData == sound) ClearEditing();

            string path = AssetDatabase.GetAssetPath(sound);

            library.sounds.Remove(sound);
            SaveLibrary(library);

            AssetDatabase.MoveAssetToTrash(path);
            Dev.Log($"SoundData 삭제: {path}");

            GUIUtility.ExitGUI();
        }

        /// <summary> 같은 타입에서 가장 큰 번호(SFX001_ → 1) 다음 번호를 반환합니다. </summary>
        private static int GetNextSequenceNumber(SoundLibrary library, SoundType type)
        {
            string prefix = type.ToString();
            int maxNumber = 0;

            foreach (var sound in library.sounds)
            {
                if (sound == null || sound.soundType != type || !sound.name.StartsWith(prefix)) continue;
                if (sound.name.Length < prefix.Length + 3) continue;

                if (int.TryParse(sound.name.Substring(prefix.Length, 3), out int number) && number > maxNumber)
                {
                    maxNumber = number;
                }
            }

            return maxNumber + 1;
        }

        private static void SaveLibrary(SoundLibrary library)
        {
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
        }

        /// <summary> Settings 창에 잠깐 떠오르는 알림을 띄웁니다. </summary>
        private static void ShowNotification(string message)
        {
            var window = EditorWindow.focusedWindow;
            if (window != null) window.ShowNotification(new GUIContent(message), 1.5f);
        }

        #endregion

        #region Layout

        private static void BeginSection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(18);
            GUILayout.BeginVertical();
            GUILayout.Space(5);
        }

        private static void EndSection()
        {
            GUILayout.Space(5);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary> 폴드아웃 바 위 아이콘이 클릭되었는지 확인합니다. 바를 그리기 전에 호출해야 바가 클릭을 가져가지 않습니다. </summary>
        private static bool TryClickBarIcon(Rect iconRect, bool enabled)
        {
            Event e = Event.current;
            if (!enabled || e.type != EventType.MouseDown || e.button != 0 || !iconRect.Contains(e.mousePosition)) return false;

            e.Use();
            return true;
        }

        /// <summary> 방금 그린 폴드아웃 바 오른쪽 끝에 아이콘을 그립니다. </summary>
        private static void DrawBarIcon(ref Rect iconRect, GUIContent content, bool enabled)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rect barRect = GUILayoutUtility.GetLastRect();
                iconRect = new Rect(barRect.xMax - 26f, barRect.y + 3f, 20f, 18f);
            }

            using (new EditorGUI.DisabledScope(!enabled))
            {
                GUI.Label(iconRect, content, EditorStyles.iconButton);
            }
        }

        #endregion
    }
}