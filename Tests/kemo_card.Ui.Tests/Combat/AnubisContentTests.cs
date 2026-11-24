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
public sealed class AnubisContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);
    private static CombatTargetRef Enemy(int index = 0) => new(ECombatSide.Enemy, index);

    #region 主动技能与内容

    [Test]
    public void Character_and_cards_pass_content_validation_and_attribute_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        var definition = BaseGameContent.Load().Characters["anubis"];
        Assert.That(definition.Element, Is.EqualTo(EElement.Red));
        Assert.That(definition.Role, Is.EqualTo(ERole.Attack));
        Assert.That(definition.Race, Is.EqualTo(ERace.God | ERace.Animal));
        Assert.That(definition.ActiveSkillChain.Single().Cooldown, Is.EqualTo(8));
        Assert.That(definition.Cards, Has.Count.EqualTo(4));
        foreach (var id in definition.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Red));
            Assert.That(card.Role, Is.EqualTo(ERole.Attack));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10), Is.EqualTo(40));
        }
    }

    #endregion

    #region 卡牌增伤边界

    [TestCase("anubis_physical_damage")]
    [TestCase("anubis_magic_damage")]
    public void Passive_two_amplifies_card_ge_damage_but_not_outside_card_context_or_other_sources(string geId)
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "anubis_passive_p2");
        var applicator = new GameplayEffectApplicator(sim.Definitions);
        var parameters = new Dictionary<string, object> { ["Amount"] = 20, ["AttackScale"] = 0, ["CardDamageBonus"] = 99 };
        applicator.ApplyToTargets(sim, Player(), [Enemy()], geId, parameters);
        Assert.That(EnemyHp(sim), Is.EqualTo(980), "区间外不能偷吃内容传入的卡牌增伤");
        using (sim.EnterCardContext(ECardType.Support, 0, (int)EElement.Red, 0))
        {
            applicator.ApplyToTargets(sim, Player(), [Enemy()], geId, parameters);
            Assert.That(EnemyHp(sim), Is.EqualTo(950), "卡牌增伤对物理、魔法都生效，包括支援卡的伤害载荷");
            applicator.ApplyToTargets(sim, Player(1), [Enemy()], geId, parameters);
            Assert.That(EnemyHp(sim), Is.EqualTo(930), "当前卡牌来源以外的角色不能借用增伤");
        }
        applicator.ApplyToTargets(sim, Player(), [Enemy()], geId, parameters);
        Assert.That(EnemyHp(sim), Is.EqualTo(910), "离开卡牌区间立即失效");
    }

    [Test]
    public void Direct_card_damage_uses_the_same_bonus_bucket_and_context_boundary()
    {
        var definitions = BaseGameContent.Load();
        var effects = new Dictionary<string, EffectDto>(definitions.Effects)
        {
            ["test_anubis_direct"] = new() { Id = "test_anubis_direct", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 20 } },
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Effects = effects })], out var report);
        Assert.That(report.HasIssues, Is.False);
        using var sim = NewSimulation(registry: registry);
        sim.Buffs.Apply(sim, Player(), "anubis_passive_p2");
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 0.2f);
        sim.EnemyTeam.Enemies[0].Asc.SetBaseValue(AttributeIds.DamageTakenScale, 0.3f);
        using (sim.EnterCardContext(ECardType.Physics, 0, (int)EElement.Red, 0))
            sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = "test_anubis_direct" }, sim, Player(), [Enemy()]);
        Assert.That(EnemyHp(sim), Is.EqualTo(960).Within(0.001), "20 × (1 + 0.5 + 0.2 + 0.3)，不是分项乘算");
        sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = "test_anubis_direct" }, sim, Player(), [Enemy()]);
        Assert.That(EnemyHp(sim), Is.EqualTo(930).Within(0.001));
    }

    [Test]
    public void Card_bonus_is_additive_with_general_bonus_taken_bonus_and_the_fourth_passive()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "anubis_passive_p2");
        sim.Buffs.Apply(sim, Player(), "anubis_passive_p4");
        sim.Buffs.Apply(sim, Player(), "anubis_frenzy");
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 0.2f);
        sim.EnemyTeam.Enemies[0].Asc.SetBaseValue(AttributeIds.DamageTakenScale, 0.3f);
        using (sim.EnterCardContext(ECardType.Magical, 0, (int)EElement.Red, 0))
            sim.EffectExecutor.ExecuteSkillActionRef(new SkillActionRefDto
            {
                ActionId = "anubis_judgment_hit", Params = new() { ["Amount"] = 20, ["AttackScale"] = 0 },
            }, sim, Player(), [Enemy()]);
        Assert.That(EnemyHp(sim), Is.EqualTo(950).Within(0.001), "20 × (1 + 0.5 + 0.5 + 0.2 + 0.3)");
    }

    [Test]
    public void Passive_two_does_not_change_normal_attack_or_orb_damage()
    {
        using var control = NewSimulation();
        using var withPassive = NewSimulation();
        withPassive.Buffs.Apply(withPassive, Player(), "anubis_passive_p2");
        foreach (var sim in new[] { control, withPassive })
        {
            sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
            sim.NormalAttacks.Execute(sim);
            Assert.That(sim.Orbs.Grant(sim, BuiltinOrbTypes.Red, 0), Is.True);
            sim.Orbs.TriggerManual(sim);
        }
        Assert.That(EnemyHp(control), Is.LessThan(1000), "对照必须实际造成伤害");
        Assert.That(EnemyHp(withPassive), Is.EqualTo(EnemyHp(control)));
    }

    #endregion

    #region 被动与连奏时机

    [Test]
    public void Floor_charge_stacks_self_red_god_and_animal_and_only_repeats_every_eight_turns()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "anubis_passive_p3");
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
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
        Assert.That(Counters(sim), Is.EqualTo(new[] { 5, 1, 1, 1 }));
    }

    #endregion

    #region 专属卡与场景构造

    [TestCase("anubis_underworld_fang", 36)]
    [TestCase("anubis_soul_guidance", 46)]
    public void Single_target_cards_hit_only_the_selected_enemy(string cardId, int damage)
    {
        using var sim = NewSimulation([cardId]);
        Mark(sim, 0, Enemy(1));
        Execute(sim);
        Assert.That(EnemyHp(sim), Is.EqualTo(1000));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(1000 - damage));
    }

    [Test]
    public void Judgment_hits_all_enemies_and_rite_is_free_with_charge_and_next_draw()
    {
        using var sim = NewSimulation(["anubis_scale_judgment", "anubis_tomb_rite"]);
        Mark(sim, 0);
        Mark(sim, 1, Player());
        Execute(sim);
        Assert.That(EnemyHp(sim), Is.EqualTo(958));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(958));
        Assert.That(sim.PlayerTeam.Characters[0].AvailableEnergy, Is.EqualTo(8));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 1, 0, 0, 0 }));
        Assert.That(sim.PlayerTeam.Characters[0].ComputeDrawCount(), Is.EqualTo(2));
    }

    private static CombatSimulation NewSimulation(string[]? cards = null, int reserves = 0, GameDefinitionRegistry? registry = null)
    {
        var identities = new[]
        {
            (EElement.Red, ERace.God | ERace.Animal), (EElement.Red, ERace.Human),
            (EElement.Blue, ERace.God), (EElement.Green, ERace.Animal),
        };
        var reserveEntries = Enumerable.Range(0, reserves).Select(index => new CardRuntimeEntry("anubis_underworld_fang", $"reserve-{index}"));
        var handEntries = (cards ?? []).Select((id, index) => new CardRuntimeEntry(id, $"hand-{index}")).Reverse();
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "anubis" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 100, [AttributeIds.PhysicalAttack] = 40, [AttributeIds.MagicAttack] = 40,
                [AttributeIds.NormalAttackDamageDealtScale] = -1, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 ? reserveEntries.Concat(handEntries) : null,
            skillCounterCap: 20,
            activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["anubis"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        var enemies = Enumerable.Range(0, 2).Select(index => new EnemyUnit($"e{index}", "slime", 1000)).ToArray();
        return new CombatSimulation(new PlayerTeamState(characters, 400), new EnemyTeamState(enemies),
            new CombatRuleEngine([]), registry ?? BaseGameContent.BuildRegistry(), initialPhase: ECombatPhase.Player, runSeed: 20261008);
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

    private static void Execute(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static float EnemyHp(CombatSimulation sim, int index = 0) => sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Health);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();

    private static void ResetCounters(CombatSimulation sim)
    {
        foreach (var character in sim.PlayerTeam.Characters)
            character.PaySkillCounter(character.SkillCounter);
    }

    #endregion
}