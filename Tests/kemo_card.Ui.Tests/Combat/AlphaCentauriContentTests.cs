using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class AlphaCentauriContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);

    #region 内容与主动技能

    [Test]
    public void Player_selects_exact_cards_without_consuming_discard_rng()
    {
        using var sim = NewSimulation(Enumerable.Repeat("alpha_centauri_southern_light", 5).ToArray());
        using var control = NewSimulation();
        var caster = sim.PlayerTeam.Characters[0];
        caster.GainSkillCounter(9);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [], [1, 4])).Success, Is.True);
        Assert.That(caster.HandSlots.Select(slot => slot.IsEmpty), Is.EqualTo(new[] { false, true, false, false, true }));
        Assert.That(sim.LastDiscardCount, Is.EqualTo(2));
        Assert.That(sim.DiscardRng.NextInt(0, 100000), Is.EqualTo(control.DiscardRng.NextInt(0, 100000)));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Up_to_two_allows_fewer_cards_even_with_a_full_hand(int count)
    {
        using var sim = NewSimulation(Enumerable.Repeat("alpha_centauri_southern_light", 5).ToArray());
        sim.PlayerTeam.Characters[0].GainSkillCounter(9);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [], Enumerable.Range(0, count).ToArray())).Success, Is.True);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(count));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(5 - count));
    }

    [TestCase(new[] { 0, 0 })]
    [TestCase(new[] { 0, 1, 2 })]
    [TestCase(new[] { 4 })]
    public void Illegal_selection_is_rejected_before_any_payment_or_discard(int[] slots)
    {
        using var sim = NewSimulation(Enumerable.Repeat("alpha_centauri_southern_light", 3).ToArray());
        var caster = sim.PlayerTeam.Characters[0];
        caster.GainSkillCounter(9);
        var result = sim.TryApply(new CastActiveSkillCommand(0, [], slots));
        Assert.That(result.Success, Is.False);
        Assert.That(caster.SkillCounter, Is.EqualTo(9));
        Assert.That(caster.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(3));
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1));
    }

    [Test]
    public void Content_has_four_exclusive_support_cards_and_passes_budget_and_target_validation()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var character = BaseGameContent.Load().Characters["alpha_centauri"];
        Assert.That(character.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(character.Role, Is.EqualTo(ERole.Support));
        Assert.That(character.Race, Is.EqualTo(ERace.Astronomy | ERace.Academic));
        Assert.That(character.ActiveSkillChain.Single().Cooldown, Is.EqualTo(9));
        Assert.That(character.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(character.Cards, Has.Count.EqualTo(4));
        foreach (var id in character.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow));
            Assert.That(card.Role, Is.EqualTo(ERole.Support));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10), Is.EqualTo(40));
        }
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(5, 2)]
    public void Active_scales_by_actual_discard_and_does_not_draw_immediately(int handSize, int discarded)
    {
        using var sim = NewSimulation(Enumerable.Repeat("alpha_centauri_southern_light", handSize).ToArray());
        Cast(sim);
        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(sim.LastDiscardCount, Is.EqualTo(discarded));
        Assert.That(caster.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(handSize - discarded));
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1 + discarded));
        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10 + 15 * discarded));
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(20 + 15 * discarded));
        }
        Assert.That(sim.PlayerTeam.Characters.Skip(1).All(character => character.ComputeDrawCount() == 1), Is.True);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(20 + 15 * discarded));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Buffs.Find("alpha_centauri_alpha_blessing") is null), Is.True);
    }

    [Test]
    public void Active_discard_cancels_marked_cards_and_refunds_paid_energy()
    {
        using var sim = NewSimulation(["alpha_centauri_southern_light", "alpha_centauri_binary_resonance"]);
        Mark(sim, 0);
        Mark(sim, 1);
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(7));
        Cast(sim);
        Assert.That(sim.CardQueue.Count, Is.Zero);
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(10));
        Assert.That(sim.LastDiscardCount, Is.EqualTo(2));
    }

    [Test]
    public void Extra_draw_stacks_with_ordinary_bonus_and_other_casts_then_is_consumed()
    {
        using var sim = NewSimulation(Enumerable.Repeat("alpha_centauri_southern_light", 4).ToArray(), reserves: 12);
        var caster = sim.PlayerTeam.Characters[0];
        caster.AddDrawModifier(3);
        caster.AddDrawModifier(2);
        caster.AddDrawModifier(-1);
        Cast(sim);
        Cast(sim);
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(7), "1 + 普通最大增益3 - 减益1 + 两次额外2");
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(50), "主动增益按最新快照替换，不叠层");
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(caster.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(5), "额外抽牌仍遵守手牌上限");
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(1));
        Assert.That(sim.LastDiscardCount, Is.Zero);
    }

    [Test]
    public void Ordinary_discard_draw_keeps_maximum_rule_and_additive_effect_channel_stacks()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, EffectDto>(definitions.Effects)
        {
            ["test_alpha_draw"] = new() { Id = "test_alpha_draw", Kind = EEffectKind.ModifyDrawCountByDiscard },
            ["test_alpha_buff"] = new() { Id = "test_alpha_buff", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "alpha_centauri_alpha_blessing" } },
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Effects = effects })], out var report);
        Assert.That(report.HasIssues, Is.False);
        using var sim = NewSimulation(["alpha_centauri_southern_light"], registry: registry);
        Cast(sim);
        var caster = sim.PlayerTeam.Characters[0];
        sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = "test_alpha_draw", Params = new() { ["perCard"] = 2 } }, sim, Player(), [Player()]);
        caster.AddDrawModifier(3);
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(5), "普通2与3只取3，加主动额外1");
        sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = "test_alpha_draw", Params = new() { ["perCard"] = 2, ["additive"] = true } }, sim, Player(), [Player()]);
        Assert.That(caster.ComputeDrawCount(), Is.EqualTo(7));
        sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = "test_alpha_buff", Params = new() { ["buffId"] = "alpha_centauri_alpha_blessing", ["amountPerDiscard"] = 7 } }, sim, Player(), [Player()]);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(27), "效果通道也按实际弃牌数缩放");
    }

    #endregion

    #region 被动技能

    [Test]
    public void Poison_immunity_blocks_poison_while_control_ally_receives_it()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, GameplayEffectDefDto>(definitions.GameplayEffects)
        {
            ["test_alpha_poison"] = new() { Id = "test_alpha_poison", DurationPolicy = EDurationPolicy.Infinite, GrantedTags = [CombatConstants.PoisonTag] },
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { GameplayEffects = effects })], out var report);
        Assert.That(report.HasIssues, Is.False);
        using var sim = NewSimulation(registry: registry);
        sim.Buffs.Apply(sim, Player(), "alpha_centauri_passive_p1");
        new GameplayEffectApplicator(registry).ApplyToTargets(sim, new(ECombatSide.Enemy, 0), [Player(), Player(1)], "test_alpha_poison");
        Assert.That(sim.PlayerTeam.Characters[0].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.False);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.True);
    }

    [Test]
    public void Floor_charge_stacks_identities_and_repeats_every_nine_turns()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "alpha_centauri_passive_p3");
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 2, 0 }));
        ResetCounters(sim);
        sim.Buffs.FireTurnStart(sim);
        for (var turn = 1; turn < 9; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 2, 0 }));
    }

    [Test]
    public void Passive_four_grants_battle_and_active_progress_but_not_card_or_floor_progress()
    {
        using var sim = NewSimulation(["alpha_centauri_centaur_guidance"], start: true);
        sim.AdvancePhase();
        Assert.That(Counters(sim), Is.EqualTo(new[] { 9, 2, 3, 1 }), "自身开战3+阶层5+自然1");
        var result = sim.TryApply(new CastActiveSkillCommand(0, [], sim.PlayerTeam.Characters[0].HandSlots.Where(slot => !slot.IsEmpty).Take(2).Select(slot => slot.SlotIndex).ToArray()));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(3));
        sim.Buffs.FireWaveStart(sim);
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(8), "换阶层只加P3的5，不重发P4的3");
        using var cards = NewSimulation(["alpha_centauri_centaur_guidance"]);
        cards.Buffs.Apply(cards, Player(), "alpha_centauri_passive_p4");
        ResetCounters(cards);
        Mark(cards, 0);
        Execute(cards);
        Assert.That(cards.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(1), "只获得卡牌本身的1点充能");
    }

    #endregion

    #region 四张专属卡

    [TestCase("alpha_centauri_southern_light", 12)]
    [TestCase("alpha_centauri_stellar_guard", 6)]
    public void Healing_uses_own_heal_power_and_passive_amplification_once(string cardId, int amount)
    {
        using var sim = NewSimulation([cardId]);
        sim.Buffs.Apply(sim, Player(), "alpha_centauri_passive_p2");
        sim.Buffs.Apply(sim, Player(), "alpha_centauri_passive_p2");
        sim.PlayerTeam.ApplySharedDamage(600);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(400 + (amount + 12) * 1.25));
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.GetCurrentValue(AttributeIds.HealingDealtScale), Is.Zero);
        if (cardId.EndsWith("stellar_guard", StringComparison.Ordinal))
            Assert.That(sim.PlayerTeam.Characters.Select(character => character.Asc.GetCurrentValue(AttributeIds.Shield)), Is.EqualTo(new[] { 25f, 25f, 25f, 25f }));
    }

    [Test]
    public void Resonance_buffs_all_allies_for_two_turns()
    {
        using var sim = NewSimulation(["alpha_centauri_binary_resonance"]);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Asc.GetCurrentValue(AttributeIds.MagicAttack) == 28), Is.True);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(28));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Asc.GetCurrentValue(AttributeIds.MagicAttack) == 20), Is.True);
    }

    [Test]
    public void Guidance_grants_temporary_energy_and_one_skill_progress()
    {
        using var sim = NewSimulation(["alpha_centauri_centaur_guidance"]);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(11));
        Assert.That(sim.PlayerTeam.Characters[0].CurrentEnergy, Is.EqualTo(10));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 1, 0, 0, 0 }));
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(10));
    }

    #endregion

    #region 场景构造

    private static CombatSimulation NewSimulation(string[]? cards = null, GameDefinitionRegistry? registry = null, int reserves = 0, bool start = false)
    {
        var identities = new[] { (EElement.Yellow, ERace.Astronomy | ERace.Academic), (EElement.Yellow, ERace.Human), (EElement.Blue, ERace.Astronomy | ERace.Academic), (EElement.Green, ERace.Animal) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "alpha_centauri" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.PhysicalAttack] = 10, [AttributeIds.MagicAttack] = 20,
                [AttributeIds.HealPower] = 6, [AttributeIds.NormalAttackDamageDealtScale] = -1,
                [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 ? (cards ?? []).Concat(Enumerable.Repeat("alpha_centauri_southern_light", reserves)).Select((id, number) => new CardRuntimeEntry(id, $"card-{number}")) : null,
            skillCounterCap: 20,
            activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["alpha_centauri"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        if (!start)
            characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        var enemies = new[] { new EnemyUnit("e0", "slime", new Dictionary<string, float> { [AttributeIds.MaxHealth] = 1000 }) };
        return new CombatSimulation(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies), new CombatRuleEngine([]),
            registry ?? BaseGameContent.BuildRegistry(), initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261008,
            initialBuffs: start ? [new(0, "alpha_centauri_passive_p3"), new(0, "alpha_centauri_passive_p4")] : null);
    }

    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(9);
        var result = sim.TryApply(new CastActiveSkillCommand(0, [], sim.PlayerTeam.Characters[0].HandSlots.Where(slot => !slot.IsEmpty).Take(2).Select(slot => slot.SlotIndex).ToArray()));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Mark(CombatSimulation sim, int slot)
    {
        var result = sim.TryApply(new PlayCardCommand(0, slot, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Execute(CombatSimulation sim) { sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase(); }
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();
    private static void ResetCounters(CombatSimulation sim) { foreach (var character in sim.PlayerTeam.Characters) character.PaySkillCounter(character.SkillCounter); }

    #endregion
}