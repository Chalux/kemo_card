using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Ui.Tests.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 攻击次数（2026-09-27）：伤害执行不再恒为"每个目标结算一次"，而是按内容参数决定**总次数**——
/// <list type="bullet">
/// <item><c>AttackCount</c>（整数）= 固定次数；</item>
/// <item><c>AttackCountByChain</c>（布尔）= 当前连携参与人数（无连携回落 1）；</item>
/// <item><c>AttackCountMinusDiscard</c>（整数 = 基准次数）= 基准 − 已弃牌数（下限 1）。</item>
/// </list>
/// 次数经 <c>setByCaller["AttackCount"]</c> 传进 <c>DamageExecution</c>，逐次结算：
/// 每次都是一次独立的伤害事件（逐次过规则 / 护盾 / 受击钩子，表现层逐次记账），数额逐次相同。
/// </summary>
[TestFixture]
public sealed class DamageAttackCountTests
{
    private const string HitAction = "action.hit";
    private const string DiscardAction = "action.discard";
    private const string DamageEffectId = "ge.hit";
    private const string DirectEffect = "effect.direct";
    private const string TestCard = "card.test_attack_count";

    /// <summary>单次命中数额：Amount 5 + 魔攻 30 − 魔防 1 = 34（每次完全相同）。</summary>
    private const float SingleHit = 34f;

    private const float EnemyMaxHealth = 500f;

    #region 固定次数

    [Test]
    public void Attack_count_applies_three_identical_hits()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCount", 3));

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit, SingleHit, SingleHit }),
            "逐次结算：每次都是一次独立伤害事件，数额逐次相同");
        Assert.That(DamageEvents(sim), Has.Count.EqualTo(3), "表现层每次命中各记一条伤害事件");
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - (3 * SingleHit)).Within(0.001f));
    }

    [Test]
    public void Attack_count_applies_per_target()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule, enemyCount: 2);

        Execute(sim, HitAction, [Enemy(0), Enemy(1)], ("Amount", 5), ("AttackCount", 2));

        // 次数是"每个目标各打 N 次"：两个目标各 2 次，共 4 次结算。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit, SingleHit, SingleHit, SingleHit }));
        foreach (var enemy in sim.EnemyTeam.Enemies)
        {
            Assert.That(
                enemy.Asc.GetCurrentValue(AttributeIds.Health),
                Is.EqualTo(EnemyMaxHealth - (2 * SingleHit)).Within(0.001f));
        }
    }

    [Test]
    public void Attack_count_zero_deals_no_damage()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCount", 0));

        Assert.That(rule.BeforeAmounts, Is.Empty, "显式声明 0 次 = 本次执行不造成伤害");
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth).Within(0.001f));
    }

    [Test]
    public void Damage_without_any_attack_count_param_applies_exactly_once()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5));

        // 回归守卫：没声明次数时与历史行为逐字节一致（每次执行只结算一次）。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit }));
        Assert.That(DamageEvents(sim), Has.Count.EqualTo(1));
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - SingleHit).Within(0.001f));
    }

    [Test]
    public void Direct_damage_fallback_honours_the_attack_count()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        // 直伤通道（没有 damageGameplayEffectId）：与 GAS 公式通道同口径。
        sim.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto
            {
                EffectId = DirectEffect,
                Params = new Dictionary<string, object> { ["AttackCount"] = 3 },
            },
            sim,
            Player(0),
            [Enemy(0)]);

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { 7f, 7f, 7f }));
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - 21f).Within(0.001f));
    }

    #endregion

    #region 按弃牌数递减

    [Test]
    public void Attack_count_minus_discard_subtracts_the_discarded_cards()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule, handSize: 3);

        Discard(sim, 1);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(1), "弃牌数必须记在模拟上（另一条并行特性）");

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCountMinusDiscard", 3));

        // 基准 3 − 弃 1 = 2 次。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit, SingleHit }));
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - (2 * SingleHit)).Within(0.001f));
    }

    [Test]
    public void Attack_count_minus_discard_floors_at_one()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule, handSize: 3);

        Discard(sim, 3);
        Assert.That(sim.LastDiscardCount, Is.EqualTo(3));

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCountMinusDiscard", 3));

        // 基准 3 − 弃 3 = 0 → 下限 1 次（弃得再多也至少打一下）。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit }));
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - SingleHit).Within(0.001f));
    }

    #endregion

    #region 按连携人数

    [Test]
    public void Attack_count_by_chain_uses_the_participant_count()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);
        SetChain(sim, EElement.Blue, participants: 2);

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCountByChain", true));

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit, SingleHit }), "2 人连携 = 打 2 次");
        Assert.That(
            sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(EnemyMaxHealth - (2 * SingleHit)).Within(0.001f));
    }

    [Test]
    public void Attack_count_by_chain_without_a_chain_applies_once()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);

        // 结算区间之外连携为空 → 人数 0 → 下限 1。
        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCountByChain", true));

        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit }));
    }

    [Test]
    public void Fixed_attack_count_wins_over_the_chain_count()
    {
        var rule = new RecordingRule();
        using var sim = Build(rule);
        SetChain(sim, EElement.Blue, participants: 4);

        Execute(sim, HitAction, [Enemy(0)], ("Amount", 5), ("AttackCount", 2), ("AttackCountByChain", true));

        // 固定次数最具体：两个都声明时以固定值为准（内容只应声明一个）。
        Assert.That(rule.BeforeAmounts, Is.EqualTo(new[] { SingleHit, SingleHit }));
    }

    #endregion

    #region GAS 公式通道

    [Test]
    public void DamageExecution_repeats_the_hit_per_set_by_caller_attack_count()
    {
        var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var target = GasTestHelper.CreateAscWithAttributes(
            (AttributeIds.PhysicalDefense, 3f),
            (AttributeIds.Health, 100f));
        var hits = new List<float>();
        var spec = new GameplayEffectSpec(
            CreateDamageGe(),
            sourceAsc: source,
            targetAsc: target,
            setByCaller: new Dictionary<string, float>
            {
                ["Amount"] = 6f,
                [DamageExecution.SetByCallerAttackCount] = 3f,
            },
            damageReceiver: (_, amount) => hits.Add(amount));

        new ExecutionRunner().Run(spec, target);

        // 6 + 物攻 10 − 物防 3 = 13，逐次回调（宿主据此逐次走伤害管线）。
        Assert.That(hits, Is.EqualTo(new[] { 13f, 13f, 13f }));
    }

    [Test]
    public void DamageExecution_without_the_key_keeps_the_single_hit()
    {
        var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var target = GasTestHelper.CreateAscWithAttributes(
            (AttributeIds.PhysicalDefense, 3f),
            (AttributeIds.Health, 100f));
        var hits = new List<float>();
        var spec = new GameplayEffectSpec(
            CreateDamageGe(),
            sourceAsc: source,
            targetAsc: target,
            setByCaller: new Dictionary<string, float> { ["Amount"] = 6f },
            damageReceiver: (_, amount) => hits.Add(amount));

        new ExecutionRunner().Run(spec, target);

        Assert.That(hits, Is.EqualTo(new[] { 13f }));
    }

    #endregion

    #region 内容准入

    [Test]
    public void Validator_rejects_malformed_attack_count_params()
    {
        var store = new GameDefinitionStore();
        store.GameplayEffectsMutable[DamageEffectId] = DamageGameplayEffect();
        store.SkillActionsMutable["action.bad_int"] = AttackCountAction("action.bad_int", ("AttackCount", "three"));
        store.SkillActionsMutable["action.negative"] =
            AttackCountAction("action.negative", ("AttackCountMinusDiscard", -1));
        store.SkillActionsMutable["action.bad_bool"] =
            AttackCountAction("action.bad_bool", ("AttackCountByChain", "yes"));
        store.SkillActionsMutable["action.good"] = AttackCountAction(
            "action.good",
            ("AttackCount", 2),
            ("AttackCountMinusDiscard", 3),
            ("AttackCountByChain", false));

        var messages = new ContentDefinitionValidator()
            .Validate(store)
            .Where(error => error.Category == EContentCategory.SkillAction)
            .Select(error => $"{error.DefinitionId}: {error.Message}")
            .ToList();

        // 次数参数在运行期是"静默退化"（打 3 次变成打 1 次），必须在内容准入阶段拦下。
        Assert.That(messages, Has.Some.Contains("action.bad_int: params.AttackCount must be an integer."));
        Assert.That(messages, Has.Some.Contains("action.negative: params.AttackCountMinusDiscard must be >= 0."));
        Assert.That(messages, Has.Some.Contains("action.bad_bool: params.AttackCountByChain must be a boolean."));
        Assert.That(
            messages.Any(message => message.Contains("action.good")),
            Is.False,
            "合法声明不得误报：" + string.Join("; ", messages));
    }

    #endregion

    #region 装配

    private sealed class RecordingRule : ICombatRule
    {
        public string Id => "test.attack_count_recording";

        public int Priority => 0;

        public List<float> BeforeAmounts { get; } = [];

        public void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) => BeforeAmounts.Add(packet.Amount);

        public void OnAfterDamage(CombatContext ctx, in DamagePacket packet)
        {
        }
    }

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    /// <summary>本回合该属性的连携人头数快照（与状态机结算开始时写入的口径一致）。</summary>
    private static void SetChain(CombatSimulation simulation, EElement element, int participants)
    {
        simulation.SetChainCounts(new Dictionary<EElement, int> { [element] = participants });
        // 连携人头数按"当前结算卡"的属性位取（0 = 用当前卡属性），因此这里同时定档当前卡属性。
        simulation.SetChainCardElementFlags((int)element);
    }

    /// <summary>走内容通道（技能动作）执行一次伤害，次数参数由调用方经 <c>actionRef.params</c> 下传。</summary>
    private static void Execute(
        CombatSimulation simulation,
        string actionId,
        IReadOnlyList<CombatTargetRef> targets,
        params (string Key, object Value)[] parameters)
    {
        simulation.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto
            {
                ActionId = actionId,
                Params = parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            },
            simulation,
            Player(0),
            targets);
    }

    /// <summary>弃 <paramref name="count"/> 张手牌（走内容通道的 DiscardAndRecord 技能动作，通道为 Other）。</summary>
    private static void Discard(CombatSimulation simulation, int count) =>
        Execute(simulation, DiscardAction, [Player(0)], ("count", count));

    private static IReadOnlyList<DamageDealtEvent> DamageEvents(CombatSimulation simulation) =>
        [.. simulation.Presentation.Drain().OfType<DamageDealtEvent>()];

    private static CombatSimulation Build(ICombatRule? rule, int enemyCount = 1, int handSize = 0)
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>(StringComparer.Ordinal)
            {
                // 不带 skillRefs：本用例只关心手牌数量，不需要卡牌能被打出。
                [TestCard] = new()
                {
                    Id = TestCard,
                    DisplayNameId = TestCard,
                    Element = (int)EElement.Blue,
                    CardType = ECardType.Magical,
                    TargetSide = ETargetSide.Enemy,
                    TargetScope = ETargetScope.Single,
                    TargetCount = 1,
                    Priority = 1,
                },
            },
            effects: new Dictionary<string, EffectDto>(StringComparer.Ordinal)
            {
                [DirectEffect] = new()
                {
                    Id = DirectEffect,
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 7 },
                },
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal)
            {
                [DamageEffectId] = DamageGameplayEffect(),
            },
            skillActions: new Dictionary<string, SkillActionDto>(StringComparer.Ordinal)
            {
                [HitAction] = new()
                {
                    Id = HitAction,
                    Kind = ESkillActionKind.ApplyGameplayEffect,
                    Params = new Dictionary<string, object> { ["gameplayEffectId"] = DamageEffectId },
                },
                [DiscardAction] = new()
                {
                    // 只有 DiscardAndRecord 会把实际弃置张数登记到 LastDiscardCount（Discard 保持不记账）。
                    Id = DiscardAction,
                    Kind = ESkillActionKind.DiscardAndRecord,
                    Params = new Dictionary<string, object> { ["count"] = 1, ["random"] = true },
                },
            });

        var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MagicAttack] = 30f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                $"c{index}",
                attributes,
                drawPile: Enumerable.Range(0, Math.Max(handSize, 1))
                    .Select(slot => new CardRuntimeEntry(TestCard, $"rt-c{index}-{slot}"))))
            .ToArray();
        if (handSize > 0)
        {
            foreach (var character in characters)
                character.DrawCards(handSize);
        }

        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = EnemyMaxHealth,
                [AttributeIds.PhysicalDefense] = 5f,
                [AttributeIds.MagicDefense] = 1f,
            }))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine(rule is null ? [] : [rule]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260927);
    }

    /// <summary>魔伤 GE：<c>Amount</c> 由调用方给，公式 = Amount + 魔攻 − 魔防。</summary>
    private static GameplayEffectDefDto DamageGameplayEffect() => new()
    {
        Id = DamageEffectId,
        DurationPolicy = EDurationPolicy.Instant,
        Executions =
        [
            new ExecutionDefDto
            {
                Kind = DamageExecution.DamageKind,
                DamageType = "Magical",
                Element = "Blue",
            },
        ],
    };

    /// <summary>物理伤害 GE（GAS 层单测用：物攻 10 − 物防 3）。</summary>
    private static GameplayEffectDefDto CreateDamageGe() => new()
    {
        Id = "ge.test.attack_count",
        DurationPolicy = EDurationPolicy.Instant,
        Executions = [new ExecutionDefDto { Kind = DamageExecution.DamageKind, DamageType = "Physical" }],
    };

    private static SkillActionDto AttackCountAction(
        string id,
        params (string Key, object Value)[] parameters)
    {
        var actionParams = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["gameplayEffectId"] = DamageEffectId,
        };
        foreach (var (key, value) in parameters)
            actionParams[key] = value;

        return new SkillActionDto
        {
            Id = id,
            Kind = ESkillActionKind.ApplyGameplayEffect,
            Params = actionParams,
        };
    }

    #endregion
}