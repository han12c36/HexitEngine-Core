using UnityEngine;
using UnityEngine.EventSystems;

namespace HexitEngine.Sample
{
    /// <summary>
    /// [샘플] 채팅 창 위아래 크기 조절 손잡이. 창의 위쪽 가장자리에 얇은 Image(Raycast Target 켜짐)로 두고 이 컴포넌트를 붙입니다.
    /// - 끌어 올리면 창이 위로 커지고, 내리면 줄어듭니다. 창의 아래쪽 가장자리는 고정됩니다. (창의 Pivot과 무관)
    /// - 크기를 바꿀 때 맨 아래를 보고 있었다면 맨 아래를 유지합니다. (chatUI 지정 시)
    /// </summary>
    public class ChatResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [Tooltip("크기를 바꿀 채팅 창 (비우면 부모)")]
        [SerializeField] private RectTransform target;
        [SerializeField] private ChatUI chatUI;

        [SerializeField, Min(0f)] private float minHeight = 120f;
        [SerializeField, Min(0f)] private float maxHeight = 600f;

        private Canvas canvas;
        private bool keepBottom;

        private void Awake()
        {
            if (target == null) target = transform.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            keepBottom = chatUI != null && chatUI.IsAtBottom();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null) return;

            float scale = canvas != null ? canvas.scaleFactor : 1f;
            float delta = eventData.delta.y / Mathf.Max(scale, 0.0001f);

            float current = target.rect.height;
            float next = Mathf.Clamp(current + delta, minHeight, Mathf.Max(minHeight, maxHeight));
            float applied = next - current;

            if (Mathf.Approximately(applied, 0f)) return;

            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, next);

            // 아래쪽 가장자리 고정: 높이가 늘어난 만큼 Pivot 비율로 위치 보정
            target.anchoredPosition += new Vector2(0f, applied * target.pivot.y);

            if (keepBottom) chatUI.RequestScrollToBottom();
        }
    }
}
