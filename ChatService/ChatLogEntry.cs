using System;

public struct ChatLogEntry
{
    /// <summary> 해당 메세지가 ChatMessage의 History에서 몇 번째 인지 </summary>
    public int Index;

    /// <summary> 해당 메세지가 요청된 시간 </summary>
    public DateTime Time;

    /// <summary> 요청자의 UserName </summary>
    public string SenderUserName;

    /// <summary> 요청자의 원본 메세지 </summary>
    public string Message;

    /// <summary> TMP에 출력될 문자열 </summary>
    public string Text;

    /// <summary>  TMP에 출력될 문자열 줄 수 </summary>
    public int LineCount;

    public ChatLogEntry(int index, DateTime time, string senderUserName, string message, string text, int lineCount)
    {
        Index = index;
        Time = time;
        SenderUserName = senderUserName;
        Message = message;
        Text = text;
        LineCount = lineCount;
    }
}