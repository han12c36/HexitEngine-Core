using Steamworks.Data;

public struct LobbyResult
{
    public bool IsSuccess;
    public int RawErrorCode;
    public string ErrorSource;
    public string Message;

    public Lobby? LobbyData { get; set; }

    public static LobbyResult Success(Lobby? lobby) =>
        new LobbyResult { IsSuccess = true, RawErrorCode = 0, ErrorSource = string.Empty, LobbyData = lobby };

    public static LobbyResult Fail(int rawErrorCode, string errorSource, string message) =>
        new LobbyResult { IsSuccess = false, RawErrorCode = rawErrorCode, ErrorSource = errorSource, Message = message, LobbyData = null };
}