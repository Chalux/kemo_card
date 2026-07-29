namespace KemoCard.Frame.Logging;

public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    public void Debug(string message, string? category = null)
    {
    }

    public void Info(string message, string? category = null)
    {
    }

    public void Warning(string message, string? category = null)
    {
    }

    public void Error(string message, string? category = null)
    {
    }
}