namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 弹珠盘面：四枚固定钉 + 场上活跃球 + 本轮累计点数。
/// 盘面本身跨回合 / 跨波保留（<see cref="ResetRound"/> 只清本轮球与点数，不动撞钉）；
/// 模拟固定 60Hz 步进，不读取渲染帧、不依赖 Godot、不消耗随机数。
/// </summary>
public sealed class MarbleBoard
{
    private readonly List<MarbleBall> _balls = [];
    private readonly MarblePeg[] _pegs;
    private readonly MarbleAdvanceResult _stepResult = new();
    private readonly List<string> _detachScratch = [];
    private int _nextBallId;

    public MarbleBoard(MarblePhysicsConfig? config = null, IEnumerable<MarblePegDefinition>? pegs = null)
    {
        Config = config ?? MarblePhysicsConfig.Default;
        Config.Validate();

        var definitions = pegs?.ToList() ?? MarbleBoardLayout.CreateDefaultPegs(Config).ToList();
        _pegs = definitions.Select((definition, index) => new MarblePeg(definition, index)).ToArray();
        ValidatePegs();
    }

    public MarblePhysicsConfig Config { get; }

    public IReadOnlyList<MarblePeg> Pegs => _pegs;

    public IReadOnlyList<MarbleBall> ActiveBalls => _balls;

    /// <summary>本轮累计点数（每轮从 0 开始，不跨波继承；达到 <see cref="int.MaxValue"/> 后饱和）。</summary>
    public int Count { get; private set; }

    /// <summary>累计物理步数（诊断 / 界面闪烁节拍；跨轮不清零）。</summary>
    public int StepIndex { get; private set; }

    #region 回合与生成

    /// <summary>开始新一轮：清空场上球与本轮点数，保留全部撞钉（跨波 / 跨回合继承）。</summary>
    public void ResetRound()
    {
        RecallBalls();
        Count = 0;
    }

    internal void RecallBalls() => _balls.Clear();

    /// <summary>
    /// 场上直接生成一颗活跃弹珠（分裂 / 命中效果）。属性无效、半径超过盘面上限
    /// 或达到活跃上限时返回 false，抑制生成。
    /// 生成位置会按该球实际半径做墙壁裁剪（底部开放，允许生成在底界之外），
    /// 并把重叠 / 贴合的撞钉预先记入 <c>ContactedPegIds</c>，避免第 0 帧原地重复计分。
    /// </summary>
    public bool TrySpawnActive(MarbleBallSpec spec, MarbleVec position, MarbleVec velocity)
    {
        if (!spec.IsValid || spec.Radius > Config.MaxBallRadius)
            return false;
        // 设计硬上限 20：配置只能下调实际生效值，不能突破。
        if (_balls.Count >= Math.Min(Config.MaxActiveBalls, MarblePhysicsConfig.MaxActiveBallsLimit))
            return false;
        if (!position.IsFinite || !velocity.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "生成位置与速度必须为有限值。");

        var ball = new MarbleBall(_nextBallId++, spec, position, velocity, Config);
        LegalizeSpawn(ball);
        _balls.Add(ball);
        return true;
    }

    /// <summary>发射口出射：由 <see cref="MarbleRuntime.TryLaunch"/> 在队列出队后调用。</summary>
    internal MarbleBall AddLaunchedBall(MarbleBallSpec spec, MarbleVec velocity)
    {
        if (!spec.IsValid || spec.Radius > Config.MaxBallRadius)
            throw new ArgumentOutOfRangeException(nameof(spec), "弹珠属性无效或半径超过盘面上限。");

        var ball = new MarbleBall(_nextBallId++, spec, Config.LaunchOrigin, velocity, Config);
        _balls.Add(ball);
        return ball;
    }

    private void LegalizeSpawn(MarbleBall ball)
    {
        ball.Position = ClampToBoard(ball.Position, ball.Radius);
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var moved = false;
            foreach (var peg in _pegs)
            {
                var delta = ball.Position - peg.Position;
                var limit = ball.Radius + peg.Radius;
                var distance = delta.Length;
                if (distance > limit + Config.ContactEpsilon)
                    continue;

                // 生成瞬间贴住的钉先记入接触集合：即使被推出，也不在第 0 帧重复计分。
                ball.TryRegisterContact(peg.Id);
                if (distance >= limit + Config.ContactEpsilon)
                    continue;

                var normal = distance > 1e-4f ? delta * (1f / distance) : new MarbleVec(0f, -1f);
                ball.Position = peg.Position + normal * (limit + Config.ContactEpsilon);
                moved = true;
            }

            ball.Position = ClampToBoard(ball.Position, ball.Radius);
            if (!moved)
                break;
        }
    }

    /// <summary>
    /// 按球的实际半径做合法化：左右与顶部按半径裁剪；底部是开放离盘出口，
    /// 不设上限，允许球生成在底界之外（下一次步进即按离盘回收）。
    /// </summary>
    private MarbleVec ClampToBoard(MarbleVec position, float radius)
    {
        var maxX = MathF.Max(radius, Config.Width - radius);
        return new MarbleVec(
            Math.Clamp(position.X, radius, maxX),
            MathF.Max(radius, position.Y));
    }

    #endregion

    #region 物理步进

    /// <summary>推进一个固定 <c>1/60s</c> 物理步，返回本步事件与最新累计点数。</summary>
    public MarbleStepResult Step()
    {
        StepIndex++;
        var events = new List<MarbleEvent>();

        foreach (var ball in _balls.ToArray())
        {
            var advance = MarblePhysics.Advance(
                Config, _pegs, ball, MarblePhysicsConfig.FixedDelta, scoreContacts: true);

            foreach (var contact in advance.PegContacts)
            {
                // 先以 long 计算乘积与总和，再按 int 上限饱和：避免 int 乘法先溢出。
                var gained64 = (long)ball.Points * contact.Peg.Points;
                var gained = SaturateToInt(gained64);
                Count = SaturateToInt((long)Count + gained64);
                events.Add(new MarblePegHitEvent(
                    ball.Id,
                    contact.Peg.Id,
                    ball.Points,
                    contact.Peg.Points,
                    gained,
                    Count,
                    contact.Position));
            }

            if (advance.LeftBoard)
            {
                _balls.Remove(ball);
                events.Add(new MarbleBallLeftEvent(ball.Id, Count));
                continue;
            }

            ball.IsStuckWarning = ball.Motion.IsNearlyStill(
                Config.StuckWarningSamples,
                Config.StuckWarningDisplacementTolerance,
                Config.StuckWarningPathTolerance);
            if (ball.Motion.IsNearlyStill(
                Config.StuckSamples,
                Config.StuckDisplacementTolerance,
                Config.StuckPathTolerance))
            {
                _balls.Remove(ball);
                events.Add(new MarbleBallStuckEvent(ball.Id, Count));
            }
        }

        return new MarbleStepResult(events, Count, _balls.Count);
    }

    /// <summary>
    /// 固定步进的轻量版本：只更新盘面状态与点数，不生成表现事件、不收集折线路径。
    /// 供战斗模拟器按帧推进使用（当前无人消费逐步事件）；需要撞钉 / 离盘事件的调用方走
    /// <see cref="Step"/>。复用结果对象与接触集合临时列表，避免每球每步的小对象分配。
    /// </summary>
    internal void StepPhysics()
    {
        StepIndex++;
        // 倒序删除避免 ToArray 快照；各球互不碰撞，倒序不影响物理结果。
        for (var i = _balls.Count - 1; i >= 0; i--)
        {
            var ball = _balls[i];
            var advance = MarblePhysics.Advance(
                Config,
                _pegs,
                ball,
                MarblePhysicsConfig.FixedDelta,
                scoreContacts: true,
                collectPath: false,
                scratch: _stepResult,
                detachScratch: _detachScratch);

            foreach (var contact in advance.PegContacts)
            {
                // 先以 long 计算乘积与总和，再按 int 上限饱和：避免 int 乘法先溢出。
                var gained64 = (long)ball.Points * contact.Peg.Points;
                Count = SaturateToInt((long)Count + gained64);
            }

            if (advance.LeftBoard)
            {
                _balls.RemoveAt(i);
                continue;
            }

            ball.IsStuckWarning = ball.Motion.IsNearlyStill(
                Config.StuckWarningSamples,
                Config.StuckWarningDisplacementTolerance,
                Config.StuckWarningPathTolerance);
            if (ball.Motion.IsNearlyStill(
                Config.StuckSamples,
                Config.StuckDisplacementTolerance,
                Config.StuckPathTolerance))
            {
                _balls.RemoveAt(i);
            }
        }
    }

    /// <summary>把 long 口径的累计值按 int 上下限饱和，作为点数 / 事件增量的统一口径。</summary>
    private static int SaturateToInt(long value) =>
        (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    #endregion

    #region 配置校验

    private void ValidatePegs()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var peg in _pegs)
        {
            if (!ids.Add(peg.Id))
                throw new ArgumentException($"撞钉 id 重复：{peg.Id}", nameof(_pegs));
            if (peg.Radius > Config.MaxPegRadius)
                throw new ArgumentException($"撞钉 {peg.Id} 半径超过上限。", nameof(_pegs));
            if (peg.Position.X - peg.Radius < 0f || peg.Position.X + peg.Radius > Config.Width ||
                peg.Position.Y - peg.Radius < 0f || peg.Position.Y + peg.Radius > Config.Height)
            {
                throw new ArgumentException($"撞钉 {peg.Id} 必须完全位于盘面内。", nameof(_pegs));
            }

            // 发射口与初始出射路径保护区：任何最大半径的球都不能在发射口覆盖撞钉。
            if (MarbleVec.Distance(Config.LaunchOrigin, peg.Position) <=
                peg.Radius + Config.MaxBallRadius + Config.LaunchClearance)
            {
                throw new ArgumentException($"撞钉 {peg.Id} 覆盖了发射口保护区。", nameof(_pegs));
            }
        }
    }

    #endregion
}