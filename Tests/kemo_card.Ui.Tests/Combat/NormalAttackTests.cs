using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 普通攻击：槽位轮转（(回合-1) % 队伍人数）、物/魔取值与减对应防御、
/// 全元素带出、吃增伤/受伤倍率与规则管线、不吃连携、与"打出牌"相关机制完全解耦。
/// </summary>
[TestFixture]
public sealed class NormalAttackTests
{
    #region 槽位轮转

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(3, 2)]
    [TestCase(4, 3)]
    [TestCase(5, 0)]
    [TestCase(8, 3)]
    [TestCase(9, 0)]
    public void Slot_rotates_by_turn_number(int turnNumber, int expectedSlot)
    {
        Assert.That(
            NormalAttackRuntime.ResolveSlotIndex(turnNumber, characterCount: 4),
            Is.EqualTo(expectedSlot),
            $"第 {turnNumber} 回合应归槽位 {expectedSlot + 1}");
    }

    [Test]
    public void Slot_rotation_uses_the_simulation_turn_number()
    {
        using var sim = Build();
        Assert.That(sim.NormalAttacks.ResolveSlotIndex(sim), Is.Zero, "第 1 回合");

        sim.IncrementTurnNumber();
        Assert.That(sim.NormalAttacks.ResolveSlotIndex(sim), Is.EqualTo(1), "第 2 回合");
    }

    [Test]
    public void Slot_rotation_respects_actual_team_size()
    {
        Assert.That(NormalAttackRuntime.ResolveSlotIndex(turnNumber: 4, characterCount: 3), Is.Zero);
        Assert.That(NormalAttackRuntime.ResolveSlotIndex(turnNumber: 5, characterCount: 3), Is.EqualTo(1));
        Assert.That(NormalAttackRuntime.ResolveSlotIndex(turnNumber: 1, characterCount: 0), Is.EqualTo(-1));
    }

    #endregion

    #region 类型与数值

    [Test]
    public void Physical_when_physical_attack_is_higher_and_defense_subtracts()
    {
        using var sim = Build(attacks: new[] { (20f, 5f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.PhysicalDefense, 6f);
        enemy.Asc.SetBaseValue(AttributeIds.MagicDefense, 99f);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Kind, Is.EqualTo(EDamageKind.Physical));
        Assert.That(result.SlotIndex, Is.Zero, "第 1 回合归槽位 1");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(300f - (20f - 6f)).Within(0.001f));
    }

    [Test]
    public void Magical_when_magic_attack_is_higher_and_magic_defense_subtracts()
    {
        // 魔攻更高 → 走魔法分支，减的是魔防（MagicDefense 自本次起第一次有消费方）。
        using var sim = Build(attacks: new[] { (5f, 30f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.PhysicalDefense, 99f);
        enemy.Asc.SetBaseValue(AttributeIds.MagicDefense, 8f);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.Kind, Is.EqualTo(EDamageKind.Magical));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(300f - (30f - 8f)).Within(0.001f));
    }

    [Test]
    public void Equal_attacks_resolve_to_physical_deterministically()
    {
        using var sim = Build(attacks: new[] { (10f, 10f), (1f, 1f), (1f, 1f), (1f, 1f) });

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.Kind, Is.EqualTo(EDamageKind.Physical), "平手取物理，保证确定性");
    }

    [Test]
    public void Damage_never_goes_negative_when_defense_exceeds_attack()
    {
        using var sim = Build(attacks: new[] { (5f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.PhysicalDefense, 50f);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.TotalDamage, Is.Zero);
        Assert.That(result.TargetCount, Is.Zero, "被完全防住时不产生伤害包");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(300f));
    }

    [Test]
    public void Attack_targets_every_alive_enemy_and_skips_dead_ones()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) }, enemyCount: 3);
        var dead = sim.EnemyTeam.Enemies[2];
        dead.ApplyDamage(dead.MaxHp);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.TargetCount, Is.EqualTo(2), "两个存活敌人各一次");
        Assert.That(result.TotalDamage, Is.EqualTo(20f).Within(0.001f));
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(290f).Within(0.001f));
        Assert.That(sim.EnemyTeam.Enemies[1].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(290f).Within(0.001f));
        Assert.That(dead.Asc.GetCurrentValue(AttributeIds.Health), Is.Zero, "已阵亡敌人不再吃伤害");
    }

    #endregion

    #region 元素 / 增伤 / 规则

    [Test]
    public void Attack_carries_all_of_the_attackers_elements()
    {
        using var sim = Build(
            attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) },
            elements: new[] { EElement.Red | EElement.Blue, EElement.None, EElement.None, EElement.None });

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.Element, Is.EqualTo(EElement.Red | EElement.Blue), "多元素角色全部生效");
    }

    [Test]
    public void Dealt_and_taken_scales_apply()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 0.5f);
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 1f);

        var result = sim.NormalAttacks.Execute(sim);

        // 10 × (1 + 0.5 增伤 + 1 受伤增加) = 25：两者同桶加算（规格「一律加算」）。
        Assert.That(result!.TotalDamage, Is.EqualTo(25f).Within(0.001f));
    }

    [Test]
    public void Chain_bonus_does_not_apply()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        sim.SetChainBonus(1f);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result!.TotalDamage, Is.EqualTo(10f).Within(0.001f), "普攻不是卡，不吃连携");
    }

    [Test]
    public void Damage_goes_through_the_rule_pipeline()
    {
        var rule = new RecordingRule { OverrideAmount = 3f };
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) }, rule: rule);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 10f }));
        Assert.That(rule.AfterAmounts, Is.EqualTo(new[] { 3f }));
        Assert.That(rule.LastKind, Is.EqualTo(EDamageKind.Physical));
        Assert.That(result!.TotalDamage, Is.EqualTo(3f).Within(0.001f), "规则修正后的数额才是最终伤害");
    }

    #endregion

    #region 解耦与流程

    [Test]
    public void Attack_does_not_trigger_slot_buffs_or_consume_charge()
    {
        var charge = new BuffDto
        {
            Id = "buff.charge",
            DurationType = EBuffDurationType.Turns,
            Duration = 3,
            StackRule = EBuffStackRule.Replace,
            Tags = [BuiltinBuffTags.SlotCharge],
            Hooks = new BuffEffectHooksDto
            {
                OnSlotCardPlayed = [new EffectRefDto { EffectId = "effect.charge_load" }],
            },
        };
        using var sim = Build(
            attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) },
            buffs: new Dictionary<string, BuffDto> { [charge.Id] = charge },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.charge_load"] = new()
                {
                    Id = "effect.charge_load",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 7 },
                },
            });
        var slot = sim.PlayerTeam.Characters[0].HandSlots[0];
        sim.Buffs.ApplyToSlot(sim, 0, 0, charge.Id);

        sim.NormalAttacks.Execute(sim);

        var instance = slot.Buffs.Find(charge.Id);
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.ChargeCounter, Is.EqualTo(1), "普攻不算打出牌，充能计数不变");
    }

    [Test]
    public void Attack_does_not_take_the_characters_action_or_energy()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var character = sim.PlayerTeam.Characters[0];
        character.RefillAvailableEnergy();
        var energyBefore = character.AvailableEnergy;

        sim.NormalAttacks.Execute(sim);

        Assert.That(character.HasActed, Is.False, "普攻不占已行动");
        Assert.That(character.AvailableEnergy, Is.EqualTo(energyBefore), "普攻不消耗能量");
    }

    [Test]
    public void Sealed_character_still_attacks()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var character = sim.PlayerTeam.Characters[0];
        ApplySeal(character);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(character.IsSealed, Is.True, "封印只挡出牌");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TotalDamage, Is.EqualTo(10f).Within(0.001f));
        Assert.That(character.HasActed, Is.False, "普攻不占已行动");
    }

    [Test]
    public void Attack_is_not_counted_as_a_played_card()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });

        sim.NormalAttacks.Execute(sim);

        Assert.That(sim.TakePlayedThisTurn(), Is.Empty, "普攻不计入回合结束产球的卡牌统计口径");
    }

    [Test]
    public void No_alive_enemy_still_counts_as_executed_without_damage()
    {
        using var sim = Build(attacks: new[] { (10f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) });
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.ApplyDamage(enemy.MaxHp);

        var result = sim.NormalAttacks.Execute(sim);

        Assert.That(result, Is.Not.Null, "无存活目标也算执行过（消费本回合机会）");
        Assert.That(result!.TargetCount, Is.Zero);
        Assert.That(result.TotalDamage, Is.Zero);
        Assert.That(sim.NormalAttacks.ExecutionCount, Is.EqualTo(1));
    }

    [Test]
    public void Card_execution_phase_runs_the_attack_before_end_condition_check()
    {
        using var sim = Build(
            attacks: new[] { (300f, 0f), (1f, 1f), (1f, 1f), (1f, 1f) },
            rules: [new AllEnemiesDefeatedVictoryRule()]);
        sim.TransitionTo(ECombatPhase.CardExecution);

        sim.AdvancePhase();

        Assert.That(sim.NormalAttacks.LastResult, Is.Not.Null, "卡牌执行阶段末尾必执行普攻");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory), "普攻击杀最后一名敌人 → 本回合敌人不再行动");
    }

    #endregion

    #region 装配

    private sealed class RecordingRule : ICombatRule
    {
        public string Id => "test.normal_attack_recording";

        public int Priority => 0;

        public List<float> BeforeAmounts { get; } = [];

        public List<float> AfterAmounts { get; } = [];

        public EDamageKind LastKind { get; private set; }

        public EElement LastElement { get; private set; }

        public float? OverrideAmount { get; init; }

        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet)
        {
            BeforeAmounts.Add(packet.Amount);
            LastKind = packet.Kind;
            LastElement = packet.Element;
            if (OverrideAmount is { } amount)
                packet.Amount = amount;
        }

        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet) => AfterAmounts.Add(packet.Amount);
    }

    private static void ApplySeal(CharacterBattleInstance character)
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.test.seal",
            DurationPolicy = EDurationPolicy.Infinite,
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
            GrantedTags = [CombatConstants.SealedTag],
        };
        var result = character.Asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: character.Asc));
        Assert.That(result.Success, Is.True);
    }

    private static CombatSimulation Build(
        int characterCount = 4,
        int enemyCount = 1,
        (float Physical, float Magic)[]? attacks = null,
        EElement[]? elements = null,
        ICombatRule? rule = null,
        IEnumerable<ICombatRule>? rules = null,
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null)
    {
        var registry = CombatTestHelper.CreateFullRegistry(buffs: buffs, effects: effects);
        var characters = Enumerable.Range(0, characterCount)
            .Select(index =>
            {
                var attack = attacks is not null && index < attacks.Length ? attacks[index] : (Physical: 10f, Magic: 0f);
                return CharacterBattleInstance.CreateForTests(
                    $"c{index}",
                    new Dictionary<string, float>(StringComparer.Ordinal)
                    {
                        [AttributeIds.MaxHealth] = 50f,
                        [AttributeIds.PhysicalAttack] = attack.Physical,
                        [AttributeIds.MagicAttack] = attack.Magic,
                        [AttributeIds.MaxEnergy] = 10f,
                        [AttributeIds.InitialEnergy] = 10f,
                    },
                    element: elements is not null && index < elements.Length ? elements[index] : EElement.None);
            })
            .ToArray();

        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", maxHp: 300))
            .ToArray();

        var ruleList = rules is not null
            ? rules.ToList()
            : rule is null ? [] : [rule];

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState(enemies),
            new CombatRuleEngine(ruleList),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260920);
    }

    #endregion
}
