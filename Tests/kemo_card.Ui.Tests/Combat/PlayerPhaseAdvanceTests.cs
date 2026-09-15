using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 玩家阶段推进与 RandomN 目标选择。
/// 回归点：阶段推进此前只在 Confirm 指令里判定「全员已行动」，
/// 宿主对封印角色禁用确认按钮时永远收不到那条指令 → 战斗硬锁死。
/// </summary>
[TestFixture]
public sealed class PlayerPhaseAdvanceTests
{
    #region 阶段推进

    [Test]
    public void Confirming_all_slots_advances_to_card_execution()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();

        for (var i = 0; i < sim.PlayerTeam.Characters.Count; i++)
        {
            Assert.That(sim.TryApply(new ConfirmCharacterCommand(i)).Success, Is.True);
        }

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    [Test]
    public void Partial_confirmation_does_not_advance_phase()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();

        Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
    }

    /// <summary>
    /// 全员封印：不会有任何 Confirm 指令到达，阶段仍必须推进（规格 §2.1 / §2.5「封印视作已行动」）。
    /// </summary>
    [Test]
    public void All_sealed_party_advances_without_any_confirm_command()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        foreach (var character in sim.PlayerTeam.Characters)
        {
            ApplySeal(character);
        }

        // 模拟下一个玩家阶段开局：PlayerPhasePipeline 会套用封印行动封锁。
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    /// <summary>玩家阶段内的任意成功指令都应重新求值，而不只是 Confirm。</summary>
    [Test]
    public void Any_successful_command_reevaluates_phase_advance()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        var characters = sim.PlayerTeam.Characters;

        // 先封印除 0 号外的所有槽位并套用封锁 → 它们视作已行动。
        for (var i = 1; i < characters.Count; i++)
        {
            ApplySeal(characters[i]);
            CombatStateMachine.EnforceSeal(sim, i);
        }

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player), "还有 0 号未行动");

        // 0 号用「取消确认」之外的普通成功指令结束自己的行动：标记一张牌后确认。
        Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    [Test]
    public void Non_player_phase_does_not_advance()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        sim.TransitionTo(ECombatPhase.CardExecution);

        foreach (var character in sim.PlayerTeam.Characters)
        {
            character.SetHasActed(true);
        }

        CombatStateMachine.AdvanceToCardExecutionIfAllActed(sim);

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution), "已是结算阶段，不应重复推进");
    }

    #endregion

    #region RandomN 目标选择

    [Test]
    public void PickRandomTargets_returns_requested_count_of_distinct_targets()
    {
        var legal = EnemyTargets(4);

        var picked = CombatStateMachine.PickRandomTargets(legal, 2, new HostRng(7, "combat.retarget"));

        Assert.That(picked, Has.Count.EqualTo(2));
        Assert.That(picked.Distinct().Count(), Is.EqualTo(2), "必须无放回");
        Assert.That(picked, Is.All.Matches<CombatTargetRef>(t => legal.Contains(t)));
    }

    /// <summary>
    /// 核心回归：<c>RandomN</c> 曾经退化为 <c>legal.Take(N)</c>，永远只取前 N 个
    /// （敌方技能固定打 0 号槽），枚举名与规格承诺都与行为不符。
    /// </summary>
    [Test]
    public void PickRandomTargets_does_not_degenerate_to_take_first_n()
    {
        var legal = EnemyTargets(4);
        var seen = new HashSet<int>();

        for (var seed = 0; seed < 64; seed++)
        {
            var picked = CombatStateMachine.PickRandomTargets(legal, 1, new HostRng(seed, "combat.retarget"));
            seen.Add(picked[0].Index);
        }

        Assert.That(
            seen.Count,
            Is.GreaterThan(1),
            "RandomN 若退化为 Take(N)，所有种子都会选到索引 0");
    }

    [Test]
    public void PickRandomTargets_is_deterministic_for_same_seed()
    {
        var legal = EnemyTargets(4);

        var first = CombatStateMachine.PickRandomTargets(legal, 3, new HostRng(42, "combat.retarget"));
        var second = CombatStateMachine.PickRandomTargets(legal, 3, new HostRng(42, "combat.retarget"));

        Assert.That(first, Is.EqualTo(second), "同种子必须可复现");
    }

    [Test]
    public void PickRandomTargets_returns_all_when_count_covers_pool()
    {
        var legal = EnemyTargets(3);

        var picked = CombatStateMachine.PickRandomTargets(legal, 5, new HostRng(1, "combat.retarget"));

        Assert.That(picked, Is.EquivalentTo(legal));
    }

    #endregion

    private static List<CombatTargetRef> EnemyTargets(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new CombatTargetRef(ECombatSide.Enemy, i))];

    private static void ApplySeal(CharacterBattleInstance character)
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.test.seal",
            DurationPolicy = EDurationPolicy.Infinite,
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
            GrantedTags = [CombatConstants.SealedTag],
        };
        var result = character.Asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: character.Asc));
        Assert.That(result.Success, Is.True);
        Assert.That(character.IsSealed, Is.True);
    }
}