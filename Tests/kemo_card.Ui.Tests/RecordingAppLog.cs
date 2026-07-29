using System.Collections.Generic;
using KemoCard.Frame.Logging;

namespace KemoCard.Ui.Tests;

public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record AppLogEntry(AppLogLevel Level, string Message, string? Category);

public sealed class RecordingAppLog : IAppLog
{
    public List<AppLogEntry> Entries { get; } = new();

    public void Debug(string message, string? category = null) =>
        Entries.Add(new AppLogEntry(AppLogLevel.Debug, message, category));

    public void Info(string message, string? category = null) =>
        Entries.Add(new AppLogEntry(AppLogLevel.Info, message, category));

    public void Warning(string message, string? category = null) =>
        Entries.Add(new AppLogEntry(AppLogLevel.Warning, message, category));

    public void Error(string message, string? category = null) =>
        Entries.Add(new AppLogEntry(AppLogLevel.Error, message, category));
}