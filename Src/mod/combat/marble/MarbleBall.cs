namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 场上的一颗活跃弹珠：位置 / 速度 / 半径 / 点数 / 已接触撞钉集合 / 困球观察窗口。
/// 各活跃球之间默认互不碰撞（相互穿透），只与墙壁和撞钉碰撞。
/// </summary>
public sealed class MarbleBall
{
    private readonly HashSet<string> _contactedPegIds = new(StringComparer.Ordinal);

    internal MarbleBall(int id, MarbleBallSpec spec, MarbleVec position, MarbleVec velocity, MarblePhysicsConfig config)
    {
        Id = id;
        Spec = spec;
        Position = position;
        Velocity = velocity;
        Motion = new MarbleMotionWindow(config.StuckSamples + 1);
    }

    public int Id { get; }

    public MarbleBallSpec Spec { get; }

    public int Points => Spec.Points;

    public float Radius => Spec.Radius;

    public MarbleVec Position { get; internal set; }

    public MarbleVec Velocity { get; internal set; }

    /// <summary>
    /// 持续贴合去重集合：在接触范围内且已计过分的撞钉。
    /// 只有后续物理步中心距严格大于半径和 + <c>DetachMargin</c> 后才移出。
    /// </summary>
    public IReadOnlyCollection<string> ContactedPegIds => _contactedPegIds;

    /// <summary>接近困球阈值时给界面的提示状态（由盘面在每步结束后刷新）。</summary>
    public bool IsStuckWarning { get; internal set; }

    internal MarbleMotionWindow Motion { get; }

    internal bool TryRegisterContact(string pegId) => _contactedPegIds.Add(pegId);

    internal bool IsContacting(string pegId) => _contactedPegIds.Contains(pegId);

    internal void RemoveContact(string pegId) => _contactedPegIds.Remove(pegId);
}
