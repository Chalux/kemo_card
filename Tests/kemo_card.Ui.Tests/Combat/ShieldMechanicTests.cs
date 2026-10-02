using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 护盾机制（2026-09-26）：<b>1 点护盾抵 1 点伤害</b>，只抵扣「敌方来源 + 点名自己槽位」的伤害；
/// 队伍账本（<c>scope: Team</c>）与自身结算的伤害（中毒 / 手牌槽伤害）不抵扣——与受击钩子同口径。
/// 护盾是可消耗资源，存在属性 base 值上，可叠加、无上限、战斗内永久。
/// </summary>
/// <remarks>
/// 用合成效果驱动，不依赖任何出货卡面；护盾在真实内容里的两个消费方（「热血阶梯」授予、
/// 卡特被动 4 受击授予）见 <see cref="CarterContentTests"/>。
/// </remarks>
[TestFixture]
public sealed class ShieldMechanicTests
{
    private const string GrantEffect = "test.grant_shield";
    private const string DamageEffect = "test.slot_damage";
    private const int ShieldGrant = 10;
    private const int DamageAmount = 30;
    private const int TeamMaxHp = 200;

    [Test]
    public void Gain_shield_effect_stacks_without_a_cap()
    {
        using var sim = BuildSim();

        Execute(sim, GrantEffect, Player(0), [Player(0)]);
        Execute(sim, GrantEffect, Player(0), [Player(0)]);

        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(20f));
    }

    [Test]
    public void Enemy_damage_is_absorbed_then_the_overflow_reaches_the_ledger()
    {
        using var sim = BuildSim();
        Execute(sim, GrantEffect, Player(0), [Player(0)]);

        Execute(sim, DamageEffect, Enemy(0), [Player(0)]);

        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero, "护盾被吃光");
        Assert.That(
            sim.PlayerTeam.SharedHp,
            Is.EqualTo(TeamMaxHp - (DamageAmount - ShieldGrant)),
            "超出护盾的部分落到共享账本");
    }

    [Test]
    public void Damage_without_a_shield_hits_the_ledger_in_full()
    {
        using var sim = BuildSim();

        Execute(sim, DamageEffect, Enemy(0), [Player(0)]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(TeamMaxHp - DamageAmount));
    }

    [Test]
    public void Team_ledger_damage_is_not_absorbed()
    {
        using var sim = BuildSim();
        Execute(sim, GrantEffect, Player(0), [Player(0)]);

        Execute(sim, DamageEffect, Enemy(0), [CombatTargetRef.PlayerTeam]);

        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo((float)ShieldGrant),
            "账本目标没有具体受击角色，不吃分槽护盾");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(TeamMaxHp - DamageAmount));
    }

    [Test]
    public void Self_inflicted_damage_is_not_absorbed()
    {
        using var sim = BuildSim();
        Execute(sim, GrantEffect, Player(0), [Player(0)]);

        Execute(sim, DamageEffect, Player(0), [Player(0)]);

        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield),
            Is.EqualTo((float)ShieldGrant),
            "中毒 / 手牌槽伤害这类自身结算的来源不吃护盾");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(TeamMaxHp - DamageAmount));
    }

    [Test]
    public void Shield_only_protects_the_character_that_holds_it()
    {
        using var sim = BuildSim();
        Execute(sim, GrantEffect, Player(1), [Player(1)]);

        Execute(sim, DamageEffect, Enemy(0), [Player(0)]);

        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo((float)ShieldGrant));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(TeamMaxHp - DamageAmount), "没护盾的槽位照常扣账本");
    }

    #region 装配

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    /// <summary>真实内容（含 <c>Shield</c> 属性定义）+ 两个只用于驱动的合成效果。</summary>
    private static GameDefinitionRegistry BuildRegistry()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, EffectDto>(definitions.Effects, StringComparer.Ordinal)
        {
            [GrantEffect] = new()
            {
                Id = GrantEffect,
                Kind = EEffectKind.GainShield,
                Params = new Dictionary<string, object>(StringComparer.Ordinal) { ["amount"] = ShieldGrant },
            },
            [DamageEffect] = new()
            {
                Id = DamageEffect,
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object>(StringComparer.Ordinal) { ["amount"] = DamageAmount },
            },
        };

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Effects = effects })], out _);
        return registry;
    }

    private static CombatSimulation BuildSim()
    {
        var characters = Enumerable.Range(0, 4)
            .Select(index => CharacterBattleInstance.CreateForTests(
                $"c{index}",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [AttributeIds.MaxHealth] = 50f,
                    [AttributeIds.PhysicalAttack] = 10f,
                }))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: TeamMaxHp),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 500)]),
            new CombatRuleEngine([]),
            BuildRegistry(),
            initialPhase: ECombatPhase.Player,
            runSeed: 20260926);
    }

    private static void Execute(
        CombatSimulation simulation,
        string effectId,
        CombatTargetRef source,
        CombatTargetRef[] targets) =>
        simulation.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = effectId },
            simulation,
            source,
            targets);

    #endregion
}