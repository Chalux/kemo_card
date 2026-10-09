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
public sealed class PerseusContentTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);
    private static CombatTargetRef Enemy(int index = 0) => new(ECombatSide.Enemy, index);

    #region 定义与主动技

    [Test]
    public void Shipped_character_and_four_cards_pass_content_validation_and_attribute_budget()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        var character = BaseGameContent.Load().Characters["perseus"];
        Assert.That(character.Element, Is.EqualTo(EElement.Green));
        Assert.That(character.Role, Is.EqualTo(ERole.Controller));
        Assert.That(character.Race, Is.EqualTo(ERace.Human | ERace.God));
        Assert.That(character.ActiveSkillChain.Single().Cooldown, Is.EqualTo(10));
        Assert.That(character.Passives.Select(passive => passive.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(character.Cards, Has.Count.EqualTo(4));
        foreach (var id in character.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Role, Is.EqualTo(ERole.Controller));
            Assert.That(card.Element, Is.EqualTo((int)EElement.Green));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.Stats!.Attributes.Sum(pair => pair.Key == AttributeIds.MaxHealth ? pair.Value : pair.Value * 10),
                Is.EqualTo(40));
        }
    }

    [Test]
    public void Active_debuffs_all_enemies_for_three_turns_and_charges_each_matching_ally_once()
    {
        using var sim = NewSimulation();
        Cast(sim);
        AssertAttacks(sim, 65);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 1, 1, 1 }), "绿、单人类、单神族均可；自身排除");
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        AssertAttacks(sim, 65);
        sim.Buffs.FireTurnEnd(sim);
        AssertAttacks(sim, 80);
    }

    [Test]
    public void Active_charge_does_not_stack_multiple_matching_identities_or_reward_nonmatching_allies()
    {
        using var sim = NewSimulation(identities:
        [
            (EElement.Green, ERace.Human | ERace.God), (EElement.Green, ERace.Human | ERace.God),
            (EElement.Red, ERace.Animal), (EElement.Blue, ERace.Dragon),
        ]);
        Cast(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 1, 0, 0 }));
    }

    [Test]
    public void Active_without_matching_allies_does_not_fall_back_to_self()
    {
        using var sim = NewSimulation(identities:
        [
            (EElement.Green, ERace.Human | ERace.God), (EElement.Red, ERace.Animal),
            (EElement.Red, ERace.Animal), (EElement.Red, ERace.Animal),
        ]);
        Cast(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
    }

    #endregion

    #region 被动

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    public void Pressure_counts_only_own_played_cards_and_expires_at_turn_end(int ownCards)
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p2");
        for (var index = 0; index < ownCards; index++)
            sim.RecordPlayedCard("perseus_hero_blade", 0);
        for (var index = 0; index < 4; index++)
            sim.RecordPlayedCard("perseus_serpent_slayer", 1);
        sim.Buffs.FireCardExecutionEnd(sim);
        AssertAttacks(sim, 80 - 6 * ownCards);
        if (ownCards == 0)
            Assert.That(sim.EnemyTeam.Enemies[0].Buffs.Find("perseus_battle_pressure"), Is.Null);
        sim.Buffs.FireTurnEnd(sim);
        AssertAttacks(sim, 80);
    }

    [Test]
    public void Pressure_takes_a_snapshot_and_reapplication_updates_the_amount()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p2");
        sim.RecordPlayedCard("perseus_hero_blade", 0);
        sim.Buffs.FireCardExecutionEnd(sim);
        sim.RecordPlayedCard("perseus_hero_blade", 0);
        AssertAttacks(sim, 74, "已投放实例不能随出牌账变化");
        sim.Buffs.FireCardExecutionEnd(sim);
        AssertAttacks(sim, 68, "新快照替换旧快照，不能叠加为 -18");
    }

    [Test]
    public void Floor_charge_stacks_the_three_identities_but_only_repeats_every_ten_turns()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p3");
        sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 7, 2, 2, 2 }));
        foreach (var character in sim.PlayerTeam.Characters)
            character.PaySkillCounter(character.SkillCounter);
        sim.Buffs.FireTurnStart(sim);
        for (var turn = 1; turn <= 9; turn++)
        {
            sim.IncrementTurnsIntoWave();
            sim.Buffs.FireTurnStart(sim);
        }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 7, 2, 2, 2 }));
    }

    [Test]
    public void Passive_four_replaces_active_debuff_and_draws_only_at_the_next_draw_step()
    {
        using var sim = NewSimulation(drawPileSize: 12);
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p4");
        Cast(sim);
        AssertAttacks(sim, 50, "强化值是 -30，而不是 -15 加 -30");
        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.Find("perseus_valor_debuff"), Is.Null);
        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.Find("perseus_valor_empowered")!.RemainingTurns, Is.EqualTo(3));
        var perseus = sim.PlayerTeam.Characters[0];
        Assert.That(perseus.ComputeDrawCount(), Is.EqualTo(6));
        Assert.That(perseus.HandSlots.Count(slot => !slot.IsEmpty), Is.Zero, "释放后不立即抽牌");
        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(perseus.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(5), "抽牌遵守手牌槽上限");
        Assert.That(perseus.ComputeDrawCount(), Is.EqualTo(1), "一次抽牌步骤后清除 +5");
    }

    [Test]
    public void Empowered_active_and_one_turn_pressure_have_independent_durations()
    {
        using var sim = NewSimulation();
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p2");
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p4");
        Cast(sim);
        sim.RecordPlayedCard("perseus_hero_blade", 0);
        sim.RecordPlayedCard("perseus_serpent_slayer", 0);
        sim.Buffs.FireCardExecutionEnd(sim);
        AssertAttacks(sim, 38);
        sim.Buffs.FireTurnEnd(sim);
        AssertAttacks(sim, 50, "仅出牌压制到期；强化主动仍生效");
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        AssertAttacks(sim, 80);
    }

    [Test]
    public void Virus_immunity_blocks_buff_payload_and_instant_ge_damage_before_application()
    {
        var definitions = BaseGameContent.Load();
        var buffs = new Dictionary<string, BuffDto>(definitions.Buffs)
        {
            ["test_virus"] = new()
            {
                Id = "test_virus", Tags = [CombatConstants.VirusTag],
                DurationType = EBuffDurationType.Turns, Duration = 2,
                Hooks = new BuffEffectHooksDto
                {
                    OnApply = [new EffectRefDto { EffectId = "gain_skill_counter", Params = new() { ["amount"] = 3 } }],
                },
            },
        };
        var effects = new Dictionary<string, GameplayEffectDefDto>(definitions.GameplayEffects)
        {
            ["test_virus_damage"] = new()
            {
                Id = "test_virus_damage", DurationPolicy = EDurationPolicy.Instant,
                GrantedTags = [CombatConstants.VirusTag],
                Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Physical", AttackScale = 0 }],
            },
        };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", definitions with { Buffs = buffs, GameplayEffects = effects })],
            out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.RemovedValidationErrors.Select(error => error.Message)));
        using var sim = NewSimulation(registry: registry);
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p1");
        Assert.That(sim.Buffs.Apply(sim, Player(), "test_virus").Success, Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("test_virus"), Is.Null);
        Assert.That(Counters(sim)[0], Is.Zero, "病毒 onApply 不得执行");
        sim.Buffs.ApplyToSlot(sim, 0, 0, "test_virus");
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].Buffs.Find("test_virus"), Is.Null);
        Assert.That(Counters(sim)[0], Is.Zero, "病毒槽位载荷也不得执行");
        sim.Buffs.Apply(sim, Player(1), "test_virus");
        Assert.That(Counters(sim)[1], Is.EqualTo(3), "对照角色仍受病毒影响");
        var applicator = new GameplayEffectApplicator(registry);
        applicator.ApplyToTargets(sim, Enemy(), [Player()], "test_virus_damage", new Dictionary<string, object> { ["Amount"] = 30 });
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(100));
        applicator.ApplyToTargets(sim, Enemy(), [Player(1)], "test_virus_damage", new Dictionary<string, object> { ["Amount"] = 30 });
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(70));
    }

    #endregion

    #region 专属卡

    [Test]
    public void Hero_blade_hits_only_the_selected_enemy()
    {
        using var sim = NewSimulation("perseus_hero_blade");
        Play(sim, Enemy(1));
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(500));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(464));
    }

    [Test]
    public void Mirror_gaze_delays_and_weakens_only_the_selected_enemy()
    {
        using var sim = NewSimulation("perseus_mirror_gaze");
        Play(sim, Enemy(1));
        Assert.That(sim.EnemyTeam.Enemies[0].ActionCount, Is.EqualTo(1));
        Assert.That(sim.EnemyTeam.Enemies[1].ActionCount, Is.EqualTo(2));
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.Zero);
        Assert.That(sim.EnemyTeam.Enemies[1].Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.EqualTo(-8));
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.EnemyTeam.Enemies[1].Buffs.Find("perseus_mirror_weakness"), Is.Null);
    }

    [Test]
    public void Winged_hunt_charges_and_schedules_one_extra_draw()
    {
        using var sim = NewSimulation("perseus_winged_hunt");
        Play(sim, Player());
        Assert.That(Counters(sim), Is.EqualTo(new[] { 2, 0, 0, 0 }));
        Assert.That(sim.PlayerTeam.Characters[0].ComputeDrawCount(), Is.EqualTo(2));
    }

    [Test]
    public void Two_hit_serpent_card_counts_as_one_played_card_for_pressure()
    {
        using var sim = NewSimulation("perseus_serpent_slayer");
        sim.Buffs.Apply(sim, Player(), "perseus_passive_p2");
        Play(sim, Enemy(1));
        Assert.That(EnemyHp(sim, 0), Is.EqualTo(500));
        Assert.That(EnemyHp(sim, 1), Is.EqualTo(396));
        AssertAttacks(sim, 74, "两段攻击仍只是一张出牌");
    }

    #endregion

    #region 场景构造

    private static CombatSimulation NewSimulation(string? cardId = null,
        (EElement Element, ERace Race)[]? identities = null, int drawPileSize = 0, GameDefinitionRegistry? registry = null)
    {
        identities ??=
        [
            (EElement.Green, ERace.Human | ERace.God), (EElement.Green, ERace.Animal),
            (EElement.Red, ERace.Human), (EElement.Blue, ERace.God),
        ];
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(
            index == 0 ? "perseus" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 25, [AttributeIds.PhysicalAttack] = 40,
                [AttributeIds.NormalAttackDamageDealtScale] = -1,
                [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
            },
            drawPile: index == 0 ? Enumerable.Range(0, cardId is not null ? 1 : drawPileSize)
                .Select(number => new CardRuntimeEntry(cardId ?? "perseus_hero_blade", $"card-{number}")) : null,
            skillCounterCap: 20,
            activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["perseus"].ActiveSkillChain : null,
            element: identity.Element, race: identity.Race)).ToArray();
        if (cardId is not null)
            characters[0].DrawCards(1);
        foreach (var character in characters)
            character.RefillAvailableEnergy();
        var enemies = Enumerable.Range(0, 2).Select(index => new EnemyUnit($"e{index}", "slime",
            new Dictionary<string, float> { [AttributeIds.MaxHealth] = 500, [AttributeIds.PhysicalAttack] = 80, [AttributeIds.MagicAttack] = 80 })).ToArray();
        return new CombatSimulation(new PlayerTeamState(characters, 100), new EnemyTeamState(enemies),
            new CombatRuleEngine([]), registry ?? BaseGameContent.BuildRegistry(), initialPhase: ECombatPhase.Player, runSeed: 20261008);
    }

    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(10);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void Play(CombatSimulation sim, CombatTargetRef target)
    {
        var result = sim.TryApply(new PlayCardCommand(0, 0, [target]));
        Assert.That(result.Success, Is.True, result.Error);
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static void AssertAttacks(CombatSimulation sim, int expected, string? message = null)
    {
        foreach (var enemy in sim.EnemyTeam.Enemies)
        {
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(expected), message);
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(expected), message);
        }
    }

    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(character => character.SkillCounter).ToArray();
    private static float EnemyHp(CombatSimulation sim, int index) => sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Health);

    #endregion
}