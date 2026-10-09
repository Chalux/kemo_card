using System.Text.Json;
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
public sealed class CombatReviewFixTests
{
    private static CombatTargetRef Player => new(ECombatSide.Player, 0);
    private static CombatTargetRef Enemy => new(ECombatSide.Enemy, 0);

    #region 嵌套选牌弃置

    [TestCase(true)]
    [TestCase(false)]
    public void Nested_discard_must_accept_selected_slot(bool useActions)
    {
        using var sim = Build();
        var skill = AddNestedDiscard(sim, useActions);
        sim.Definitions.Store.SkillsMutable[skill.Id] = skill;
        sim.PlayerTeam.Characters[0].GainSkillCounter(5);

        var result = sim.TryApply(new CastActiveSkillCommand(0, [], [0]));

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots[0].IsEmpty, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Nested_required_discard_must_reject_missing_selection_before_spending_cooldown(bool useActions)
    {
        using var sim = Build();
        var skill = AddNestedDiscard(sim, useActions);
        sim.Definitions.Store.SkillsMutable[skill.Id] = skill;
        var character = sim.PlayerTeam.Characters[0];
        character.GainSkillCounter(5);

        var result = sim.TryApply(new CastActiveSkillCommand(0, []));

        Assert.That(result.Success, Is.False);
        Assert.That(character.SkillCounter, Is.EqualTo(5));
        Assert.That(character.HandSlots[0].IsEmpty, Is.False);
    }

    [Test]
    public void Discard_selection_must_count_repeated_leaves_merge_each_reference_and_ignore_random_discards()
    {
        using var sim = Build();
        sim.PlayerTeam.Characters[0].DrawCards(4);
        sim.Definitions.Store.SkillActionsMutable["review.discard"] = new()
        {
            Id = "review.discard", Kind = ESkillActionKind.Discard,
            Params = new() { ["count"] = 5, ["random"] = true }
        };
        sim.Definitions.Store.SkillActionsMutable["review.chain"] = new()
        {
            Id = "review.chain", Kind = ESkillActionKind.ChainActions,
            ActionRefs =
            [
                new() { ActionId = "review.discard", Params = new() { ["count"] = 1, ["random"] = false } },
                new() { ActionId = "review.discard", Params = new() { ["count"] = 2, ["random"] = false, ["allowFewer"] = true } },
                new() { ActionId = "review.discard" }
            ]
        };
        var skill = new SkillDto { Id = "review.skill", ActionRefs = [new() { ActionId = "review.chain" }] };

        Assert.That(DiscardSelection.Resolve(sim, skill, 0), Is.EqualTo(new DiscardSelection(1, 3)));
    }

    [Test]
    public void Discard_selection_cycle_guard_must_preserve_noncyclic_sibling()
    {
        using var sim = Build();
        var skill = AddNestedDiscard(sim, useActions: true);
        sim.Definitions.Store.SkillActionsMutable["review.chain"].ActionRefs.Insert(0, new() { ActionId = "review.chain" });

        Assert.That(DiscardSelection.Resolve(sim, skill, 0), Is.EqualTo(new DiscardSelection(1, 1)));
    }

    #endregion

    #region 显式筛选的奖励边界

    [TestCase(EEffectKind.GainShield)]
    [TestCase(EEffectKind.ModifyDrawCount)]
    [TestCase(EEffectKind.ModifyDrawCountByDiscard)]
    public void Empty_legacy_target_filter_must_not_reward_caster(EEffectKind kind)
    {
        using var sim = Build();
        sim.SetLastDiscardCount(1);
        sim.Definitions.Store.EffectsMutable["review.effect"] = new()
        {
            Id = "review.effect", Kind = kind, Params = BlueTargetParameters()
        };

        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "review.effect" }, sim, Player, [Player]);

        AssertUnrewarded(sim);
    }

    [TestCase(ESkillActionKind.GainShield)]
    [TestCase(ESkillActionKind.ModifyDrawCount)]
    [TestCase(ESkillActionKind.ModifyDrawCountByDiscard)]
    [TestCase(ESkillActionKind.DrawByDiscard)]
    public void Empty_action_target_filter_must_not_reward_caster(ESkillActionKind kind)
    {
        using var sim = Build();
        sim.SetLastDiscardCount(1);
        sim.Definitions.Store.SkillActionsMutable["review.reward"] = new()
        {
            Id = "review.reward", Kind = kind, Params = BlueTargetParameters()
        };

        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "review.reward" }, sim, Player, [Player]);

        AssertUnrewarded(sim);
    }

    [Test]
    public void Explicit_enemy_selector_must_not_fall_back_to_player_shield()
    {
        using var sim = Build();
        sim.Definitions.Store.EffectsMutable["review.effect"] = new()
        {
            Id = "review.effect", Kind = EEffectKind.GainShield,
            Params = new() { ["amount"] = 10, ["hookTargets"] = "allEnemies" }
        };

        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "review.effect" }, sim, Player, [Player]);

        AssertUnrewarded(sim);
    }

    [Test]
    public void Shield_without_selector_must_preserve_source_fallback_for_enemy_targeted_card()
    {
        using var sim = Build();
        sim.Definitions.Store.EffectsMutable["review.effect"] = new()
        {
            Id = "review.effect", Kind = EEffectKind.GainShield, Params = new() { ["amount"] = 10 }
        };

        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "review.effect" }, sim, Player, [Enemy]);

        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.Shield), Is.EqualTo(10));
    }

    #endregion

    #region 魔法普攻受伤倍率

    [TestCase(40, 10, 0, 0, 980)]
    [TestCase(10, 40, 0, 0, 960)]
    [TestCase(40, 10, -0.1f, -0.2f, 992)]
    public void Normal_attack_must_combine_kind_specific_general_and_normal_taken_scales(
        float magicAttack, float physicalAttack, float generalTaken, float normalTaken, float expectedHp)
    {
        using var sim = Build();
        var attacker = sim.PlayerTeam.Characters[0].Asc;
        attacker.SetBaseValue(AttributeIds.MagicAttack, magicAttack);
        attacker.SetBaseValue(AttributeIds.PhysicalAttack, physicalAttack);
        var target = sim.EnemyTeam.Enemies[0].Asc;
        target.SetBaseValue(AttributeIds.MagicDamageTakenScale, -0.5f);
        target.SetBaseValue(AttributeIds.DamageTakenScale, generalTaken);
        target.SetBaseValue(AttributeIds.NormalAttackDamageTakenScale, normalTaken);

        sim.NormalAttacks.Execute(sim);

        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(expectedHp).Within(0.001f));
    }

    #endregion

    #region 免疫的投放边界

    [TestCase(CombatConstants.PoisonTag, "hualing_passive_p1")]
    [TestCase(CombatConstants.SealedTag, "anubis_passive_p1")]
    [TestCase(CombatConstants.VirusTag, "perseus_passive_p1")]
    public void Immune_effect_must_not_execute_apply_or_remove_payload(string tag, string passive)
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player, passive);
        AddDamagingDebuff(sim, tag);

        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy, [Player], "review.debuff");

        Assert.That(sim.PlayerTeam.Characters[0].Asc.Tags.HasTag(tag), Is.False);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000), "免疫应拦截应用与移除钩子的伤害");
    }

    [Test]
    public void Immune_damage_effect_must_not_fall_back_to_untagged_damage()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player, "hualing_passive_p1");
        sim.Definitions.Store.GameplayEffectsMutable["review.poison"] = new()
        {
            Id = "review.poison", DurationPolicy = EDurationPolicy.Instant,
            GrantedTags = [CombatConstants.PoisonTag], Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        sim.Definitions.Store.EffectsMutable["review.hit"] = new()
        {
            Id = "review.hit", Kind = EEffectKind.Damage,
            Params = new() { ["amount"] = 25, ["damageGameplayEffectId"] = "review.poison" }
        };

        sim.EffectExecutor.ExecuteEffectRef(new() { EffectId = "review.hit" }, sim, Enemy, [Player]);

        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
    }

    [Test]
    public void Immune_target_must_not_prevent_effect_on_nonimmune_teammate()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player, "hualing_passive_p1");
        AddDamagingDebuff(sim, CombatConstants.PoisonTag);
        var teammate = new CombatTargetRef(ECombatSide.Player, 1);

        new GameplayEffectApplicator(sim.Definitions).ApplyToTargets(sim, Enemy, [Player, teammate], "review.debuff");

        Assert.That(sim.PlayerTeam.Characters[0].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.False);
        Assert.That(sim.PlayerTeam.Characters[1].Asc.Tags.HasTag(CombatConstants.PoisonTag), Is.True);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(975));
    }

    #endregion

    #region 测试数据

    private static CombatSimulation Build()
    {
        var characters = Enumerable.Range(0, 4).Select(i => CharacterBattleInstance.CreateForTests($"c{i}",
            new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 250, [AttributeIds.MagicAttack] = 40, [AttributeIds.PhysicalAttack] = 10
            },
            drawPile: Enumerable.Range(0, 5).Select(j => new CardRuntimeEntry("xuanyu_thunder_raid", $"card{i}.{j}")),
            activeSkillChain: [new() { SkillId = "review.skill", Cooldown = 5 }], element: EElement.Red)).ToArray();
        characters[0].DrawCards(1);
        var enemies = new[] { new EnemyUnit("e", "unknown", new Dictionary<string, float> { [AttributeIds.MaxHealth] = 1000 }) };
        return new(new PlayerTeamState(characters, 1000), new EnemyTeamState(enemies), new CombatRuleEngine([]),
            BaseGameContent.BuildRegistry(), initialPhase: ECombatPhase.Player);
    }

    private static SkillDto AddNestedDiscard(CombatSimulation sim, bool useActions)
    {
        if (useActions)
        {
            sim.Definitions.Store.SkillActionsMutable["review.discard"] = new()
            {
                Id = "review.discard", Kind = ESkillActionKind.DiscardAndRecord, Params = new() { ["count"] = 1 }
            };
            sim.Definitions.Store.SkillActionsMutable["review.chain"] = new()
            {
                Id = "review.chain", Kind = ESkillActionKind.ChainActions, ActionRefs = [new() { ActionId = "review.discard" }]
            };
            return new() { Id = "review.skill", ActionRefs = [new() { ActionId = "review.chain" }] };
        }
        sim.Definitions.Store.EffectsMutable["review.discard"] = new()
        {
            Id = "review.discard", Kind = EEffectKind.Discard, Params = new() { ["count"] = 1 }
        };
        sim.Definitions.Store.EffectsMutable["review.chain"] = new()
        {
            Id = "review.chain", Kind = EEffectKind.ChainEffects, EffectRefs = [new() { EffectId = "review.discard" }]
        };
        return new() { Id = "review.skill", EffectRefs = [new() { EffectId = "review.chain" }] };
    }

    private static Dictionary<string, object> BlueTargetParameters() => JsonSerializer.Deserialize<Dictionary<string, object>>(
        """{"amount":10,"targetFilter":{"condition":{"kind":"IdentityMatch","params":{"elementAny":["Blue"]}}}}""")!;

    private static void AssertUnrewarded(CombatSimulation sim)
    {
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(character.Asc.GetCurrentValue(AttributeIds.Shield), Is.Zero);
        Assert.That(character.ComputeDrawCount(), Is.EqualTo(1));
        Assert.That(character.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(1));
    }

    private static void AddDamagingDebuff(CombatSimulation sim, string tag)
    {
        sim.Definitions.Store.SkillActionsMutable["review.apply_damage"] = new()
        {
            Id = "review.apply_damage", Kind = ESkillActionKind.ApplyGameplayEffect,
            Params = new() { ["gameplayEffectId"] = "review.damage", ["Amount"] = 25 }
        };
        sim.Definitions.Store.GameplayEffectsMutable["review.damage"] = new()
        {
            Id = "review.damage", DurationPolicy = EDurationPolicy.Instant, Executions = [new() { Kind = "Damage", AttackScale = 0 }]
        };
        sim.Definitions.Store.GameplayEffectsMutable["review.debuff"] = new()
        {
            Id = "review.debuff", DurationPolicy = EDurationPolicy.HasDuration, DurationTurns = 3, GrantedTags = [tag],
            Hooks = new()
            {
                OnApply = [new() { ActionId = "review.apply_damage" }],
                OnRemove = [new() { ActionId = "review.apply_damage" }]
            }
        };
    }

    #endregion
}