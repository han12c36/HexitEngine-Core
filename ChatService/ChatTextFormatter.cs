using System;
using System.Text;
using TMPro;
using UnityEngine;

public static class ChatTextFormatter
{
    /// <summary>
    /// 시간/이름/메세지를 받아서
    /// [HH:mm] Name : prefix + 수동 줄바꿈된 최종 텍스트와 라인 수를 계산한다.
    /// </summary>
    public static string BuildWrappedMessage(
        TMP_Text measureText,
        float maxWidth,
        int maxLines,
        DateTime time,
        string senderUserName,
        string message,
        out int lineCount)
    {
        lineCount = 1;

        string timeText = time.ToString("HH:mm");
        string prefix = $"<color=grey>[{timeText}]</color> {senderUserName} : ";

        if (measureText == null)
        {
            // TMP가 없으면 그냥 한 줄로만 반환
            lineCount = 1;
            return prefix + message;
        }

        int allowedLines = Mathf.Max(1, maxLines);

        StringBuilder sb = new StringBuilder();

        int currentLine = 1;
        string currentLineText = prefix;

        foreach (char ch in message)
        {
            string testLine = currentLineText + ch;
            float width = measureText.GetPreferredValues(testLine).x;

            if (width <= maxWidth)
            {
                currentLineText = testLine;
            }
            else
            {
                // 현재 줄 확정 + 줄바꿈
                sb.AppendLine(currentLineText);
                currentLine++;

                if (currentLine > allowedLines)
                {
                    // 더 이상 줄을 못 쓰면 ... 붙이고 종료
                    sb.Length = Math.Max(0, sb.Length - Environment.NewLine.Length);
                    sb.Append(" …");

                    lineCount = allowedLines;
                    return sb.ToString();
                }

                currentLineText = ch.ToString();
            }
        }

        sb.Append(currentLineText);
        lineCount = currentLine;
        return sb.ToString();
    }
}
