using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 魔法伤害落地（2026-09-21）：GAS 公式通道此前固定用 <c>物攻 − 物防</c>，
/// <c>ExecutionDefDto.damageType</c> 字符串没有任何代码读取（战斗规格 §7 后置项）。
/// 现在 <c>damageType</c>/<c>element</c> 解析成伤害包的两维（Kind/Element）：
/// <c>Physical</c> 吃物攻物防、<c>Magical</c> 吃魔攻魔防、<c>Elemental</c> 不吃攻防；
/// 维度经 <c>SharedHpSettlement</c> 传进伤害包管线，规则侧因此区分得开。
/// </summary>
[TestFixture]
public sealed class MagicDamageTests
{
    private const string MagicEffect = "effect.magic_hit";
    private const string PhysicalEffect = "effect.physical_hit";
    private const string ElementalEffect = "effect.elemental_hit";

    #region 公式

    [Test]
    public void Magical_damage_uses_magic_attack_and_magic_defense()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);
        var enemy = sim.EnemyTeam.Enemies[0];

        Execute(sim, MagicEffect, [Enemy(0)]);

        // Amount 0 + 魔攻 30 − 魔防 1 = 29（物防 5 与物攻 10 都不参与）。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 29f }));
        Assert.That(rule.Kinds, Is.EqualTo(new[] { EDamageKind.Magical }), "伤害包必须带魔法维度");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 29f));
    }

    [Test]
    public void Physical_damage_still_uses_physical_attack_and_physical_defense()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, PhysicalEffect, [Enemy(0)]);

        // Amount 0 + 物攻 10 − 物防 5 = 5：回归守卫，魔法落地不得改动物理口径。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 5f }));
        Assert.That(rule.Kinds, Is.EqualTo(new[] { EDamageKind.Physical }));
    }

    [Test]
    public void Elemental_damage_ignores_attack_and_defenses()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, ElementalEffect, [Enemy(0)]);

        // 元素不吃攻防（规格 §1.3）：只有 Amount 10 + 源 Damage 属性 0。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 10f }));
        Assert.That(rule.Kinds, Is.EqualTo(new[] { EDamageKind.Elemental }));
    }

    [Test]
    public void Damage_packet_carries_the_declared_element()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, PhysicalEffect, [Enemy(0)]);

        Assert.That(rule.Elements, Is.EqualTo(new[] { EElement.Blue }),
            "damageType/element 解析出的属性标签要随伤害包广播给规则");
    }

    #endregion

    #region 解析

    [Test]
    public void Legacy_element_only_damage_type_stays_physical_with_that_element()
    {
        // 迁移前的写法（damageType 直接写属性名）必须保持原语义：物理公式 + 该属性。
        Assert.That(
            DamageTypeParser.TryParse("Blue", null, out var legacy, out var error),
            Is.True,
            error);
        Assert.That(legacy.Kind, Is.EqualTo(EDamageKind.Physical));
        Assert.That(legacy.Element, Is.EqualTo(EElement.Blue));
    }

    [Test]
    public void Explicit_damage_type_and_element_are_combined()
    {
        Assert.That(
            DamageTypeParser.TryParse("Magical", "Blue,Green", out var spec, out var error),
            Is.True,
            error);
        Assert.That(spec.Kind, Is.EqualTo(EDamageKind.Magical));
        Assert.That(spec.Element, Is.EqualTo(EElement.Blue | EElement.Green));
    }

    [Test]
    public void Unknown_damage_type_or_element_is_rejected()
    {
        Assert.That(DamageTypeParser.TryParse("Magicka", null, out _, out var kindError), Is.False);
        Assert.That(kindError, Does.Contain("Magicka"));

        Assert.That(DamageTypeParser.TryParse("Physical", "Purple", out _, out var elementError), Is.False);
        Assert.That(elementError, Does.Contain("Purple"));
    }

    [Test]
    public void Empty_damage_type_defaults_to_physical_without_element()
    {
        Assert.That(DamageTypeParser.TryParse(null, null, out var spec, out _), Is.True);
        Assert.That(spec, Is.EqualTo(DamageTypeSpec.Default));
    }

    #endregion

    #region 内容准入

    [Test]
    public void Validator_rejects_unparsable_damage_type_and_element()
    {
        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            ("ge.bad_kind", new GameplayEffectDefDto
            {
                Id = "ge.bad_kind",
                DurationPolicy = EDurationPolicy.Instant,
                Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Magicka" }],
            }),
            ("ge.bad_element", new GameplayEffectDefDto
            {
                Id = "ge.bad_element",
                DurationPolicy = EDurationPolicy.Instant,
                Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Magical", Element = "Purple" }],
            }),
            ("ge.good", new GameplayEffectDefDto
            {
                Id = "ge.good",
                DurationPolicy = EDurationPolicy.Instant,
                Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Magical", Element = "Blue" }],
            })));

        var messages = errors
            .Where(error => error.Category == EContentCategory.GameplayEffect)
            .Select(error => $"{error.DefinitionId}: {error.Message}")
            .ToList();

        Assert.That(
            messages,
            Has.Some.Contains("ge.bad_kind"),
            "写错的 damageType 不能静默退化成物理伤害，必须在内容准入阶段被拒：" + string.Join("; ", messages));
        Assert.That(messages, Has.Some.Contains("ge.bad_element"));
        Assert.That(messages.Any(message => message.Contains("ge.good")), Is.False, "合法声明不得误报");
    }

    #endregion

    #region 装配

    private sealed class RecordingRule : ICombatRule
    {
        public string Id => "test.magic_recording";

        public int Priority => 0;

        public List<float> BeforeAmounts { get; } = [];

        public List<EDamageKind> Kinds { get; } = [];

        public List<EElement> Elements { get; } = [];

        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet)
        {
            BeforeAmounts.Add(packet.Amount);
            Kinds.Add(packet.Kind);
            Elements.Add(packet.Element);
        }

        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet)
        {
        }
    }

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static CombatSimulation Build(ICombatRule? rule)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                [MagicEffect] = DamageEffect(MagicEffect, "ge.magic", amount: 0),
                [PhysicalEffect] = DamageEffect(PhysicalEffect, "ge.physical", amount: 0),
                [ElementalEffect] = DamageEffect(ElementalEffect, "ge.elemental", amount: 10),
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["ge.magic"] = DamageGameplayEffect("ge.magic", "Magical", "Blue"),
                // 旧写法：damageType 直接写属性名 → 物理公式 + 蓝属性。
                ["ge.physical"] = DamageGameplayEffect("ge.physical", "Blue", null),
                ["ge.elemental"] = DamageGameplayEffect("ge.elemental", "Elemental", "Red"),
            });

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MagicAttack] = 30f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests($"c{index}", attrs))
            .ToArray();
        var enemy = new EnemyUnit("e0", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 100f,
            [AttributeIds.PhysicalDefense] = 5f,
            [AttributeIds.MagicDefense] = 1f,
        });

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState([enemy]),
            new CombatRuleEngine(rule is null ? [] : [rule]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260921);
    }

    private static EffectDto DamageEffect(string id, string gameplayEffectId, int amount) => new()
    {
        Id = id,
        Kind = EEffectKind.Damage,
        Params = new Dictionary<string, object>
        {
            ["amount"] = amount,
            ["damageGameplayEffectId"] = gameplayEffectId,
        },
    };

    private static GameplayEffectDefDto DamageGameplayEffect(string id, string damageType, string? element) => new()
    {
        Id = id,
        DurationPolicy = EDurationPolicy.Instant,
        Executions =
        [
            new ExecutionDefDto
            {
                Kind = "Damage",
                DamageType = damageType,
                Element = element ?? "",
            },
        ],
    };

    private static void Execute(CombatSimulation simulation, string effectId, IReadOnlyList<CombatTargetRef> targets) =>
        simulation.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = effectId },
            simulation,
            new CombatTargetRef(ECombatSide.Player, 0),
            targets);

    /// <summary>直接构造 store，绕开 Rebuild 的「先校验后剔除」，以便断言校验器本身的行为。</summary>
    private static GameDefinitionStore StoreWith(
        params (string Id, GameplayEffectDefDto Dto)[] gameplayEffects)
    {
        var store = new GameDefinitionStore();
        foreach (var (id, dto) in gameplayEffects)
            store.GameplayEffectsMutable[id] = dto;

        return store;
    }

    #endregion
}
