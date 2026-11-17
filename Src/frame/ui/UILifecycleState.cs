namespace KemoCard.Frame.UI;

/// <summary>
/// UI 生命周期时间戳，从 UIVo 中拆出的独立对象。
/// </summary>
public sealed class UILifecycleState
{
    public long LoadTime { get; set; }
    public long CreateTime { get; set; }
    public long OpenTime { get; set; }
    /// <summary>开始关闭的时刻（进入 Close 状态），用于关闭超时看门狗。</summary>
    public long CloseStartTime { get; set; }
    /// <summary>关闭完成的时刻（进入 CloseDone 状态）；只在此处写入一次。</summary>
    public long CloseTime { get; set; }
    public long DestroyTime { get; set; }
}