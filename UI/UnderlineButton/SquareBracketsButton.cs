using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class SquareBracketsButton : Button
{
    private TextMeshProUGUI buttonText;
    private string originalString;

    protected override void Awake()
    {
        base.Awake();

        // 버튼 하위에 있는 TextMeshPro 컴포넌트를 가져옵니다.
        buttonText = GetComponent<TextMeshProUGUI>();

        if (buttonText != null) originalString = buttonText.text;
    }

    // 1. 키보드/패드 방향키 등으로 버튼이 '선택'되었을 때
    public override void OnSelect(BaseEventData eventData)
    {
        base.OnSelect(eventData);
        ApplyHighlight();
    }

    // 2. 다른 버튼으로 이동해서 '선택 해제'되었을 때
    public override void OnDeselect(BaseEventData eventData)
    {
        base.OnDeselect(eventData);
        RemoveHighlight();
    }

    // 3. 마우스 커서가 버튼 위에 올라갔을 때 (선택사항이지만 보통 같이 처리함)
    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        ApplyHighlight();
    }

    // 4. 마우스 커서가 버튼에서 벗어났을 때
    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        RemoveHighlight();
    }

    // 텍스트에 [ ] 와 밑줄(Underline)을 적용하는 함수
    private void ApplyHighlight()
    {
        if (buttonText != null)
        {
            // <u> 태그는 TextMeshPro의 밑줄 Rich Text 입니다.
            buttonText.text = $"[ <u>{originalString}</u> ]";
        }
    }

    // 텍스트를 원래 상태로 되돌리는 함수
    private void RemoveHighlight()
    {
        if (buttonText != null)
        {
            buttonText.text = originalString;
        }
    }
}