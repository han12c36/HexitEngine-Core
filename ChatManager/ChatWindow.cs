using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexitEngine.Sample
{
    [RequireComponent(typeof(RectTransform))]
    public class ChatWindow : MonoBehaviour, IPointerDownHandler
    {
        private const string SAVE_KEY_PREFIX = "HexitEngine.ChatWindow.";

        [Header("Size")]
        [Tooltip("최소 크기. 창 안의 입력칸, 버튼이 들어가는 크기로 지정합니다.")]
        [SerializeField] private Vector2 minSize = new(260f, 140f);
        [Tooltip("최대 크기. 0이면 제한 없음 (화면 크기까지)")]
        [SerializeField] private Vector2 maxSize = Vector2.zero;

        [Header("Bounds")]
        [Tooltip("부모 영역(화면) 밖으로 나가지 않게 합니다.")]
        [SerializeField] private bool keepInsideParent = true;

        [Header("Front")]
        [Tooltip("창을 누르면 같은 부모 안에서 맨 앞으로 가져옵니다.")]
        [SerializeField] private bool bringToFrontOnClick = true;

        [Header("Cursor")]
        [Tooltip("손잡이 위에서 커서 모양을 바꿉니다. 이 창의 손잡이 위에서만 바뀌고, 벗어나면 기본 커서로 되돌립니다.")]
        [SerializeField] private bool changeCursor = true;
        [Tooltip("헤더(이동) 커서 (✥). 커서 이미지는 Texture Type: Cursor로 가져옵니다. 비우면 이 종류는 커서를 바꾸지 않습니다. (아래 모두 동일)")]
        [SerializeField] private Texture2D moveCursor;
        [Tooltip("왼쪽/오른쪽 변 커서 (↔)")]
        [SerializeField] private Texture2D horizontalCursor;
        [Tooltip("위/아래 변 커서 (↕)")]
        [SerializeField] private Texture2D verticalCursor;
        [Tooltip("왼쪽 위 / 오른쪽 아래 모서리 커서 (↖↘)")]
        [SerializeField] private Texture2D diagonalMainCursor;
        [Tooltip("오른쪽 위 / 왼쪽 아래 모서리 커서 (↗↙)")]
        [SerializeField] private Texture2D diagonalSubCursor;

        [Header("Save")]
        [Tooltip("위치와 크기를 PlayerPrefs에 저장하고 다음에 열 때 복원합니다.")]
        [SerializeField] private bool saveLayout = false;
        [Tooltip("저장 이름. 같은 이름의 창끼리 위치와 크기를 공유합니다. (예: Ready와 Game의 채팅 창)")]
        [SerializeField] private string saveKey = "Chat";

        private RectTransform rectTransform;
        private RectTransform parentRect;

        private ChatWindowHandle.HandleMode dragMode;
        private bool isDragging;
        private Vector2 dragStartPointer;
        private Vector2 dragStartMin;
        private Vector2 dragStartMax;
        private Vector2 lastParentSize;

        private ChatWindowHandle hoveredHandle;
        private bool isCursorChanged;

        /// <summary> 마지막으로 확인한 선택 오브젝트 (창 안의 버튼/입력칸 선택 감지용) </summary>
        private GameObject lastSelected;

        /// <summary> 크기 조절 시작 (드래그, 복원, 화면 크기 변경). 내용이 맨 아래를 보고 있었는지 기록하는 등의 용도 </summary>
        public event Action OnResizeStarted;

        /// <summary> 크기가 바뀜 (드래그 중에는 매번) </summary>
        public event Action OnResized;

        /// <summary> 이동/크기 조절 드래그가 끝남 </summary>
        public event Action OnLayoutChanged;

        public bool IsDragging => isDragging;

        #region Lifecycle

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            parentRect = transform.parent as RectTransform;
        }

        private void Start()
        {
            // 부모 크기가 정해진 뒤 복원/보정
            Canvas.ForceUpdateCanvases();

            if (saveLayout) LoadLayout();
            else ClampToParent();

            if (parentRect != null) lastParentSize = parentRect.rect.size;
        }

        private void LateUpdate()
        {
            CheckSelectionFocus();

            if (parentRect == null) return;

            // 해상도/창 크기가 바뀌면 다시 화면 안으로
            var parentSize = parentRect.rect.size;
            if (parentSize == lastParentSize) return;

            lastParentSize = parentSize;
            ClampToParent();
        }

        /// <summary> Alt+Tab 등으로 포커스를 잃으면 손잡이에서 벗어남 이벤트가 오지 않을 수 있으므로 커서를 되돌림 </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;

            hoveredHandle = null;
            if (!isDragging) RestoreCursor();
        }

        private void OnDisable()
        {
            isDragging = false;
            hoveredHandle = null;
            RestoreCursor();
        }

        #endregion

        #region Focus

        /// <summary> 창의 빈 곳(배경, 스크롤 영역 등)을 누름 </summary>
        public void OnPointerDown(PointerEventData eventData) => BringToFront();

        /// <summary> 같은 부모 안에서 맨 앞으로 (옵션이 켜져 있을 때) </summary>
        public void BringToFront()
        {
            if (bringToFrontOnClick) transform.SetAsLastSibling();
        }

        /// <summary>
        /// 버튼, 입력칸처럼 스스로 클릭을 받는 자식은 창까지 클릭이 전달되지 않으므로,
        /// 창 안의 오브젝트가 새로 선택되면(클릭, 키보드 이동 포함) 맨 앞으로 가져옵니다.
        /// </summary>
        private void CheckSelectionFocus()
        {
            if (!bringToFrontOnClick || EventSystem.current == null) return;

            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == lastSelected) return;

            lastSelected = selected;
            if (selected != null && selected.transform.IsChildOf(transform)) BringToFront();
        }

        #endregion

        #region Drag (ChatWindowHandle이 호출)

        internal void BeginDrag(ChatWindowHandle.HandleMode mode, PointerEventData eventData)
        {
            if (parentRect == null || !TryGetPointerInParent(eventData, out dragStartPointer)) return;

            BringToFront();

            dragMode = mode;
            isDragging = true;
            GetEdges(out dragStartMin, out dragStartMax);

            if (mode != ChatWindowHandle.HandleMode.Move) OnResizeStarted?.Invoke();
        }

        internal void Drag(PointerEventData eventData)
        {
            if (!isDragging || !TryGetPointerInParent(eventData, out var pointer)) return;

            var delta = pointer - dragStartPointer;

            if (dragMode == ChatWindowHandle.HandleMode.Move) ApplyMove(delta);
            else
            {
                ApplyResize(delta);
                OnResized?.Invoke();
            }
        }

        internal void EndDrag()
        {
            if (!isDragging) return;
            isDragging = false;

            if (saveLayout) SaveLayout();
            OnLayoutChanged?.Invoke();

            // 손잡이 밖에서 놓았으면 커서 복원
            if (hoveredHandle == null) RestoreCursor();
            else SetCursorFor(hoveredHandle.Mode);
        }

        private void ApplyMove(Vector2 delta)
        {
            var min = dragStartMin + delta;
            var max = dragStartMax + delta;

            if (keepInsideParent) ShiftInside(ref min, ref max);

            SetEdges(min, max);
        }

        /// <summary> 잡은 변만 움직이고 반대쪽 변은 고정. 최소/최대 크기와 화면 끝에서 멈춤 </summary>
        private void ApplyResize(Vector2 delta)
        {
            var bounds = GetBounds();
            var min = dragStartMin;
            var max = dragStartMax;

            GetSizeLimits(bounds, out var minimum, out var maximum);

            ChatWindowHandle.GetEdges(dragMode, out bool left, out bool right, out bool bottom, out bool top);

            if (left)
            {
                float lowest = Mathf.Max(dragStartMax.x - maximum.x, keepInsideParent ? bounds.xMin : float.NegativeInfinity);
                float highest = dragStartMax.x - minimum.x;
                min.x = ClampOrdered(dragStartMin.x + delta.x, lowest, highest);
            }
            else if (right)
            {
                float lowest = dragStartMin.x + minimum.x;
                float highest = Mathf.Min(dragStartMin.x + maximum.x, keepInsideParent ? bounds.xMax : float.PositiveInfinity);
                max.x = ClampOrdered(dragStartMax.x + delta.x, lowest, highest);
            }

            if (bottom)
            {
                float lowest = Mathf.Max(dragStartMax.y - maximum.y, keepInsideParent ? bounds.yMin : float.NegativeInfinity);
                float highest = dragStartMax.y - minimum.y;
                min.y = ClampOrdered(dragStartMin.y + delta.y, lowest, highest);
            }
            else if (top)
            {
                float lowest = dragStartMin.y + minimum.y;
                float highest = Mathf.Min(dragStartMin.y + maximum.y, keepInsideParent ? bounds.yMax : float.PositiveInfinity);
                max.y = ClampOrdered(dragStartMax.y + delta.y, lowest, highest);
            }

            SetEdges(min, max);
        }

        #endregion

        #region Clamp

        /// <summary> 크기를 제한 안으로 줄이고 화면 안으로 옮깁니다. (복원, 화면 크기 변경 시) </summary>
        public void ClampToParent()
        {
            if (parentRect == null) return;

            OnResizeStarted?.Invoke();

            GetEdges(out var min, out var max);
            GetSizeLimits(GetBounds(), out var minimum, out var maximum);

            var size = max - min;
            size.x = Mathf.Clamp(size.x, minimum.x, Mathf.Max(minimum.x, maximum.x));
            size.y = Mathf.Clamp(size.y, minimum.y, Mathf.Max(minimum.y, maximum.y));

            // 위쪽(헤더)을 기준으로 크기 보정
            min = new Vector2(min.x, max.y - size.y);
            max = new Vector2(min.x + size.x, max.y);

            if (keepInsideParent) ShiftInside(ref min, ref max);

            SetEdges(min, max);
            OnResized?.Invoke();
        }

        /// <summary> 크기는 유지하고 화면 안으로 옮김. 화면보다 크면 왼쪽 위에 맞춤 (헤더가 보이도록) </summary>
        private void ShiftInside(ref Vector2 min, ref Vector2 max)
        {
            var bounds = GetBounds();
            var shift = Vector2.zero;

            if (min.x < bounds.xMin) shift.x = bounds.xMin - min.x;
            else if (max.x > bounds.xMax) shift.x = Mathf.Max(bounds.xMax - max.x, bounds.xMin - min.x);

            if (max.y > bounds.yMax) shift.y = bounds.yMax - max.y;
            else if (min.y < bounds.yMin) shift.y = Mathf.Min(bounds.yMin - min.y, bounds.yMax - max.y);

            min += shift;
            max += shift;
        }

        /// <summary> 최소/최대 크기 (최대가 0이면 제한 없음, 화면 안 유지 시 화면 크기까지) </summary>
        private void GetSizeLimits(Rect bounds, out Vector2 minimum, out Vector2 maximum)
        {
            minimum = new Vector2(Mathf.Max(0f, minSize.x), Mathf.Max(0f, minSize.y));

            maximum = new Vector2(
                maxSize.x > 0f ? maxSize.x : float.PositiveInfinity,
                maxSize.y > 0f ? maxSize.y : float.PositiveInfinity);

            if (keepInsideParent)
            {
                maximum.x = Mathf.Min(maximum.x, bounds.width);
                maximum.y = Mathf.Min(maximum.y, bounds.height);
            }

            // 화면이 최소 크기보다 작으면 화면에 맞춤
            minimum = Vector2.Min(minimum, maximum);
        }

        private static float ClampOrdered(float value, float lowest, float highest)
        {
            if (highest < lowest) highest = lowest;
            return Mathf.Clamp(value, lowest, highest);
        }

        #endregion

        #region Rect (부모 기준 좌표)

        private Rect GetBounds() => parentRect != null ? parentRect.rect : new Rect(0f, 0f, Screen.width, Screen.height);

        /// <summary> 창의 왼쪽 아래(min)와 오른쪽 위(max). 부모 기준 좌표 (Anchor, Pivot과 무관) </summary>
        private void GetEdges(out Vector2 min, out Vector2 max)
        {
            Vector2 position = rectTransform.localPosition;
            var rect = rectTransform.rect;

            min = position + rect.min;
            max = position + rect.max;
        }

        /// <summary> 창의 왼쪽 아래와 오른쪽 위를 지정합니다. Anchor는 그대로 두고 크기와 위치만 바꿉니다. </summary>
        private void SetEdges(Vector2 min, Vector2 max)
        {
            var size = max - min;

            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            var position = min + Vector2.Scale(size, rectTransform.pivot);
            rectTransform.localPosition = new Vector3(position.x, position.y, rectTransform.localPosition.z);
        }

        private bool TryGetPointerInParent(PointerEventData eventData, out Vector2 local)
        {
            local = default;
            return parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out local);
        }

        #endregion

        #region Save

        /// <summary> 저장 형식: 왼쪽, 아래, 너비, 높이 (부모 기준, Canvas 단위) </summary>
        private void SaveLayout()
        {
            if (string.IsNullOrEmpty(saveKey)) return;

            GetEdges(out var min, out var max);
            var size = max - min;

            string value = string.Join(",",
                min.x.ToString(CultureInfo.InvariantCulture),
                min.y.ToString(CultureInfo.InvariantCulture),
                size.x.ToString(CultureInfo.InvariantCulture),
                size.y.ToString(CultureInfo.InvariantCulture));

            PlayerPrefs.SetString(SAVE_KEY_PREFIX + saveKey, value);
            PlayerPrefs.Save();
        }

        /// <summary> 저장된 위치/크기를 복원한 뒤 화면 안으로 보정. 저장된 것이 없거나 형식이 틀리면 지금 위치 유지 </summary>
        private void LoadLayout()
        {
            string value = string.IsNullOrEmpty(saveKey) ? null : PlayerPrefs.GetString(SAVE_KEY_PREFIX + saveKey, null);

            if (!string.IsNullOrEmpty(value))
            {
                var parts = value.Split(',');

                if (parts.Length == 4
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
                    && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float height))
                {
                    SetEdges(new Vector2(x, y), new Vector2(x + width, y + height));
                }
            }

            ClampToParent();
        }

        /// <summary> 저장된 위치/크기를 지웁니다. (다음에 열 때 프리팹 기본값) </summary>
        public void ClearSavedLayout()
        {
            if (!string.IsNullOrEmpty(saveKey)) PlayerPrefs.DeleteKey(SAVE_KEY_PREFIX + saveKey);
        }

        #endregion

        #region Cursor (ChatWindowHandle이 호출)

        internal void HandlePointerEnter(ChatWindowHandle handle)
        {
            hoveredHandle = handle;

            // 끄는 중에는 잡은 손잡이의 커서 유지
            if (!isDragging) SetCursorFor(handle.Mode);
        }

        internal void HandlePointerExit(ChatWindowHandle handle)
        {
            if (hoveredHandle == handle) hoveredHandle = null;
            if (!isDragging) RestoreCursor();
        }

        internal void HandleBeginDragCursor(ChatWindowHandle handle) => SetCursorFor(handle.Mode);

        private void SetCursorFor(ChatWindowHandle.HandleMode mode)
        {
            if (!changeCursor) return;

            var texture = GetCursorTexture(mode);

            // 이 종류의 이미지가 없으면 이전 손잡이에서 바꾼 커서가 남지 않도록 되돌림
            if (texture == null)
            {
                RestoreCursor();
                return;
            }

            Cursor.SetCursor(texture, new Vector2(texture.width * 0.5f, texture.height * 0.5f), CursorMode.Auto);
            isCursorChanged = true;
        }

        /// <summary> 이 창이 바꾼 커서만 기본 커서로 되돌립니다. </summary>
        private void RestoreCursor()
        {
            if (!isCursorChanged) return;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            isCursorChanged = false;
        }

        /// <summary> 손잡이 모드에 맞는 커서 이미지. 지정되지 않았으면 null (커서를 바꾸지 않음) </summary>
        private Texture2D GetCursorTexture(ChatWindowHandle.HandleMode mode) => mode switch
        {
            ChatWindowHandle.HandleMode.Move => moveCursor,
            ChatWindowHandle.HandleMode.Left or ChatWindowHandle.HandleMode.Right => horizontalCursor,
            ChatWindowHandle.HandleMode.Top or ChatWindowHandle.HandleMode.Bottom => verticalCursor,
            ChatWindowHandle.HandleMode.TopLeft or ChatWindowHandle.HandleMode.BottomRight => diagonalMainCursor,
            _ => diagonalSubCursor,
        };

        #endregion
    }
}