using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class WeilongContentTests
{
    private static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);
    private static CombatTargetRef Enemy(int i = 0) => new(ECombatSide.Enemy, i);

    #region 定义与主动技能

    [Test]
    public void Content_is_admitted_with_identity_four_cards_and_standard_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        var def = BaseGameContent.Load().Characters["weilong"];
        Assert.That(def.Element, Is.EqualTo(EElement.Green));
        Assert.That(def.Role, Is.EqualTo(ERole.Shield));
        Assert.That(def.Race, Is.EqualTo(ERace.Machine | ERace.Dragon));
        Assert.That(def.ActiveSkillChain.Select(t => t.Cooldown), Is.EqualTo(new[] { 6, 6 }));
        Assert.That(def.Passives.Select(p => p.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Green));
            Assert.That(card.Role, Is.EqualTo(ERole.Shield));
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [TestCase(5, false, 0, 0, 0)]
    [TestCase(6, true, 50, 2, 0)]
    [TestCase(11, true, 50, 2, 0)]
    [TestCase(12, true, 100, 4, 6)]
    public void Active_uses_cumulative_thresholds_and_shield_outlasts_the_stance(int progress, bool succeeds, int shield, int turns, int defense)
    {
        using var sim = Build();
        var c = sim.PlayerTeam.Characters[0];
        c.GainSkillCounter(progress);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.EqualTo(succeeds));
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(shield));
        if (!succeeds) { Assert.That(c.SkillCounter, Is.EqualTo(progress)); return; }
        Assert.That(c.SkillCounter, Is.EqualTo(progress == 11 ? 5 : 0));
        Assert.That(Value(sim, AttributeIds.Taunt), Is.EqualTo(3));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.EqualTo(defense));
        Assert.That(Value(sim, AttributeIds.MagicDefense), Is.EqualTo(defense));
        for (var i = 1; i < turns; i++) sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.Taunt), Is.EqualTo(3));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.Taunt), Is.Zero);
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(shield));
        Assert.That(Value(sim, AttributeIds.Shield, 1), Is.Zero);
    }

    [Test]
    public void Strong_and_base_stances_replace_each_other_without_stacking_taunt()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player(), "weilong_shield_stance");
        sim.Buffs.Apply(sim, Player(), "weilong_charged_stance");
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("weilong_shield_stance"), Is.Null);
        Assert.That(Value(sim, AttributeIds.Taunt), Is.EqualTo(3));
        sim.Buffs.Apply(sim, Player(), "weilong_shield_stance");
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(Value(sim, AttributeIds.Taunt), Is.EqualTo(3));
    }

    #endregion

    #region 受击被动与协同

    [TestCase(0)]
    [TestCase(10)]
    public void Fully_negated_hits_counter_only_the_attacker_and_refresh_reactive_armor(float amount)
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 100);
        DamagePipeline.Settle(sim, Enemy(1), Player(), amount);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(Hp(sim, 0), Is.EqualTo(1000));
        Assert.That(Hp(sim, 1), Is.EqualTo(980));
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(32));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.EqualTo(12));
        Assert.That(Value(sim, AttributeIds.Taunt), Is.EqualTo(3));
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(100 - amount), "反击不提供普攻护盾");
        DamagePipeline.Settle(sim, Enemy(1), Player(), 0);
        sim.FlushOnDamagedHits();
        Assert.That(Hp(sim, 1), Is.EqualTo(948));
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(32), "重复受击只刷新");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(20));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(Value(sim, AttributeIds.Taunt), Is.Zero);
    }

    [Test]
    public void Defense_fully_negates_real_attack_but_both_passives_still_trigger()
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.PhysicalDefense, 100);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "training_dummy_strike", Params = new Dictionary<string, object> { ["Amount"] = 5 } }, sim, Enemy(1), [Player()]);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(Hp(sim, 1), Is.EqualTo(980));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.EqualTo(112));
    }

    [Test]
    public void Self_damage_and_teammate_hits_do_not_trigger_owner_passives()
    {
        using var sim = Build();
        Passive(sim, 2); Passive(sim, 4);
        DamagePipeline.Settle(sim, Player(), Player(), 10);
        DamagePipeline.Settle(sim, Enemy(), Player(1), 10);
        sim.FlushOnDamagedHits();
        Assert.That(Hp(sim, 0), Is.EqualTo(1000));
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(20));
    }

    [Test]
    public void Timer_immunity_blocks_slots_and_battle_start_wires_all_passives()
    {
        using var sim = Build(start: true);
        sim.RunBattleStart();
        sim.Buffs.ApplyToSlot(sim, 0, 2, "slot_timer", new Dictionary<string, object> { ["timerTurns"] = 1, ["amount"] = 100 });
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[2].Buffs.Find("slot_timer"), Is.Null);
        Assert.That(Value(sim, AttributeIds.NormalAttackShieldGainScale), Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("weilong_passive_p4"), Is.Not.Null);
    }

    [Test]
    public void Floor_progress_stacks_each_identity_and_repeats_every_six_floor_turns()
    {
        using var sim = Build();
        Passive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 1 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 5; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 1 }));
    }

    #endregion

    #region 普攻护盾与卡牌

    [Test]
    public void Normal_attacks_retain_damage_and_grant_actual_damage_shield_per_target_and_round()
    {
        using var sim = Build();
        Passive(sim, 2);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackCount, 1);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 7);
        sim.EnemyTeam.Enemies[0].Asc.Attributes.SetCurrentValue(AttributeIds.Health, 5);
        sim.EnemyTeam.Enemies[1].Asc.SetBaseValue(AttributeIds.PhysicalDefense, 3);
        sim.EnemyTeam.Enemies[1].Asc.SetBaseValue(AttributeIds.DamageTakenScale, -0.5f);
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.TotalDamage, Is.EqualTo(22));
        Assert.That(Hp(sim, 0), Is.Zero);
        Assert.That(Hp(sim, 1), Is.EqualTo(983));
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(29));
        Assert.That(Value(sim, AttributeIds.Shield, 1), Is.Zero);
    }

    [TestCase(100, 0)]
    [TestCase(0, -1)]
    public void Blocked_normal_damage_does_not_grant_shield(int defense, int reduction)
    {
        using var sim = Build();
        Passive(sim, 2);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
        foreach (var e in sim.EnemyTeam.Enemies)
        {
            e.Asc.SetBaseValue(AttributeIds.PhysicalDefense, defense);
            e.Asc.SetBaseValue(AttributeIds.DamageTakenScale, reduction);
        }
        sim.NormalAttacks.Execute(sim);
        Assert.That(Value(sim, AttributeIds.Shield), Is.Zero);
        Assert.That(Hp(sim, 0), Is.EqualTo(1000));
    }

    [Test]
    public void Follow_up_shield_belongs_to_the_character_that_deals_the_damage()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player(1), "weilong_passive_p2");
        sim.Buffs.Apply(sim, Player(1), "fomalhaut_passive_p4", new Dictionary<string, object> { ["percent"] = 200 });
        sim.PlayerTeam.Characters[1].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.FollowUpDamage, Is.EqualTo(80));
        Assert.That(Value(sim, AttributeIds.Shield, 1), Is.EqualTo(80));
        Assert.That(Value(sim, AttributeIds.Shield), Is.Zero);
    }

    [Test]
    public void Orb_and_fixed_damage_do_not_grant_normal_attack_shield()
    {
        using var sim = Build();
        Passive(sim, 2);
        sim.EffectExecutor.ApplyFixedDamage(sim, Player(), [Enemy()], 25);
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Red, 0, 3);
        sim.Orbs.TriggerManual(sim);
        Assert.That(Hp(sim, 0), Is.LessThan(975));
        Assert.That(Value(sim, AttributeIds.Shield), Is.Zero);
    }

    [Test]
    public void All_four_cards_execute_in_queue_and_damage_cards_do_not_grant_extra_shield()
    {
        var cards = BaseGameContent.Load().Characters["weilong"].Cards.ToArray();
        using var sim = Build(cards: cards);
        Passive(sim, 2);
        for (var slot = 0; slot < 4; slot++)
        {
            var result = sim.TryApply(new PlayCardCommand(0, slot, slot == 1 ? [Enemy(1)] : []));
            Assert.That(result.Success, Is.True, result.Error);
        }
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        Assert.That(Hp(sim, 0), Is.EqualTo(1000));
        Assert.That(Hp(sim, 1), Is.EqualTo(960));
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(90));
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(32));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.EqualTo(6));
        Assert.That(Value(sim, AttributeIds.MagicDefense), Is.EqualTo(6));
        Assert.That(Value(sim, AttributeIds.NormalAttackCount), Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(2));
        Assert.That(sim.PlayerTeam.Characters[0].Graveyard, Has.Count.EqualTo(4));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.NormalAttackCount), Is.Zero);
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(32));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Value(sim, AttributeIds.PhysicalAttack), Is.EqualTo(20));
        Assert.That(Value(sim, AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(Value(sim, AttributeIds.Shield), Is.EqualTo(90));
    }

    #endregion

    #region 构造与断言

    private static CombatSimulation Build(bool start = false, string[]? cards = null)
    {
        var identities = new[] { (EElement.Green, ERace.Machine | ERace.Dragon), (EElement.Green, ERace.Human), (EElement.Blue, ERace.Machine), (EElement.Red, ERace.Dragon) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "weilong" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 10, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["weilong"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var c in characters) c.RefillAvailableEnergy();
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState([new EnemyUnit("e0", "slime", 1000), new EnemyUnit("e1", "slime", 1000)]),
            new CombatRuleEngine([]), BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009,
            initialBuffs: start ? BaseGameContent.Load().Characters["weilong"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
    }

    private static void Passive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"weilong_passive_p{i}");
    private static float Value(CombatSimulation sim, string id, int index = 0) => sim.PlayerTeam.Characters[index].Asc.GetCurrentValue(id);
    private static float Hp(CombatSimulation sim, int i) => sim.EnemyTeam.Enemies[i].Asc.GetCurrentValue(AttributeIds.Health);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();

    #endregion
}