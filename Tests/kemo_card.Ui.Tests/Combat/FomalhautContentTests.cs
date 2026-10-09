using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class FomalhautContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);

    #region 内容和主动技能

    [Test]
    public void Shipped_character_has_four_blue_mage_cards_and_valid_attribute_budgets()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        var character = BaseGameContent.Load().Characters["fomalhaut"];
        Assert.That(character.Element, Is.EqualTo(EElement.Blue));
        Assert.That(character.Role, Is.EqualTo(ERole.Mage));
        Assert.That(character.Race, Is.EqualTo(ERace.Astronomy | ERace.Unknown));
        Assert.That(character.ActiveSkillChain.Single().Cooldown, Is.EqualTo(8));
        Assert.That(character.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(character.Cards, Has.Count.EqualTo(4));
        foreach (var id in character.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Role, Is.EqualTo(ERole.Mage));
            Assert.That(card.Element, Is.EqualTo((int)EElement.Blue));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10), Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_increases_only_own_magic_attack_for_exactly_three_turns()
    {
        using var sim = NewSimulation();
        Cast(sim);
        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(35));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10));
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(character => character.Asc.GetCurrentValue(AttributeIds.MagicAttack) == 20), Is.True);
        Assert.That(caster.SkillCounter, Is.Zero);
        Assert.That(caster.AvailableEnergy, Is.EqualTo(10));
        Assert.That(caster.HasActed, Is.False);
        for (var turn = 0; turn < 2; turn++)
        {
            sim.Buffs.FireTurnEnd(sim);
            Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(35));
        }
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(20));
    }

    [Test]
    public void Recasting_active_refreshes_duration_without_stacking_attack()
    {
        using var sim = NewSimulation();
        Cast(sim);
        sim.Buffs.FireTurnEnd(sim);
        Cast(sim);
        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(caster.Buffs.All.Count(buff => buff.Def.Id == "fomalhaut_alpha_power"), Is.EqualTo(1));
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(35));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(20));
    }

    #endregion

    #region 被动和普攻

    [Test]
    public void Slot_damage_immunity_blocks_real_slot_damage_payload()
    {
        using var sim = NewSimulation();
        var caster = sim.PlayerTeam.Characters[0];
        caster.HandSlots[0].Buffs.Add(new BuffDto
        {
            Id = "test_fomalhaut_slot", Tags = [BuiltinBuffTags.SlotDamage],
            Hooks = new BuffEffectHooksDto { OnSlotCardPlayed = [new EffectRefDto { EffectId = "chalux_charge_burst" }] },
        }, null);
        sim.Buffs.FireSlotCardPlayed(sim, 0, caster.HandSlots[0]);
        var hp = sim.PlayerTeam.SharedHpExact;
        Assert.That(hp, Is.LessThan(1000));
        ApplyPassive(sim, 1);
        sim.Buffs.FireSlotCardPlayed(sim, 0, caster.HandSlots[0]);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(hp));
    }

    [Test]
    public void Extra_attack_adds_one_round_and_followup_does_not_repeat_on_own_turn()
    {
        using var sim = NewSimulation();
        EnableNormalAttacks(sim);
        ApplyPassive(sim, 2);
        ApplyPassive(sim, 2);
        ApplyPassive(sim, 4);
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.Executions, Is.EqualTo(2));
        Assert.That(result.Strikes, Has.Count.EqualTo(2));
        Assert.That(result.Strikes.All(strike => strike.IsOwnerStrike && strike.Percent == 100), Is.True);
        Assert.That(result.TotalDamage, Is.EqualTo(60), "两次普攻，每次对两名敌人造成20-5");
        Assert.That(FollowUpAttack.ResolvePercent(sim.PlayerTeam.Characters[0]), Is.EqualTo(200));
    }

    [Test]
    public void Two_hundred_percent_followup_uses_attack_before_defense_and_repeats_per_owner_round()
    {
        using var sim = NewSimulation();
        EnableNormalAttacks(sim);
        ApplyPassive(sim, 2);
        ApplyPassive(sim, 4);
        sim.IncrementTurnNumber();
        sim.PlayerTeam.Characters[1].Asc.SetBaseValue(AttributeIds.NormalAttackCount, 1);
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.Executions, Is.EqualTo(2));
        Assert.That(result.Strikes, Has.Count.EqualTo(4));
        var followups = result.Strikes.Where(strike => !strike.IsOwnerStrike).ToArray();
        Assert.That(followups, Has.Length.EqualTo(2));
        Assert.That(followups.All(strike => strike.SlotIndex == 0 && strike.Percent == 200 && strike.Kind == EDamageKind.Magical && strike.Element == EElement.Blue), Is.True);
        Assert.That(result.FollowUpDamage, Is.EqualTo(140), "每轮(20×2-5)×2敌人；自身额外普攻次数不增加追打次数");
    }

    [Test]
    public void Followup_parameter_is_wired_from_shipped_passive_through_battle_start()
    {
        using var sim = NewSimulation(start: true);
        sim.AdvancePhase();
        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(FollowUpAttack.ResolvePercent(caster), Is.EqualTo(200));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.NormalAttackCount), Is.EqualTo(1));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 6, 3, 3, 1 }));
        Assert.That(sim.Buffs.Dispel(sim, Player(), "fomalhaut_passive_p4"), Is.Zero);
    }

    [Test]
    public void Floor_charge_stacks_identities_and_repeats_every_eight_turns()
    {
        using var sim = NewSimulation();
        ApplyPassive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 2, 2, 0 }));
        ResetCounters(sim);
        sim.Buffs.FireTurnStart(sim);
        for (var turn = 1; turn < 8; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 2, 2, 0 }));
    }

    #endregion

    #region 四张专属卡

    [Test]
    public void Southern_light_hits_only_selected_enemy_with_blue_magic_damage()
    {
        using var sim = NewSimulation(["fomalhaut_southern_fish_light"]);
        Mark(sim, 0, new CombatTargetRef(ECombatSide.Enemy, 1));
        Execute(sim);
        Assert.That(Hp(sim, 0), Is.EqualTo(1000));
        Assert.That(Hp(sim, 1), Is.EqualTo(977), "8+20魔攻-5魔防");
    }

    [Test]
    public void Stellar_tide_hits_all_enemies_and_charges_only_self()
    {
        using var sim = NewSimulation(["fomalhaut_stellar_tide"]);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(Hp(sim, 0), Is.EqualTo(986));
        Assert.That(Hp(sim, 1), Is.EqualTo(986));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 1, 0, 0, 0 }));
    }

    [Test]
    public void Dust_ring_stacks_with_active_and_expires_after_three_turns()
    {
        using var sim = NewSimulation(["fomalhaut_dust_ring"]);
        Mark(sim, 0);
        Execute(sim);
        sim.TransitionTo(ECombatPhase.Player);
        Cast(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(43));
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(43));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(20));
    }

    [Test]
    public void Echo_amplifies_followup_but_does_not_amplify_card_damage()
    {
        using var sim = NewSimulation(["fomalhaut_lone_star_echo", "fomalhaut_southern_fish_light"]);
        Mark(sim, 0);
        Mark(sim, 1, new CombatTargetRef(ECombatSide.Enemy, 0));
        Execute(sim);
        Assert.That(Hp(sim, 0), Is.EqualTo(977), "普攻增伤不改变卡牌伤害");
        EnableNormalAttacks(sim);
        ApplyPassive(sim, 4);
        sim.IncrementTurnNumber();
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.FollowUpDamage, Is.EqualTo(105), "(20×2-5)×1.5×2敌人");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.NormalAttackDamageDealtScale), Is.EqualTo(0.5));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.NormalAttackDamageDealtScale), Is.Zero);
    }

    #endregion

    #region 场景构造

    private static CombatSimulation NewSimulation(string[]? cards = null, bool start = false)
    {
        var definition = BaseGameContent.Load().Characters["fomalhaut"];
        var identities = new[] { (EElement.Blue, ERace.Astronomy | ERace.Unknown), (EElement.Blue, ERace.Human), (EElement.Red, ERace.Astronomy | ERace.Unknown), (EElement.Green, ERace.Animal) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "fomalhaut" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.PhysicalAttack] = 10, [AttributeIds.MagicAttack] = 20,
                [AttributeIds.NormalAttackDamageDealtScale] = -2, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, number) => new CardRuntimeEntry(id, $"card-{number}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? definition.ActiveSkillChain : null, element: identity.Item1, race: identity.Item2)).ToArray();
        if (!start)
            characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        var enemies = Enumerable.Range(0, 2).Select(index => new EnemyUnit($"e{index}", "slime", new Dictionary<string, float>
        {
            [AttributeIds.MaxHealth] = 1000, [AttributeIds.PhysicalDefense] = 50, [AttributeIds.MagicDefense] = 5,
        })).ToArray();
        return new CombatSimulation(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies), new CombatRuleEngine([]),
            BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261008,
            initialBuffs: start ? definition.Passives.Select(passive => new BattleStartBuffEntry(0, passive.BuffId, passive.Params)).ToArray() : null);
    }

    private static void ApplyPassive(CombatSimulation sim, int number)
    {
        var passive = BaseGameContent.Load().Characters["fomalhaut"].Passives[number - 1];
        sim.Buffs.Apply(sim, Player(), passive.BuffId, passive.Params);
    }

    private static void EnableNormalAttacks(CombatSimulation sim)
    {
        foreach (var character in sim.PlayerTeam.Characters)
            character.Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
    }

    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(8);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Mark(CombatSimulation sim, int slot, params CombatTargetRef[] targets)
    {
        var result = sim.TryApply(new PlayCardCommand(0, slot, targets));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Execute(CombatSimulation sim) { sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase(); }
    private static float Hp(CombatSimulation sim, int index) => sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Health);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();
    private static void ResetCounters(CombatSimulation sim) { foreach (var character in sim.PlayerTeam.Characters) character.PaySkillCounter(character.SkillCounter); }

    #endregion
}