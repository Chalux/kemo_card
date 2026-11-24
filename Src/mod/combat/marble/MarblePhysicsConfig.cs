namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 弹珠盘面的物理与首轮参数（全部集中在这里，便于原型期调整）。
/// 步长固定为 <see cref="StepsPerSecond"/> 帧/秒；模拟不读渲染帧、不消耗随机数。
/// </summary>
public sealed record MarblePhysicsConfig
{
    /// <summary>固定物理步长：60 帧/秒。</summary>
    public const int StepsPerSecond = 60;

    /// <summary>单个物理步的时长（<c>1/60s</c>）。</summary>
    public const float FixedDelta = 1f / StepsPerSecond;

    #region 盘面与发射

    /// <summary>盘面逻辑宽度（与分辨率无关）。</summary>
    public float Width { get; init; } = 360f;

    /// <summary>盘面逻辑高度；底部是开放离盘出口。</summary>
    public float Height { get; init; } = 540f;

    /// <summary>悬空发射口：离顶壁预留垂直空间，允许向斜上 / 正上射出后撞顶折返。</summary>
    public MarbleVec LaunchOrigin { get; init; } = new(180f, 56f);

    /// <summary>固定发射速率；玩家只调整角度。</summary>
    public float LaunchSpeed { get; init; } = 560f;

    /// <summary>发射口保护区半径（相对最大弹珠半径再加的余量）。</summary>
    public float LaunchClearance { get; init; } = 10f;

    /// <summary>每轮基础发放球数。</summary>
    public int BaseBallsPerTurn { get; init; } = 2;

    /// <summary>基础弹珠点数（首轮试验值）。</summary>
    public int BaseBallPoints { get; init; } = 1;

    #endregion

    #region 物理

    /// <summary>竖向重力加速度（逻辑单位/秒²）。</summary>
    public float Gravity { get; init; } = 900f;

    /// <summary>最高速度阈值：每步移动前钳制，避免加速后单步穿越过远。</summary>
    public float MaxSpeed { get; init; } = 1600f;

    /// <summary>墙壁恢复系数（&lt; 1 表示轻微动能损失）。</summary>
    public float WallRestitution { get; init; } = 0.92f;

    /// <summary>撞钉恢复系数（&lt; 1 表示轻微动能损失）。</summary>
    public float PegRestitution { get; init; } = 0.94f;

    /// <summary>单个物理步内连续碰撞迭代上限；超出则截断该步剩余位移以防卡死。</summary>
    public int MaxCollisionsPerStep { get; init; } = 8;

    /// <summary>接触判定容差（擦边 / 浮点误差）。</summary>
    public float ContactEpsilon { get; init; } = 0.5f;

    /// <summary>脱离容差：中心距必须严格大于半径和 + 此值才移出 <c>ContactedPegIds</c>。</summary>
    public float DetachMargin { get; init; } = 0.5f;

    /// <summary>碰撞后沿法线推离的距离，避免同一位置反复触发。</summary>
    public float ContactSkin { get; init; } = 0.05f;

    #endregion

    #region 半径上限与活跃球

    public float BallRadius { get; init; } = 8f;

    /// <summary>弹珠最大半径（所有成长都受此上限约束）。</summary>
    public float MaxBallRadius { get; init; } = 16f;

    public float PegRadius { get; init; } = 12f;

    /// <summary>撞钉最大半径（所有成长都受此上限约束）。</summary>
    public float MaxPegRadius { get; init; } = 28f;

    /// <summary>场上同时活跃弹珠上限；生成时还会按 <see cref="MaxActiveBallsLimit"/> 硬截断。</summary>
    public int MaxActiveBalls { get; init; } = 20;

    /// <summary>场上同时活跃弹珠的硬上限（暂定 20）；配置只能下调实际生效值。</summary>
    public const int MaxActiveBallsLimit = 20;

    #endregion

    #region 困球回收

    /// <summary>连续几乎没有移动多久判定为困球（秒）。</summary>
    public float StuckWindowSeconds { get; init; } = 3f;

    /// <summary>接近困球阈值多久开始提示（秒）。</summary>
    public float StuckWarningSeconds { get; init; } = 1.5f;

    /// <summary>观察窗口内球心净位移容差。</summary>
    public float StuckDisplacementTolerance { get; init; } = 6f;

    /// <summary>观察窗口内实际行进距离容差：狭缝高速往返会因路径过长而不算困球。</summary>
    public float StuckPathTolerance { get; init; } = 24f;

    /// <summary>提示窗口内球心净位移容差。</summary>
    public float StuckWarningDisplacementTolerance { get; init; } = 4f;

    /// <summary>提示窗口内实际行进距离容差。</summary>
    public float StuckWarningPathTolerance { get; init; } = 12f;

    /// <summary>困球判定窗口的物理步数（3 秒 = 180 步）。</summary>
    public int StuckSamples => Math.Max(2, (int)MathF.Round(StuckWindowSeconds * StepsPerSecond));

    /// <summary>困球提示窗口的物理步数（1.5 秒 = 90 步）。</summary>
    public int StuckWarningSamples =>
        Math.Clamp((int)MathF.Round(StuckWarningSeconds * StepsPerSecond), 1, StuckSamples - 1);

    #endregion

    public static MarblePhysicsConfig Default { get; } = new();

    /// <summary>参数合法性检查；非法配置在进入战斗前明确失败，而不是运行期产生 NaN。</summary>
    public void Validate()
    {
        if (!float.IsFinite(Width) || Width <= 0f)
            throw new ArgumentException("弹珠盘面宽度必须为正有限值。", nameof(Width));
        if (!float.IsFinite(Height) || Height <= 0f)
            throw new ArgumentException("弹珠盘面高度必须为正有限值。", nameof(Height));
        if (!LaunchOrigin.IsFinite)
            throw new ArgumentException("发射口坐标必须为有限值。", nameof(LaunchOrigin));
        if (!float.IsFinite(LaunchSpeed) || LaunchSpeed <= 0f)
            throw new ArgumentException("发射速率必须为正有限值。", nameof(LaunchSpeed));
        if (!float.IsFinite(LaunchClearance) || LaunchClearance < 0f)
            throw new ArgumentException("发射口保护区半径必须为非负有限值。", nameof(LaunchClearance));
        if (!float.IsFinite(MaxSpeed) || MaxSpeed < LaunchSpeed)
            throw new ArgumentException("最高速度阈值必须不小于发射速率。", nameof(MaxSpeed));
        if (!float.IsFinite(Gravity) || Gravity < 0f)
            throw new ArgumentException("重力加速度不能为负。", nameof(Gravity));
        if (!float.IsFinite(WallRestitution) || WallRestitution is < 0f or > 1f ||
            !float.IsFinite(PegRestitution) || PegRestitution is < 0f or > 1f)
            throw new ArgumentException("恢复系数必须在 [0, 1] 内。");
        if (MaxCollisionsPerStep < 1)
            throw new ArgumentException("单个物理步的连续碰撞迭代上限至少为 1。", nameof(MaxCollisionsPerStep));
        if (!float.IsFinite(ContactEpsilon) || ContactEpsilon < 0f ||
            !float.IsFinite(DetachMargin) || DetachMargin < 0f ||
            !float.IsFinite(ContactSkin) || ContactSkin < 0f)
            throw new ArgumentException("碰撞容差必须为非负有限值。");
        if (!float.IsFinite(BallRadius) || BallRadius <= 0f ||
            !float.IsFinite(MaxBallRadius) || MaxBallRadius < BallRadius)
            throw new ArgumentException("弹珠半径必须为正，且最大半径不得小于基础半径。");
        if (!float.IsFinite(PegRadius) || PegRadius <= 0f ||
            !float.IsFinite(MaxPegRadius) || MaxPegRadius < PegRadius)
            throw new ArgumentException("撞钉半径必须为正，且最大半径不得小于基础半径。");
        if (Width <= 2f * MaxBallRadius || Height <= 2f * MaxBallRadius)
            throw new ArgumentException("盘面必须容纳最大半径的弹珠。");
        if (MaxActiveBalls < 1)
            throw new ArgumentException("活跃弹珠上限至少为 1。", nameof(MaxActiveBalls));
        if (BaseBallsPerTurn < 0 || BaseBallPoints < 0)
            throw new ArgumentException("基础发放球数与基础点数不能为负。");
        if (!float.IsFinite(StuckWindowSeconds) || StuckWindowSeconds <= 0f ||
            !float.IsFinite(StuckWarningSeconds) || StuckWarningSeconds <= 0f ||
            StuckWarningSeconds >= StuckWindowSeconds)
        {
            throw new ArgumentException("困球提示窗口必须为正且小于困球判定窗口。");
        }
        if (!float.IsFinite(StuckDisplacementTolerance) || StuckDisplacementTolerance < 0f ||
            !float.IsFinite(StuckPathTolerance) || StuckPathTolerance < 0f ||
            !float.IsFinite(StuckWarningDisplacementTolerance) || StuckWarningDisplacementTolerance < 0f ||
            !float.IsFinite(StuckWarningPathTolerance) || StuckWarningPathTolerance < 0f)
        {
            throw new ArgumentException("困球容差必须为非负有限值。");
        }

        // 发射口悬空要求 + 保护区：任何最大半径的球都从离顶壁足够远处出射。
        if (LaunchOrigin.Y < MaxBallRadius + LaunchClearance)
            throw new ArgumentException("发射口距顶壁的空间不足（需大于最大弹珠半径加保护区）。", nameof(LaunchOrigin));
        if (LaunchOrigin.X < MaxBallRadius || LaunchOrigin.X > Width - MaxBallRadius)
            throw new ArgumentException("发射口必须完全位于左右墙壁之间。", nameof(LaunchOrigin));
        if (LaunchOrigin.Y > Height - MaxBallRadius)
            throw new ArgumentException("发射口必须位于盘面内。", nameof(LaunchOrigin));
    }
}
