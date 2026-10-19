using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems; // 포인터 이벤트를 위해 추가

// 유니티 기본 Button을 상속받습니다.
public class HoverButton : Button
{
    // 마우스가 현재 버튼 위에 있는지 추적하는 변수
    private bool isPointerInside = false;

    // 마우스가 들어올 때
    public override void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        //SoundManager.Singleton.PlaySfx2D("SFX008");
        base.OnPointerEnter(eventData);
    }

    // 마우스가 나갈 때
    public override void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        base.OnPointerExit(eventData);
    }

    // 상태 전환 가로채기
    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (!gameObject.activeInHierarchy || targetGraphic == null)
            return;

        Color targetColor = colors.normalColor;

        switch (state)
        {
            case SelectionState.Normal:
                targetColor = colors.normalColor;
                break;
            case SelectionState.Highlighted:
                targetColor = colors.highlightedColor;
                break;
            case SelectionState.Pressed:
                targetColor = colors.pressedColor;
                break;
            case SelectionState.Selected:
                // 핵심 수정 포인트:
                // 유니티가 강제로 Selected 상태를 유지하더라도, 
                // 마우스가 버튼 위에 있다면 하이라이트 색상을 강제로 덮어씌웁니다.
                // 마우스가 밖에 있다면 노멀 색상으로 돌려놓습니다.
                targetColor = isPointerInside ? colors.highlightedColor : colors.normalColor;
                break;
            case SelectionState.Disabled:
                targetColor = colors.disabledColor;
                break;
        }

        // 유니티의 연산 간섭 없이 지정한 색상으로 덮어씌우기
        targetGraphic.color = targetColor;
    }
}