using System;

namespace KemoCard.Frame.Logging;

public static class AppLog
{
    private static IAppLog _implementation = NullAppLog.Instance;

    public static void Configure(IAppLog implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _implementation = implementation;
    }

    public static void Debug(string message, string? category = null) =>
        _implementation.Debug(message, category);

    public static void Info(string message, string? category = null) =>
        _implementation.Info(message, category);

    public static void Warning(string message, string? category = null) =>
        _implementation.Warning(message, category);

    public static void Error(string message, string? category = null) =>
        _implementation.Error(message, category);
}