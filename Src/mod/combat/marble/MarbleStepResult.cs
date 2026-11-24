namespace KemoCard.Mod.Combat.Marble;

/// <summary>弹珠盘面的表现 / 调试事件基类：只携带值，不持有 Godot 对象。</summary>
public abstract record MarbleEvent;

/// <summary>
/// 一次有效撞钉接触并计分。<see cref="Gained"/> 与 <see cref="TotalCount"/>
/// 均按 <see cref="int.MaxValue"/> 饱和（乘积先用 long 计算）。
/// </summary>
public sealed record MarblePegHitEvent(
    int BallId,
    string PegId,
    int BallPoints,
    int PegPoints,
    int Gained,
    int TotalCount,
    MarbleVec Position) : MarbleEvent;

/// <summary>球体完全越过底部出口离盘；已得点数保留在盘面 <c>Count</c> 中。</summary>
public sealed record MarbleBallLeftEvent(int BallId, int Count) : MarbleEvent;

/// <summary>困球回收；已得点数保留在盘面 <c>Count</c> 中。</summary>
public sealed record MarbleBallStuckEvent(int BallId, int Count) : MarbleEvent;

/// <summary>一个固定物理步的结果。</summary>
public sealed record MarbleStepResult(IReadOnlyList<MarbleEvent> Events, int Count, int ActiveBallCount);

/// <summary>
/// 瞄准预测结果。
/// </summary>
/// <param name="Points">
/// 逐物理步的球心终点采样（首点为发射口；被路径上限截断的那一步不计入），用于实弹一致性检查。
/// </param>
/// <param name="PathLength">已展示路径的累计长度，不会超过预览上限。</param>
/// <param name="FirstCollisionPoint">首次碰撞点；只有落在已展示路径内时才返回，否则为 null。</param>
/// <param name="LeftBoard">预测是否推进到球体完全越过底部出口。</param>
public sealed record MarbleTrajectory(
    IReadOnlyList<MarbleVec> Points,
    float PathLength,
    MarbleVec? FirstCollisionPoint,
    bool LeftBoard)
{
    /// <summary>
    /// 逐段折线点（绘制用）：首点为发射口，之后是每个物理步内实际各段位移的终点
    /// （同一步内的撞击折返会产生多个顶点），末点为按路径上限精确裁剪后的位置。
    /// </summary>
    public IReadOnlyList<MarbleVec> PathPoints { get; init; } = Array.Empty<MarbleVec>();
}
