namespace KemoCard.Frame.UI.Def;

public interface IUIPayloadHandle
{
    string Id { get; }
    EUIType Type { get; }
    object? Payload { get; }
    UIOpenOpt OpenOpt { get; }
}