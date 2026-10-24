using System.Text;
using TMPro;
using UnityEngine;

namespace HexitEngine.Sample
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ChatMessageItem : MonoBehaviour
    {
        private const string NOPARSE_OPEN = "<noparse>";
        private const string NOPARSE_CLOSE = "</noparse>";

        /// <summary> 모든 줄이 함께 쓰는 버퍼 (메인 스레드 전용) </summary>
        private static readonly StringBuilder builder = new(256);

        private TextMeshProUGUI text;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        public void Bind(ChatMessage message, Color nameColor, Color systemColor)
        {
            if (text == null) text = GetComponent<TextMeshProUGUI>();

            builder.Clear();

            if (message.IsSystem)
            {
                AppendColorOpen(systemColor);
                AppendSafe(message.Text);
                builder.Append("</color>");
            }
            else
            {
                AppendColorOpen(nameColor);
                builder.Append("<b>");
                AppendSafe(message.SenderName);
                builder.Append("</b></color>: ");
                AppendSafe(message.Text);
            }

            text.SetText(builder);
        }

        private static void AppendColorOpen(Color color)
        {
            builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>');
        }

        /// <summary> 서식 태그를 그대로 글자로 보이게 (유저 입력에 들어 있는 </noparse>는 무력화) </summary>
        private static void AppendSafe(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            builder.Append(NOPARSE_OPEN);
            builder.Append(value.Replace(NOPARSE_CLOSE, "</ noparse>"));
            builder.Append(NOPARSE_CLOSE);
        }
    }
}
