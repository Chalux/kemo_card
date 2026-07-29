namespace KemoCard.Frame.UI;

/// <summary>
/// UI 生命周期时间戳，从 UIVo 中拆出的独立对象。
/// </summary>
public sealed class UILifecycleState
{
    public long LoadTime { get; set; }
    public long CreateTime { get; set; }
    public long OpenTime { get; set; }
    public long CloseTime { get; set; }
    public long DestroyTime { get; set; }

    /// <summary>UI 是否已经打开（Create ~ Open）</summary>
    public bool IsOpened => OpenTime > 0;

    /// <summary>UI 是否已经关闭</summary>
    public bool IsClosed => CloseTime > 0;
}