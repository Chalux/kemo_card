using KemoCard.Mod.Combat.NormalAttack;

namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 战斗专属的弹珠运行时：独立盘面 + 每回合 FIFO 待发队列 + 本轮轮值归属。
/// </summary>
/// <remarks>
/// <para>
/// <b>归属</b>：每轮轮值角色口径与普攻一致（<c>(TurnNumber - 1) % 队伍人数</c>），
/// 因此被封印角色仍正常轮值；本轮全部弹珠（含队友技能添加的）都归属该角色。
/// </para>
/// <para>
/// <b>盘面生命周期</b>：撞钉布局属于整场战斗，跨回合、跨波保留；
/// <see cref="BeginTurn"/> 只重置本轮点数 / 活跃球 / 待发队列，不重建盘面。
/// 出牌期间清波不会调用 <see cref="BeginTurn"/>，因此本轮已入队的弹珠与盘面改动保留到弹珠阶段。
/// </para>
/// </remarks>
public sealed class MarbleRuntime
{
    /// <summary>瞄准预测的最大物理步数（防止零速 / 极端反弹导致预览无限推进）。</summary>
    public const int MaxPredictionSteps = 1800;

    private readonly MarbleBoard _board;
    private readonly MarbleQueue _queue;

    public MarbleRuntime(MarblePhysicsConfig? config = null, IEnumerable<MarblePegDefinition>? pegs = null)
    {
        _board = new MarbleBoard(config, pegs);
        _queue = new MarbleQueue(_board.Config.MaxBallRadius);
    }

    public MarblePhysicsConfig Config => _board.Config;

    public IReadOnlyList<MarblePeg> Pegs => _board.Pegs;

    public IReadOnlyList<MarbleBall> ActiveBalls => _board.ActiveBalls;

    /// <summary>本轮累计点数（每轮从 0 开始，不跨波继承；饱和到 <see cref="int.MaxValue"/>）。</summary>
    public int Count => _board.Count;

    public int StepIndex => _board.StepIndex;

    /// <summary>待发弹珠队列；入队追加到队尾，发射消耗队首。</summary>
    public MarbleQueue Queue => _queue;

    /// <summary>最近一次 <see cref="BeginTurn"/> 的回合号。</summary>
    public int TurnNumber { get; private set; }

    /// <summary>本轮轮值普攻角色槽位；无队伍时为 -1。</summary>
    public int OwnerSlotIndex { get; private set; } = -1;

    /// <summary>每轮基础弹珠属性（点数 / 半径）。</summary>
    public MarbleBallSpec BaseBallSpec => new(Config.BaseBallPoints, Config.BallRadius);

    /// <summary>场上无活跃球且队首属性有效时才可发射（多球同屏只由场上生成实现）。</summary>
    public bool CanLaunch => _board.ActiveBalls.Count == 0 && _queue.Count > 0;

    /// <summary>
    /// 进入新的玩家回合：刷新轮值归属、清空本轮点数与场上球，并按基础数量重新发放待发弹珠。
    /// 撞钉布局保持不变（跨回合 / 跨波继承）。
    /// </summary>
    public void BeginTurn(int turnNumber, int characterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterCount);
        TurnNumber = turnNumber;
        OwnerSlotIndex = NormalAttackRuntime.ResolveSlotIndex(turnNumber, characterCount);
        _board.ResetRound();
        _queue.Clear();
        for (var i = 0; i < Config.BaseBallsPerTurn; i++)
            _queue.Enqueue(BaseBallSpec);
    }

    /// <summary>通过效果向队列尾部追加一颗弹珠（队友 / 卡牌 / 技能共用）。</summary>
    public void EnqueueBall(MarbleBallSpec spec) => _queue.Enqueue(spec);

    /// <summary>
    /// 以任意有限角度发射队首弹珠并消耗 1 颗；场上已有活跃球或队列为空时返回 false。
    /// 角度 0 为 +X（右），<c>π/2</c> 为 +Y（下），不设象限限制。
    /// </summary>
    public bool TryLaunch(float angleRadians)
    {
        if (!CanLaunch || !float.IsFinite(angleRadians))
            return false;

        if (!_queue.TryDequeue(out var spec))
            return false;

        var velocity = MarbleVec.FromAngle(angleRadians) * Config.LaunchSpeed;
        _board.AddLaunchedBall(spec, velocity);
        return true;
    }

    /// <summary>推进一个固定 <c>1/60s</c> 物理步（界面按累加器决定每帧调用几次）。</summary>
    public MarbleStepResult Step() => _board.Step();

    /// <summary>
    /// 推进一个固定物理步，只更新盘面与点数（不构建表现事件 / 路径段）。
    /// 战斗模拟器使用；需要事件的调用方走 <see cref="Step"/>。
    /// </summary>
    internal void StepPhysics() => _board.StepPhysics();

    internal void RecallBalls()
    {
        _queue.Clear();
        _board.RecallBalls();
    }

    /// <summary>
    /// 无副作用的下一颗发射预览：复制队首球属性，在临时球上复用实弹的
    /// <see cref="MarblePhysics.Advance"/> 步进，不写盘面、不改队列、不计分。
    /// </summary>
    /// <param name="angleRadians">发射角度（弧度）。</param>
    /// <param name="maxPathLength">累计路径长度上限；达到后在最后一段内精确裁剪。</param>
    /// <remarks>
    /// <see cref="MarbleTrajectory.Points"/> 保留每个完整物理步的终点供实弹一致性检查；
    /// <see cref="MarbleTrajectory.PathPoints"/> 收集 Advance 给出的实际各段位移终点
    /// （含同一步内的撞击折返与裁剪点），供界面绘制准确折线。
    /// 预测与实弹使用同一困球判定，避免对已经卡死的球空转到最大步数。
    /// </remarks>
    public MarbleTrajectory? PredictNextLaunch(float angleRadians, float maxPathLength = 4000f)
    {
        if (!CanLaunch || !float.IsFinite(angleRadians) ||
            !float.IsFinite(maxPathLength) || maxPathLength <= 0f)
        {
            return null;
        }

        var spec = _queue[0];
        var ball = new MarbleBall(
            -1,
            spec,
            Config.LaunchOrigin,
            MarbleVec.FromAngle(angleRadians) * Config.LaunchSpeed,
            Config);
        var stepPoints = new List<MarbleVec> { ball.Position };
        var pathPoints = new List<MarbleVec> { ball.Position };
        var pathLength = 0d;
        MarbleVec? firstCollision = null;
        var leftBoard = false;

        for (var step = 0; step < MaxPredictionSteps; step++)
        {
            var advance = MarblePhysics.Advance(
                Config, _board.Pegs, ball, MarblePhysicsConfig.FixedDelta, scoreContacts: false);

            var remaining = maxPathLength - pathLength;
            var displayedSegments = 0;
            var clipped = false;
            foreach (var segment in advance.PathSegments)
            {
                if (segment.Length <= remaining)
                {
                    pathPoints.Add(segment.End);
                    pathLength += segment.Length;
                    remaining = maxPathLength - pathLength;
                    displayedSegments++;
                    continue;
                }

                if (remaining > 0d)
                {
                    var ratio = (float)(remaining / segment.Length);
                    pathPoints.Add(segment.Start + (segment.End - segment.Start) * ratio);
                }

                pathLength = maxPathLength;
                clipped = true;
                break;
            }

            if (firstCollision is null && advance.FirstCollisionPoint is { } collisionPoint &&
                advance.FirstCollisionSegmentIndex < displayedSegments)
            {
                // 首次撞点必须落在已展示的折线段内；被路径上限裁掉的落点不提示。
                firstCollision = collisionPoint;
            }

            if (advance.LeftBoard)
            {
                leftBoard = !clipped;
                break;
            }

            if (!clipped)
                stepPoints.Add(ball.Position);

            if (clipped || pathLength >= maxPathLength)
                break;

            if (ball.Motion.IsNearlyStill(
                    Config.StuckSamples,
                    Config.StuckDisplacementTolerance,
                    Config.StuckPathTolerance))
            {
                break;
            }
        }

        return new MarbleTrajectory(
            stepPoints,
            (float)Math.Min(maxPathLength, pathLength),
            firstCollision,
            leftBoard)
        {
            PathPoints = pathPoints,
        };
    }

    /// <summary>场上直接生成一颗球（分裂 / 命中效果）；达到活跃上限（硬上限 20，可下调）时被抑制。</summary>
    public bool TrySpawnActive(MarbleBallSpec spec, MarbleVec position, MarbleVec velocity) =>
        _board.TrySpawnActive(spec, position, velocity);

    /// <summary>接近困球阈值的提示状态（界面画微弱闪烁 / 进度环用）。</summary>
    public bool IsStuckWarning(MarbleBall ball) => ball.IsStuckWarning;
}