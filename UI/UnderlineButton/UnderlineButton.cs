using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class UnderlineButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("연결할 자식 텍스트")]
    [SerializeField] private TextMeshProUGUI targetText;

    public bool showUnderLine = true;

    private Button _cachedButton;

    public Button Button
    {
        get
        {
            if (_cachedButton == null) _cachedButton = GetComponent<Button>();
            return _cachedButton;
        }
    }

    private void OnEnable()
    {
        if (targetText != null && showUnderLine) 
            targetText.fontStyle &= ~FontStyles.Underline;
        if (!showUnderLine)
        {
            targetText.fontStyle &= ~FontStyles.Underline;
        }
    }
    
    private void OnDisable()
    {
        if (targetText != null) 
            targetText.fontStyle &= ~FontStyles.Underline;
        
        if (!showUnderLine)
        {
            targetText.fontStyle &= ~FontStyles.Underline;
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetText != null && showUnderLine && Button.interactable)
        {
            targetText.fontStyle |= FontStyles.Underline;
        }
        if (!showUnderLine)
        {
            targetText.fontStyle &= ~FontStyles.Underline;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetText != null && showUnderLine && Button.interactable) 
            targetText.fontStyle &= ~FontStyles.Underline;
        
        if (!showUnderLine)
        {
            targetText.fontStyle &= ~FontStyles.Underline;
        }
    }

    public void Reset()
    {
        if (targetText != null) 
            targetText.fontStyle &= ~FontStyles.Underline;
        if (!showUnderLine)
        {
            targetText.fontStyle &= ~FontStyles.Underline;
        }
    }
}