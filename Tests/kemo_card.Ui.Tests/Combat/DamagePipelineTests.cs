using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 伤害包管线统一（2026-09-19）：GAS 公式通道、直伤定值通道、充能球通道
/// 一律 先 <c>OnBeforeDamage</c>（可改数额 / 可完全抵消）→ 写入 → 后 <c>OnAfterDamage</c>（只读观测），
/// 且三条通道的目标受伤倍率口径一致。
/// </summary>
[TestFixture]
public sealed class DamagePipelineTests
{
    private const int SharedMaxHp = 100;
    private const string GeHitEffect = "effect.ge_hit";
    private const string RawHitEffect = "effect.raw_hit";

    #region GAS 通道

    [Test]
    public void Gas_damage_on_enemy_goes_through_before_and_after_hooks()
    {
        var rule = new RecordingRule { OverrideAmount = 1f };
        using var sim = Build(rule);
        var enemy = sim.EnemyTeam.Enemies[0];

        Execute(sim, GeHitEffect, [new CombatTargetRef(ECombatSide.Enemy, 0)]);

        // GAS 公式：Amount 10 + 100% 源物攻 10 − 目标物防 0 = 20；规则压到 1 后写入不得回到 20。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 20f }), "GAS 伤害必须进 OnBeforeDamage");
        Assert.That(rule.AfterAmounts, Is.EqualTo(new[] { 1f }), "OnAfterDamage 观测到的是最终写入数额");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(99f), "规则修正后的数额才写进血量");
    }

    [Test]
    public void Gas_damage_on_player_slot_obeys_slot_reduction_rules()
    {
        // 归档计划（2026-07-28 战斗规格对齐）L422 的契约：玩家槽位伤害跑 DispatchBeforeDamage，
        // 槽位护盾/减伤在此参与，之后结果扣共享账本。
        var rule = new RecordingRule { OverrideAmount = 4f };
        using var sim = Build(rule, sharedMaxHp: SharedMaxHp);

        Execute(sim, GeHitEffect, [new CombatTargetRef(ECombatSide.Player, 1)]);

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 20f }), "GAS 公式进入规则的是含 100% 物攻的数额");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - 4), "账本扣的是规则修正后的数额");
    }

    #endregion

    #region 直伤通道

    [Test]
    public void Direct_damage_multiplies_target_taken_scale_per_target()
    {
        using var sim = Build(rule: null, enemyCount: 2);
        var plain = sim.EnemyTeam.Enemies[0];
        var vulnerable = sim.EnemyTeam.Enemies[1];
        vulnerable.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 1f);

        Execute(sim, RawHitEffect, [new CombatTargetRef(ECombatSide.Enemy, 0), new CombatTargetRef(ECombatSide.Enemy, 1)]);

        Assert.That(plain.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(90f), "无受伤倍率 → 10");
        Assert.That(vulnerable.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(80f), "受伤倍率 +100% → 20（逐目标算）");
    }

    [Test]
    public void Ledger_direct_damage_uses_team_asc_taken_scale()
    {
        using var sim = Build(rule: null);
        sim.PlayerTeam.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 1f);

        Execute(sim, RawHitEffect, [CombatTargetRef.PlayerTeam]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(SharedMaxHp - 20), "账本目标取队伍 ASC 的受伤倍率");
    }

    [Test]
    public void Direct_damage_broadcasts_after_damage()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, RawHitEffect, [new CombatTargetRef(ECombatSide.Enemy, 0)]);

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 10f }));
        Assert.That(rule.AfterAmounts, Is.EqualTo(new[] { 10f }));
    }

    #endregion

    #region 充能球通道

    [Test]
    public void Orb_damage_goes_through_the_pipeline_per_orb()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule, orbTypes: BlueOrb());
        var enemy = sim.EnemyTeam.Enemies[0];

        sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);

        // 单球 = 6 + 100%×物攻 10 = 16；三球逐个结算 → 三次包。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 16f, 16f, 16f }));
        Assert.That(rule.AfterAmounts, Is.EqualTo(new[] { 16f, 16f, 16f }));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 48f));
    }

    [Test]
    public void Fully_cancelled_damage_writes_nothing_and_does_not_broadcast_after()
    {
        var rule = new RecordingRule { OverrideAmount = 0f };
        using var sim = Build(rule, orbTypes: BlueOrb());
        var enemy = sim.EnemyTeam.Enemies[0];

        sim.Orbs.Grant(sim, BuiltinOrbTypes.Blue, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);

        Assert.That(rule.BeforeAmounts, Has.Count.EqualTo(3), "规则看得到每次球的伤害包");
        Assert.That(rule.AfterAmounts, Is.Empty, "完全抵消不产生伤害事件");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f), "血量不变");
    }

    #endregion

    #region 装配

    private sealed class RecordingRule : ICombatRule
    {
        public string Id => "test.recording";

        public int Priority => 0;

        public List<float> BeforeAmounts { get; } = [];

        public List<float> AfterAmounts { get; } = [];

        /// <summary>非 null 时把每次伤害压成该数额（0 = 完全抵消）。</summary>
        public float? OverrideAmount { get; init; }

        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet)
        {
            BeforeAmounts.Add(packet.Amount);
            if (OverrideAmount is { } amount)
                packet.Amount = amount;
        }

        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet) => AfterAmounts.Add(packet.Amount);
    }

    private static Dictionary<string, OrbTypeDto> BlueOrb() => new(StringComparer.Ordinal)
    {
        [BuiltinOrbTypes.Blue] = new()
        {
            Id = BuiltinOrbTypes.Blue,
            DisplayNameId = "orb.blue.name",
            DamageKind = EDamageKind.Elemental,
            Element = EElement.Blue,
            PerOrbAmount = 6f,
            AttackBonusScale = 1f,
            AttackSource = EOrbAttackSource.Higher,
        },
    };

    private static CombatSimulation Build(
        ICombatRule? rule,
        int sharedMaxHp = SharedMaxHp,
        int enemyCount = 1,
        IReadOnlyDictionary<string, OrbTypeDto>? orbTypes = null)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                [GeHitEffect] = new()
                {
                    Id = GeHitEffect,
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object>
                    {
                        ["amount"] = 10,
                        ["damageGameplayEffectId"] = "ge.hit",
                    },
                },
                [RawHitEffect] = new()
                {
                    Id = RawHitEffect,
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 10 },
                },
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["ge.hit"] = new()
                {
                    Id = "ge.hit",
                    DurationPolicy = EDurationPolicy.Instant,
                    Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Physical" }],
                },
            },
            orbs: orbTypes);

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests($"c{index}", attrs))
            .ToArray();
        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", maxHp: 100))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp),
            new EnemyTeamState(enemies),
            new CombatRuleEngine(rule is null ? [] : [rule]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260919);
    }

    private static void Execute(CombatSimulation simulation, string effectId, IReadOnlyList<CombatTargetRef> targets) =>
        simulation.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = effectId },
            simulation,
            new CombatTargetRef(ECombatSide.Player, 0),
            targets);

    #endregion
}
