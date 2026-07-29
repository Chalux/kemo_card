using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;

namespace KemoCard.Fixed.Godot;

public sealed class GodotEventDispatcherLogger : IEventDispatcherLogger
{
    private readonly IAppLog _log;

    public GodotEventDispatcherLogger(IAppLog log)
    {
        _log = log;
    }

    public void LogError(string message) => _log.Error(message, "Mvc");
}