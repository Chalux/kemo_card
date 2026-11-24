namespace KemoCard.Mod.Combat.Marble;

/// <summary>物理步内新接触的一枚撞钉（去重后的首次接触）。</summary>
internal readonly record struct MarblePegContact(MarblePeg Peg, MarbleVec Position);

/// <summary>物理步内的一次实际位移（碰撞折返会把同一步拆成多段）。</summary>
internal readonly record struct MarblePathSegment(MarbleVec Start, MarbleVec End, float Length);

/// <summary>单颗球推进一个物理步的结果：新接触、首次碰撞点、逐段折线、实际行进距离与是否离盘。</summary>
internal sealed class MarbleAdvanceResult
{
    public List<MarblePegContact> PegContacts { get; } = [];

    /// <summary>本步内每次实际位移的起点 / 终点 / 长度（按发生顺序，含撞击折返后的各段）。</summary>
    public List<MarblePathSegment> PathSegments { get; } = [];

    /// <summary>本步内第一次碰撞（墙或撞钉）的位置；未碰撞为 null。</summary>
    public MarbleVec? FirstCollisionPoint { get; set; }

    /// <summary>
    /// 首次碰撞发生时已完成的最末一段在 <see cref="PathSegments"/> 中的索引；
    /// <c>-1</c> 表示碰撞发生在步首且本步尚未产生位移。
    /// </summary>
    public int FirstCollisionSegmentIndex { get; set; } = -1;

    /// <summary>球体完全越过底部开放出口。</summary>
    public bool LeftBoard { get; set; }

    /// <summary>本步实际行进距离（含折返各段，不含被碰撞截断的剩余位移）。</summary>
    public float Distance { get; set; }

    /// <summary>重置为一次新的物理步结果，供固定步进复用同一实例以避免热循环分配。</summary>
    public void Reset()
    {
        PegContacts.Clear();
        PathSegments.Clear();
        FirstCollisionPoint = null;
        FirstCollisionSegmentIndex = -1;
        LeftBoard = false;
        Distance = 0f;
    }
}

/// <summary>
/// 弹珠的连续碰撞步进：实弹与轨迹预测共用同一份推进 / 碰撞 / 反弹逻辑。
/// 本类不持有盘面状态、不读随机数、不引用 Godot——预测只是在临时球上跑同一段代码。
/// </summary>
internal static class MarblePhysics
{
    private const float TimeEpsilon = 1e-6f;
    private const float NormalEpsilonSquared = 1e-8f;

    #region 单步推进

    /// <summary>
    /// 推进一颗球一个固定物理步。
    /// <paramref name="scoreContacts"/> 为 true 时收集新接触撞钉（实弹计分用）；
    /// 轨迹预测传 false，只推进几何，不产生任何计分副作用。
    /// 实弹固定步进可传入 <paramref name="scratch"/> 复用结果对象、传
    /// <paramref name="collectPath"/>=false 跳过折线段（表现层不需要逐步路径），避免热循环分配。
    /// </summary>
    public static MarbleAdvanceResult Advance(
        MarblePhysicsConfig config,
        IReadOnlyList<MarblePeg> pegs,
        MarbleBall ball,
        float dt,
        bool scoreContacts,
        bool collectPath = true,
        MarbleAdvanceResult? scratch = null,
        List<string>? detachScratch = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(pegs);
        ArgumentNullException.ThrowIfNull(ball);
        if (!float.IsFinite(dt) || dt <= 0f)
            throw new ArgumentOutOfRangeException(nameof(dt));

        var result = scratch ?? new MarbleAdvanceResult();
        result.Reset();
        var exitY = config.Height + ball.Radius;
        if (ball.Position.Y > exitY)
        {
            result.LeftBoard = true;
            return result;
        }

        // 半隐式欧拉：先按本步重力更新速度，再以恒定速度做连续碰撞推进。
        ball.Velocity += new MarbleVec(0f, config.Gravity) * dt;
        ClampSpeed(config, ball);
        if (scoreContacts)
            CollectContacts(config, pegs, ball, result.PegContacts);

        var remaining = dt;
        var iterations = 0;
        while (remaining > TimeEpsilon && iterations < config.MaxCollisionsPerStep)
        {
            var hit = FindEarliestCollision(config, pegs, ball, remaining);
            var travelTime = hit.HasHit ? hit.Time : remaining;
            if (ball.Velocity.Y > 0 && ball.Position.Y + ball.Velocity.Y * travelTime > exitY)
            {
                Move(ball, (exitY - ball.Position.Y) / ball.Velocity.Y, result, collectPath);
                result.LeftBoard = true;
                break;
            }
            if (!hit.HasHit)
            {
                Move(ball, remaining, result, collectPath);
                if (scoreContacts)
                    CollectContacts(config, pegs, ball, result.PegContacts);
                break;
            }

            if (hit.Time > TimeEpsilon)
            {
                Move(ball, hit.Time, result, collectPath);
                remaining -= hit.Time;
            }

            if (result.FirstCollisionPoint is null)
            {
                result.FirstCollisionPoint = ball.Position;
                result.FirstCollisionSegmentIndex = result.PathSegments.Count - 1;
            }
            if (scoreContacts)
                CollectContacts(config, pegs, ball, result.PegContacts);

            var contact = ResolveContactNormal(config, pegs, ball, hit.Normal);
            var restitution = contact.Wall && contact.Peg
                ? MathF.Min(config.WallRestitution, config.PegRestitution)
                : contact.Peg ? config.PegRestitution : config.WallRestitution;

            var velocityAlongNormal = MarbleVec.Dot(ball.Velocity, contact.Normal);
            if (velocityAlongNormal < 0f)
            {
                // 重力每步都会把停在钉顶的球压回接触面。低于接触容差的微跳视为支撑，保留切向速度。
                var normalGravity = MathF.Max(0f, -contact.Normal.Y * config.Gravity);
                var restingSpeed = MathF.Sqrt(2f * normalGravity * (config.ContactEpsilon + config.ContactSkin)) + normalGravity * dt;
                if (contact.Peg && -velocityAlongNormal <= restingSpeed)
                    restitution = 0f;
                ball.Velocity -= contact.Normal * ((1f + restitution) * velocityAlongNormal);
            }

            ball.Position += contact.Normal * config.ContactSkin;
            ClampInsideWalls(config, ball);
            iterations++;

            if (ball.Velocity.LengthSquared < NormalEpsilonSquared)
                break;
        }

        ClampSpeed(config, ball);
        if (scoreContacts)
            RefreshDetachments(config, pegs, ball, detachScratch);

        if (!ball.Position.IsFinite || !ball.Velocity.IsFinite)
            throw new InvalidOperationException("弹珠模拟产生非有限数值。");

        ball.Motion.Push(ball.Position, result.Distance);
        return result;
    }

    private static void Move(MarbleBall ball, float time, MarbleAdvanceResult result, bool collectPath)
    {
        var start = ball.Position;
        ball.Position += ball.Velocity * time;
        var length = MarbleVec.Distance(start, ball.Position);
        result.Distance += length;
        if (collectPath && length > 0f)
            result.PathSegments.Add(new MarblePathSegment(start, ball.Position, length));
    }

    /// <summary>最高速度阈值：每步移动前 / 后都钳制，防止加速后的单步位移过大。</summary>
    private static void ClampSpeed(MarblePhysicsConfig config, MarbleBall ball)
    {
        var speedSquared = ball.Velocity.LengthSquared;
        var maxSquared = config.MaxSpeed * config.MaxSpeed;
        if (speedSquared > maxSquared)
            ball.Velocity *= MathF.Sqrt(maxSquared / speedSquared);
    }

    private static void ClampInsideWalls(MarblePhysicsConfig config, MarbleBall ball)
    {
        var minX = ball.Radius;
        var maxX = MathF.Max(minX, config.Width - ball.Radius);
        var x = Math.Clamp(ball.Position.X, minX, maxX);
        var y = MathF.Max(ball.Radius, ball.Position.Y);
        ball.Position = new MarbleVec(x, y);
    }

    #endregion

    #region 碰撞检测

    private readonly record struct CollisionHit(bool HasHit, float Time, MarbleVec Normal, MarblePeg? Peg, bool Wall);

    /// <summary>
    /// 求下一步内最早的碰撞：顶部 / 左右墙壁与全部撞钉；底部开放，不产生碰撞。
    /// 使用扫掠圆检测（位移 × 时间），因此高速球不会穿过小撞钉。
    /// </summary>
    private static CollisionHit FindEarliestCollision(
        MarblePhysicsConfig config,
        IReadOnlyList<MarblePeg> pegs,
        MarbleBall ball,
        float remaining)
    {
        var result = default(CollisionHit);
        var bestTime = float.MaxValue;
        var radius = ball.Radius;
        var position = ball.Position;
        var velocity = ball.Velocity;
        var speedSquared = velocity.LengthSquared;

        // 左墙：已越过边界按 0 时刻处理；否则只在朝墙移动时求交。
        if (position.X - radius <= 0f)
            Consider(0f, new MarbleVec(1f, 0f), null, wall: true);
        else if (velocity.X < 0f)
            Consider((radius - position.X) / velocity.X, new MarbleVec(1f, 0f), null, wall: true);

        // 右墙。
        if (position.X + radius >= config.Width)
            Consider(0f, new MarbleVec(-1f, 0f), null, wall: true);
        else if (velocity.X > 0f)
            Consider((config.Width - radius - position.X) / velocity.X, new MarbleVec(-1f, 0f), null, wall: true);

        // 顶墙；底部开放。
        if (position.Y - radius <= 0f)
            Consider(0f, new MarbleVec(0f, 1f), null, wall: true);
        else if (velocity.Y < 0f)
            Consider((radius - position.Y) / velocity.Y, new MarbleVec(0f, 1f), null, wall: true);

        // 撞钉：圆与圆的扫掠求交；擦边由 ContactEpsilon 在接触检测中兜底。
        foreach (var peg in pegs)
        {
            var delta = position - peg.Position;
            var sum = radius + peg.Radius;
            var distanceSquared = delta.LengthSquared;
            if (distanceSquared <= sum * sum)
            {
                var normal = distanceSquared > NormalEpsilonSquared
                    ? delta.Normalized
                    : new MarbleVec(0f, -1f);
                Consider(0f, normal, peg, wall: false);
                continue;
            }

            if (speedSquared <= NormalEpsilonSquared)
                continue;

            var projection = MarbleVec.Dot(delta, velocity);
            if (projection >= 0f)
                continue;

            var discriminant = projection * projection - speedSquared * (distanceSquared - sum * sum);
            if (discriminant < 0f)
                continue;

            var time = (-projection - MathF.Sqrt(discriminant)) / speedSquared;
            if (time < 0f)
                time = 0f;
            if (time > remaining)
                continue;

            var normalPoint = position + velocity * time - peg.Position;
            var hitNormal = normalPoint.LengthSquared > NormalEpsilonSquared
                ? normalPoint.Normalized
                : new MarbleVec(0f, -1f);
            Consider(time, hitNormal, peg, wall: false);
        }

        void Consider(float time, MarbleVec normal, MarblePeg? peg, bool wall)
        {
            if (time < 0f)
                time = 0f;
            if (time > remaining || time >= bestTime)
                return;

            bestTime = time;
            result = new CollisionHit(true, time, normal, peg, wall);
        }

        return result;
    }

    /// <summary>
    /// 同时接触的合成法线：把同一时刻压在球上的墙壁 / 撞钉法线相加后归一化，
    /// 一次反弹处理多枚钉；返回是否涉及墙壁 / 撞钉以选择恢复系数。
    /// </summary>
    private static (MarbleVec Normal, bool Wall, bool Peg) ResolveContactNormal(
        MarblePhysicsConfig config,
        IReadOnlyList<MarblePeg> pegs,
        MarbleBall ball,
        MarbleVec fallback)
    {
        var sum = MarbleVec.Zero;
        var wall = false;
        var pegContact = false;
        var position = ball.Position;
        var velocity = ball.Velocity;
        var epsilon = config.ContactEpsilon;

        if (position.X - ball.Radius < 0f ||
            (position.X - ball.Radius <= epsilon && velocity.X < 0f))
        {
            sum += new MarbleVec(1f, 0f);
            wall = true;
        }

        if (position.X + ball.Radius > config.Width ||
            (position.X + ball.Radius >= config.Width - epsilon && velocity.X > 0f))
        {
            sum += new MarbleVec(-1f, 0f);
            wall = true;
        }

        if (position.Y - ball.Radius < 0f ||
            (position.Y - ball.Radius <= epsilon && velocity.Y < 0f))
        {
            sum += new MarbleVec(0f, 1f);
            wall = true;
        }

        foreach (var peg in pegs)
        {
            var delta = position - peg.Position;
            var limit = ball.Radius + peg.Radius;
            var distanceSquared = delta.LengthSquared;
            if (distanceSquared > (limit + epsilon) * (limit + epsilon))
                continue;

            var normal = distanceSquared > NormalEpsilonSquared
                ? delta.Normalized
                : new MarbleVec(0f, -1f);
            if (distanceSquared >= limit * limit && MarbleVec.Dot(velocity, normal) > 0f)
                continue;

            sum += normal;
            pegContact = true;
        }

        return sum.LengthSquared <= NormalEpsilonSquared
            ? (fallback, wall, pegContact)
            : (sum.Normalized, wall, pegContact);
    }

    #endregion

    #region 接触计分与脱离

    /// <summary>
    /// 收集当前中心距落在接触容差内的撞钉；已在 <c>ContactedPegIds</c> 中的不重复计分。
    /// 同一物理步内同时接触多枚不同钉会各收集一次（去重仅按钉 ID）。
    /// </summary>
    private static void CollectContacts(
        MarblePhysicsConfig config,
        IReadOnlyList<MarblePeg> pegs,
        MarbleBall ball,
        List<MarblePegContact> into)
    {
        foreach (var peg in pegs)
        {
            var limit = ball.Radius + peg.Radius + config.ContactEpsilon;
            if (MarbleVec.Distance(ball.Position, peg.Position) > limit)
                continue;
            if (ball.TryRegisterContact(peg.Id))
                into.Add(new MarblePegContact(peg, ball.Position));
        }
    }

    /// <summary>仅当后续物理步中心距严格大于半径和 + <c>DetachMargin</c> 时才移出接触集合。</summary>
    private static void RefreshDetachments(
        MarblePhysicsConfig config,
        IReadOnlyList<MarblePeg> pegs,
        MarbleBall ball,
        List<string>? scratch = null)
    {
        // 不能在遍历 HashSet 时删除；固定步进复用外部传入的临时列表，避免每球每步分配。
        scratch ??= [];
        scratch.Clear();
        scratch.AddRange(ball.ContactedPegIds);
        foreach (var pegId in scratch)
        {
            MarblePeg? peg = null;
            foreach (var candidate in pegs)
            {
                if (string.Equals(candidate.Id, pegId, StringComparison.Ordinal))
                {
                    peg = candidate;
                    break;
                }
            }

            if (peg is null ||
                MarbleVec.Distance(ball.Position, peg.Position) > ball.Radius + peg.Radius + config.DetachMargin)
            {
                ball.RemoveContact(pegId);
            }
        }
    }

    #endregion
}