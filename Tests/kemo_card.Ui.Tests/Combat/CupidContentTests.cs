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
public sealed class CupidContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);

    #region 内容与主动技能

    [Test]
    public void Shipped_character_and_cards_pass_validation_and_attribute_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out var error), Is.True, error);
        var character = BaseGameContent.Load().Characters["cupid"];
        Assert.That(character.Element, Is.EqualTo(EElement.Blue));
        Assert.That(character.Role, Is.EqualTo(ERole.Healer));
        Assert.That(character.Race, Is.EqualTo(ERace.God));
        Assert.That(character.ActiveSkillChain.Single().Cooldown, Is.EqualTo(7));
        Assert.That(character.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(character.Cards, Has.Count.EqualTo(4));
        foreach (var id in character.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Blue));
            Assert.That(card.Role, Is.EqualTo(ERole.Healer));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10),
                Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_heals_the_ledger_once_and_grants_temporary_energy_to_every_ally()
    {
        using var sim = NewSimulation();
        sim.PlayerTeam.ApplySharedDamage(600);
        var currentEnergy = sim.PlayerTeam.Characters.Select(character => character.CurrentEnergy).ToArray();
        Cast(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(500), "100 点队伍治疗，不能按人数重复或额外加恢复量");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.AvailableEnergy), Is.EqualTo(new[] { 11, 11, 11, 11 }));
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.CurrentEnergy), Is.EqualTo(currentEnergy));
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        Assert.That(sim.PlayerTeam.Characters[0].HasActed, Is.False);
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.AvailableEnergy), Is.EqualTo(currentEnergy),
            "下个己方阶段重新填充可用能量，临时点数不保留");
    }

    [Test]
    public void Active_at_full_health_still_grants_energy_and_healing_never_exceeds_the_cap()
    {
        using var sim = NewSimulation();
        Cast(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(sim.PlayerTeam.Characters.All(character => character.AvailableEnergy == 11), Is.True);
        sim.PlayerTeam.ApplySharedDamage(25);
        sim.TransitionTo(ECombatPhase.Player);
        Cast(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
    }

    #endregion

    #region 被动技能

    [TestCase(0, 0, 3, 0)]
    [TestCase(0, 3, 0, 0)]
    [TestCase(1, 0, 0, 50)]
    [TestCase(3, 2, 2, 50)]
    public void Passive_two_requires_own_blue_card_and_heals_only_once(int ownBlue, int ownOther, int allyBlue, int healed)
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p2");
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p4");
        sim.PlayerTeam.ApplySharedDamage(600);
        for (var index = 0; index < ownBlue; index++)
            sim.RecordPlayedCard("cupid_golden_blessing", 0);
        for (var index = 0; index < ownOther; index++)
            sim.RecordPlayedCard("perseus_hero_blade", 0);
        for (var index = 0; index < allyBlue; index++)
            sim.RecordPlayedCard("cupid_golden_blessing", 1);
        sim.Buffs.FireCardExecutionEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(400 + healed), "至少 1 张，不按张数累乘，也不额外吃恢复量");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
        sim.TakePlayedThisTurn();
        sim.Buffs.FireCardExecutionEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(400 + healed), "回合出牌账清除后不可沿用上回合条件");
    }

    [Test]
    public void Execution_of_two_blue_cards_triggers_one_fifty_point_heal_at_the_end()
    {
        using var sim = NewSimulation(["cupid_golden_blessing", "cupid_leaden_peace"]);
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p2");
        sim.PlayerTeam.ApplySharedDamage(600);
        Mark(sim, 0);
        Mark(sim, 1);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(400), "标记阶段不执行被动治疗");
        Execute(sim);
        Assert.That(sim.CountCardsPlayedThisTurn(0, (int)EElement.Blue), Is.EqualTo(2));
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(450));
    }

    [Test]
    public void Passive_three_stacks_self_blue_and_god_and_repeats_every_seven_floor_turns()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p3");
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
        ResetCounters(sim);
        sim.Buffs.FireTurnStart(sim);
        for (var turn = 1; turn < 7; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }), "阶层起始不重复触发，未到第 7 回合不触发");
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
        ResetCounters(sim);
        for (var turn = 8; turn <= 14; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
    }

    [Test]
    public void Passive_four_buffs_all_identities_permanently_without_multiplying_on_reapply()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p4");
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p4");
        for (var turn = 0; turn < 8; turn++)
            sim.Buffs.FireTurnEnd(sim);
        for (var index = 0; index < sim.PlayerTeam.Characters.Count; index++)
        {
            var character = sim.PlayerTeam.Characters[index];
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(22));
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(32));
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(18));
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.HealingDealtScale), Is.Zero, "恢复量 +12 是属性加算");
            Assert.That(sim.Buffs.Dispel(sim, Player(index), "cupid_passive_p4"), Is.Zero);
        }
        sim.PlayerTeam.ApplySharedDamage(600);
        Cast(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(500), "主动治疗保持定值 100，不随被动恢复量加算");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
    }

    [Test]
    public void Passive_one_preserves_neighbour_cards_when_storm_fires()
    {
        var definitions = BaseGameContent.Load();
        var buffs = new Dictionary<string, BuffDto>(definitions.Buffs)
        {
            ["test_cupid_storm"] = new()
            {
                Id = "test_cupid_storm", DurationType = EBuffDurationType.Permanent,
                Tags = [BuiltinBuffTags.SlotStorm],
                Hooks = new BuffEffectHooksDto
                {
                    OnSlotCardPlayed = [new EffectRefDto { EffectId = "test_cupid_discard" }],
                },
            },
        };
        var effects = new Dictionary<string, EffectDto>(definitions.Effects)
        {
            ["test_cupid_discard"] = new() { Id = "test_cupid_discard", Kind = EEffectKind.DiscardSlot },
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Buffs = buffs, Effects = effects })], out var report);
        Assert.That(report.HasIssues, Is.False);
        using var sim = NewSimulation(Enumerable.Repeat("cupid_love_nectar", 5).ToArray(), registry);
        var cupid = sim.PlayerTeam.Characters[0];
        sim.Buffs.ApplyToSlot(sim, 0, 2, "test_cupid_storm");
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p1");
        sim.Buffs.FireSlotCardPlayed(sim, 0, cupid.HandSlots[2]);
        Assert.That(cupid.HandSlots.All(slot => !slot.IsEmpty), Is.True);
        cupid.Buffs.Remove(cupid.Buffs.Find("cupid_passive_p1")!);
        sim.Buffs.FireSlotCardPlayed(sim, 0, cupid.HandSlots[2]);
        Assert.That(cupid.HandSlots[1].IsEmpty, Is.True, "对照：没有免疫时暴风仍吹散相邻牌");
    }

    #endregion

    #region 四张专属卡

    [Test]
    public void Nectar_heals_team_with_heal_power_including_the_team_passive()
    {
        using var sim = NewSimulation(["cupid_love_nectar"]);
        sim.Buffs.Apply(sim, Player(), "cupid_passive_p4");
        sim.PlayerTeam.ApplySharedDamage(600);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(458), "40 + 恢复量 6 + 被动 12");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
    }

    [Test]
    public void Golden_blessing_grants_all_allies_temporary_energy_and_charges_only_self()
    {
        using var sim = NewSimulation(["cupid_golden_blessing"]);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.AvailableEnergy), Is.EqualTo(new[] { 9, 11, 11, 11 }));
        Assert.That(sim.PlayerTeam.Characters.All(character => character.CurrentEnergy == 10), Is.True);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 1, 0, 0, 0 }));
    }

    [Test]
    public void Leaden_peace_weakens_every_enemy_for_two_turns_only()
    {
        using var sim = NewSimulation(["cupid_leaden_peace"]);
        Mark(sim, 0);
        Execute(sim);
        foreach (var enemy in sim.EnemyTeam.Enemies)
        {
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(72));
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(72));
        }
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(72));
        sim.Buffs.FireTurnEnd(sim);
        foreach (var enemy in sim.EnemyTeam.Enemies)
        {
            Assert.That(enemy.Buffs.Find("cupid_peace"), Is.Null);
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(80));
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(80));
        }
    }

    [Test]
    public void United_guard_shields_each_ally_and_heals_the_ledger_once()
    {
        using var sim = NewSimulation(["cupid_united_guard"]);
        sim.PlayerTeam.ApplySharedDamage(600);
        Mark(sim, 0);
        Execute(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(436));
        Assert.That(sim.PlayerTeam.Characters.Select(character => character.Asc.GetCurrentValue(AttributeIds.Shield)),
            Is.EqualTo(new[] { 40f, 40f, 40f, 40f }));
        Assert.That(sim.PlayerTeam.Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero, "护盾挂到角色，治疗挂到队伍账本");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.Zero);
    }

    #endregion

    #region 场景构造

    private static CombatSimulation NewSimulation(string[]? cards = null, GameDefinitionRegistry? registry = null)
    {
        var identities = new[]
        {
            (EElement.Blue, ERace.God), (EElement.Blue, ERace.Human),
            (EElement.Red, ERace.God), (EElement.Green, ERace.Animal),
        };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "cupid" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.PhysicalAttack] = 10,
                [AttributeIds.MagicAttack] = 20, [AttributeIds.HealPower] = 6,
                [AttributeIds.NormalAttackDamageDealtScale] = -1,
                [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 ? cards?.Select((id, number) => new CardRuntimeEntry(id, $"card-{number}")) : null,
            skillCounterCap: 20,
            activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["cupid"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        var enemies = Enumerable.Range(0, 2).Select(index => new EnemyUnit($"e{index}", "slime",
            new Dictionary<string, float> { [AttributeIds.MaxHealth] = 500, [AttributeIds.PhysicalAttack] = 80, [AttributeIds.MagicAttack] = 80 })).ToArray();
        return new CombatSimulation(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies),
            new CombatRuleEngine([]), registry ?? BaseGameContent.BuildRegistry(), initialPhase: ECombatPhase.Player, runSeed: 20261008);
    }

    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(7);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Mark(CombatSimulation sim, int slot)
    {
        var result = sim.TryApply(new PlayCardCommand(0, slot, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Execute(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();

    private static void ResetCounters(CombatSimulation sim)
    {
        foreach (var character in sim.PlayerTeam.Characters)
            character.PaySkillCounter(character.SkillCounter);
    }

    #endregion
}