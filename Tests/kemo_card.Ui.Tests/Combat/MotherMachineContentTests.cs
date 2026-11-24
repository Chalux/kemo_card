using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class MotherMachineContentTests
{
    private static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);

    #region 定义与主动充能

    [Test]
    public void Content_and_healing_targets_validate_with_the_requested_identity_and_card_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var def = BaseGameContent.Load().Characters["mother_machine"];
        Assert.That(def.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(def.Role, Is.EqualTo(ERole.Support));
        Assert.That(def.Race, Is.EqualTo(ERace.Machine | ERace.Academic));
        Assert.That(def.ActiveSkillChain.Single().Cooldown, Is.EqualTo(10));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow));
            Assert.That(card.Role, Is.EqualTo(ERole.Support));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_spends_ten_progress_and_slot_payloads_have_no_effect()
    {
        using var sim = Build();
        var c = sim.PlayerTeam.Characters[0];
        c.GainSkillCounter(9);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.False);
        Assert.That(c.SkillCounter, Is.EqualTo(9));
        c.GainSkillCounter(1);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(c.SkillCounter, Is.Zero);
        Assert.That(sim.Presentation.Drain().OfType<KemoCard.Mod.Combat.Presentation.BuffAppliedEvent>(), Is.Empty);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(100f));
    }

    #endregion

    #region 被动维护与叠层

    [Test]
    public void Progress_stacks_self_yellow_machine_and_academic_every_ten_floor_turns()
    {
        using var sim = Build();
        Passive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 9; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
    }

    #endregion

    #region 专属卡与实际指令流程

    [Test]
    public void Four_cards_execute_with_preload_healing_shields_next_draw_and_temporary_energy()
    {
        var cards = BaseGameContent.Load().Characters["mother_machine"].Cards.ToArray();
        using var sim = Build(cards: cards);
        foreach (var slot in new[] { 3, 0, 1, 2 })
        {
            var result = sim.TryApply(new PlayCardCommand(0, slot, []));
            Assert.That(result.Success, Is.True, result.Error);
        }
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(185), "预载先结算，两张治疗分别50与35");
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.Shield) == 20), Is.True);
        var owner = sim.PlayerTeam.Characters[0];
        Assert.That(owner.SkillCounter, Is.EqualTo(2));
        Assert.That(owner.AvailableEnergy, Is.EqualTo(6));
        Assert.That(owner.CurrentEnergy, Is.EqualTo(10));
        Assert.That(owner.ComputeDrawCount(), Is.EqualTo(3));
        Assert.That(owner.Graveyard, Has.Count.EqualTo(4));
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(HealPower(sim), Is.EqualTo(6));
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(owner.ComputeDrawCount(), Is.EqualTo(1));
        Assert.That(owner.AvailableEnergy, Is.EqualTo(owner.CurrentEnergy));
    }

    #endregion

    #region 构造与断言

    private static CombatSimulation Build(bool start = false, string[]? cards = null)
    {
        var identities = new[] { (EElement.Yellow, ERace.Machine | ERace.Academic), (EElement.Yellow, ERace.Human), (EElement.Blue, ERace.Machine), (EElement.Green, ERace.Academic) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "mother_machine" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10, [AttributeIds.HealPower] = 6,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 10, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["mother_machine"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var c in characters) c.RefillAvailableEnergy();
        var sim = new CombatSimulation(new PlayerTeamState(characters, 1000), new EnemyTeamState([new EnemyUnit("e0", "slime", 1000)]),
            new CombatRuleEngine([]), BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009,
            initialBuffs: start ? BaseGameContent.Load().Characters["mother_machine"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
        sim.PlayerTeam.ApplySharedDamage(900);
        return sim;
    }

    private static void Passive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"mother_machine_passive_p{i}");
    private static float HealPower(CombatSimulation sim) => sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.HealPower);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();
    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(10);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    #endregion
}
