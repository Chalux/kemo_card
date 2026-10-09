using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed partial class CombatAuditRegressionTests
{
    private static CombatTargetRef Player(int index = 0) => new(ECombatSide.Player, index);
    private static CombatTargetRef Enemy(int index = 0) => new(ECombatSide.Enemy, index);

    private static CombatSimulation Build(GameDefinitionRegistry registry, int enemyCount = 1,
        IReadOnlyList<ICombatRule>? rules = null, BattleDto? battle = null,
        IContentEffectScriptHost? scriptHost = null, string modId = "test.mod")
    {
        var characters = Enumerable.Range(0, 4).Select(i => CharacterBattleInstance.CreateForTests(
            $"c{i}", new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 25,
                [AttributeIds.PhysicalAttack] = 10,
                [AttributeIds.InitialEnergy] = 5,
                [AttributeIds.MaxEnergy] = 5,
            }, activeSkillChain: [new() { SkillId = "active", Cooldown = 1 }])).ToArray();
        foreach (var c in characters)
        {
            c.RefillAvailableEnergy();
            c.TickSkillCounter();
        }
        return new CombatSimulation(new PlayerTeamState(characters, 100),
            new EnemyTeamState(Enumerable.Range(0, enemyCount).Select(i => new EnemyUnit($"e{i}", "slime", 100))),
            new CombatRuleEngine(rules ?? []), registry, initialPhase: ECombatPhase.Player, battle: battle,
            scriptHost: scriptHost, modId: modId);
    }

    private static AttributeModifierDefDto Add(string attribute, float amount) => new()
    {
        AttributeId = attribute,
        Operation = EAttributeModifierOp.Add,
        Magnitude = new() { Kind = EMagnitudeKind.Scalar, Scalar = amount },
    };

    [Test]
    public void Obtained_generic_card_should_be_accepted_at_battle_creation()
    {
        var registry = CombatTestHelper.CreateRegistry(new() { Id = "exclusive", Stats = new() { HpCap = 25 } },
            new() { Id = "obtained", Stats = new() { HpCap = 25 } });
        var source = new CharacterInstance(new CharacterDto { Id = "hero", Cards = ["exclusive"] });
        source.GetCurrentDeck()!.TryAddCard("obtained", source.GetBuildableCardIds(new HashSet<string> { "obtained" }));
        Assert.That(source.ValidateCurrentDeck(new HashSet<string> { "obtained" }).IsValid, Is.True);
        var result = CharacterBattleInstance.TryCreate(source, registry, new HostRng(1, "audit"), out var error, new HashSet<string> { "obtained" });
        Assert.That(result, Is.Not.Null, error);
    }

    [Test]
    public void Gas_overkill_should_apply_shield_before_health_clipping()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "damage", DurationPolicy = EDurationPolicy.Instant,
            Executions = [new() { Kind = "Damage" }]
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge }));
        sim.PlayerTeam.ApplySharedDamage(80);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 50);
        Assert.That(new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy(), [Player()], ge.Id,
            new Dictionary<string, object> { ["Amount"] = 100 }), Is.True);
        Assert.That(sim.PlayerTeam.SharedHp, Is.Zero, "100 damage - 50 shield must kill a team with 20 HP");
    }

    [Test]
    public void Shielded_direct_hit_should_still_fire_on_damaged()
    {
        var hit = new EffectDto { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 10 } };
        var heal = new EffectDto { Id = "heal", Kind = EEffectKind.Heal, Params = new() { ["amount"] = 5, ["healPowerScale"] = 0 } };
        var passive = new BuffDto
        {
            Id = "passive", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnDamaged = [new() { EffectId = "heal", Params = new() { ["hookTargets"] = "team" } }] }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [hit.Id] = hit, [heal.Id] = heal },
            buffs: new Dictionary<string, BuffDto> { [passive.Id] = passive }));
        sim.PlayerTeam.ApplySharedDamage(10);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 20);
        sim.Buffs.Apply(sim, Player(), passive.Id);
        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = hit.Id }, sim, Enemy(), [Player()]);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(95));
    }

    [Test]
    public void Character_gameplay_effect_should_expire_after_two_real_turns()
    {
        var ge = new GameplayEffectDefDto
        {
            Id = "temporary", DurationPolicy = EDurationPolicy.HasDuration,
            DurationTurns = 2, Modifiers = [Add(AttributeIds.PhysicalAttack, 6)]
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [ge.Id] = ge },
            attributes: new Dictionary<string, AttributeDefDto> { [AttributeIds.PhysicalAttack] = new() { Id = AttributeIds.PhysicalAttack } }));
        Assert.That(new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Player(), [Player()], ge.Id), Is.True);
        for (var i = 0; i < 2; i++) { sim.TransitionTo(ECombatPhase.Enemy); sim.AdvancePhase(); }
        Assert.That(sim.PlayerTeam.Characters[0].Asc.ActiveEffects, Is.Empty);
    }

    private sealed class CountStartRule : ICombatRule
    {
        public string Id => "count";
        public int Priority => 0;
        public int Count { get; private set; }
        public void OnTurnStart(CombatContext context) => Count++;
    }

    [Test]
    public void Rules_should_receive_first_and_subsequent_turn_start()
    {
        var rule = new CountStartRule();
        using var sim = Build(CombatTestHelper.CreateFullRegistry(), rules: [rule]);
        sim.RunBattleStart();
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
        Assert.That(rule.Count, Is.EqualTo(2));
    }

    [Test]
    public void Single_target_card_should_reject_duplicate_targets()
    {
        var card = new CardDto
        {
            Id = "card", CostType = ECostType.Energy, Cost = 1,
            TargetSide = ETargetSide.Enemy, TargetScope = ETargetScope.Single
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(cards: new Dictionary<string, CardDto> { [card.Id] = card }));
        sim.PlayerTeam.Characters[0].HandSlots[0].PlaceCard(card.Id, "runtime");
        var result = sim.TryApply(new PlayCardCommand(0, 0, [Enemy(), Enemy()]));
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void All_target_active_skill_should_reject_duplicate_subset()
    {
        var skill = new SkillDto { Id = "active", TargetOverride = new() { Side = ETargetSide.Enemy, Scope = ETargetScope.All } };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(skills: new Dictionary<string, SkillDto> { [skill.Id] = skill }), enemyCount: 2);
        var result = sim.TryApply(new CastActiveSkillCommand(0, [Enemy(), Enemy()]));
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Buff_removed_by_turn_end_hook_should_not_reregister_modifiers()
    {
        var remove = new EffectDto { Id = "remove", Kind = EEffectKind.RemoveBuff, Params = new() { ["buffId"] = "temporary" } };
        var buff = new BuffDto
        {
            Id = "temporary", StackRule = EBuffStackRule.Add, MaxStacks = 2, DurationType = EBuffDurationType.Turns, Duration = 2,
            Modifiers = [Add(AttributeIds.PhysicalAttack, 6)], Hooks = new() { OnTurnEnd = [new() { EffectId = remove.Id }] }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [remove.Id] = remove },
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }));
        sim.Buffs.Apply(sim, Player(), buff.Id).Instance!.TickTurnEnd();
        sim.Buffs.Apply(sim, Player(), buff.Id);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.All, Is.Empty);
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10));
    }

    [Test]
    public void Self_target_mark_should_not_survive_player_phase_wave_clear()
    {
        var card = new CardDto { Id = "self", CostType = ECostType.Energy, Cost = 1, TargetSide = ETargetSide.Self, TargetScope = ETargetScope.Self };
        var skill = new SkillDto
        {
            Id = "active", TargetOverride = new() { Side = ETargetSide.Enemy, Scope = ETargetScope.All },
            EffectRefs = [new() { EffectId = "kill" }]
        };
        var kill = new EffectDto { Id = "kill", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 1000 } };
        var wave = new BattleWaveDto { EnemySpawns = [new() { EnemyId = "slime", Count = 1 }] };
        var battle = new BattleDto { Id = "waves", Waves = [wave, wave] };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(cards: new Dictionary<string, CardDto> { [card.Id] = card },
            skills: new Dictionary<string, SkillDto> { [skill.Id] = skill }, effects: new Dictionary<string, EffectDto> { [kill.Id] = kill },
            enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = 100 } }), battle: battle);
        sim.PlayerTeam.Characters[0].HandSlots[0].PlaceCard(card.Id, "runtime");
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [Player()])).Success, Is.True);
        Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);
        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1));
        Assert.That(sim.CardQueue.Count, Is.Zero, "old paid marks must not cross the turn boundary");
    }

    [Test]
    public void Team_hp_clamp_should_use_effective_max_including_domain()
    {
        var domain = new GameplayEffectDefDto
        {
            Id = "domain", DurationPolicy = EDurationPolicy.Infinite,
            Modifiers = [Add(AttributeIds.MaxHealth, 100)]
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [domain.Id] = domain },
            attributes: new Dictionary<string, AttributeDefDto> { [AttributeIds.MaxHealth] = new() { Id = AttributeIds.MaxHealth } }));
        Assert.That(sim.DomainManager.TrySetPlayerDomain(domain.Id), Is.True);
        sim.PlayerTeam.HealShared(50);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.MaxHealth, 35);
        Assert.That(sim.PlayerTeam.MaxHp, Is.EqualTo(210));
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(150));
    }

    [Test]
    public void Lethal_turn_start_hook_should_end_battle_before_player_input()
    {
        var damage = new EffectDto { Id = "fatal", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 1000 } };
        var buff = new BuffDto
        {
            Id = "fatal", DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnTurnStart = [new() { EffectId = damage.Id, Params = new() { ["hookTargets"] = "team" } }] }
        };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [damage.Id] = damage },
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }));
        sim.Buffs.Apply(sim, Player(), buff.Id);
        sim.RunBattleStart();
        Assert.That(sim.PlayerTeam.IsDefeated, Is.True);
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
    }

    [Test]
    public void Buff_apply_hook_cycle_should_be_rejected_by_content_validator()
    {
        var effect = new EffectDto { Id = "apply_again", Kind = EEffectKind.ApplyBuff, Params = new() { ["buffId"] = "recursive" } };
        var buff = new BuffDto
        {
            Id = "recursive", StackRule = EBuffStackRule.Replace, DurationType = EBuffDurationType.Permanent,
            Hooks = new() { OnApply = [new() { EffectId = effect.Id }] }
        };
        var registry = CombatTestHelper.CreateFullRegistry(effects: new Dictionary<string, EffectDto> { [effect.Id] = effect },
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff });
        Assert.That(registry.Store.TryGetBuff(buff.Id, out _), Is.False);
        Assert.That(registry.Store.TryGetEffect(effect.Id, out _), Is.False);
    }

    [Test]
    public void Domain_turn_start_hook_may_replace_domain_without_collection_exception()
    {
        var action = new SkillActionDto
        {
            Id = "replace", Kind = ESkillActionKind.SetDomain,
            Params = new() { ["gameplayEffectId"] = "new" }
        };
        var old = new GameplayEffectDefDto
        {
            Id = "old", DurationPolicy = EDurationPolicy.Infinite,
            Hooks = new() { OnTurnStart = [new() { ActionId = action.Id }] }
        };
        var next = new GameplayEffectDefDto { Id = "new", DurationPolicy = EDurationPolicy.Infinite };
        using var sim = Build(CombatTestHelper.CreateFullRegistry(skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto> { [old.Id] = old, [next.Id] = next }));
        Assert.That(sim.DomainManager.TrySetPlayerDomain(old.Id), Is.True);
        Assert.DoesNotThrow(() => sim.DomainManager.FireTurnStartHooks());
    }

    [Test]
    public void Fixed_damage_result_should_report_actual_hp_loss()
    {
        using var sim = Build(CombatTestHelper.CreateFullRegistry());
        var result = sim.EffectExecutor.ApplyFixedDamage(sim, Player(), [Enemy()], 1000);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.Zero);
        Assert.That(result, Is.EqualTo(100));
    }

    private sealed class CaptureHost : IContentEffectScriptHost
    {
        public string? CalledMod { get; private set; }
        public bool TryExecute(string modId, string scriptPath, string scriptEntry,
            IReadOnlyDictionary<string, object>? context, out IReadOnlyList<Dictionary<string, object>> proposedEffects)
        {
            CalledMod = modId;
            proposedEffects = [];
            return true;
        }
    }

    [Test]
    public void Scripted_action_should_execute_in_its_definition_owner_mod()
    {
        var action = new SkillActionDto
        {
            Id = "foreign_action", Kind = ESkillActionKind.ExecuteScript,
            ScriptPath = "effects/foreign.js"
        };
        var registry = CombatTestHelper.CreateFullRegistry(skillActions: new Dictionary<string, SkillActionDto> { [action.Id] = action });
        var host = new CaptureHost();
        using var sim = Build(registry, scriptHost: host, modId: "battle.mod");
        Assert.That(registry.TryGetOwnerModId(EContentCategory.SkillAction, action.Id, out var owner), Is.True);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = action.Id }, sim, Player(), [Player()]);
        Assert.That(host.CalledMod, Is.EqualTo(owner));
    }
}