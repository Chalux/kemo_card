using KemoCard.Frame.Content;
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
public sealed class EtaCarinaeContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);

    #region 内容与主动技能

    [Test]
    public void Character_and_four_exclusive_cards_pass_content_and_heal_validation()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var character = BaseGameContent.Load().Characters["eta_carinae"];
        Assert.That(character.Element, Is.EqualTo(EElement.Green));
        Assert.That(character.Role, Is.EqualTo(ERole.Elementist));
        Assert.That(character.Race, Is.EqualTo(ERace.Astronomy | ERace.Calamity));
        Assert.That(character.ActiveSkillChain.Single().Cooldown, Is.EqualTo(6));
        Assert.That(character.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(character.Cards, Has.Count.EqualTo(4));
        foreach (var id in character.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Role, Is.EqualTo(ERole.Elementist));
            Assert.That(card.Element, Is.EqualTo((int)EElement.Green));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10), Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_duplicates_each_turn_end_orb_with_eta_as_copy_producer()
    {
        using var sim = Build();
        Cast(sim);
        sim.Orbs.GrantTurnEndOrbs(sim, [new PlayedCardRecord("eta_carinae_carina_nova", 0)]);
        var orbs = sim.Orbs.Queue.Orbs;
        Assert.That(orbs, Has.Count.EqualTo(4));
        Assert.That(orbs[0].OrbTypeId, Is.EqualTo(BuiltinOrbTypes.Green));
        Assert.That(orbs[1].OrbTypeId, Is.EqualTo(orbs[0].OrbTypeId));
        Assert.That(orbs[2].OrbTypeId, Is.EqualTo(orbs[3].OrbTypeId));
        Assert.That(orbs.Select(orb => orb.ProducerIndex), Is.EqualTo(new[] { 1, 0, 1, 0 }));
        sim.Orbs.Queue.Clear();
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Green, 1);
        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(1), "卡牌/钩子产球不受主动翻倍");
    }

    [Test]
    public void Active_duplicates_four_end_steps_including_the_expiration_turn()
    {
        using var sim = Build();
        Cast(sim);
        for (var turn = 0; turn < 5; turn++)
        {
            sim.Orbs.Queue.Clear();
            sim.RecordPlayedCard("eta_carinae_carina_nova", 0);
            sim.ResolveTurnEnd();
            Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(turn < 4 ? 4 : 2), $"第{turn + 1}次回合结束");
            sim.BeginNextTurn(incrementTurnsIntoWave: true);
        }
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("eta_carinae_double_orbs"), Is.Null);
    }

    #endregion

    #region 自动触发与护盾

    [TestCase(true, 7, 483, 50)]
    [TestCase(false, 3, 138, 0)]
    public void Automatic_passives_apply_to_whole_batch_even_when_eta_produces_no_orb(bool automatic, int count, int damage, int shield)
    {
        using var sim = Build();
        ApplyPassive(sim, 2);
        ApplyPassive(sim, 4);
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Green, 1, count);
        if (!automatic)
            sim.Orbs.TriggerManual(sim);
        Assert.That(Hp(sim), Is.EqualTo(10000 - damage));
        Assert.That(Shield(sim), Is.EqualTo(shield));
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(character => character.Asc.GetCurrentValue(AttributeIds.Shield) == 0), Is.True);
    }

    [Test]
    public void Each_automatic_batch_grants_one_shield_reward_not_one_per_orb()
    {
        using var sim = Build();
        ApplyPassive(sim, 4);
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Green, 1, 14);
        Assert.That(Shield(sim), Is.EqualTo(100));
        Assert.That(sim.Orbs.Queue.Count, Is.Zero);
    }

    [Test]
    public void Shield_break_requires_positive_to_zero_and_buff_lasts_two_turns()
    {
        using var sim = Build();
        ApplyPassive(sim, 4);
        var caster = sim.PlayerTeam.Characters[0];
        caster.Asc.SetBaseValue(AttributeIds.Shield, 0);
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost"), Is.Null);
        caster.Asc.SetBaseValue(AttributeIds.Shield, 50);
        DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), Player(), 20);
        Assert.That(Shield(sim), Is.EqualTo(30));
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost"), Is.Null);
        DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), Player(), 30);
        Assert.That(Shield(sim), Is.Zero);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.OrbDamageScale), Is.EqualTo(0.5));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.OrbHealingScale), Is.EqualTo(0.5));
        sim.Buffs.FireTurnEnd(sim);
        caster.Asc.Aggregator.Recalculate(AttributeIds.Shield);
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost")!.RemainingTurns, Is.EqualTo(1), "重复零不能刷新");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost"), Is.Null);
    }

    [Test]
    public void Removing_temporary_shield_also_triggers_break_but_base_write_intermediate_zero_does_not()
    {
        using var sim = Build();
        ApplyPassive(sim, 4);
        var caster = sim.PlayerTeam.Characters[0];
        var temporary = new BuffDto
        {
            Id = "test_eta_shield", Modifiers = [new AttributeModifierDefDto
        {
            AttributeId = AttributeIds.Shield, Operation = EAttributeModifierOp.Add,
            Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 20 },
        }]
        };
        var instance = caster.Buffs.Add(temporary, null);
        caster.Asc.SetBaseValue(AttributeIds.Shield, 0);
        Assert.That(Shield(sim), Is.EqualTo(20));
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost"), Is.Null);
        caster.Buffs.Remove(instance);
        Assert.That(caster.Buffs.Find("eta_carinae_orb_boost"), Is.Not.Null);
    }

    [Test]
    public void Automatic_and_own_orb_bonus_stack_additively_without_boosting_ally_orbs_twice()
    {
        using var sim = Build();
        ApplyPassive(sim, 2);
        sim.Buffs.Apply(sim, Player(), "eta_carinae_orb_boost");
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Green, 1, 6);
        sim.Orbs.Grant(sim, BuiltinOrbTypes.Green, 0);
        Assert.That(Hp(sim), Is.EqualTo(10000 - (6 * 46 * 1.5 + 26 * 2)));
    }

    [TestCase(true, 7, 105, false)]
    [TestCase(true, 7, 140, true)]
    [TestCase(false, 3, 30, false)]
    [TestCase(false, 3, 45, true)]
    public void Automatic_healing_orbs_receive_batch_bonus_and_heal_shared_ledger(bool automatic, int count, int healed, bool ownBonus)
    {
        using var sim = Build(registry: HealingRegistry());
        ApplyPassive(sim, 2);
        sim.PlayerTeam.ApplySharedDamage(600);
        if (ownBonus)
            sim.Buffs.Apply(sim, Player(), "eta_carinae_orb_boost");
        sim.SetChainBonus(0.5f);
        sim.Orbs.Grant(sim, "test_eta_heal_orb", ownBonus ? 0 : 1, count);
        if (!automatic)
            sim.Orbs.TriggerManual(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(400 + healed));
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        Assert.That(sim.CurrentOrbHealingBonus, Is.Zero);
        Assert.That(sim.CurrentChainBonus, Is.EqualTo(0.5f), "元素球治疗不吃连携，结束后恢复上下文");
    }

    #endregion

    #region 被动与卡牌

    [Test]
    public void Floor_progress_stacks_green_astronomy_and_calamity_every_eight_turns()
    {
        using var sim = Build();
        ApplyPassive(sim, 3);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 2, 0 }));
        foreach (var character in sim.PlayerTeam.Characters)
            character.PaySkillCounter(character.SkillCounter);
        sim.Buffs.FireTurnStart(sim);
        for (var turn = 1; turn < 8; turn++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 2, 0 }));
    }

    [Test]
    public void Storm_immunity_protects_neighbouring_cards()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, EffectDto>(definitions.Effects) { ["test_eta_discard_slot"] = new() { Id = "test_eta_discard_slot", Kind = EEffectKind.DiscardSlot } };
        var buffs = new Dictionary<string, BuffDto>(definitions.Buffs)
        {
            ["test_eta_storm"] = new()
            {
                Id = "test_eta_storm", Tags = [BuiltinBuffTags.SlotStorm],
                Hooks = new BuffEffectHooksDto { OnSlotCardPlayed = [new EffectRefDto { EffectId = "test_eta_discard_slot" }] },
            }
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Effects = effects, Buffs = buffs })], out var report);
        Assert.That(report.HasIssues, Is.False);
        using var sim = Build(Enumerable.Repeat("eta_carinae_carina_nova", 5).ToArray(), registry);
        var caster = sim.PlayerTeam.Characters[0];
        sim.Buffs.ApplyToSlot(sim, 0, 2, "test_eta_storm");
        ApplyPassive(sim, 1);
        sim.Buffs.FireSlotCardPlayed(sim, 0, caster.HandSlots[2]);
        Assert.That(caster.HandSlots.All(slot => !slot.IsEmpty), Is.True);
        caster.Buffs.Remove(caster.Buffs.Find("eta_carinae_passive_p1")!);
        sim.Buffs.FireSlotCardPlayed(sim, 0, caster.HandSlots[2]);
        Assert.That(caster.HandSlots[1].IsEmpty, Is.True);
    }

    [TestCase("eta_carinae_carina_nova", new[] { "green", "green" })]
    [TestCase("eta_carinae_bipolar_jet", new[] { "green", "magic" })]
    public void Orb_cards_grant_expected_orbs_with_self_producer(string card, string[] types)
    {
        using var sim = Build([card]);
        Play(sim);
        Assert.That(sim.Orbs.Queue.Orbs.Select(orb => orb.OrbTypeId), Is.EqualTo(types));
        Assert.That(sim.Orbs.Queue.Orbs.All(orb => orb.ProducerIndex == 0), Is.True);
    }

    [Test]
    public void Ring_grants_shield_and_progress_and_afterglow_heals_without_orb_amplification()
    {
        using var sim = Build(["eta_carinae_cataclysm_ring", "eta_carinae_nebula_afterglow"]);
        sim.Buffs.Apply(sim, Player(), "eta_carinae_orb_boost");
        ApplyPassive(sim, 2);
        sim.PlayerTeam.ApplySharedDamage(600);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.True);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 1, [])).Success, Is.True);
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        Assert.That(Shield(sim), Is.EqualTo(35));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 1, 0, 0, 0 }));
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(425), "恢复15+恢复量10，非元素球治疗不吃球倍率");
        Assert.That(sim.Orbs.Queue.Orbs.Single().ProducerIndex, Is.Zero);
    }

    #endregion

    #region 场景构造

    private static CombatSimulation Build(string[]? cards = null, GameDefinitionRegistry? registry = null)
    {
        var identities = new[] { (EElement.Green, ERace.Astronomy | ERace.Calamity), (EElement.Green, ERace.Human), (EElement.Blue, ERace.Astronomy | ERace.Calamity), (EElement.Red, ERace.Animal) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "eta_carinae" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
                [AttributeIds.PhysicalAttack] = index == 1 ? 40 : 10, [AttributeIds.MagicAttack] = index == 1 ? 40 : 20,
                [AttributeIds.HealPower] = 10, [AttributeIds.NormalAttackDamageDealtScale] = -1
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["eta_carinae"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters) character.RefillAvailableEnergy();
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 10000)]),
            new CombatRuleEngine([]), registry ?? BaseGameContent.BuildRegistry(), initialPhase: ECombatPhase.Player, runSeed: 20261008);
    }

    private static GameDefinitionRegistry HealingRegistry()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, EffectDto>(definitions.Effects)
        {
            ["test_eta_heal"] = new()
            {
                Id = "test_eta_heal", Kind = EEffectKind.Heal,
                Params = new() { ["amount"] = 10, ["healPowerScale"] = 0, ["hookTargets"] = "team" }
            }
        };
        var orbs = new Dictionary<string, OrbTypeDto>(definitions.OrbTypes)
        {
            ["test_eta_heal_orb"] = new()
            {
                Id = "test_eta_heal_orb", DealsDamage = false,
                TriggerEffects = [new EffectRefDto { EffectId = "test_eta_heal" }]
            }
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Effects = effects, OrbTypes = orbs })], out var report);
        Assert.That(report.HasIssues, Is.False);
        return registry;
    }

    private static void ApplyPassive(CombatSimulation sim, int number) => sim.Buffs.Apply(sim, Player(), $"eta_carinae_passive_p{number}");
    private static float Shield(CombatSimulation sim) => sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield);
    private static float Hp(CombatSimulation sim) => sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();
    private static void Cast(CombatSimulation sim) { sim.PlayerTeam.Characters[0].GainSkillCounter(6); var r = sim.TryApply(new CastActiveSkillCommand(0, [])); Assert.That(r.Success, Is.True, r.Error); }
    private static void Play(CombatSimulation sim) { var r = sim.TryApply(new PlayCardCommand(0, 0, [])); Assert.That(r.Success, Is.True, r.Error); sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase(); }

    #endregion
}