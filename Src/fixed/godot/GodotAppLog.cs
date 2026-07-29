using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Fixed.Godot;

public sealed class GodotAppLog : IAppLog
{
    private readonly bool _enableVerbose;

    public GodotAppLog(bool? enableVerbose = null)
    {
        _enableVerbose = enableVerbose ?? OS.IsDebugBuild();
    }

    public void Debug(string message, string? category = null)
    {
        if (_enableVerbose)
            GD.Print(Format(message, category));
    }

    public void Info(string message, string? category = null)
    {
        if (_enableVerbose)
            GD.Print(Format(message, category));
    }

    public void Warning(string message, string? category = null) =>
        GD.PushWarning(Format(message, category));

    public void Error(string message, string? category = null) =>
        GD.PushError(Format(message, category));

    private static string Format(string message, string? category) =>
        string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
}