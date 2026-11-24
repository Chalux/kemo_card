using System.Globalization;
using System.Text;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 弹珠盘面第一批：四钉固定布局、每回合 FIFO 队列、60Hz 确定性物理、
/// 全角发射、连续碰撞封顶、去重计分、离盘 / 困球回收、20 球上限与无副作用预测。
/// </summary>
[TestFixture]
public sealed class MarbleSimulationTests
{
    [Test]
    public void Default_board_has_four_fixed_pegs_and_two_base_balls_per_turn()
    {
        var runtime = new MarbleRuntime();

        runtime.BeginTurn(1, 3);

        Assert.That(runtime.Pegs, Has.Count.EqualTo(MarbleBoardLayout.FixedPegCount));
        Assert.That(runtime.Queue.Count, Is.EqualTo(2), "每轮基础发放 2 颗弹珠");
        Assert.That(runtime.OwnerSlotIndex, Is.EqualTo(0));
        Assert.That(runtime.Count, Is.Zero);
        Assert.That(runtime.ActiveBalls, Is.Empty);

        runtime.BeginTurn(2, 3);

        Assert.That(runtime.OwnerSlotIndex, Is.EqualTo(1), "轮值按 (回合 - 1) % 队伍人数");
        Assert.That(runtime.Queue.Count, Is.EqualTo(2), "新回合重新发放基础球");
        Assert.That(runtime.Pegs, Has.Count.EqualTo(MarbleBoardLayout.FixedPegCount), "换回合不重建撞钉");
    }

    [Test]
    public void Queue_is_fifo_and_launch_consumes_only_the_head_ball()
    {
        var runtime = new MarbleRuntime();
        runtime.BeginTurn(1, 3);
        var baseSpec = runtime.BaseBallSpec;
        runtime.EnqueueBall(new MarbleBallSpec(7, 10f));

        Assert.That(runtime.Queue.Count, Is.EqualTo(3));
        Assert.That(runtime.Queue[0], Is.EqualTo(baseSpec), "队首仍是本轮基础球");
        Assert.That(runtime.Queue[2].Points, Is.EqualTo(7), "后加入的球追加到队尾");

        Assert.That(runtime.TryLaunch(0f), Is.True);
        Assert.That(runtime.Queue.Count, Is.EqualTo(2));
        var ball = runtime.ActiveBalls.Single();
        Assert.That(ball.Points, Is.EqualTo(baseSpec.Points));
        Assert.That(ball.Radius, Is.EqualTo(baseSpec.Radius));
        Assert.That(runtime.CanLaunch, Is.False, "场上已有活跃球时不能继续发射");
        Assert.That(runtime.TryLaunch(MathF.PI), Is.False);
        Assert.That(runtime.Queue.Count, Is.EqualTo(2), "拒绝发射不得消耗队列");
    }

    [Test]
    public void Launch_accepts_every_angle_and_uses_the_requested_direction()
    {
        var angles = new[] { 0f, MathF.PI / 2f, -MathF.PI / 2f, MathF.PI, 2.4f };
        foreach (var angle in angles)
        {
            var runtime = new MarbleRuntime(MarblePhysicsConfig.Default with { Gravity = 0f });
            runtime.BeginTurn(1, 3);

            Assert.That(runtime.TryLaunch(angle), Is.True, $"角度 {angle} 必须可发射");
            var velocity = runtime.ActiveBalls.Single().Velocity;
            var expected = MarbleVec.FromAngle(angle) * runtime.Config.LaunchSpeed;
            AssertClose(expected.X, velocity.X, 1e-3f);
            AssertClose(expected.Y, velocity.Y, 1e-3f);
        }
    }

    [Test]
    public void Upward_launch_bounces_off_the_top_wall()
    {
        var runtime = new MarbleRuntime(
            MarblePhysicsConfig.Default with { Gravity = 0f },
            Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 3);
        Assert.That(runtime.TryLaunch(-MathF.PI / 2f), Is.True);

        for (var i = 0; i < 30; i++)
            runtime.Step();

        var ball = runtime.ActiveBalls.Single();
        Assert.That(ball.Velocity.Y, Is.GreaterThan(0f), "顶壁必须把向上的球弹回向下");
        Assert.That(ball.Position.Y, Is.GreaterThanOrEqualTo(ball.Radius - 0.01f), "球心不能穿透顶壁");
    }

    [Test]
    public void Sustained_peg_contact_scores_once_until_the_ball_detaches()
    {
        var config = FlatConfig(new MarbleVec(180f, 100f)) with { PegRestitution = 0f, WallRestitution = 0f };
        var peg = new MarblePegDefinition("peg", new MarbleVec(180f, 260f), 10f, 2);
        var runtime = new MarbleRuntime(config, new[] { peg });
        runtime.BeginTurn(1, 3);
        Assert.That(runtime.TryLaunch(MathF.PI / 2f), Is.True);

        var hits = 0;
        for (var i = 0; i < 90; i++)
            hits += runtime.Step().Events.OfType<MarblePegHitEvent>().Count();

        Assert.That(hits, Is.EqualTo(1), "持续贴合期间同一撞钉只计一次");
        Assert.That(runtime.Count, Is.EqualTo(runtime.BaseBallSpec.Points * 2));
        Assert.That(runtime.ActiveBalls, Has.Count.EqualTo(1));
        Assert.That(runtime.ActiveBalls[0].ContactedPegIds, Does.Contain("peg"));
    }

    [Test]
    public void A_ball_that_leaves_the_detach_margin_can_score_the_same_peg_again()
    {
        var config = FlatConfig(new MarbleVec(180f, 100f)) with { PegRestitution = 1f, WallRestitution = 1f };
        var peg = new MarblePegDefinition("peg", new MarbleVec(260f, 100f), 10f, 1);
        var runtime = new MarbleRuntime(config, new[] { peg });
        runtime.BeginTurn(1, 3);
        Assert.That(runtime.TryLaunch(0f), Is.True);

        var hits = 0;
        for (var i = 0; i < 240; i++)
            hits += runtime.Step().Events.OfType<MarblePegHitEvent>().Count();

        Assert.That(hits, Is.GreaterThanOrEqualTo(2), "离开脱离容差后再次接触必须重新计分");
        Assert.That(runtime.Count, Is.EqualTo(hits * runtime.BaseBallSpec.Points));
    }

    [Test]
    public void A_ball_that_fully_crosses_the_bottom_is_recovered()
    {
        var runtime = new MarbleRuntime(
            MarblePhysicsConfig.Default with { Gravity = 900f },
            Array.Empty<MarblePegDefinition>());
        runtime.BeginTurn(1, 3);
        Assert.That(runtime.TryLaunch(MathF.PI / 2f), Is.True);

        var leftBoard = false;
        for (var i = 0; i < 300 && runtime.ActiveBalls.Count > 0; i++)
            leftBoard |= runtime.Step().Events.OfType<MarbleBallLeftEvent>().Any();

        Assert.That(leftBoard, Is.True, "球体完全越过底界必须判离盘");
        Assert.That(runtime.ActiveBalls, Is.Empty);
        Assert.That(runtime.Queue.Count, Is.EqualTo(1));
        Assert.That(runtime.Count, Is.Zero);
    }

    [Test]
    public void A_ball_wedged_on_a_peg_warns_then_is_recovered_after_three_seconds()
    {
        var config = FlatConfig(new MarbleVec(180f, 100f)) with { PegRestitution = 0f };
        var peg = new MarblePegDefinition("peg", new MarbleVec(180f, 260f), 10f, 2);
        var runtime = new MarbleRuntime(config, new[] { peg });
        runtime.BeginTurn(1, 3);
        Assert.That(runtime.TryLaunch(MathF.PI / 2f), Is.True);

        var warningSeen = false;
        var recoveryStep = -1;
        for (var i = 1; i <= 400; i++)
        {
            var result = runtime.Step();
            if (runtime.ActiveBalls.Count == 1 && runtime.ActiveBalls[0].IsStuckWarning)
                warningSeen = true;
            if (result.Events.OfType<MarbleBallStuckEvent>().Any())
            {
                recoveryStep = i;
                break;
            }
        }

        Assert.That(warningSeen, Is.True, "接近困球阈值时应先给出提示");
        Assert.That(recoveryStep, Is.GreaterThan(0), "约 3 秒无有效移动必须回收");
        Assert.That(runtime.ActiveBalls, Is.Empty);
        Assert.That(runtime.Count, Is.EqualTo(runtime.BaseBallSpec.Points * 2), "困球回收保留已得点数");
    }

    [Test]
    public void Active_ball_count_is_capped_at_twenty()
    {
        var config = MarblePhysicsConfig.Default with { Gravity = 0f };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        var spec = new MarbleBallSpec(1, 4f);

        for (var i = 0; i < 20; i++)
        {
            Assert.That(
                runtime.TrySpawnActive(spec, new MarbleVec(30f + i * 12f, 200f), MarbleVec.Zero),
                Is.True,
                $"第 {i + 1} 颗球应允许生成");
        }

        Assert.That(runtime.ActiveBalls, Has.Count.EqualTo(20));
        Assert.That(
            runtime.TrySpawnActive(spec, new MarbleVec(180f, 300f), MarbleVec.Zero),
            Is.False,
            "达到 20 个活跃上限后必须抑制生成");
        Assert.That(runtime.ActiveBalls, Has.Count.EqualTo(20));
    }

    [Test]
    public void Spawning_inside_a_peg_marks_contact_to_avoid_first_frame_scoring()
    {
        var peg = new MarblePegDefinition("peg", new MarbleVec(180f, 150f), 12f, 5);
        var runtime = new MarbleRuntime(MarblePhysicsConfig.Default with { Gravity = 0f }, new[] { peg });

        Assert.That(
            runtime.TrySpawnActive(new MarbleBallSpec(1, 8f), new MarbleVec(180f, 150f), MarbleVec.Zero),
            Is.True);
        Assert.That(runtime.ActiveBalls.Single().ContactedPegIds, Does.Contain("peg"));

        runtime.Step();

        Assert.That(runtime.Count, Is.Zero, "生成瞬间已贴住的钉不得在第 0 帧计分");
    }

    [Test]
    public void High_speed_balls_are_clamped_and_still_hit_small_pegs()
    {
        var config = FlatConfig(new MarbleVec(180f, 100f)) with { MaxSpeed = 1000f };
        var peg = new MarblePegDefinition("peg", new MarbleVec(180f, 200f), 6f, 1);
        var runtime = new MarbleRuntime(config, new[] { peg });

        Assert.That(
            runtime.TrySpawnActive(
                new MarbleBallSpec(1, 4f), new MarbleVec(50f, 200f), new MarbleVec(5000f, 0f)),
            Is.True);

        runtime.Step();
        Assert.That(
            runtime.ActiveBalls.Single().Velocity.Length,
            Is.LessThanOrEqualTo(config.MaxSpeed + 0.01f),
            "加速度 / 生成速度必须被最高速度阈值钳制");

        for (var i = 0; i < 10; i++)
            runtime.Step();
        Assert.That(runtime.Count, Is.GreaterThan(0), "扫掠碰撞不得让高速球穿过小撞钉");
    }

    [Test]
    public void Collision_iterations_are_capped_per_step()
    {
        var config = MarblePhysicsConfig.Default with
        {
            Width = 40f,
            Height = 60f,
            Gravity = 0f,
            MaxSpeed = 12000f,
            MaxBallRadius = 8f,
            BallRadius = 4f,
            PegRadius = 3f,
            MaxPegRadius = 3f,
            LaunchOrigin = new MarbleVec(20f, 20f),
            MaxCollisionsPerStep = 2,
        };
        var runtime = new MarbleRuntime(config, Array.Empty<MarblePegDefinition>());
        Assert.That(
            runtime.TrySpawnActive(new MarbleBallSpec(1, 4f), new MarbleVec(20f, 20f), new MarbleVec(12000f, 0f)),
            Is.True);

        for (var i = 0; i < 10; i++)
        {
            var result = runtime.Step();
            Assert.That(runtime.ActiveBalls, Has.Count.EqualTo(1));
            var ball = runtime.ActiveBalls[0];
            Assert.That(ball.Position.X, Is.InRange(ball.Radius - 0.01f, config.Width - ball.Radius + 0.01f));
            Assert.That(result.Count, Is.Zero);
        }
    }

    [Test]
    public void Identical_inputs_produce_identical_score_and_positions()
    {
        static string Trace()
        {
            var runtime = new MarbleRuntime();
            runtime.BeginTurn(1, 3);
            Assert.That(runtime.TryLaunch(0.9f), Is.True);
            var builder = new StringBuilder();
            for (var i = 0; i < 240; i++)
            {
                var result = runtime.Step();
                builder.Append(result.Count).Append(':');
                foreach (var ball in runtime.ActiveBalls)
                {
                    builder.Append(ball.Position.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(ball.Position.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        Assert.That(Trace(), Is.EqualTo(Trace()), "同一初始状态与同一角度必须得到完全相同的轨迹与点数");
    }

    [Test]
    public void Prediction_reuses_live_stepping_without_changing_board_state()
    {
        var runtime = new MarbleRuntime(MarblePhysicsConfig.Default with { Gravity = 0f });
        runtime.BeginTurn(1, 3);
        var queueBefore = runtime.Queue.Count;
        var countBefore = runtime.Count;
        var pegPositions = runtime.Pegs.Select(peg => peg.Position).ToArray();

        const float angle = -MathF.PI / 2f;
        var trajectory = runtime.PredictNextLaunch(angle);

        Assert.That(trajectory, Is.Not.Null);
        if (trajectory is null)
            return;

        Assert.That(trajectory.Points.Count, Is.GreaterThan(5));
        Assert.That(trajectory.PathLength, Is.GreaterThan(0f));
        Assert.That(trajectory.FirstCollisionPoint, Is.Not.Null, "顶壁碰撞应作为首个落点提示");
        Assert.That(runtime.Queue.Count, Is.EqualTo(queueBefore), "预测不得消耗队列");
        Assert.That(runtime.Count, Is.EqualTo(countBefore), "预测不得写点数");
        Assert.That(runtime.ActiveBalls, Is.Empty, "预测不得生成活跃球");
        Assert.That(runtime.Pegs.Select(peg => peg.Position), Is.EqualTo(pegPositions), "预测不得改盘面");

        Assert.That(runtime.TryLaunch(angle), Is.True);
        var steps = Math.Min(40, trajectory.Points.Count - 1);
        for (var i = 1; i <= steps; i++)
        {
            runtime.Step();
            Assert.That(runtime.ActiveBalls, Is.Not.Empty);
            var actual = runtime.ActiveBalls[0].Position;
            var expected = trajectory.Points[i];
            AssertClose(expected.X, actual.X, 1e-3f);
            AssertClose(expected.Y, actual.Y, 1e-3f);
        }
    }

    [Test]
    public void Peg_covering_the_launch_port_is_rejected()
    {
        var config = MarblePhysicsConfig.Default;
        var peg = new MarblePegDefinition("peg", config.LaunchOrigin + new MarbleVec(0f, 4f), 12f, 1);

        Assert.Throws<ArgumentException>(() => new MarbleRuntime(config, new[] { peg }));
    }

    [Test]
    public void Peg_outside_the_board_is_rejected()
    {
        var peg = new MarblePegDefinition("peg", new MarbleVec(5f, 100f), 12f, 1);

        Assert.Throws<ArgumentException>(() => new MarbleRuntime(MarblePhysicsConfig.Default, new[] { peg }));
    }

    [Test]
    public void Queue_text_lists_fifo_order_points_and_radius()
    {
        var queue = new MarbleQueue();
        queue.Enqueue(new MarbleBallSpec(1, 8f));
        queue.Enqueue(new MarbleBallSpec(7, 12f));

        static string Translate(string key) => key switch
        {
            "UI_MARBLE_QUEUE_ENTRY" => "{0}. P{1} R{2}",
            "UI_MARBLE_QUEUE_EMPTY" => "empty",
            _ => key,
        };

        Assert.That(MarbleQueueText.Format(queue, Translate), Is.EqualTo("1. P1 R8\n2. P7 R12"));
        Assert.That(MarbleQueueText.Format(new MarbleQueue(), Translate), Is.EqualTo("empty"));
        Assert.That(MarbleQueueText.FormatEntry(3, new MarbleBallSpec(2, 9.5f), Translate),
            Is.EqualTo("3. P2 R9.5"));
    }

    [Test]
    public void Battle_start_queues_two_balls_and_a_wave_clear_keeps_the_current_round()
    {
        using var sim = BuildTwoWaveBattle();

        sim.RunBattleStart();

        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(2));
        Assert.That(sim.Marble.Pegs, Has.Count.EqualTo(MarbleBoardLayout.FixedPegCount));
        Assert.That(sim.Marble.OwnerSlotIndex, Is.EqualTo(0));

        var runtime = sim.Marble;
        var extra = new MarbleBallSpec(9, 10f);
        runtime.EnqueueBall(extra);

        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);
        sim.CheckEndConditions();

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "前提：确实换到了第二波");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        Assert.That(sim.Marble, Is.SameAs(runtime), "盘面属于整场战斗，换波不重建");
        Assert.That(sim.Marble.Pegs, Has.Count.EqualTo(MarbleBoardLayout.FixedPegCount));
        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(3), "出牌期清波保留本轮待发队列");
        Assert.That(sim.Marble.Queue[2], Is.EqualTo(extra));
        Assert.That(sim.Marble.OwnerSlotIndex, Is.EqualTo(0), "清波保留当前回合，轮值不变");
    }

    [Test]
    public void Turn_start_hooks_can_append_marbles_to_the_new_turn_queue()
    {
        var registry = CombatTestHelper.CreateFullRegistry();
        using var sim = CombatSimulationTestBuilder.Minimal(
            new EnemyUnit("e0", "slime", maxHp: 10), registry, [new TurnStartMarbleRule()]);

        sim.BeginNextTurn(incrementTurnsIntoWave: false);

        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(sim.Marble.Config.BaseBallsPerTurn + 1),
            "回合开始钩子追加的弹珠不能被随后的基础发球重置清掉");
        Assert.That(sim.Marble.Queue[sim.Marble.Queue.Count - 1].Points, Is.EqualTo(9));
    }

    [Test]
    public void Lightweight_simulation_step_matches_the_event_collecting_step()
    {
        var eventRuntime = new MarbleRuntime();
        eventRuntime.BeginTurn(1, 3);
        var lightRuntime = new MarbleRuntime();
        lightRuntime.BeginTurn(1, 3);
        Assert.That(eventRuntime.TryLaunch(0.9f), Is.True);
        Assert.That(lightRuntime.TryLaunch(0.9f), Is.True);

        for (var step = 0; step < 600; step++)
        {
            eventRuntime.Step();
            lightRuntime.StepPhysics();

            if (step < 10)
            {
                Assert.That(lightRuntime.ActiveBalls, Has.Count.EqualTo(eventRuntime.ActiveBalls.Count));
                if (eventRuntime.ActiveBalls.Count > 0)
                {
                    var expected = eventRuntime.ActiveBalls[0].Position;
                    var actual = lightRuntime.ActiveBalls[0].Position;
                    AssertClose(expected.X, actual.X, 1e-4f);
                    AssertClose(expected.Y, actual.Y, 1e-4f);
                }
            }
        }

        Assert.That(lightRuntime.StepIndex, Is.EqualTo(eventRuntime.StepIndex));
        Assert.That(lightRuntime.Count, Is.EqualTo(eventRuntime.Count));
        Assert.That(lightRuntime.ActiveBalls, Has.Count.EqualTo(eventRuntime.ActiveBalls.Count));
    }

    private sealed class TurnStartMarbleRule : ICombatRule
    {
        public string Id => "test.turn_start_marble";
        public int Priority => 0;
        public void OnTurnStart(CombatContext ctx) =>
            ctx.Simulation.Marble.EnqueueBall(new MarbleBallSpec(9, 10f));
    }

    private static MarblePhysicsConfig FlatConfig(MarbleVec launchOrigin) =>
        MarblePhysicsConfig.Default with
        {
            Gravity = 0f,
            LaunchOrigin = launchOrigin,
            WallRestitution = 1f,
            PegRestitution = 1f,
        };

    private static void AssertClose(float expected, float actual, float tolerance) =>
        Assert.That(actual, Is.EqualTo(expected).Within(tolerance));

    private static CombatSimulation BuildTwoWaveBattle()
    {
        const string enemyId = "marble_wave_dummy";
        var battle = new BattleDto
        {
            Id = "marble_wave_battle",
            Waves =
            [
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = enemyId, Count = 1 }] },
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = enemyId, Count = 1 }] },
            ],
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            enemies: new Dictionary<string, EnemyDto> { [enemyId] = new() { Id = enemyId, MaxHp = 20 } },
            battles: new Dictionary<string, BattleDto> { [battle.Id] = battle });
        var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = 0f,
            [AttributeIds.InitialEnergy] = 0f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests($"marble-c{index}", attributes))
            .ToArray();
        var firstWave = CombatSimulationFactory.SpawnWaveEnemies(battle.Waves[0], registry, out var error);
        Assert.That(firstWave, Is.Not.Null, error);

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState(firstWave!),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            battle: battle);
    }
}