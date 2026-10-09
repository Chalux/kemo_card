using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class HualingContentTests
{
    private static CombatTargetRef Player(int i = 0) => new(ECombatSide.Player, i);
    private static CombatTargetRef Enemy(int i = 0) => new(ECombatSide.Enemy, i);

    #region 定义与入场

    [Test]
    public void Content_validates_identity_and_four_standard_budget_cards()
    {
        var registry = BaseGameContent.BuildRegistry(out var report);
        Assert.That(report.HasIssues, Is.False, string.Join("\n", report.ValidationErrors));
        var def = BaseGameContent.Load().Characters["hualing"];
        Assert.That(def.Element, Is.EqualTo(EElement.Blue));
        Assert.That(def.Role, Is.EqualTo(ERole.Controller));
        Assert.That(def.Race, Is.EqualTo(ERace.Machine | ERace.Human));
        Assert.That(def.ActiveSkillChain.Single().Cooldown, Is.EqualTo(6));
        Assert.That(def.Passives.Select(p => p.RequiredPotential), Is.EqualTo(new[] { 0, 10, 30, 50 }));
        Assert.That(def.Cards, Has.Count.EqualTo(4));
        foreach (var id in def.Cards)
        {
            Assert.That(registry.Store.TryGetCard(id, out var card), Is.True);
            Assert.That(card!.Element, Is.EqualTo((int)EElement.Blue));
            Assert.That(card.Role, Is.EqualTo(ERole.Controller));
            Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
            Assert.That(card.IsExclusive, Is.True);
            Assert.That(card.Stats!.Attributes.Sum(p => p.Key == AttributeIds.MaxHealth ? p.Value : 10 * p.Value), Is.EqualTo(40));
        }
    }

    [Test]
    public void Battle_start_applies_permanent_undispellable_suppression_to_every_enemy()
    {
        using var sim = Build(start: true);
        sim.RunBattleStart();
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.HasTag(BuiltinBuffTags.TraitImmunePoison), Is.True);
        foreach (var (enemy, index) in sim.EnemyTeam.Enemies.Select((e, i) => (e, i)))
        {
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(68));
            Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(68));
            Assert.That(sim.Buffs.Dispel(sim, Enemy(index), "hualing_entrance_suppression"), Is.Zero);
        }
        for (var turn = 0; turn < 5; turn++) sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireWaveStart(sim);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == 68), Is.True, "不按回合或阶层重复累减");
    }

    [Test]
    public void Entry_event_targets_only_the_arriving_enemy_and_is_idempotent_for_this_debuff()
    {
        using var sim = Build();
        Passive(sim, 2);
        sim.Buffs.FireEnemyEntered(sim, 2);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalAttack, 2), Is.EqualTo(68));
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalAttack, 0), Is.EqualTo(80));
        sim.Buffs.FireEnemyEntered(sim, 2);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalAttack, 2), Is.EqualTo(68));
        sim.PlayerTeam.Characters[0].Buffs.Find("hualing_passive_p2")!.SetDormant(true);
        sim.Buffs.FireEnemyEntered(sim, 1);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalAttack, 1), Is.EqualTo(80));
    }

    [Test]
    public void Actual_wave_transition_initializes_suppression_on_new_enemies()
    {
        var wave = new BattleWaveDto { EnemySpawns = [new() { EnemyId = "slime", Count = 2 }] };
        using var sim = Build(start: true, battle: new BattleDto { Id = "test.waves", Waves = [wave, wave] });
        sim.RunBattleStart();
        foreach (var enemy in sim.EnemyTeam.Enemies) enemy.Asc.Attributes.SetCurrentValue(AttributeIds.Health, 0);
        sim.CheckEndConditions();
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
        Assert.That(sim.EnemyTeam.Enemies, Has.Count.EqualTo(2));
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Buffs.Find("hualing_entrance_suppression") is not null), Is.True);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == -7), Is.True, "史莱姆基础物攻5减12");
    }

    [Test]
    public void Entry_and_domain_references_are_validated_before_admission()
    {
        var store = new GameDefinitionStore();
        store.BuffsMutable["entry"] = new BuffDto { Id = "entry", Hooks = new() { OnEnemyEntered = [new() { EffectId = "missing.effect" }] } };
        store.GameplayEffectsMutable["domain"] = new GameplayEffectDefDto { Id = "domain", DomainBuffRefs = [new() { BuffId = "missing.buff" }] };
        var errors = new ContentDefinitionValidator().Validate(store);
        Assert.That(errors.Any(e => e.DefinitionId == "entry" && e.Message.Contains("missing.effect")), Is.True);
        Assert.That(errors.Any(e => e.DefinitionId == "domain" && e.Message.Contains("missing.buff")), Is.True);
    }

    [Test]
    public void Poison_immunity_prevents_poison_tag_for_hualing_but_not_teammates()
    {
        using var sim = Build();
        sim.Definitions.Store.GameplayEffectsMutable["test.poison"] = new GameplayEffectDefDto
        {
            Id = "test.poison", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 3, GrantedTags = [CombatConstants.PoisonTag]
        };
        Passive(sim, 1);
        var applicator = new GameplayEffectApplicator(sim.Definitions);
        Assert.That(applicator.ApplyToTargets(sim, Enemy(), [Player(), Player(1)], "test.poison"), Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.False);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.True);
    }

    [Test]
    public void Domain_buff_cycles_are_rejected_and_limited_duration_auras_are_not_admitted()
    {
        var store = new GameDefinitionStore();
        store.BuffsMutable["aura"] = new BuffDto
        {
            Id = "aura", DurationType = EBuffDurationType.Permanent, Hooks = new() { OnApply = [new() { EffectId = "reopen" }] }
        };
        store.GameplayEffectsMutable["domain"] = new GameplayEffectDefDto { Id = "domain", DomainBuffRefs = [new() { BuffId = "aura" }] };
        store.EffectsMutable["reopen"] = new EffectDto { Id = "reopen", Kind = EEffectKind.SetDomain, Params = new() { ["gameplayEffectId"] = "domain" } };
        Assert.That(new ContentDefinitionValidator().Validate(store).Any(e => e.Message.Contains("Synchronous reference cycle")), Is.True);
        store.BuffsMutable["aura"] = new BuffDto { Id = "aura", DurationType = EBuffDurationType.Turns, Duration = 1 };
        Assert.That(new ContentDefinitionValidator().Validate(store).Any(e => e.Message.Contains("must be permanent")), Is.True);
    }

    #endregion

    #region 主动与领域

    [Test]
    public void Active_requires_six_progress_and_weakens_all_four_stats_for_one_turn()
    {
        using var sim = Build();
        var c = sim.PlayerTeam.Characters[0];
        c.GainSkillCounter(5);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.False);
        Assert.That(c.SkillCounter, Is.EqualTo(5));
        c.GainSkillCounter(1);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(c.SkillCounter, Is.Zero);
        foreach (var e in sim.EnemyTeam.Enemies)
        {
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(50));
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(50));
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.EqualTo(-20));
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.EqualTo(-15));
        }
        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Null);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(EnemyValue(sim, AttributeIds.MagicAttack), Is.EqualTo(80));
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.EqualTo(10));
    }

    [Test]
    public void Domain_matches_any_identity_on_both_sides_once_and_sets_every_enemys_defenses_to_zero()
    {
        using var sim = Build();
        Passive(sim, 4); Cast(sim);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("hualing_illusory_world"));
        Assert.That(sim.PlayerTeam.Characters.Select(c => c.Asc.GetCurrentValue(AttributeIds.DamageTakenScale)), Is.EqualTo(new[] { -0.5f, -0.5f, -0.5f, 0f }));
        Assert.That(sim.EnemyTeam.Enemies.Select(e => e.Asc.GetCurrentValue(AttributeIds.DamageTakenScale)), Is.EqualTo(new[] { -0.5f, -0.5f, -0.5f, 0f, -0.5f }));
        foreach (var e in sim.EnemyTeam.Enemies)
        {
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.PhysicalDefense), Is.Zero);
            Assert.That(e.Asc.GetCurrentValue(AttributeIds.MagicDefense), Is.Zero);
        }
        Assert.That(sim.Buffs.Dispel(sim, Player(), "hualing_illusory_protection"), Is.Zero, "领域加成由领域生命周期控制");
        sim.DomainManager.FireTurnEndHooks(); sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Null);
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.DamageTakenScale) == 0), Is.True);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Buffs.Find("hualing_illusory_protection") is null), Is.True);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.EqualTo(10));
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense), Is.EqualTo(15));
    }

    [Test]
    public void Domain_affects_magic_damage_and_normal_attacks_on_matching_targets()
    {
        using var sim = Build();
        Passive(sim, 4); Cast(sim);
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "hualing_prism_hit" }, sim, Player(), [Enemy(), Enemy(3)]);
        Assert.That(Hp(sim, 0), Is.EqualTo(984));
        Assert.That(Hp(sim, 3), Is.EqualTo(968));
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "hualing_prism_hit" }, sim, Enemy(3), [Player(), Player(3)]);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(944.5f), "37*0.5与37，只有符合身份的槽位减伤");
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackDamageDealtScale, 0);
        var result = sim.NormalAttacks.Execute(sim)!;
        Assert.That(result.TotalDamage, Is.EqualTo(120));
        Assert.That(Hp(sim, 0), Is.EqualTo(964));
        Assert.That(Hp(sim, 3), Is.EqualTo(928));
    }

    [Test]
    public void Domain_damage_reduction_adds_with_other_damage_modifiers()
    {
        using var sim = Build();
        sim.DomainManager.TrySetPlayerDomain("hualing_illusory_world", null, 1);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 0.25f);
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "hualing_prism_hit" }, sim, Player(), [Enemy()]);
        Assert.That(Hp(sim, 0), Is.EqualTo(987.25f), "(12+20-15)*(1+0.25-0.5)");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Replacing_or_clearing_domain_removes_only_its_owned_buffs(bool replace)
    {
        using var sim = Build();
        Passive(sim, 4); Cast(sim);
        sim.Buffs.Apply(sim, Player(), "hualing_illusory_protection");
        Assert.That(Value(sim, AttributeIds.DamageTakenScale), Is.EqualTo(-1));
        if (replace) Assert.That(sim.DomainManager.TrySetPlayerDomain("von_neumann_fate_domain"), Is.True);
        else sim.DomainManager.ClearPlayerDomain();
        Assert.That(Value(sim, AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f), "同名普通Buff不被领域撤销误删");
        Assert.That(Value(sim, AttributeIds.DamageTakenScale, 1), Is.Zero);
        Assert.That(EnemyValue(sim, AttributeIds.DamageTakenScale), Is.Zero);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.Zero, "归零减益的1回合期限独立于领域");
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.EqualTo(10));
    }

    [Test]
    public void Recasting_domain_does_not_stack_and_dead_enemy_aura_is_removed_when_domain_closes()
    {
        using var sim = Build();
        Passive(sim, 4); Cast(sim); Cast(sim);
        Assert.That(Value(sim, AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f));
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.Attributes.SetCurrentValue(AttributeIds.Health, 0);
        sim.DomainManager.ClearPlayerDomain();
        Assert.That(enemy.Buffs.Find("hualing_illusory_protection"), Is.Null);
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.Zero);
    }

    [Test]
    public void New_enemy_initialization_receives_current_domain_without_reapplying_existing_allies()
    {
        using var sim = Build();
        sim.DomainManager.TrySetPlayerDomain("hualing_illusory_world");
        var previous = sim.EnemyTeam.Enemies[0];
        var newEnemy = new EnemyUnit("new", "unknown", new Dictionary<string, float> { [AttributeIds.MaxHealth] = 100 }, EElement.Red, ERace.Machine);
        sim.EnemyTeam.ReplaceEnemies([newEnemy]);
        sim.ApplyEnemyInitialBuffs(); sim.DomainManager.RefreshDomainBuffs();
        Assert.That(newEnemy.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f));
        Assert.That(Value(sim, AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f));
        sim.DomainManager.ClearPlayerDomain();
        Assert.That(newEnemy.Buffs.Find("hualing_illusory_protection"), Is.Null);
        Assert.That(previous.Buffs.Find("hualing_illusory_protection"), Is.Null);
    }

    [Test]
    public void Zero_defense_overrides_positive_negative_and_later_additive_modifiers_then_restores_them()
    {
        using var sim = Build();
        sim.EnemyTeam.Enemies[0].Asc.SetBaseValue(AttributeIds.PhysicalDefense, -5);
        Passive(sim, 4); Cast(sim);
        sim.Buffs.Apply(sim, Enemy(), "weilong_armor_defense");
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.Zero);
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense), Is.Zero);
        sim.DomainManager.FireTurnEndHooks(); sim.Buffs.FireTurnEnd(sim);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalDefense), Is.EqualTo(1));
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense), Is.EqualTo(21));
    }

    [Test]
    public void Domain_replaced_by_its_aura_on_apply_leaves_no_orphaned_buffs()
    {
        using var sim = Build();
        sim.Definitions.Store.BuffsMutable["test.replacing_aura"] = new BuffDto
        {
            Id = "test.replacing_aura", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnApply = [new() { EffectId = "test.swap_domain" }] }
        };
        sim.Definitions.Store.EffectsMutable["test.swap_domain"] = new EffectDto
        {
            Id = "test.swap_domain", Kind = EEffectKind.SetDomain, Params = new() { ["gameplayEffectId"] = "von_neumann_fate_domain" }
        };
        sim.Definitions.Store.GameplayEffectsMutable["test.old_domain"] = new GameplayEffectDefDto
        {
            Id = "test.old_domain", DomainBuffRefs = [new() { BuffId = "test.replacing_aura" }]
        };
        Assert.That(sim.DomainManager.TrySetPlayerDomain("test.old_domain"), Is.True);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("von_neumann_fate_domain"));
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Buffs.Find("test.replacing_aura") is null), Is.True);
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Buffs.Find("test.replacing_aura") is null), Is.True);
    }

    #endregion

    #region 协同与专属卡

    [Test]
    public void Progress_stacks_self_blue_machine_and_human_every_six_floor_turns()
    {
        using var sim = Build();
        Passive(sim, 3); sim.Buffs.FireWaveStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
        foreach (var c in sim.PlayerTeam.Characters) c.PaySkillCounter(c.SkillCounter);
        for (var i = 0; i < 5; i++) { sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim); }
        Assert.That(Counters(sim), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        sim.IncrementTurnsIntoWave(); sim.Buffs.FireTurnStart(sim);
        Assert.That(Counters(sim), Is.EqualTo(new[] { 4, 1, 1, 0 }));
    }

    [Test]
    public void All_four_cards_execute_with_control_damage_shields_and_progress()
    {
        var cards = BaseGameContent.Load().Characters["hualing"].Cards.ToArray();
        using var sim = Build(cards: cards);
        for (var slot = 0; slot < 4; slot++)
        {
            var result = sim.TryApply(new PlayCardCommand(0, slot, slot == 3 ? [Enemy(3)] : []));
            Assert.That(result.Success, Is.True, result.Error);
        }
        sim.TransitionTo(ECombatPhase.CardExecution); sim.AdvancePhase();
        Assert.That(Hp(sim, 0), Is.EqualTo(983), "12+50%*40-15");
        Assert.That(Hp(sim, 3), Is.EqualTo(971), "定格先降低魔防12");
        Assert.That(sim.EnemyTeam.Enemies.All(e => e.Asc.GetCurrentValue(AttributeIds.PhysicalAttack) == 72), Is.True);
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense, 0), Is.EqualTo(9));
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense, 3), Is.EqualTo(-3));
        Assert.That(sim.EnemyTeam.Enemies[3].ActionCount, Is.EqualTo(2));
        Assert.That(sim.EnemyTeam.Enemies[0].ActionCount, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters.All(c => c.Asc.GetCurrentValue(AttributeIds.Shield) == 25), Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].SkillCounter, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].Graveyard, Has.Count.EqualTo(4));
        sim.Buffs.FireTurnEnd(sim); sim.Buffs.FireTurnEnd(sim);
        Assert.That(EnemyValue(sim, AttributeIds.PhysicalAttack), Is.EqualTo(80));
        Assert.That(EnemyValue(sim, AttributeIds.MagicDefense, 3), Is.EqualTo(15));
    }

    #endregion

    #region 构造与断言

    private static CombatSimulation Build(bool start = false, string[]? cards = null, BattleDto? battle = null)
    {
        var identities = new[] { (EElement.Blue, ERace.Machine | ERace.Human), (EElement.Green, ERace.Machine), (EElement.Red, ERace.Human), (EElement.Yellow, ERace.God) };
        var characters = identities.Select((identity, index) => CharacterBattleInstance.CreateForTests(index == 0 ? "hualing" : $"ally{index}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MaxEnergy] = 10, [AttributeIds.InitialEnergy] = 10,
                [AttributeIds.PhysicalAttack] = 20, [AttributeIds.MagicAttack] = 40, [AttributeIds.NormalAttackDamageDealtScale] = -10
            },
            drawPile: index == 0 ? cards?.Reverse().Select((id, i) => new CardRuntimeEntry(id, $"c{i}")) : null,
            skillCounterCap: 20, activeSkillChain: index == 0 ? BaseGameContent.Load().Characters["hualing"].ActiveSkillChain : null,
            element: identity.Item1, race: identity.Item2)).ToArray();
        characters[0].DrawCards(cards?.Length ?? 0);
        foreach (var c in characters) c.RefillAvailableEnergy();
        var enemies = new[] { (EElement.Blue, ERace.Animal), (EElement.Red, ERace.Machine), (EElement.Green, ERace.Human), (EElement.Yellow, ERace.God), (EElement.Blue, ERace.Machine | ERace.Human) }
            .Select((identity, i) => new EnemyUnit($"e{i}", "unknown", new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 1000, [AttributeIds.PhysicalAttack] = 80, [AttributeIds.MagicAttack] = 80,
                [AttributeIds.PhysicalDefense] = 10, [AttributeIds.MagicDefense] = 15
            }, identity.Item1, identity.Item2));
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies), new CombatRuleEngine([]), BaseGameContent.BuildRegistry(),
            initialPhase: start ? ECombatPhase.BattleStart : ECombatPhase.Player, runSeed: 20261009, battle: battle,
            initialBuffs: start ? BaseGameContent.Load().Characters["hualing"].Passives.Select(p => new BattleStartBuffEntry(0, p.BuffId, p.Params)).ToArray() : null);
    }

    private static void Passive(CombatSimulation sim, int i) => sim.Buffs.Apply(sim, Player(), $"hualing_passive_p{i}");
    private static float Value(CombatSimulation sim, string id, int index = 0) => sim.PlayerTeam.Characters[index].Asc.GetCurrentValue(id);
    private static float EnemyValue(CombatSimulation sim, string id, int index = 0) => sim.EnemyTeam.Enemies[index].Asc.GetCurrentValue(id);
    private static float Hp(CombatSimulation sim, int index) => EnemyValue(sim, AttributeIds.Health, index);
    private static int[] Counters(CombatSimulation sim) => sim.PlayerTeam.Characters.Select(c => c.SkillCounter).ToArray();
    private static void Cast(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].GainSkillCounter(6);
        var result = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(result.Success, Is.True, result.Error);
    }

    #endregion
}