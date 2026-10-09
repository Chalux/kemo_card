using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 充能球触发批次条件（<c>OrbTriggered</c>）、魔法专精受伤倍率（<c>MagicDamageTakenScale</c>）
/// 与"上一回合受到过魔法伤害"条件（<c>TookMagicDamageLastTurn</c>）。
/// </summary>
/// <remarks>
/// 三者是同一条内容线的配套机制（爱因斯坦被动2 / 凝析 buff）：
/// <list type="bullet">
/// <item><c>OrbTriggered</c>：<c>onOrbTriggered</c> 钩子的效果要能问"这次触发里有没有黄球"——
/// 批次信息由 <c>OrbRuntime.Trigger</c> 在钩子前后开关，钩子之外读不到；</item>
/// <item><c>MagicDamageTakenScale</c>：与全伤害受伤倍率同桶加算，只在伤害维度为魔法时并入；</item>
/// <item><c>TookMagicDamageLastTurn</c>：魔法受击账在回合边界滚动（本回合 → 上一回合），
/// 因此本回合挨打、下一回合的回合开始钩子才读得到。</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class OrbAndMagicDamageConditionTests
{
    private const string Yellow = BuiltinOrbTypes.Yellow;
    private const string Red = BuiltinOrbTypes.Red;

    private const string OrbElementProbeBuff = "buff.orb_element_probe";
    private const string OrbIdProbeBuff = "buff.orb_id_probe";
    private const string MagicProbeBuff = "buff.magic_probe";
    private const string OrbMarkBuff = "buff.orb_mark";
    private const string MagicMarkBuff = "buff.magic_mark";

    private const string OrbElementProbeEffect = "effect.orb_element_probe";
    private const string OrbIdProbeEffect = "effect.orb_id_probe";
    private const string MagicProbeEffect = "effect.magic_probe";
    private const string MagicHitEffect = "effect.magic_hit";
    private const string PhysicalHitEffect = "effect.physical_hit";
    private const string MagicGe = "ge.magic_hit";
    private const string PhysicalGe = "ge.physical_hit";

    /// <summary>条件域必须在内容 Rebuild（校验条件类型）之前注册——与 <c>ModFactory.Bootstrap</c> 同序。</summary>
    [SetUp]
    public void RegisterBuiltinConditions() => BaseGameContent.RegisterBuiltinConditions();

    #region OrbTriggered

    /// <summary>
    /// 批次里有黄球 → 两条探针都通过；只有红球 → 都不通过。
    /// 两种参数形态各测一条：<c>elementAny: ["Yellow"]</c> 与 <c>orbTypeId: "yellow"</c>。
    /// </summary>
    [Test]
    public void OrbTriggered_passes_only_when_the_batch_contains_the_configured_orb()
    {
        using var sim = Build();
        ApplyProbe(sim, OrbElementProbeBuff, slot: 0);
        ApplyProbe(sim, OrbIdProbeBuff, slot: 1);

        // 红球批次（产球者 0 与 1：两条探针都会被钩子求值）。
        TriggerOrbs(sim, Red);
        Assert.That(Mark(sim, 0, OrbMarkBuff), Is.Null, "只有红球 → elementAny 黄 不通过");
        Assert.That(Mark(sim, 1, OrbMarkBuff), Is.Null, "只有红球 → orbTypeId yellow 不通过");

        // 黄球批次。
        TriggerOrbs(sim, Yellow);
        Assert.That(Mark(sim, 0, OrbMarkBuff), Is.Not.Null, "批次里有黄球 → elementAny 通过");
        Assert.That(Mark(sim, 1, OrbMarkBuff), Is.Not.Null, "批次里有黄球 → orbTypeId 通过");
    }

    /// <summary>批次上下文只在钩子求值期间存在：触发前与触发后都不通过（区间清空）。</summary>
    [Test]
    public void OrbTriggered_batch_is_visible_only_inside_the_trigger_hook()
    {
        using var sim = Build();

        Assert.That(sim.OrbBatchMatches((int)EElement.Yellow, null), Is.False, "没有触发批次时不通过");

        TriggerOrbs(sim, Yellow);

        Assert.That(sim.OrbBatchMatches((int)EElement.Yellow, null), Is.False, "钩子结束后批次必须清空");
        Assert.That(sim.OrbBatchMatches(0, Yellow), Is.False, "批次清空后按球类型查也不通过");
    }

    #endregion

    #region MagicDamageTakenScale

    /// <summary>魔法受伤倍率只吃魔法伤害：魔法 GE 翻倍、物理 GE 分毫不动。</summary>
    [Test]
    public void Magic_damage_taken_scale_reduces_magical_damage_but_not_physical_damage()
    {
        using var sim = Build();
        var vulnerable = sim.EnemyTeam.Enemies[0];
        var plain = sim.EnemyTeam.Enemies[1];
        vulnerable.Asc.SetBaseValue(AttributeIds.MagicDamageTakenScale, 1f);

        // 魔攻 20 − 魔防 0 = 20；受伤倍率 +100% → 40（物防 0 不参与）。
        Execute(sim, MagicHitEffect, [Enemy(0), Enemy(1)]);
        Assert.That(vulnerable.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 40f).Within(0.001f));
        Assert.That(plain.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 20f).Within(0.001f),
            "没有魔法受伤倍率的目标吃基础数额");

        // 物攻 10 − 物防 0 = 10：魔法受伤倍率对物理伤害完全无效。
        Execute(sim, PhysicalHitEffect, [Enemy(0), Enemy(1)]);
        Assert.That(vulnerable.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 40f - 10f).Within(0.001f));
        Assert.That(plain.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 20f - 10f).Within(0.001f));
    }

    /// <summary>
    /// 玩家侧的魔法受伤倍率（爱因斯坦被动2 的实际用例）：敌方魔法伤害落到队伍共享账本时被削减，
    /// 同一角色的物理伤害分毫不动。
    /// </summary>
    [Test]
    public void Magic_damage_taken_scale_reduces_enemy_magical_damage_on_a_player_slot()
    {
        using var sim = Build();
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.MagicDamageTakenScale, -0.25f);
        var before = sim.PlayerTeam.SharedHp;

        ExecuteEnemyDamage(sim, MagicHitEffect, slot: 0);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(before - 6), "8 点魔法伤害 × (1 − 0.25) = 6");

        ExecuteEnemyDamage(sim, PhysicalHitEffect, slot: 0);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(before - 6 - 8), "物理伤害不吃魔法专精倍率");
    }

    /// <summary>
    /// 受伤倍率查询（直伤 / 充能球 / 普攻共用的入口）：魔法维度并入 <c>MagicDamageTakenScale</c>，
    /// 其余维度与缺省调用完全一致（旧调用点行为不变）。
    /// </summary>
    [Test]
    public void Taken_scale_lookup_adds_the_magic_component_only_for_magical_kinds()
    {
        using var sim = Build();
        var enemy = sim.EnemyTeam.Enemies[0];
        enemy.Asc.SetBaseValue(AttributeIds.DamageTakenScale, 0.5f);
        enemy.Asc.SetBaseValue(AttributeIds.MagicDamageTakenScale, 0.25f);

        Assert.That(
            DamagePipeline.ResolveTakenScale(sim, Enemy(0), EDamageKind.Magical),
            Is.EqualTo(0.75f).Within(0.001f),
            "魔法：全伤害 + 魔法专精同桶加算");
        Assert.That(
            DamagePipeline.ResolveTakenScale(sim, Enemy(0), EDamageKind.Physical),
            Is.EqualTo(0.5f).Within(0.001f),
            "物理只吃全伤害倍率");
        Assert.That(
            DamagePipeline.ResolveTakenScale(sim, Enemy(0)),
            Is.EqualTo(0.5f).Within(0.001f),
            "缺省维度 = 物理，旧调用点行为不变");
        Assert.That(
            DamagePipeline.ResolveTakenScale(sim, Enemy(0), EDamageKind.Elemental),
            Is.EqualTo(0.5f).Within(0.001f),
            "元素伤害同样不吃魔法专精倍率");
    }

    #endregion

    #region TookMagicDamageLastTurn

    /// <summary>
    /// 端到端：上一回合挨了魔法伤害 → 下一回合的 onTurnStart 钩子通过；
    /// 同一钩子挂在只挨物理伤害的角色上不通过。形态与出货内容（爱因斯坦被动2）一致。
    /// </summary>
    [Test]
    public void TookMagicDamageLastTurn_passes_on_the_next_turn_after_magical_damage()
    {
        using var sim = Build();
        ApplyProbe(sim, MagicProbeBuff, slot: 0);
        ApplyProbe(sim, MagicProbeBuff, slot: 1);

        ExecuteEnemyDamage(sim, MagicHitEffect, slot: 0);
        ExecuteEnemyDamage(sim, PhysicalHitEffect, slot: 1);

        AdvanceTurn(sim);

        Assert.That(Mark(sim, 0, MagicMarkBuff), Is.Not.Null, "上一回合挨了魔法伤害 → 通过");
        Assert.That(Mark(sim, 1, MagicMarkBuff), Is.Null, "只挨了物理伤害 → 不通过");
        Assert.That(sim.TurnNumber, Is.EqualTo(2), "账期与回合边界对齐");
    }

    /// <summary>本回合的账要等回合边界才可读；且只保留"上一回合"，两回合前的账不再通过。</summary>
    [Test]
    public void TookMagicDamageLastTurn_is_per_character_and_covers_only_the_previous_turn()
    {
        using var sim = Build();
        ExecuteEnemyDamage(sim, MagicHitEffect, slot: 0);
        ExecuteEnemyDamage(sim, PhysicalHitEffect, slot: 1);

        Assert.That(Passes(sim, 0), Is.False, "本回合的账在回合边界之前不可读");
        Assert.That(Passes(sim, 1), Is.False);

        AdvanceTurn(sim);

        Assert.That(Passes(sim, 0), Is.True, "上一回合的魔法受击账");
        Assert.That(Passes(sim, 1), Is.False, "物理伤害不记魔法账");
        Assert.That(Passes(sim, 2), Is.False, "没挨魔法伤害的角色不通过");

        AdvanceTurn(sim);

        Assert.That(Passes(sim, 0), Is.False, "只保留上一回合：两回合前的账已滚掉");
    }

    /// <summary>自我结算（中毒 / 手牌槽伤害）与友方来源不计入魔法受击账——与 onDamaged 同门闩。</summary>
    [Test]
    public void TookMagicDamageLastTurn_ignores_self_inflicted_magical_damage()
    {
        using var sim = Build();
        var player = new CombatTargetRef(ECombatSide.Player, 0);

        Execute(sim, MagicHitEffect, [player], player);

        AdvanceTurn(sim);

        Assert.That(Passes(sim, 0), Is.False, "自身结算的魔法伤害不计入（与 onDamaged 同门闩）");
    }

    /// <summary>
    /// 护盾把伤害完全吃掉也算"挨了这一下"：记账发生在数额判定之前（与 onDamaged 同口径），
    /// 因此"受到魔法伤害后……"的被动不会被护盾静默屏蔽。
    /// </summary>
    [Test]
    public void TookMagicDamageLastTurn_counts_magical_hits_fully_absorbed_by_shield()
    {
        using var sim = Build();
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.Shield, 999f);
        var before = sim.PlayerTeam.SharedHp;

        ExecuteEnemyDamage(sim, MagicHitEffect, slot: 0);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(before), "护盾全额抵扣，账本不掉血");

        AdvanceTurn(sim);

        Assert.That(Passes(sim, 0), Is.True, "被护盾吃掉也算受到魔法伤害");
    }

    #endregion

    #region 注册与出货内容回归

    /// <summary>新条件与既有条件同时注册，短/长提示键齐备；既有条件的解析口径不变。</summary>
    [Test]
    public void Builtin_combat_conditions_register_the_new_types_alongside_the_existing_ones()
    {
        var registry = new ConditionRegistry<ICombatCondContext>();
        BuiltinCombatConditions.RegisterAll(registry);

        foreach (var kind in new[]
        {
            BuiltinCombatConditions.CardPlayedThisTurn,
            BuiltinCombatConditions.ChainTierAtLeast,
            BuiltinCombatConditions.IdentityMatch,
            BuiltinCombatConditions.OrbTriggered,
            BuiltinCombatConditions.TookMagicDamageLastTurn,
        })
        {
            Assert.That(registry.Contains(kind), Is.True, $"内置条件 {kind} 必须注册");
            Assert.That(registry.TryGet(kind, out var handler), Is.True);
            Assert.That(handler!.ShortTipKey, Is.Not.Empty, $"{kind} 缺短提示键");
            Assert.That(handler.LongTipKey, Is.Not.Empty, $"{kind} 缺长提示键");
        }

        // 既有条件（回归守卫）：参数形态与旧口径一致。
        Assert.That(TryParse(registry, BuiltinCombatConditions.CardPlayedThisTurn, """{"count":2,"elementAny":["Yellow"]}"""), Is.True);
        Assert.That(TryParse(registry, BuiltinCombatConditions.ChainTierAtLeast, """{"tier":3}"""), Is.True);
        Assert.That(TryParse(registry, BuiltinCombatConditions.IdentityMatch, """{"elementAny":["Green"]}"""), Is.True);

        // 新条件：属性名大小写不敏感（与 TryParseElementFlags 同口径），非法参数一律拒绝。
        Assert.That(TryParse(registry, BuiltinCombatConditions.OrbTriggered, """{"elementAny":["yellow"]}"""), Is.True);
        Assert.That(TryParse(registry, BuiltinCombatConditions.OrbTriggered, """{"orbTypeId":"yellow"}"""), Is.True);
        Assert.That(TryParse(registry, BuiltinCombatConditions.OrbTriggered, "{}"), Is.False, "至少要配置一项");
        Assert.That(TryParse(registry, BuiltinCombatConditions.OrbTriggered, """{"elementAny":["Purple"]}"""), Is.False);
        Assert.That(TryParse(registry, BuiltinCombatConditions.OrbTriggered, """{"elementAny":"Yellow"}"""), Is.False);
        Assert.That(TryParse(registry, BuiltinCombatConditions.TookMagicDamageLastTurn, "{}"), Is.True, "无参数条件：不写 params 即合法");
        Assert.That(TryParse(registry, BuiltinCombatConditions.TookMagicDamageLastTurn, """{"within":"lastTurn"}"""), Is.False);
    }

    /// <summary>
    /// 出货内容（<c>Config/mods/base-game</c>）里所有效果 / buff 条件都必须能被已注册的条件类型解析：
    /// 新增条件类型后内容先行的写法（<c>OrbTriggered</c> / <c>TookMagicDamageLastTurn</c>）不能成为静默失效。
    /// </summary>
    [Test]
    public void Shipped_content_conditions_all_parse_with_the_registered_types()
    {
        var definitions = BaseGameContent.Load();
        var registry = new ConditionRegistry<ICombatCondContext>();
        BuiltinCombatConditions.RegisterAll(registry);

        var checkedCount = 0;
        foreach (var effect in definitions.Effects.Values)
            checkedCount += AssertConditionsParse(registry, $"effect:{effect.Id}", effect.Conditions);
        foreach (var buff in definitions.Buffs.Values)
            checkedCount += AssertConditionsParse(registry, $"buff:{buff.Id}", buff.Conditions);

        Assert.That(checkedCount, Is.GreaterThan(0), "出货内容里应当有条件需要校验");
    }

    private static int AssertConditionsParse(
        ConditionRegistry<ICombatCondContext> registry,
        string ownerPath,
        IReadOnlyList<ConditionRefDto> conditions)
    {
        foreach (var condition in conditions)
        {
            Assert.That(registry.TryGet(condition.Kind, out var handler), Is.True,
                $"{ownerPath}: 未知条件类型 '{condition.Kind}'");
            var args = JsonSerializer.SerializeToElement(
                condition.Params ?? new Dictionary<string, object>(StringComparer.Ordinal));
            Assert.That(handler!.TryParse(args, $"{ownerPath}.{condition.Kind}", out _, out var error), Is.True,
                $"{ownerPath}: 条件参数非法 —— {error}");
        }

        return conditions.Count;
    }

    #endregion

    #region 装配

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatSimulation Build()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            orbs: new Dictionary<string, OrbTypeDto>(StringComparer.Ordinal)
            {
                [Yellow] = Orb(Yellow, EElement.Yellow),
                [Red] = Orb(Red, EElement.Red),
            },
            buffs: new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                [OrbElementProbeBuff] = ProbeBuff(OrbElementProbeBuff, OrbElementProbeEffect),
                [OrbIdProbeBuff] = ProbeBuff(OrbIdProbeBuff, OrbIdProbeEffect),
                [MagicProbeBuff] = ProbeBuff(MagicProbeBuff, MagicProbeEffect, onTurnStart: true),
                [OrbMarkBuff] = MarkBuff(OrbMarkBuff),
                [MagicMarkBuff] = MarkBuff(MagicMarkBuff),
            },
            effects: new Dictionary<string, EffectDto>(StringComparer.Ordinal)
            {
                [OrbElementProbeEffect] = MarkEffect(
                    OrbElementProbeEffect,
                    OrbMarkBuff,
                    BuiltinCombatConditions.OrbTriggered,
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["elementAny"] = new[] { "Yellow" },
                    }),
                [OrbIdProbeEffect] = MarkEffect(
                    OrbIdProbeEffect,
                    OrbMarkBuff,
                    BuiltinCombatConditions.OrbTriggered,
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["orbTypeId"] = Yellow,
                    }),
                [MagicProbeEffect] = MarkEffect(
                    MagicProbeEffect,
                    MagicMarkBuff,
                    BuiltinCombatConditions.TookMagicDamageLastTurn,
                    parameters: null),
                [MagicHitEffect] = DamageEffect(MagicHitEffect, MagicGe, amount: 0),
                [PhysicalHitEffect] = DamageEffect(PhysicalHitEffect, PhysicalGe, amount: 0),
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal)
            {
                [MagicGe] = DamageGameplayEffect(MagicGe, "Magical"),
                [PhysicalGe] = DamageGameplayEffect(PhysicalGe, "Physical"),
            });

        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MagicAttack] = 20f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests($"c{index}", attrs))
            .ToArray();
        // 敌人物防 / 魔防都归零，断言只反映"受伤倍率"这一项差异；物攻 / 魔攻各 8 是
        // 敌方来源伤害的数额（玩家侧防御为 0，伤害 = 8）。
        var enemies = Enumerable.Range(0, 2)
            .Select(index => new EnemyUnit($"e{index}", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = 100f,
                [AttributeIds.PhysicalAttack] = 8f,
                [AttributeIds.MagicAttack] = 8f,
                [AttributeIds.PhysicalDefense] = 0f,
                [AttributeIds.MagicDefense] = 0f,
            }))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260926);
    }

    private static OrbTypeDto Orb(string id, EElement element) => new()
    {
        Id = id,
        DisplayNameId = $"orb.{id}.name",
        DealsDamage = true,
        DamageKind = EDamageKind.Elemental,
        Element = element,
        // 数额为 0：本用例只关心触发批次条件，不产生伤害与表现事件。
        PerOrbAmount = 0f,
        AttackBonusScale = 0f,
        AttackSource = EOrbAttackSource.Higher,
    };

    private static BuffDto ProbeBuff(string id, string effectId, bool onTurnStart = false) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        Hooks = new BuffEffectHooksDto
        {
            OnOrbTriggered = onTurnStart ? [] : [new EffectRefDto { EffectId = effectId }],
            OnTurnStart = onTurnStart ? [new EffectRefDto { EffectId = effectId }] : [],
        },
    };

    private static BuffDto MarkBuff(string id) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        StackRule = EBuffStackRule.Replace,
    };

    private static EffectDto MarkEffect(
        string id,
        string buffId,
        string conditionKind,
        Dictionary<string, object>? parameters) => new()
        {
            Id = id,
            Kind = EEffectKind.ApplyBuff,
            Params = new Dictionary<string, object>(StringComparer.Ordinal) { ["buffId"] = buffId },
            Conditions =
            [
                new ConditionRefDto { Kind = conditionKind, Params = parameters },
            ],
        };

    private static EffectDto DamageEffect(string id, string gameplayEffectId, int amount) => new()
    {
        Id = id,
        Kind = EEffectKind.Damage,
        Params = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["amount"] = amount,
            ["damageGameplayEffectId"] = gameplayEffectId,
        },
    };

    private static GameplayEffectDefDto DamageGameplayEffect(string id, string damageType) => new()
    {
        Id = id,
        DurationPolicy = EDurationPolicy.Instant,
        Executions =
        [
            new ExecutionDefDto { Kind = "Damage", DamageType = damageType, Element = "" },
        ],
    };

    private static void ApplyProbe(CombatSimulation sim, string buffId, int slot)
    {
        var result = sim.Buffs.Apply(sim, Player(slot), buffId);
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static BuffInstance? Mark(CombatSimulation sim, int slot, string buffId) =>
        sim.PlayerTeam.Characters[slot].Buffs.Find(buffId);

    /// <summary>产球者 0 与 1 各产一些球（保证两条探针的钩子都被求值），再主动触发。</summary>
    private static void TriggerOrbs(CombatSimulation sim, string orbTypeId)
    {
        Assert.That(sim.Orbs.Grant(sim, orbTypeId, producerIndex: 0, count: 2), Is.True);
        Assert.That(sim.Orbs.Grant(sim, orbTypeId, producerIndex: 1), Is.True);

        var result = sim.Orbs.TriggerManual(sim);
        Assert.That(result.Triggered, Is.True, result.Error);
        Assert.That(result.ClearedByType[orbTypeId], Is.EqualTo(3));
    }

    private static void Execute(
        CombatSimulation sim,
        string effectId,
        IReadOnlyList<CombatTargetRef> targets,
        CombatTargetRef? source = null) =>
        sim.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = effectId },
            sim,
            source ?? Player(0),
            targets);

    private static void ExecuteEnemyDamage(CombatSimulation sim, string effectId, int slot) =>
        Execute(sim, effectId, [Player(slot)], Enemy(0));

    /// <summary>走完一个回合：卡牌执行 → 敌方阶段（回合结束管线 + 下一回合开始，含受击账滚动）。</summary>
    private static void AdvanceTurn(CombatSimulation sim)
    {
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        sim.AdvancePhase();
    }

    /// <summary>按效果条件的口径求值（主体 = 来源角色槽位，与 <c>CombatEffectExecutor.ConditionsPass</c> 同构）。</summary>
    private static bool Passes(CombatSimulation sim, int slot) =>
        CombatConditionEvaluator.Pass(
            new ConditionRefDto { Kind = BuiltinCombatConditions.TookMagicDamageLastTurn },
            new CombatCondContext(sim, slot),
            $"test:{BuiltinCombatConditions.TookMagicDamageLastTurn}");

    private static bool TryParse(
        ConditionRegistry<ICombatCondContext> registry,
        string kind,
        string argsJson)
    {
        Assert.That(registry.TryGet(kind, out var handler), Is.True, $"未注册的条件类型 {kind}");
        var args = JsonSerializer.Deserialize<JsonElement>(argsJson);
        return handler!.TryParse(args, $"test:{kind}", out _, out _);
    }

    #endregion
}
