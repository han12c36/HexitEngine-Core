using UnityEngine;
using TMPro;

public class ChatMessage : MonoBehaviour
{
    [SerializeField] private TMP_Text textField;

    public bool IsVisible => gameObject.activeSelf;
    public int Index => logEntry.Index;
    public TMP_Text TextField => textField;

    private float lineHeight;
    private ChatLogEntry logEntry;

    private void Awake()
    {
        textField.textWrappingMode = TextWrappingModes.NoWrap;
        textField.overflowMode = TextOverflowModes.Overflow;

        lineHeight = ((RectTransform)transform).rect.height;
    }

    public void SetMessage(ChatLogEntry logEntry)
    {
        this.logEntry = logEntry;

        UpdateVisual(logEntry.LineCount, logEntry.Text);
    }

    public void UpdateVisual(int lineCount, string text)
    {
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, lineHeight * Mathf.Max(1, lineCount));

        textField.text = text;

        gameObject.SetActive(true);
    }

    public void ClearMessage()
    {
        gameObject.SetActive(false);
    }
}