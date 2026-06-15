using Godot;
using KemoCard.Frame.Mvc;

namespace KemoCard.Fixed.Godot;

public sealed class GodotEventDispatcherLogger : IEventDispatcherLogger
{
    public void LogError(string message) => GD.PushError(message);
}
