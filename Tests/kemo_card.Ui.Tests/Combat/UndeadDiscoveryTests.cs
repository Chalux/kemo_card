using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class UndeadDiscoveryTests
{
    private static readonly CombatTargetRef Hero = new(ECombatSide.Player, 0);

    #region 出货内容与机制伤害

    [Test]
    public void Shipped_enemies_have_requested_stats_and_battle_passes_validation()
    {
        var registry = BaseGameContent.BuildRegistry();
        Assert.That(new ContentDefinitionValidator().Validate(registry.Store), Is.Empty);
        foreach (var (id, attack, bars) in new[] {
            ("undead_ghost", 20, new[] { 800 }), ("evil_thought", 40, new[] { 1800, 2400, 2800 }) })
        {
            var enemy = registry.Store.Enemies[id];
            Assert.That(enemy.Element, Is.EqualTo(EElement.Blue));
            Assert.That(enemy.Race, Is.EqualTo(ERace.Undead | ERace.Unknown));
            Assert.That(enemy.BaseAttributes[AttributeIds.MagicAttack], Is.EqualTo(attack));
            Assert.That(enemy.BaseAttributes[AttributeIds.PhysicalAttack], Is.Zero);
            Assert.That(enemy.BaseAttributes[AttributeIds.PhysicalDefense], Is.Zero);
            Assert.That(enemy.BaseAttributes[AttributeIds.MagicDefense], Is.Zero);
            Assert.That(enemy.LifeBars.Select(bar => bar.MaxHp), Is.EqualTo(bars));
        }
    }

    [Test]
    public void Wave_mechanisms_fire_once_bypass_rules_shields_and_modifiers_and_are_not_enemy_actions()
    {
        var probe = new BlockDamageRule();
        using var sim = Start(runStart: false, rule: probe);
        sim.Definitions.Store.BuffsMutable["test.mechanism-immunity"] = new()
        {
            Id = "test.mechanism-immunity", DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.TraitImmuneDamage]
        };
        sim.PlayerTeam.Asc.SetBaseValue(AttributeIds.DamageTakenScale, -1);
        foreach (var character in sim.PlayerTeam.Characters)
        {
            character.Asc.SetBaseValue(AttributeIds.Shield, 99999);
            character.Asc.SetBaseValue(AttributeIds.MagicDefense, 99999);
            character.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 5);
            sim.Buffs.Apply(sim, new(ECombatSide.Player, sim.PlayerTeam.Characters.ToList().IndexOf(character)), "test.mechanism-immunity");
        }
        sim.RunBattleStart();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp * 0.75));
        var events = sim.Presentation.Drain();
        Assert.That(events.OfType<BattleMechanismDamageEvent>().Count(), Is.EqualTo(1));
        Assert.That(events.OfType<EnemyActionStartedEvent>(), Is.Empty);
        Assert.That(events.OfType<DamageDealtEvent>(), Is.Empty);
        sim.Mechanisms.WaveEntry();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp * 0.75));
        sim.PlayerTeam.HealShared(sim.PlayerTeam.MaxHp);
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(100000);
        sim.CheckEndConditions();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp * 0.2));
        Assert.That(probe.Before, Is.Zero);
        Assert.That(probe.After, Is.Zero);
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Asc.GetCurrentValue(AttributeIds.Shield) == 99999), Is.True);
        var hp = sim.PlayerTeam.SharedHp;
        Execute(sim, 1, "evil_thought_entry_2");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp), "召唤幽灵不重复触发波次机制伤害");
    }

    [Test]
    public void Wave_two_mechanism_can_defeat_the_party_before_turn_start_healing()
    {
        using var sim = Start();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp * 0.75));
        Hit(sim, 0);
        Hit(sim, 1);
        sim.CheckEndConditions();
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.SharedHp, Is.Zero);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
        Assert.That(sim.TurnNumber, Is.EqualTo(1));
    }

    #endregion

    #region 幽灵循环、封印与减攻

    [Test]
    public void Ghosts_start_offset_in_wave_one_and_together_in_wave_two()
    {
        using var sim = Start();
        var hp = sim.PlayerTeam.SharedHp;
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "ghost_strike", "ghost_seal_twice" }));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 50));
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "ghost_seal_twice", "ghost_strike" }));
        EnterSecondWave(sim);
        Assert.That(Turn(sim).Select(evt => evt.SkillId), Is.EqualTo(new[] { "ghost_strike", "evil_thought_double", "ghost_strike" }));
    }

    [Test]
    public void Malice_attack_down_survives_application_turn_end_and_expires_after_one_full_turn()
    {
        using var sim = Start();
        EnterSecondWave(sim);
        Turn(sim);
        Turn(sim);
        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(-10));
            Assert.That(character.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(-10));
            Assert.That(character.Buffs.Find("evil_thought_attack_down")!.RemainingTurns, Is.EqualTo(1));
        }
        var hp = sim.PlayerTeam.SharedHp;
        Turn(sim);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hp - 50 * (2 + CombatConstants.SlotCount))); // 两只幽灵各 50，恶念攻击全队。
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Buffs.Find("evil_thought_attack_down") is null), Is.True);
    }

    #endregion

    #region 召唤、生命条与终局

    [Test]
    public void Summoning_pauses_cycle_and_new_ghost_waits_until_next_enemy_phase()
    {
        using var sim = Start();
        EnterSecondWave(sim);
        Turn(sim);
        Hit(sim, 0);
        var events = Turn(sim);
        var summonedIndex = sim.EnemyTeam.Enemies.Count - 1;
        Assert.That(events.Any(evt => evt.SkillId == "evil_thought_summon_1"), Is.True);
        Assert.That(events.Any(evt => evt.EnemyIndex == summonedIndex), Is.False);
        Assert.That(sim.EnemyTeam.Enemies[1].ActionSequenceIndex, Is.EqualTo(1));
        Assert.That(sim.EnemyTeam.Enemies[summonedIndex].MaxHp, Is.EqualTo(800));
        Assert.That(sim.EnemyTeam.Enemies.Select(enemy => enemy.RuntimeId).Distinct().Count(), Is.EqualTo(sim.EnemyTeam.Enemies.Count));
        events = Turn(sim);
        Assert.That(events.Single(evt => evt.EnemyIndex == 1).SkillId, Is.EqualTo("evil_thought_weaken"));
        Assert.That(events.Single(evt => evt.EnemyIndex == summonedIndex).SkillId, Is.EqualTo("ghost_strike"));
    }

    [TestCase(0, 2, 4, 1)]
    [TestCase(1, 3, 5, 2)]
    [TestCase(2, 4, 8, 2)]
    public void Life_bar_entry_fills_population_without_using_the_independent_strict_summon_budget(int bar, int maxAlive, int cap, int perAction)
    {
        using var sim = Start();
        EnterSecondWave(sim);
        for (var i = 0; i < bar; i++)
            Hit(sim, 1);
        var boss = sim.EnemyTeam.Enemies[1];
        Assert.That(Ghosts(sim).Count(), Is.EqualTo(maxAlive));
        Assert.That(boss.SummonsThisLifeBar("undead_ghost"), Is.Zero);
        while (boss.SummonsThisLifeBar("undead_ghost") < cap)
        {
            foreach (var (_, index) in Ghosts(sim).ToArray())
                Hit(sim, index);
            var before = boss.SummonsThisLifeBar("undead_ghost");
            Execute(sim, 1, $"evil_thought_summon_{bar + 1}");
            Assert.That(Ghosts(sim).Count(), Is.EqualTo(Math.Min(perAction, cap - before)));
        }
        foreach (var (_, index) in Ghosts(sim).ToArray())
            Hit(sim, index);
        Execute(sim, 1, $"evil_thought_summon_{bar + 1}");
        Assert.That(Ghosts(sim), Is.Empty);
        Assert.That(boss.SummonsThisLifeBar("undead_ghost"), Is.EqualTo(cap));
        Assert.That(Turn(sim).Single().SkillId, Is.EqualTo("evil_thought_double"));
    }

    [Test]
    public void Life_bar_entry_summons_in_enemy_phase_wait_for_following_phase_and_previous_budget_resets()
    {
        using var sim = Start();
        EnterSecondWave(sim);
        Hit(sim, 0);
        Turn(sim);
        Assert.That(sim.EnemyTeam.Enemies[1].SummonsThisLifeBar("undead_ghost"), Is.EqualTo(1));
        sim.TransitionTo(ECombatPhase.CardExecution);
        Hit(sim, 1);
        var count = sim.EnemyTeam.Enemies.Count;
        var actions = Turn(sim);
        var boss = sim.EnemyTeam.Enemies[1];
        Assert.That(boss.LifeBarIndex, Is.EqualTo(1));
        Assert.That(boss.SummonsThisLifeBar("undead_ghost"), Is.Zero);
        Assert.That(Ghosts(sim).Count(), Is.EqualTo(3));
        Assert.That(actions.Any(evt => evt.EnemyIndex >= count), Is.False);
        Assert.That(Turn(sim).Any(evt => evt.EnemyIndex >= count), Is.True);
    }

    [TestCase(ECombatPhase.Player)]
    [TestCase(ECombatPhase.CardExecution)]
    public void Third_bar_deadline_fires_at_start_of_turn_ten_and_not_turn_nine(ECombatPhase breakPhase)
    {
        using var sim = Start();
        EnterSecondWave(sim);
        Hit(sim, 1);
        sim.TransitionTo(breakPhase);
        Hit(sim, 1);
        if (breakPhase == ECombatPhase.CardExecution)
            Turn(sim);
        var boss = sim.EnemyTeam.Enemies[1];
        Assert.That(boss.LifeBarIndex, Is.EqualTo(2));
        while (sim.TurnNumber - boss.LifeBarEnteredTurn + 1 < 9)
            sim.BeginNextTurn(incrementTurnsIntoWave: true);
        Assert.That(sim.PlayerTeam.SharedHp, Is.GreaterThan(0));
        sim.Presentation.Clear();
        sim.BeginNextTurn(incrementTurnsIntoWave: true);
        Assert.That(sim.TurnNumber - boss.LifeBarEnteredTurn + 1, Is.EqualTo(10));
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
        Assert.That(sim.PlayerTeam.SharedHp, Is.Zero);
        Assert.That(sim.Presentation.Drain().OfType<BattleMechanismDamageEvent>().Single().MechanismId, Is.EqualTo("evil_thought_deadline"));
    }

    [Test]
    public void Defeating_malice_does_not_end_the_battle_while_summoned_ghosts_remain()
    {
        using var sim = Start();
        EnterSecondWave(sim);
        Hit(sim, 1);
        Hit(sim, 1);
        Hit(sim, 1);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
        foreach (var (_, index) in Ghosts(sim).ToArray())
            Hit(sim, index);
        sim.CheckEndConditions();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
    }

    #endregion

    [Test]
    public void Invalid_mechanism_turn_summon_reference_and_summon_override_are_rejected()
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.BattlesMutable["bad-mechanism"] = new()
        {
            Id = "bad-mechanism", Waves = [new() {
                EnemySpawns = [new() { EnemyId = "undead_ghost" }],
                EntryMechanisms = [new() { Id = "invalid-entry", MaxHpDamagePercent = 25, Turn = 1 }]
            }]
        };
        registry.Store.SkillActionsMutable["bad-summon"] = new()
        {
            Id = "bad-summon", Kind = ESkillActionKind.SummonEnemy,
            Params = new() { ["enemyId"] = "missing-ghost", ["count"] = 1, ["maxAlive"] = 2 }
        };
        registry.Store.SkillsMutable["bad-summon-override"] = new()
        {
            Id = "bad-summon-override", ActionRefs = [new() { ActionId = "ghost_summon", Params = new() { ["maxAlive"] = 0 } }]
        };
        var errors = new ContentDefinitionValidator().Validate(registry.Store);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-mechanism"), Is.True);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-summon"), Is.True);
        Assert.That(errors.Any(error => error.DefinitionId == "bad-summon-override"), Is.True);
    }

    private static IEnumerable<(EnemyUnit Enemy, int Index)> Ghosts(CombatSimulation sim) =>
        sim.EnemyTeam.Enemies.Select((enemy, index) => (enemy, index))
            .Where(pair => pair.enemy.IsAlive && pair.enemy.DefinitionId == "undead_ghost");

    private static void Hit(CombatSimulation sim, int enemyIndex) =>
        DamagePipeline.Settle(sim, Hero, new(ECombatSide.Enemy, enemyIndex), 100000);

    private static void EnterSecondWave(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.Player);
        sim.PlayerTeam.HealShared(sim.PlayerTeam.MaxHp);
        Hit(sim, 0);
        Hit(sim, 1);
        sim.CheckEndConditions();
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
    }

    private static CombatPresentationEvent[] Execute(CombatSimulation sim, int sourceIndex, string skillId)
    {
        sim.Presentation.Clear();
        var skill = sim.Definitions.Store.Skills[skillId];
        SkillPayloadExecutor.Execute(sim, skill, new(ECombatSide.Enemy, sourceIndex),
            CombatTargetResolver.ResolveEnemySkillTargets(sim, skill, sourceIndex));
        return sim.Presentation.Drain().ToArray();
    }

    private static EnemyActionStartedEvent[] Turn(CombatSimulation sim)
    {
        sim.Presentation.Clear();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        return sim.Presentation.Drain().OfType<EnemyActionStartedEvent>().ToArray();
    }

    private static CombatSimulation Start(bool runStart = true, ICombatRule? rule = null)
    {
        var registry = BaseGameContent.BuildRegistry();
        registry.Store.CardsMutable["test.undead.hp"] = new()
        {
            Id = "test.undead.hp", Stats = new() { HpCap = 10000 }, CostType = ECostType.Energy, Cost = 1,
            TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.Single, TargetCount = 1
        };
        var catalog = CombatRuleCatalog.CreateDefault();
        var battle = registry.Store.Battles["undead_discovery"];
        if (rule is not null)
        {
            catalog.Register(rule.Id, () => rule);
            battle = new BattleDto { Id = battle.Id, Waves = battle.Waves, CombatRuleIds = [rule.Id] };
        }
        var party = Enumerable.Range(0, KemoCard.Mod.Combat.CombatConstants.SlotCount).Select(index => new CharacterInstance(
            new CharacterDto { Id = $"hero-{index}", Cards = ["test.undead.hp"], InitialEnergy = 6, MaxEnergy = 10 }, $"inst-{index}")).ToArray();
        var sim = CombatSimulationFactory.TryCreate(battle, party, registry, new HostRng(42, "combat"), 42,
            null, null, "base.game", catalog, out var error);
        Assert.That(sim, Is.Not.Null, error);
        if (runStart)
            sim!.RunBattleStart();
        return sim!;
    }

    private sealed class BlockDamageRule : ICombatRule
    {
        public string Id => "test.block-damage";
        public int Priority => 1;
        public int Before { get; private set; }
        public int After { get; private set; }
        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) { Before++; packet.Amount = 0; }
        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet) => After++;
    }
}