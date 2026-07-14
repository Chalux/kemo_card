namespace KemoCard.Frame.Logging;

public interface IAppLog
{
    void Debug(string message, string? category = null);
    void Info(string message, string? category = null);
    void Warning(string message, string? category = null);
    void Error(string message, string? category = null);
}
