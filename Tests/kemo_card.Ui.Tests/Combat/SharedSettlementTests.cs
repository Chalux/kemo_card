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

/// <summary>
/// 规格 §1.2 / §1.3：玩家角色没有 Health 当前值，账本只有一份 SharedHp。
/// 分槽伤害（D2）逐槽结算后各扣一次账本，<c>scope: Team</c> 对账本只结算一次且不吃分槽护盾，
/// 治疗只作用于 Team。
/// </summary>
[TestFixture]
public sealed class SharedSettlementTests
{
    private const float SlotMaxHealth = 25f;
    private const int SharedMaxHp = 100;
    private const int SlotDamage = 10;

    private static CombatTargetRef Slot(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef EnemySource => new(ECombatSide.Enemy, 0);

    private static IReadOnlyList<CombatTargetRef> AllSlots =>
        [Slot(0), Slot(1), Slot(2), Slot(3)];

    #region 分槽伤害（D2）

    [Test]
    public void Aoe_on_every_slot_settles_the_ledger_once_per_slot()
    {
        using var sim = Build();

        Execute(sim, "effect.slot_damage", AllSlots);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - (SlotDamage * 4)), "AoE 分槽逐次扣账本");
    }

    [Test]
    public void Slot_damage_leaves_the_character_health_current_value_untouched()
    {
        using var sim = Build();
        var character = sim.PlayerTeam.Characters[0];

        Execute(sim, "effect.slot_damage", [Slot(0)]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - SlotDamage));
        Assert.That(
            character.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(0f),
            "玩家角色没有 Health 当前值，结算用的工作血量必须复位");
    }

    [Test]
    public void Slot_damage_reduction_rule_only_corrects_its_own_slot_settlement()
    {
        using var sim = Build(rules: [new SlotShieldRule(slotIndex: 1, reducedAmount: 1f)]);

        Execute(sim, "effect.slot_damage", AllSlots);

        Assert.That(
            sim.PlayerTeam.SharedHp,
            Is.EqualTo(SharedMaxHp - ((SlotDamage * 3) + 1)),
            "槽位减伤只修正被点名槽的那一次结算");
    }

    [Test]
    public void Gas_damage_formula_survives_the_transfer_to_the_ledger()
    {
        using var sim = Build(extraSlotAttributes: SlotAttributes(1, AttributeIds.PhysicalDefense, 4f));

        Execute(sim, "effect.ge_damage", [Slot(1)]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - 6), "槽位防御仍参与 GAS 伤害公式");
    }

    [Test]
    public void Gas_slot_damage_taken_scale_only_affects_its_own_settlement()
    {
        using var sim = Build(extraSlotAttributes: SlotAttributes(2, AttributeIds.DamageTakenScale, -0.5f));

        Execute(sim, "effect.ge_damage", [Slot(2), Slot(3)]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - 5 - 10), "减伤槽扣 5，普通槽仍扣 10");
    }

    [Test]
    public void Overkill_on_a_slot_cannot_push_the_ledger_below_zero()
    {
        using var sim = Build();

        Execute(sim, "effect.slot_damage", AllSlots);
        Execute(sim, "effect.slot_damage", AllSlots);
        Execute(sim, "effect.slot_damage", AllSlots);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(0));
        Assert.That(sim.PlayerTeam.IsDefeated, Is.True);
    }

    #endregion

    #region Team 账本一次结算

    [Test]
    public void Team_damage_settles_the_ledger_once()
    {
        using var sim = Build();

        Execute(sim, "effect.slot_damage", [CombatTargetRef.PlayerTeam]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - SlotDamage));
    }

    [Test]
    public void Team_damage_does_not_go_through_slot_damage_rules()
    {
        using var sim = Build(rules: [new SlotShieldRule(slotIndex: -1, reducedAmount: 1f)]);

        Execute(sim, "effect.slot_damage", [CombatTargetRef.PlayerTeam]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - SlotDamage), "Team 直伤不经分槽护盾钩子");
    }

    [Test]
    public void Team_scope_on_an_enemy_skill_resolves_to_the_ledger()
    {
        using var sim = Build(enemySkillId: "skill.team_hit");

        RunEnemyPhase(sim);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - SlotDamage));
    }

    [Test]
    public void All_scope_on_an_enemy_skill_resolves_to_every_player_slot()
    {
        using var sim = Build(enemySkillId: "skill.aoe_slots");

        RunEnemyPhase(sim);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - (SlotDamage * 4)), "scope: All 是分槽逐次结算");
    }

    #endregion

    #region 治疗只作用于 Team

    [Test]
    public void Team_heal_tops_up_the_ledger_and_caps_at_max()
    {
        using var sim = Build();
        Execute(sim, "effect.slot_damage", [CombatTargetRef.PlayerTeam]);

        Execute(sim, "effect.heal", [CombatTargetRef.PlayerTeam]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp), "治疗不超过 MaxSharedHp");
    }

    [Test]
    public void Heal_targeting_a_player_slot_is_a_soft_failure()
    {
        using var sim = Build();
        Execute(sim, "effect.slot_damage", AllSlots);
        var damaged = sim.PlayerTeam.SharedHp;

        Execute(sim, "effect.heal", [Slot(0)]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(damaged), "点名槽位的治疗不入账本");
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(0f),
            "也不写角色 Health");
        Assert.That(sim.PlayerTeam.RejectedSlotHealCount, Is.EqualTo(1));
    }

    [Test]
    public void Enemy_heal_still_restores_its_own_hp()
    {
        using var sim = Build();
        sim.EnemyTeam.Enemies[0].ApplyDamage(30);

        Execute(sim, "effect.heal", [new CombatTargetRef(ECombatSide.Enemy, 0)]);

        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(90), "敌人维持独立 HP 结算");
    }

    #endregion

    #region 执行期目标复查（规格 §2.4）

    [Test]
    public void Player_slot_targets_are_never_invalidated_during_the_execution_phase()
    {
        using var sim = Build(handCardId: "card.slot_hit");
        Execute(sim, "effect.slot_damage", [CombatTargetRef.PlayerTeam]);
        MarkCard(sim, slotIndex: 0, [Slot(2)]);

        RunCardExecution(sim);

        Assert.That(
            sim.PlayerTeam.SharedHp,
            Is.EqualTo(SharedMaxHp - (SlotDamage * 2)),
            "v1 无单角色倒地，玩家槽位目标不会失效，执行期无需重定向");
    }

    [Test]
    public void Team_scope_card_fires_blank_once_the_ledger_is_empty()
    {
        using var sim = Build(handCardId: "card.team_hit");
        var holder = sim.PlayerTeam.Characters[0];
        MarkCard(sim, slotIndex: 0, [CombatTargetRef.PlayerTeam]);
        sim.PlayerTeam.ApplySharedDamage(SharedMaxHp);

        RunCardExecution(sim);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(0));
        Assert.That(holder.Graveyard, Has.Count.EqualTo(1), "账本已空，Team 目标解析为空集即空放，牌照样进弃牌堆");
    }

    #endregion

    #region 内容校验：Heal 目标

    [Test]
    public void Heal_card_targeting_player_slots_fails_content_validation()
    {
        var registry = HealCardRegistry(ETargetSide.Ally, ETargetScope.Single);

        var valid = CombatContentValidator.TryValidateHealTargeting(registry, out var error);

        Assert.That(valid, Is.False);
        Assert.That(error, Does.Contain("card.heal"));
    }

    [Test]
    public void Heal_card_targeting_all_player_slots_fails_content_validation()
    {
        var registry = HealCardRegistry(ETargetSide.Ally, ETargetScope.All);

        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out _), Is.False);
    }

    [Test]
    public void Heal_card_targeting_the_team_passes_content_validation()
    {
        var registry = HealCardRegistry(ETargetSide.Ally, ETargetScope.Team);

        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out _), Is.True);
    }

    [Test]
    public void Heal_active_skill_tier_targeting_a_player_slot_fails_content_validation()
    {
        var registry = HealChainRegistry(new TargetSpecDto
        {
            Side = ETargetSide.Ally,
            Scope = ETargetScope.Single,
        });

        var valid = CombatContentValidator.TryValidateHealTargeting(registry, out var error);

        Assert.That(valid, Is.False);
        Assert.That(error, Does.Contain("skill.heal"));
    }

    [Test]
    public void Heal_active_skill_tier_without_a_target_override_fails_content_validation()
    {
        var registry = HealChainRegistry(targetOverride: null);

        Assert.That(
            CombatContentValidator.TryValidateHealTargeting(registry, out _),
            Is.False,
            "缺省视作 Self 单体，治疗会打到施法者自己的槽位");
    }

    [Test]
    public void Heal_active_skill_tier_targeting_the_team_passes_content_validation()
    {
        var registry = HealChainRegistry(new TargetSpecDto
        {
            Side = ETargetSide.Ally,
            Scope = ETargetScope.Team,
        });

        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out _), Is.True);
    }

    [Test]
    public void Enemy_side_heal_content_is_not_restricted()
    {
        var registry = HealCardRegistry(ETargetSide.Enemy, ETargetScope.Single);

        Assert.That(CombatContentValidator.TryValidateHealTargeting(registry, out _), Is.True);
    }

    [Test]
    public void Standalone_self_heal_skill_is_not_restricted()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.heal"] = new()
                {
                    Id = "skill.heal",
                    TargetOverride = new TargetSpecDto { Side = ETargetSide.Self, Scope = ETargetScope.Self },
                    EffectRefs = [new EffectRefDto { EffectId = "effect.heal" }],
                },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.heal"] = new() { Id = "effect.heal", Kind = EEffectKind.Heal },
            });

        Assert.That(
            CombatContentValidator.TryValidateHealTargeting(registry, out _),
            Is.True,
            "独立技能的 side 是相对施法者的，敌人自愈同样写 Self，不能在此判定敌我");
    }

    #endregion

    private sealed class SlotShieldRule : ICombatRule
    {
        private readonly int _slotIndex;
        private readonly float _reducedAmount;

        public SlotShieldRule(int slotIndex, float reducedAmount)
        {
            _slotIndex = slotIndex;
            _reducedAmount = reducedAmount;
        }

        public string Id => "test.slot_shield";
        public int Priority => 0;

        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet)
        {
            if (packet.Target.Side != ECombatSide.Player || packet.Target.Index != _slotIndex)
                return;
            packet.Amount = _reducedAmount;
        }
    }

    private static void Execute(CombatSimulation sim, string effectId, IReadOnlyList<CombatTargetRef> targets) =>
        sim.EffectExecutor.ExecuteEffectRef(new EffectRefDto { EffectId = effectId }, sim, EnemySource, targets);

    private static void RunEnemyPhase(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.Enemy);
        sim.AdvancePhase();
    }

    private static void MarkCard(CombatSimulation sim, int slotIndex, IReadOnlyList<CombatTargetRef> targets)
    {
        var result = sim.TryApply(new PlayCardCommand(0, slotIndex, targets));
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static void RunCardExecution(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<string, float>> SlotAttributes(
        int slotIndex,
        string attributeId,
        float value)
    {
        return new Dictionary<int, IReadOnlyDictionary<string, float>>
        {
            [slotIndex] = new Dictionary<string, float>(StringComparer.Ordinal) { [attributeId] = value },
        };
    }

    private static GameDefinitionRegistry HealCardRegistry(ETargetSide side, ETargetScope scope) =>
        CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                ["card.heal"] = new()
                {
                    Id = "card.heal",
                    TargetSide = side,
                    TargetScope = scope,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.heal" }],
                },
            },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.heal"] = new()
                {
                    Id = "skill.heal",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.heal" }],
                },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.heal"] = new() { Id = "effect.heal", Kind = EEffectKind.Heal },
            });

    private static GameDefinitionRegistry HealChainRegistry(TargetSpecDto? targetOverride) =>
        CombatTestHelper.CreateFullRegistry(
            characters: new Dictionary<string, CharacterDto>
            {
                ["healer"] = new()
                {
                    Id = "healer",
                    ActiveSkillChain = [new ActiveSkillChainEntryDto { SkillId = "skill.heal", Cooldown = 3 }],
                },
            },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.heal"] = new()
                {
                    Id = "skill.heal",
                    TargetOverride = targetOverride,
                    EffectRefs = [new EffectRefDto { EffectId = "effect.heal" }],
                },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.heal"] = new() { Id = "effect.heal", Kind = EEffectKind.Heal },
            });

    private static CombatSimulation Build(
        IEnumerable<ICombatRule>? rules = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, float>>? extraSlotAttributes = null,
        string? enemySkillId = null,
        string? handCardId = null)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                ["card.slot_hit"] = new()
                {
                    Id = "card.slot_hit",
                    CostType = ECostType.None,
                    TargetSide = ETargetSide.Ally,
                    TargetScope = ETargetScope.Single,
                    TargetCount = 1,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.payload" }],
                },
                ["card.team_hit"] = new()
                {
                    Id = "card.team_hit",
                    CostType = ECostType.None,
                    TargetSide = ETargetSide.Ally,
                    TargetScope = ETargetScope.Team,
                    SkillRefs = [new SkillRefDto { SkillId = "skill.payload" }],
                },
            },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.aoe_slots"] = new()
                {
                    Id = "skill.aoe_slots",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.slot_damage" }],
                    TargetOverride = new TargetSpecDto { Side = ETargetSide.Enemy, Scope = ETargetScope.All },
                },
                ["skill.team_hit"] = new()
                {
                    Id = "skill.team_hit",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.slot_damage" }],
                    TargetOverride = new TargetSpecDto { Side = ETargetSide.Enemy, Scope = ETargetScope.Team },
                },
                ["skill.payload"] = new()
                {
                    Id = "skill.payload",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.slot_damage" }],
                },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.slot_damage"] = new()
                {
                    Id = "effect.slot_damage",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = SlotDamage },
                },
                ["effect.heal"] = new()
                {
                    Id = "effect.heal",
                    Kind = EEffectKind.Heal,
                    Params = new Dictionary<string, object> { ["amount"] = 20 },
                },
                ["effect.ge_damage"] = new()
                {
                    Id = "effect.ge_damage",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object>
                    {
                        ["amount"] = SlotDamage,
                        ["damageGameplayEffectId"] = "ge.strike",
                    },
                },
            },
            enemies: new Dictionary<string, EnemyDto>
            {
                ["slime"] = new()
                {
                    Id = "slime",
                    MaxHp = 100,
                    SkillRefs = enemySkillId is null ? [] : [new SkillRefDto { SkillId = enemySkillId }],
                },
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["ge.strike"] = new()
                {
                    Id = "ge.strike",
                    DurationPolicy = EDurationPolicy.Instant,
                    StackingPolicy = EStackingPolicy.None,
                    MaxStacks = 1,
                    Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Physical" }],
                },
            });

        var characters = Enumerable.Range(0, 4)
            .Select(index => CharacterBattleInstance.CreateForTests(
                $"c{index}",
                BuildSlotAttributes(index),
                handCardId is null
                    ? []
                    : Enumerable.Range(0, 3).Select(slot => new CardRuntimeEntry(handCardId, $"rt-c{index}-{slot}"))))
            .ToArray();
        if (handCardId is not null)
        {
            foreach (var character in characters)
                character.DrawCards(3);
        }

        return new CombatSimulation(
            new PlayerTeamState(characters, SharedMaxHp),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
            new CombatRuleEngine(rules ?? []),
            registry,
            initialPhase: ECombatPhase.Player);

        Dictionary<string, float> BuildSlotAttributes(int index)
        {
            var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = SlotMaxHealth,
            };
            if (extraSlotAttributes is null || !extraSlotAttributes.TryGetValue(index, out var extra))
                return attributes;

            foreach (var (id, value) in extra)
                attributes[id] = value;
            return attributes;
        }
    }
}