using System.Text;
using TMPro;
using UnityEngine;

namespace HexitEngine.Sample
{
    /// <summary>
    /// [샘플] 채팅 한 줄. 프리팹 루트에 TextMeshProUGUI와 함께 둡니다.
    /// - 높이는 TextMeshProUGUI가 글자 양과 폭에 맞춰 레이아웃에 알려주므로 따로 계산하지 않습니다. (줄바꿈은 Wrapping을 켜 둠)
    /// - 이름과 내용은 유저가 정한 문자열이라 서식 태그(<color> 등)가 먹히지 않도록 noparse로 감쌉니다.
    /// - ChatUI가 줄을 재사용하므로 Bind는 매번 모든 표시를 다시 씁니다.
    /// </summary>
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
