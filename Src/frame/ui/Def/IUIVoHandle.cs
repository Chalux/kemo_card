namespace KemoCard.Frame.UI.Def;

/// <summary>
/// UIVo对外暴露的最小接口，避免Def和UI循环引用
/// </summary>
public interface IUIVoHandle
{
    string Id { get; }
    EUIType Type { get; }
    object? Payload { get; }
    UIOpenOpt OpenOpt { get; }
}