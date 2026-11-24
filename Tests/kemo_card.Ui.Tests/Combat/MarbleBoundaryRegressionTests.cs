using System.Reflection;
using KemoCard.Mod.Combat.Marble;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 弹珠边界的独立回归：计分饱和、生成半径合法化 / 队列校验、预测路径上限与裁剪。
/// </summary>
[TestFixture]
public sealed class MarbleBoundaryRegressionTests
{
    #region 大数计分

    [Test]
    public void Huge_points_product_saturates_score_and_event_instead_of_overflowing()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var peg = new MarblePegDefinition("huge", new MarbleVec(180f, 260f), 12f, int.MaxValue);
        var runtime = new MarbleRuntime(config, [peg]);
        var spec = new MarbleBallSpec(int.MaxValue, 8f);

        // 距钉心 = 球半径 + 钉半径 + 接触容差 + 0.25：生成时不记接触，但一步内必然撞上。
        var spawn = new MarbleVec(180f, 260f - (8f + 12f + config.ContactEpsilon + 0.25f));
        Assert.That(runtime.TrySpawnActive(spec, spawn, new MarbleVec(0f, 60f)), Is.True);

        var first = runtime.Step().Events.OfType<MarblePegHitEvent>().Single();
        Assert.That(first.Gained, Is.EqualTo(int.MaxValue), "乘积溢出时必须饱和到 int 上限");
        Assert.That(first.TotalCount, Is.EqualTo(int.MaxValue));
        Assert.That(runtime.Count, Is.EqualTo(int.MaxValue));

        // 累计值已经饱和后继续命中，仍必须保持 int 上限而不是回绕。
        Assert.That(runtime.TrySpawnActive(spec, spawn, new MarbleVec(0f, 60f)), Is.True);
        var second = runtime.Step().Events.OfType<MarblePegHitEvent>().Single();
        Assert.That(second.Gained, Is.EqualTo(int.MaxValue));
        Assert.That(second.TotalCount, Is.EqualTo(int.MaxValue));
        Assert.That(runtime.Count, Is.EqualTo(int.MaxValue));
    }

    #endregion

    #region 生成半径与队列校验

    [Test]
    public void Spawn_legalization_uses_the_new_balls_actual_radius_and_allows_the_open_bottom()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var spec = new MarbleBallSpec(1, config.MaxBallRadius);
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());

        Assert.That(
            runtime.TrySpawnActive(spec, new MarbleVec(config.Width + 50f, 200f), MarbleVec.Zero),
            Is.True);
        var wide = runtime.ActiveBalls[^1];
        Assert.That(wide.Radius, Is.EqualTo(config.MaxBallRadius));
        Assert.That(wide.Position.X, Is.EqualTo(config.Width - config.MaxBallRadius).Within(1e-3f),
            "墙壁裁剪必须按新球半径而不是基础半径");
        Assert.That(wide.Position.X, Is.LessThan(config.Width - config.BallRadius),
            "按基础半径裁剪会让大球嵌进右墙");

        Assert.That(
            runtime.TrySpawnActive(spec, new MarbleVec(180f, -50f), MarbleVec.Zero),
            Is.True);
        Assert.That(runtime.ActiveBalls[^1].Position.Y,
            Is.EqualTo(config.MaxBallRadius).Within(1e-3f),
            "顶部裁剪必须按新球半径");

        var below = config.Height + config.MaxBallRadius + 1f;
        var bottomRuntime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        Assert.That(
            bottomRuntime.TrySpawnActive(spec, new MarbleVec(180f, below), MarbleVec.Zero),
            Is.True);
        Assert.That(bottomRuntime.ActiveBalls[0].Position.Y, Is.EqualTo(below).Within(1e-3f),
            "底部开放：合法化不得把球夹回盘内");
        Assert.That(
            bottomRuntime.Step().Events.OfType<MarbleBallLeftEvent>().Any(),
            Is.True,
            "底界之外的球应在下一步判离盘");
    }

    [Test]
    public void Invalid_specs_are_rejected_by_spawn_and_queue()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        var overRadius = new MarbleBallSpec(1, config.MaxBallRadius + 0.5f);

        Assert.That(runtime.TrySpawnActive(default, new MarbleVec(180f, 200f), MarbleVec.Zero),
            Is.False, "default 弹珠属性（半径为零）不得生成");
        Assert.That(runtime.TrySpawnActive(overRadius, new MarbleVec(180f, 200f), MarbleVec.Zero),
            Is.False, "超过最大半径不得生成");
        Assert.That(runtime.ActiveBalls, Is.Empty);

        runtime.BeginTurn(1, 3);
        var queued = runtime.Queue.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.EnqueueBall(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.EnqueueBall(overRadius));
        Assert.That(runtime.Queue.Count, Is.EqualTo(queued), "拒绝入队不得改变队列");

        runtime.EnqueueBall(new MarbleBallSpec(2, config.MaxBallRadius));
        Assert.That(runtime.Queue[^1].Radius, Is.EqualTo(config.MaxBallRadius));
    }

    [Test]
    public void Base_ball_spec_uses_config_ball_radius()
    {
        var config = MarblePhysicsConfig.Default with { BallRadius = 10f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());

        runtime.BeginTurn(1, 1);

        Assert.That(runtime.BaseBallSpec.Radius, Is.EqualTo(config.BallRadius));
        Assert.That(runtime.Queue[0].Radius, Is.EqualTo(config.BallRadius));
        Assert.That(runtime.TryLaunch(0f), Is.True);
        Assert.That(runtime.ActiveBalls.Single().Radius, Is.EqualTo(config.BallRadius));
    }

    [Test]
    public void Queue_has_no_public_append_entry_or_mutable_item_list()
    {
        Assert.That(
            typeof(MarbleQueue).GetMethod("Enqueue", BindingFlags.Public | BindingFlags.Instance),
            Is.Null,
            "公开 API 不得提供绕过校验的入队入口");

        var queue = new MarbleQueue();
        queue.Enqueue(new MarbleBallSpec(1, 8f));

        Assert.That(queue.Items.Count, Is.EqualTo(1));
        Assert.That(queue.Items as List<MarbleBallSpec>, Is.Null,
            "Items 不得把内部 List 暴露给调用方直接追加");
    }

    [Test]
    public void Config_rejects_non_finite_values()
    {
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { WallRestitution = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { ContactEpsilon = float.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { MaxBallRadius = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { LaunchClearance = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { StuckPathTolerance = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (MarblePhysicsConfig.Default with { StuckWarningSeconds = float.NaN }).Validate());
    }

    [Test]
    public void Active_ball_cap_stays_at_twenty_even_if_config_requests_more()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f, MaxActiveBalls = 100 };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        var spec = new MarbleBallSpec(1, 4f);

        for (var i = 0; i < MarblePhysicsConfig.MaxActiveBallsLimit; i++)
        {
            Assert.That(
                runtime.TrySpawnActive(spec, new MarbleVec(20f + i * 5f, 200f), MarbleVec.Zero),
                Is.True,
                $"第 {i + 1} 颗球应允许生成");
        }

        Assert.That(runtime.ActiveBalls, Has.Count.EqualTo(MarblePhysicsConfig.MaxActiveBallsLimit));
        Assert.That(
            runtime.TrySpawnActive(spec, new MarbleVec(330f, 300f), MarbleVec.Zero),
            Is.False,
            "硬上限 20 不随配置上调");
    }

    #endregion

    #region 预测路径上限

    [Test]
    public void Downward_preview_stops_at_exit_or_at_the_shorter_display_distance()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, []);
        runtime.BeginTurn(1, 3);
        var exitDistance = config.Height + config.BallRadius - config.LaunchOrigin.Y;
        var full = runtime.PredictNextLaunch(MathF.PI / 2f)!;
        Assert.That(full.LeftBoard, Is.True);
        Assert.That(full.PathLength, Is.EqualTo(exitDistance).Within(0.001f));
        var clipped = runtime.PredictNextLaunch(MathF.PI / 2f, exitDistance - 0.25f)!;
        Assert.That(clipped.LeftBoard, Is.False);
        Assert.That(clipped.PathPoints[^1].Y, Is.EqualTo(config.Height + config.BallRadius - 0.25f).Within(0.001f));
    }

    [Test]
    public void Prediction_clips_the_last_segment_exactly_at_the_path_limit()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 1);

        var trajectory = runtime.PredictNextLaunch(MathF.PI / 2f, 1f);

        Assert.That(trajectory, Is.Not.Null);
        if (trajectory is null)
            return;

        Assert.That(trajectory.PathLength, Is.EqualTo(1f), "路径上限为 1 时总长也只能到 1");
        Assert.That(trajectory.Points, Has.Count.EqualTo(1),
            "只记录完整物理步的终点，被裁剪的那一步不计入");
        Assert.That(trajectory.PathPoints, Has.Count.EqualTo(2));
        Assert.That(trajectory.PathPoints[0], Is.EqualTo(config.LaunchOrigin));
        Assert.That(MarbleVec.Distance(config.LaunchOrigin, trajectory.PathPoints[^1]),
            Is.EqualTo(1f).Within(1e-5f),
            "裁剪点必须正好落在路径上限处，而不是整步终点");
        Assert.That(trajectory.FirstCollisionPoint, Is.Null);
    }

    [Test]
    public void First_collision_is_only_reported_when_it_is_inside_the_displayed_path()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 1);
        const float upward = -MathF.PI / 2f;
        var wallDistance = config.LaunchOrigin.Y - config.BallRadius;

        var before = runtime.PredictNextLaunch(upward, wallDistance - 1f);
        Assert.That(before, Is.Not.Null);
        if (before is null)
            return;

        Assert.That(before.FirstCollisionPoint, Is.Null, "撞点在裁剪范围外时不得提示落点");
        Assert.That(before.PathLength, Is.EqualTo(wallDistance - 1f));

        var after = runtime.PredictNextLaunch(upward, wallDistance + 1f);
        Assert.That(after, Is.Not.Null);
        if (after is null)
            return;

        Assert.That(after.FirstCollisionPoint, Is.Not.Null, "撞点已展示时必须提示落点");
        Assert.That(after.FirstCollisionPoint!.Value.Y,
            Is.EqualTo(config.BallRadius).Within(0.01f));
        Assert.That(after.PathLength, Is.EqualTo(wallDistance + 1f));
        Assert.That(
            after.PathPoints.Any(point => MathF.Abs(point.Y - config.BallRadius) <= 0.01f),
            Is.True,
            "撞击折返顶点必须进入逐段折线");
        Assert.That(after.PathPoints.Count, Is.GreaterThan(after.Points.Count),
            "同一步内的折返会产生额外的折线顶点");
    }

    [Test]
    public void Prediction_stops_early_when_the_ball_would_be_stuck()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f, WallRestitution = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 1);

        var trajectory = runtime.PredictNextLaunch(-MathF.PI / 2f, 4000f);

        Assert.That(trajectory, Is.Not.Null);
        if (trajectory is null)
            return;

        Assert.That(trajectory.LeftBoard, Is.False);
        Assert.That(trajectory.Points.Count, Is.LessThan(MarbleRuntime.MaxPredictionSteps),
            "已卡死的球不得空转到预测步数上限");
        Assert.That(trajectory.PathLength, Is.LessThan(60f),
            "困球只应包含撞顶前及反弹的实际路径");
    }

    [Test]
    public void Prediction_step_endpoints_match_the_live_ball()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 1);
        const float upward = -MathF.PI / 2f;

        var trajectory = runtime.PredictNextLaunch(upward, 4000f);
        Assert.That(trajectory, Is.Not.Null);
        if (trajectory is null)
            return;

        Assert.That(runtime.TryLaunch(upward), Is.True);

        var steps = Math.Min(trajectory.Points.Count - 1, 60);
        Assert.That(steps, Is.GreaterThan(20), "预测应覆盖足够多的完整物理步");
        for (var i = 1; i <= steps; i++)
        {
            runtime.Step();
            var actual = runtime.ActiveBalls.Single().Position;
            Assert.That(MarbleVec.Distance(actual, trajectory.Points[i]), Is.LessThan(1e-3f),
                $"第 {i} 步终点必须与实弹一致");
        }
    }

    #endregion
}
