using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// Run 进入战斗必须走内容 + <c>CombatSimulationFactory</c> + BattleStart 管线，
/// 不得再退回硬编码敌人 / 空规则表 / <c>initialPhase: Player</c> 的占位构造。
/// </summary>
[TestFixture]
public sealed class RunBattleWiringTests
{
    [Test]
    public void StartBattle_comes_from_content_battle_definition()
    {
        var (controller, registry, rng) = BuildReadyRun();

        using var simulation = controller.StartBattle(registry, rng, runSeed: 1);

        Assert.That(simulation.Battle, Is.Not.Null, "必须挂上内容里的 BattleDto（波次 / 奖励来源）");
        Assert.That(simulation.Battle!.Id, Is.EqualTo(RunTestHelper.TestBattleId));
        Assert.That(simulation.WaveCount, Is.EqualTo(1));
        Assert.That(
            simulation.EnemyTeam.Enemies[0].DefinitionId,
            Is.EqualTo(RunTestHelper.TestEnemyId),
            "敌人必须来自内容定义，而不是硬编码的占位敌人");
    }

    /// <summary>
    /// BattleStart 管线（规格 §6.1）必须真的跑过：开局抽满手牌是它的可观测副作用之一。
    /// 旧实现以 <c>initialPhase: Player</c> 直接起手，手牌全空。
    /// </summary>
    [Test]
    public void StartBattle_runs_battle_start_pipeline_and_draws_opening_hand()
    {
        var (controller, registry, rng) = BuildReadyRun();

        using var simulation = controller.StartBattle(registry, rng, runSeed: 1);

        var cardsInHand = simulation.PlayerTeam.Characters.Sum(c => c.HandSlots.Count(s => !s.IsEmpty));
        Assert.That(cardsInHand, Is.GreaterThan(0), "BattleStart 开局抽牌未执行");
        Assert.That(simulation.Phase, Is.EqualTo(ECombatPhase.Player), "BattleStart 结束后应停在首个玩家阶段");
        Assert.That(simulation.IsFirstPlayerPhase, Is.False, "开战管线已消费首阶段标记");
    }

    [Test]
    public void StartBattle_fills_shared_hp_ledger()
    {
        var (controller, registry, rng) = BuildReadyRun();

        using var simulation = controller.StartBattle(registry, rng, runSeed: 1);

        Assert.That(simulation.PlayerTeam.SharedHp, Is.EqualTo(simulation.PlayerTeam.MaxHp));
    }

    [Test]
    public void StartBattle_uses_explicit_battle_id()
    {
        var (controller, registry, rng) = BuildReadyRun();

        var simulation = controller.StartBattle(registry, rng, runSeed: 1, battleId: RunTestHelper.TestBattleId);

        Assert.That(simulation.Battle!.Id, Is.EqualTo(RunTestHelper.TestBattleId));
        simulation.Dispose();
    }

    [Test]
    public void StartBattle_throws_when_content_has_no_battle()
    {
        var (controller, _, rng) = BuildReadyRun();
        var emptyRegistry = CombatTestHelper.CreateFullRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            controller.StartBattle(emptyRegistry, rng, runSeed: 1));

        Assert.That(ex!.Message, Does.Contain("战斗定义"));
    }

    [Test]
    public void StartBattle_throws_when_battle_id_unknown()
    {
        var (controller, registry, rng) = BuildReadyRun();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            controller.StartBattle(registry, rng, runSeed: 1, battleId: "battle.nope"));

        Assert.That(ex!.Message, Does.Contain("battle.nope"));
    }

    [Test]
    public void StartBattle_sets_run_phase_to_battle()
    {
        var (controller, registry, rng) = BuildReadyRun();

        using var simulation = controller.StartBattle(registry, rng, runSeed: 1);

        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Battle));
    }

    private static (RunController Controller, GameDefinitionRegistry Registry, HostRng Rng) BuildReadyRun()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false };
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = RunTestHelper.CreateCharacterWithHp(i);
            mod.AddToCharacterPool(character);
            mod.PlayerStates[i].SetActiveCharacter(character);
        }

        mod.AddPlayerController(new PlayerController("local", "Player", isOwner: true));
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            mod.AssignSlotInternal(i, "local");
        }

        return (new RunController(mod), registry, new HostRng(1, "battle"));
    }
}