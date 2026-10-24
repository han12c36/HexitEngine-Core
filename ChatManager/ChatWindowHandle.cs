using UnityEngine;
using UnityEngine.EventSystems;

namespace HexitEngine.Sample
{
    public class ChatWindowHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public enum HandleMode
        {
            Move,
            Top,
            Bottom,
            Left,
            Right,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        [SerializeField] private HandleMode mode = HandleMode.Move;
        [Tooltip("비우면 부모에서 찾습니다.")]
        [SerializeField] private ChatWindow window;

        public HandleMode Mode => mode;

        private void Awake()
        {
            if (window == null) window = GetComponentInParent<ChatWindow>();
            if (window == null) Dev.LogError($"'{name}'의 부모에 ChatWindow가 없습니다.");
        }

        #region Pointer

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (window != null) window.HandlePointerEnter(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (window != null) window.HandlePointerExit(this);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (window != null) window.BringToFront();
        }

        /// <summary> 조금만 움직여도 바로 따라오도록 드래그 시작 거리 없음 </summary>
        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (window == null) return;

            window.BeginDrag(mode, eventData);
            window.HandleBeginDragCursor(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (window != null) window.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (window != null) window.EndDrag();
        }

        #endregion

        #region Mode

        /// <summary> 모드가 움직이는 변 </summary>
        public static void GetEdges(HandleMode mode, out bool left, out bool right, out bool bottom, out bool top)
        {
            left = mode is HandleMode.Left or HandleMode.TopLeft or HandleMode.BottomLeft;
            right = mode is HandleMode.Right or HandleMode.TopRight or HandleMode.BottomRight;
            bottom = mode is HandleMode.Bottom or HandleMode.BottomLeft or HandleMode.BottomRight;
            top = mode is HandleMode.Top or HandleMode.TopLeft or HandleMode.TopRight;
        }

        #endregion
    }
}