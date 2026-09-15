using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 链式引用环（ChainEffects / ChainActions）：执行侧是直接递归且没有守卫，
/// a↔b 互引会让游戏进程 StackOverflow（.NET 不可捕获）。内容准入必须拒绝，执行侧必须有深度兜底。
/// </summary>
[TestFixture]
public sealed class ContentChainCycleTests
{
    #region 内容准入拒绝环（直接校验，不经 Rebuild 剔除）

    [Test]
    public void Validator_reports_self_referencing_chain_effect()
    {
        var store = StoreWith(effects: new Dictionary<string, EffectDto>
        {
            ["effect.loop"] = ChainEffect("effect.loop", "effect.loop"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.DefinitionId == "effect.loop" && e.Message.Contains("cycle")), Is.True);
    }

    [Test]
    public void Validator_reports_mutual_chain_effect_cycle()
    {
        var store = StoreWith(effects: new Dictionary<string, EffectDto>
        {
            ["effect.a"] = ChainEffect("effect.a", "effect.b"),
            ["effect.b"] = ChainEffect("effect.b", "effect.a"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.DefinitionId == "effect.a" && e.Message.Contains("cycle")), Is.True);
        Assert.That(errors.Any(e => e.DefinitionId == "effect.b" && e.Message.Contains("cycle")), Is.True);
    }

    [Test]
    public void Validator_reports_longer_chain_effect_cycle()
    {
        var store = StoreWith(effects: new Dictionary<string, EffectDto>
        {
            ["effect.a"] = ChainEffect("effect.a", "effect.b"),
            ["effect.b"] = ChainEffect("effect.b", "effect.c"),
            ["effect.c"] = ChainEffect("effect.c", "effect.a"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.DefinitionId == "effect.a" && e.Message.Contains("cycle")), Is.True);
    }

    [Test]
    public void Validator_reports_mutual_chain_action_cycle()
    {
        var store = StoreWith(skillActions: new Dictionary<string, SkillActionDto>
        {
            ["action.a"] = ChainAction("action.a", "action.b"),
            ["action.b"] = ChainAction("action.b", "action.a"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.DefinitionId == "action.a" && e.Message.Contains("cycle")), Is.True);
        Assert.That(errors.Any(e => e.DefinitionId == "action.b" && e.Message.Contains("cycle")), Is.True);
    }

    [Test]
    public void Validator_reports_self_referencing_chain_action()
    {
        var store = StoreWith(skillActions: new Dictionary<string, SkillActionDto>
        {
            ["action.loop"] = ChainAction("action.loop", "action.loop"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors.Any(e => e.DefinitionId == "action.loop" && e.Message.Contains("cycle")), Is.True);
    }

    /// <summary>非回归：合法的有向无环链不得被误判。</summary>
    [Test]
    public void Validator_accepts_acyclic_chain()
    {
        var store = StoreWith(effects: new Dictionary<string, EffectDto>
        {
            ["effect.leaf"] = new()
            {
                Id = "effect.leaf",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object> { ["amount"] = 1 },
            },
            ["effect.root"] = ChainEffect("effect.root", "effect.leaf"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors, Is.Empty);
    }

    /// <summary>菱形引用（共享子节点但无环）也不得被误判。</summary>
    [Test]
    public void Validator_accepts_diamond_reference()
    {
        var store = StoreWith(effects: new Dictionary<string, EffectDto>
        {
            ["effect.leaf"] = new()
            {
                Id = "effect.leaf",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object> { ["amount"] = 1 },
            },
            ["effect.mid1"] = ChainEffect("effect.mid1", "effect.leaf"),
            ["effect.mid2"] = ChainEffect("effect.mid2", "effect.leaf"),
            ["effect.root"] = new()
            {
                Id = "effect.root",
                Kind = EEffectKind.ChainEffects,
                EffectRefs =
                [
                    new EffectRefDto { EffectId = "effect.mid1" },
                    new EffectRefDto { EffectId = "effect.mid2" },
                ],
            },
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Validator_accepts_acyclic_chain_action()
    {
        var store = StoreWith(skillActions: new Dictionary<string, SkillActionDto>
        {
            ["action.leaf"] = new()
            {
                Id = "action.leaf",
                Kind = ESkillActionKind.Draw,
                Params = new Dictionary<string, object> { ["count"] = 1 },
            },
            ["action.root"] = ChainAction("action.root", "action.leaf"),
        });

        var errors = new ContentDefinitionValidator().Validate(store);

        Assert.That(errors, Is.Empty);
    }

    #endregion

    #region Rebuild 剔除环上的定义

    [Test]
    public void Registry_reports_and_removes_cyclic_effect()
    {
        var registry = Rebuild(effects: new Dictionary<string, EffectDto>
        {
            ["effect.loop"] = ChainEffect("effect.loop", "effect.loop"),
        }, out var report);

        Assert.That(registry.Store.Effects.ContainsKey("effect.loop"), Is.False, "环上的定义不得留在注册表里");
        Assert.That(report.RemovedValidationErrors.Any(e => e.DefinitionId == "effect.loop"), Is.True);
        Assert.That(report.HasIssues, Is.True);
    }

    [Test]
    public void Registry_reports_and_removes_cyclic_action()
    {
        var registry = RebuildActions(skillActions: new Dictionary<string, SkillActionDto>
        {
            ["action.a"] = ChainAction("action.a", "action.b"),
            ["action.b"] = ChainAction("action.b", "action.a"),
        }, out var report);

        Assert.That(registry.Store.SkillActions.ContainsKey("action.a"), Is.False);
        Assert.That(registry.Store.SkillActions.ContainsKey("action.b"), Is.False);
        Assert.That(report.RemovedValidationErrors.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Registry_keeps_acyclic_chain()
    {
        var registry = Rebuild(effects: new Dictionary<string, EffectDto>
        {
            ["effect.leaf"] = new()
            {
                Id = "effect.leaf",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object> { ["amount"] = 1 },
            },
            ["effect.root"] = ChainEffect("effect.root", "effect.leaf"),
        }, out var report);

        Assert.That(registry.Store.Effects.ContainsKey("effect.root"), Is.True);
        Assert.That(registry.Store.Effects.ContainsKey("effect.leaf"), Is.True);
        Assert.That(report.RemovedValidationErrors, Is.Empty);
    }

    #endregion

    #region 执行侧深度兜底

    /// <summary>
    /// 绕过校验直接注入环（模拟手写/热更内容），执行必须靠深度上限终止而不是 StackOverflow。
    /// </summary>
    [Test]
    public void Executor_terminates_on_cyclic_effect_injected_after_validation()
    {
        using var sim = CombatSimulationTestBuilder.Standard();
        sim.Definitions.Store.EffectsMutable["effect.cycle_a"] = ChainEffect("effect.cycle_a", "effect.cycle_b");
        sim.Definitions.Store.EffectsMutable["effect.cycle_b"] = ChainEffect("effect.cycle_b", "effect.cycle_a");

        var source = new CombatTargetRef(ECombatSide.Player, 0);
        Assert.DoesNotThrow(() =>
            sim.EffectExecutor.ExecuteEffectRef(
                new EffectRefDto { EffectId = "effect.cycle_a" },
                sim,
                source,
                [source]));
    }

    [Test]
    public void Executor_terminates_on_cyclic_skill_action_injected_after_validation()
    {
        using var sim = CombatSimulationTestBuilder.Standard();
        sim.Definitions.Store.SkillActionsMutable["action.cycle_a"] = ChainAction("action.cycle_a", "action.cycle_b");
        sim.Definitions.Store.SkillActionsMutable["action.cycle_b"] = ChainAction("action.cycle_b", "action.cycle_a");

        var source = new CombatTargetRef(ECombatSide.Player, 0);
        Assert.DoesNotThrow(() =>
            sim.EffectExecutor.ExecuteSkillActionRef(
                new SkillActionRefDto { ActionId = "action.cycle_a" },
                sim,
                source,
                [source]));
    }

    #endregion

    private static EffectDto ChainEffect(string id, string targetEffectId) => new()
    {
        Id = id,
        Kind = EEffectKind.ChainEffects,
        EffectRefs = [new EffectRefDto { EffectId = targetEffectId }],
    };

    private static SkillActionDto ChainAction(string id, string targetActionId) => new()
    {
        Id = id,
        Kind = ESkillActionKind.ChainActions,
        ActionRefs = [new SkillActionRefDto { ActionId = targetActionId }],
    };

    /// <summary>直接构造 store，绕开 Rebuild 的「先校验后剔除」，以便断言校验器本身的行为。</summary>
    private static GameDefinitionStore StoreWith(
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null)
    {
        var store = new GameDefinitionStore();
        if (effects is not null)
        {
            foreach (var (id, dto) in effects)
            {
                store.EffectsMutable[id] = dto;
            }
        }

        if (skillActions is not null)
        {
            foreach (var (id, dto) in skillActions)
            {
                store.SkillActionsMutable[id] = dto;
            }
        }

        return store;
    }

    private static GameDefinitionRegistry Rebuild(
        IReadOnlyDictionary<string, EffectDto> effects,
        out ContentLoadReport report)
    {
        var definitions = ModDefinitionsBundle.Empty with { Effects = effects };
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", definitions)], out report);
        return registry;
    }

    private static GameDefinitionRegistry RebuildActions(
        IReadOnlyDictionary<string, SkillActionDto> skillActions,
        out ContentLoadReport report)
    {
        var definitions = ModDefinitionsBundle.Empty with { SkillActions = skillActions };
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", definitions)], out report);
        return registry;
    }
}