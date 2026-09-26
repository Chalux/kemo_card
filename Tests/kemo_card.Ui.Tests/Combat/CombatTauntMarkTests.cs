using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 嘲讽 Crosshair 的判定：全 0 不标；否则标出所有等于最大嘲讽值的槽位
/// （并列最高全部显示；最高为 0 而有人为负时标 0 的那些）。
/// </summary>
[TestFixture]
public sealed class CombatTauntMarkTests
{
    [Test]
    public void All_zero_marks_nobody()
    {
        Assert.That(CombatTauntMarks.Resolve([0f, 0f, 0f, 0f]), Is.EqualTo(new[] { false, false, false, false }));
    }

    [Test]
    public void Highest_zero_with_a_negative_marks_the_zero_slots()
    {
        // 例：嘲讽值 0,-2,0,0 → 1/3/4 号显示。
        Assert.That(CombatTauntMarks.Resolve([0f, -2f, 0f, 0f]), Is.EqualTo(new[] { true, false, true, true }));
    }

    [Test]
    public void Tied_highest_values_are_all_marked()
    {
        // 例：嘲讽值 0,2,0,2 → 2/4 号显示。
        Assert.That(CombatTauntMarks.Resolve([0f, 2f, 0f, 2f]), Is.EqualTo(new[] { false, true, false, true }));
    }

    [Test]
    public void Single_highest_value_is_marked()
    {
        // 唯一最高（且非零）时只标那一个：嘲讽标识的意义就是指出"会被优先攻击的队友"。
        Assert.That(CombatTauntMarks.Resolve([0f, 2f, 0f, 0f]), Is.EqualTo(new[] { false, true, false, false }));
    }

    [Test]
    public void Highest_is_marked_even_with_three_way_tie()
    {
        Assert.That(CombatTauntMarks.Resolve([5f, 5f, 0f, 5f]), Is.EqualTo(new[] { true, true, false, true }));
    }

    [Test]
    public void Empty_input_is_empty_output()
    {
        Assert.That(CombatTauntMarks.Resolve([]), Is.Empty);
    }

    #region 与模拟器接线（读 AttributeIds.Taunt）

    [Test]
    public void Simulation_without_taunt_marks_nobody()
    {
        using var sim = BuildSim();

        Assert.That(
            CombatTauntMarks.Resolve(sim),
            Is.EqualTo(new[] { false, false, false, false }),
            "没人有嘲讽 → 谁都不标");
    }

    [Test]
    public void Simulation_marks_the_single_taunter()
    {
        using var sim = BuildSim();
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 2), "betelgeuse_passive_p3");

        Assert.That(
            CombatTauntMarks.Resolve(sim),
            Is.EqualTo(new[] { false, false, true, false }),
            "P3 的嘲讽 +5 只落在 2 号槽");
    }

    [Test]
    public void Simulation_marks_all_tied_taunters()
    {
        using var sim = BuildSim();
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), "betelgeuse_passive_p3");
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 3), "betelgeuse_passive_p3");

        Assert.That(
            CombatTauntMarks.Resolve(sim),
            Is.EqualTo(new[] { false, true, false, true }),
            "并列最高 → 1/3 号都标");
    }

    [Test]
    public void Simulation_resolve_throws_on_null()
    {
        Assert.That(() => CombatTauntMarks.Resolve((CombatSimulation)null!), Throws.ArgumentNullException);
    }

    #endregion

    private static CombatSimulation BuildSim()
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "betelgeuse",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [AttributeIds.MaxHealth] = 100f,
                    [AttributeIds.PhysicalAttack] = 10f,
                    [AttributeIds.MagicAttack] = 10f,
                    [AttributeIds.MaxEnergy] = 10f,
                    [AttributeIds.InitialEnergy] = 10f,
                },
                element: EElement.Red,
                race: ERace.Astronomy | ERace.Unknown))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260926);
    }
}
