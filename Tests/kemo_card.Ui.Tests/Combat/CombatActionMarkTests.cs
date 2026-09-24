using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 本回合普攻参与标识（Run 规格 §14.2）：归属者「普攻」、追打持有者「追打」、其余 None；
/// 归属者自己持有追打时不重复出手，仍显示「普攻」。
/// </summary>
[TestFixture]
public sealed class CombatActionMarkTests
{
    private const string FollowUpBuffId = "test.follow_up";

    [Test]
    public void Owner_slot_shows_normal_attack_mark()
    {
        using var sim = BuildPlayerPhase();

        Assert.That(sim.NormalAttacks.ResolveSlotIndex(sim), Is.EqualTo(0), "第 1 回合归属槽位 = 0");
        Assert.That(CombatActionMarks.Resolve(sim, 0), Is.EqualTo(ECombatActionMark.NormalAttack));
        Assert.That(CombatActionMarks.Resolve(sim, 1), Is.EqualTo(ECombatActionMark.None));
    }

    [Test]
    public void Follow_up_holder_shows_follow_up_mark()
    {
        using var sim = BuildPlayerPhase();
        ApplyFollowUp(sim, 2);

        Assert.That(CombatActionMarks.Resolve(sim, 2), Is.EqualTo(ECombatActionMark.FollowUp));
    }

    [Test]
    public void Owner_holding_follow_up_keeps_normal_attack_mark()
    {
        using var sim = BuildPlayerPhase();
        ApplyFollowUp(sim, 0);

        Assert.That(
            CombatActionMarks.Resolve(sim, 0),
            Is.EqualTo(ECombatActionMark.NormalAttack),
            "归属者持有追打也不会补打，标识仍为普攻");
    }

    [Test]
    public void Turn_number_rotates_the_owner()
    {
        using var sim = BuildPlayerPhase();
        sim.IncrementTurnNumber();

        Assert.That(CombatActionMarks.Resolve(sim, 1), Is.EqualTo(ECombatActionMark.NormalAttack));
        Assert.That(CombatActionMarks.Resolve(sim, 0), Is.EqualTo(ECombatActionMark.None));
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void Out_of_range_slot_is_none(int slotIndex)
    {
        using var sim = BuildPlayerPhase();

        Assert.That(CombatActionMarks.Resolve(sim, slotIndex), Is.EqualTo(ECombatActionMark.None));
    }

    private static void ApplyFollowUp(CombatSimulation simulation, int index) =>
        simulation.Buffs.Apply(
            simulation,
            new CombatTargetRef(ECombatSide.Player, index),
            FollowUpBuffId,
            parameters: null);

    private static CombatSimulation BuildPlayerPhase()
    {
        var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 100f,
        };
        var registry = CombatTestHelper.CreateFullRegistry(buffs: new Dictionary<string, BuffDto>
        {
            [FollowUpBuffId] = new()
            {
                Id = FollowUpBuffId,
                DurationType = EBuffDurationType.Permanent,
                Tags = [BuiltinBuffTags.TraitFollowUp],
            },
        });
        var characters = Enumerable.Range(0, 4)
            .Select(index => CharacterBattleInstance.CreateForTests($"c{index}", attributes))
            .ToArray();
        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 400),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260925);
    }
}
