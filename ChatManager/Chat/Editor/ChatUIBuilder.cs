#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace HexitEngine.Sample.EditorTools
{
    /// <summary>
    /// [에디터 전용] 채팅 UI 프리팹(Chat.prefab + ChatMessageItem.prefab)을 만듭니다.
    /// - 메뉴: HexitEngine/Sample/Create Chat UI Prefab → 저장할 폴더 선택
    /// - ScrollRect, Content 레이아웃(Vertical Layout Group + Content Size Fitter), 입력칸, 보내기 버튼, 크기 조절 손잡이와
    ///   ChatManager / ChatUI / ChatResizeHandle의 참조 연결까지 한 번에 구성합니다.
    /// - 만들어진 Chat.prefab을 씬의 Canvas 아래에 놓으면 바로 동작합니다. (모양은 자유롭게 바꿔도 됨)
    /// </summary>
    public static class ChatUIBuilder
    {
        private const float WIDTH = 420f;
        private const float HEIGHT = 260f;
        private const float MARGIN = 6f;
        private const float INPUT_HEIGHT = 34f;
        private const float SEND_WIDTH = 64f;
        private const float HANDLE_HEIGHT = 10f;

        [MenuItem("HexitEngine/Sample/Create Chat UI Prefab")]
        private static void Create()
        {
            string folder = SelectAssetFolder();
            if (folder == null) return;

            var resources = new TMP_DefaultControls.Resources();

            // 1. 한 줄 프리팹
            var itemPrefab = CreateItemPrefab(folder);

            // 2. 창
            var root = new GameObject("Chat", typeof(RectTransform), typeof(Image), typeof(ChatManager), typeof(ChatUI));
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.zero;
            rootRect.pivot = Vector2.zero;
            rootRect.anchoredPosition = new Vector2(16f, 16f);
            rootRect.sizeDelta = new Vector2(WIDTH, HEIGHT);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            // 3. 스크롤 영역 (입력칸 위 ~ 손잡이 아래)
            var scroll = CreateChild("Scroll", root.transform, typeof(Image), typeof(ScrollRect));
            var scrollRectTransform = (RectTransform)scroll.transform;
            Stretch(scrollRectTransform, new Vector2(MARGIN, MARGIN * 2f + INPUT_HEIGHT), new Vector2(-MARGIN, -HANDLE_HEIGHT - 2f));
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 휠/드래그를 받기 위한 투명 배경

            var viewport = CreateChild("Viewport", scroll.transform, typeof(RectMask2D));
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect, Vector2.zero, Vector2.zero);

            var content = CreateChild("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;

            // 4. 입력칸
            var inputObject = TMP_DefaultControls.CreateInputField(resources);
            inputObject.name = "Input";
            inputObject.transform.SetParent(root.transform, false);
            var inputRect = (RectTransform)inputObject.transform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.offsetMin = new Vector2(MARGIN, MARGIN);
            inputRect.offsetMax = new Vector2(-(MARGIN * 2f + SEND_WIDTH), MARGIN + INPUT_HEIGHT);

            var input = inputObject.GetComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (input.placeholder is TMP_Text placeholder) placeholder.text = "Enter message...";

            // 5. 보내기 버튼
            var sendObject = TMP_DefaultControls.CreateButton(resources);
            sendObject.name = "Send";
            sendObject.transform.SetParent(root.transform, false);
            var sendRect = (RectTransform)sendObject.transform;
            sendRect.anchorMin = new Vector2(1f, 0f);
            sendRect.anchorMax = new Vector2(1f, 0f);
            sendRect.pivot = new Vector2(1f, 0f);
            sendRect.anchoredPosition = new Vector2(-MARGIN, MARGIN);
            sendRect.sizeDelta = new Vector2(SEND_WIDTH, INPUT_HEIGHT);

            var sendLabel = sendObject.GetComponentInChildren<TMP_Text>();
            if (sendLabel != null) sendLabel.text = "Send";

            // 6. 크기 조절 손잡이 (위쪽 가장자리)
            var handle = CreateChild("ResizeHandle", root.transform, typeof(Image), typeof(ChatResizeHandle));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, 1f);
            handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.pivot = new Vector2(0.5f, 1f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(0f, HANDLE_HEIGHT);
            handle.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

            // 7. 참조 연결
            var chatUI = root.GetComponent<ChatUI>();
            SetReference(chatUI, "chatManager", root.GetComponent<ChatManager>());
            SetReference(chatUI, "scrollRect", scrollRect);
            SetReference(chatUI, "content", contentRect);
            SetReference(chatUI, "itemPrefab", itemPrefab);
            SetReference(chatUI, "input", input);
            SetReference(chatUI, "sendButton", sendObject.GetComponent<Button>());

            var resizeHandle = handle.GetComponent<ChatResizeHandle>();
            SetReference(resizeHandle, "target", rootRect);
            SetReference(resizeHandle, "chatUI", chatUI);

            // 8. 저장
            string chatPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Chat.prefab");
            var chatPrefab = PrefabUtility.SaveAsPrefabAsset(root, chatPath);
            Object.DestroyImmediate(root);

            Dev.Log($"채팅 UI 프리팹을 만들었습니다: {chatPath}");

            Selection.activeObject = chatPrefab;
            EditorGUIUtility.PingObject(chatPrefab);
        }

        /// <summary> 한 줄: TextMeshProUGUI(줄바꿈) + ChatMessageItem. 높이는 글자 양에 맞춰 레이아웃이 정합니다. </summary>
        private static ChatMessageItem CreateItemPrefab(string folder)
        {
            var item = new GameObject("ChatMessageItem", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ChatMessageItem));

            var text = item.GetComponent<TextMeshProUGUI>();
            text.fontSize = 20f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            text.raycastTarget = false;
            text.text = "Player: message";

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/ChatMessageItem.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
            Object.DestroyImmediate(item);

            return prefab.GetComponent<ChatMessageItem>();
        }

        private static GameObject CreateChild(string name, Transform parent, params System.Type[] components)
        {
            var types = new System.Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            components.CopyTo(types, 1);

            var child = new GameObject(name, types);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary> private [SerializeField] 필드에 참조를 넣습니다. </summary>
        private static void SetReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Dev.LogWarn($"{target.GetType().Name}에 '{fieldName}' 필드가 없습니다.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary> Assets 안의 폴더를 고릅니다. 취소하거나 Assets 밖이면 null </summary>
        private static string SelectAssetFolder()
        {
            string absolute = EditorUtility.SaveFolderPanel("Chat 프리팹 저장 폴더 (Assets 안)", "Assets", "");
            if (string.IsNullOrEmpty(absolute)) return null;

            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            absolute = Path.GetFullPath(absolute).Replace('\\', '/');

            if (!absolute.StartsWith(dataPath))
            {
                Dev.LogError("Assets 폴더 안을 선택해야 합니다.");
                return null;
            }

            return "Assets" + absolute.Substring(dataPath.Length);
        }
    }
}
#endif
