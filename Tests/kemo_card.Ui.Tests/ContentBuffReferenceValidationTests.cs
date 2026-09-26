using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// buff 相关引用与参数的内容准入校验（2026-09-19 buff 运行时新增的钩子与技能动作）。
/// 运行期对这些缺口一律静默失败（钩子不触发 / buff 不挂载），必须在准入阶段拒绝。
/// </summary>
[TestFixture]
public sealed class ContentBuffReferenceValidationTests
{
    [Test]
    public void Validator_reports_dangling_hook_effect_refs_for_all_buff_hook_nodes()
    {
        foreach (var hookName in new[]
                 {
                     "onApply", "onTurnStart", "onTurnEnd", "onStackChanged", "onRemove",
                     "onWaveStart", "onActiveSkillCast", "onSlotCardPlayed",
                 })
        {
            var buff = BuffWithHook(hookName, new EffectRefDto { EffectId = "effect.missing" });

            var errors = new ContentDefinitionValidator().Validate(StoreWith(buffs: Buffs(buff)));

            Assert.That(
                errors.Any(e => e.DefinitionId == "buff.hooked" && e.Message.Contains("effect.missing")),
                Is.True,
                $"钩子 {hookName} 的悬空 effectId 必须报错");
        }
    }

    [Test]
    public void Validator_accepts_buff_with_resolved_hook_effect_refs()
    {
        var buff = new BuffDto
        {
            Id = "buff.hooked",
            DurationType = EBuffDurationType.Permanent,
            Hooks = new BuffEffectHooksDto
            {
                OnWaveStart = [new EffectRefDto { EffectId = "effect.real" }],
                OnActiveSkillCast = [new EffectRefDto { EffectId = "effect.real" }],
                OnSlotCardPlayed = [new EffectRefDto { EffectId = "effect.real" }],
            },
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            buffs: Buffs(buff),
            effects: Effects(DamageEffect("effect.real"))));

        Assert.That(errors, Is.Empty);
    }

    [TestCase("ApplyBuff")]
    [TestCase("AttachSlotBuff")]
    public void Validator_reports_buff_actions_without_buffId(string kind)
    {
        var action = new SkillActionDto
        {
            Id = "action." + kind.ToLowerInvariant(),
            Kind = Enum.Parse<ESkillActionKind>(kind),
            Params = new(),
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            skillActions: Actions(action),
            buffs: Buffs(Buff("buff.real"))));

        Assert.That(
            errors.Any(e => e.Message.Contains("requires params.buffId")),
            Is.True,
            $"{kind} 缺 buffId 必须报错");
    }

    [TestCase("ApplyBuff")]
    [TestCase("AttachSlotBuff")]
    public void Validator_reports_buff_actions_with_unknown_buffId(string kind)
    {
        var action = new SkillActionDto
        {
            Id = "action." + kind.ToLowerInvariant(),
            Kind = Enum.Parse<ESkillActionKind>(kind),
            Params = new Dictionary<string, object> { ["buffId"] = "buff.missing" },
        };
        if (kind == "AttachSlotBuff")
            action.Params["slotIndex"] = 2;

        var errors = new ContentDefinitionValidator().Validate(StoreWith(skillActions: Actions(action)));

        Assert.That(
            errors.Any(e => e.Message.Contains("Unknown buffId 'buff.missing'")),
            Is.True,
            $"{kind} 的悬空 buffId 必须报错");
    }

    [Test]
    public void Validator_reports_attach_slot_buff_with_missing_or_negative_slot_index()
    {
        foreach (var slotIndex in new object?[] { null, -1 })
        {
            var parameters = new Dictionary<string, object> { ["buffId"] = "buff.real" };
            if (slotIndex is not null)
                parameters["slotIndex"] = slotIndex;

            var action = new SkillActionDto
            {
                Id = "action.attach",
                Kind = ESkillActionKind.AttachSlotBuff,
                Params = parameters,
            };

            var errors = new ContentDefinitionValidator().Validate(StoreWith(
                skillActions: Actions(action),
                buffs: Buffs(Buff("buff.real"))));

            Assert.That(
                errors.Any(e => e.Message.Contains("params.slotIndex")),
                Is.True,
                $"slotIndex={slotIndex ?? "缺失"} 必须报错");
        }
    }

    [Test]
    public void Validator_accepts_well_formed_attach_slot_buff_action()
    {
        var action = new SkillActionDto
        {
            Id = "action.attach",
            Kind = ESkillActionKind.AttachSlotBuff,
            Params = new Dictionary<string, object> { ["buffId"] = "buff.real", ["slotIndex"] = 2 },
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            skillActions: Actions(action),
            buffs: Buffs(Buff("buff.real"))));

        Assert.That(errors, Is.Empty);
    }

    [TestCase("SkillAction")]
    [TestCase("Effect")]
    public void Validator_reports_removal_without_any_filter(string category)
    {
        var errors = category switch
        {
            "SkillAction" => new ContentDefinitionValidator().Validate(StoreWith(skillActions: Actions(
                new SkillActionDto { Id = "action.cleanse", Kind = ESkillActionKind.RemoveBuff, Params = new() }))),
            _ => new ContentDefinitionValidator().Validate(StoreWith(effects: Effects(
                new EffectDto { Id = "effect.cleanse", Kind = EEffectKind.RemoveBuff, Params = new() }))),
        };

        Assert.That(errors.Any(e => e.Message.Contains("withTags")), Is.True, $"{category} 无筛选条件的驱散必须报错");
    }

    [Test]
    public void Validator_accepts_removal_by_tags_or_known_buff_id()
    {
        var byTags = new SkillActionDto
        {
            Id = "action.cleanse",
            Kind = ESkillActionKind.RemoveBuff,
            Params = new Dictionary<string, object>
            {
                ["withTags"] = new[] { "debuff.weak" },
            },
        };
        var byBuffId = new EffectDto
        {
            Id = "effect.cleanse",
            Kind = EEffectKind.RemoveBuff,
            Params = new Dictionary<string, object> { ["buffId"] = "buff.real" },
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            buffs: Buffs(Buff("buff.real")),
            skillActions: Actions(byTags),
            effects: Effects(byBuffId)));

        Assert.That(errors, Is.Empty);
    }

    #region 战斗条件与 targetFilter（2026-09-26 统一）

    /// <summary>
    /// 旧扁平 targetFilter（elementAny / raceAny / raceAll / matchAll）已删除：运行期会保守回退到
    /// [来源]，准入阶段必须直接报错——否则"筛选子集"会静默失效。
    /// </summary>
    [TestCase("Effect")]
    [TestCase("SkillAction")]
    public void Validator_reports_legacy_flat_target_filter_keys(string category)
    {
        var legacyFilter = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["elementAny"] = new[] { "Green" },
            ["matchAll"] = true,
        };

        var errors = category switch
        {
            "SkillAction" => new ContentDefinitionValidator().Validate(StoreWith(
                buffs: Buffs(Buff("buff.real")),
                skillActions: Actions(new SkillActionDto
                {
                    Id = "action.filtered",
                    Kind = ESkillActionKind.ApplyBuff,
                    Params = new Dictionary<string, object>
                    {
                        ["buffId"] = "buff.real",
                        ["targetFilter"] = legacyFilter,
                    },
                }))),
            _ => new ContentDefinitionValidator().Validate(StoreWith(effects: Effects(new EffectDto
            {
                Id = "effect.filtered",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object>
                {
                    ["amount"] = 1,
                    ["targetFilter"] = legacyFilter,
                },
            }))),
        };

        Assert.That(errors.Any(e => e.Message.Contains("未知参数 'elementAny'")), Is.True, $"{category} 的旧扁平键必须报错");
        Assert.That(errors.Any(e => e.Message.Contains("未知参数 'matchAll'")), Is.True, $"{category} 的旧扁平键必须报错");
    }

    /// <summary>
    /// <c>targetFilter.condition</c> 的 kind 写错 / 留空：运行期会让候选全部落选（静默空放），准入阶段必须报错。
    /// </summary>
    [Test]
    public void Validator_reports_target_filter_condition_with_unknown_or_empty_kind()
    {
        foreach (var (kind, expected) in new[]
                 {
                     ("Bogus", "Unknown condition kind 'Bogus'"),
                     ("", "condition.kind 不能为空"),
                 })
        {
            var effect = new EffectDto
            {
                Id = "effect.filtered",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object>
                {
                    ["amount"] = 1,
                    ["targetFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["condition"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["kind"] = kind },
                    },
                },
            };

            var errors = new ContentDefinitionValidator().Validate(StoreWith(effects: Effects(effect)));

            Assert.That(errors.Any(e => e.Message.Contains(expected)), Is.True, $"kind='{kind}' 必须报错");
        }
    }

    [Test]
    public void Validator_reports_non_object_target_filter()
    {
        var effect = new EffectDto
        {
            Id = "effect.filtered",
            Kind = EEffectKind.Damage,
            Params = new Dictionary<string, object>
            {
                ["amount"] = 1,
                ["targetFilter"] = "self",
            },
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(effects: Effects(effect)));

        Assert.That(errors.Any(e => e.Message.Contains("不是合法对象")), Is.True, "非对象 targetFilter 必须报错");
    }

    /// <summary>buff 持有者条件（<c>BuffDto.conditions</c>）与效果条件共用战斗条件域：写错必须报错。</summary>
    [Test]
    public void Validator_reports_buff_conditions_with_unknown_kind()
    {
        var buff = new BuffDto
        {
            Id = "buff.bad_condition",
            DurationType = EBuffDurationType.Permanent,
            Conditions = [new ConditionRefDto { Kind = "Bogus" }],
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(buffs: Buffs(buff)));

        Assert.That(
            errors.Any(e => e.DefinitionId == "buff.bad_condition" && e.Message.Contains("Unknown condition kind 'Bogus'")),
            Is.True,
            "buff 条件的未知 CondType 必须报错");
    }

    [Test]
    public void Validator_reports_chain_element_inject_with_empty_add()
    {
        var buff = new BuffDto
        {
            Id = "buff.bad_inject",
            DurationType = EBuffDurationType.Permanent,
            ChainElementInject = [new ChainElementInjectDto { Add = [] }],
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(buffs: Buffs(buff)));

        Assert.That(errors.Any(e => e.Message.Contains("chainElementInject.add 不能为空")), Is.True);
    }

    [Test]
    public void Validator_accepts_identity_match_in_buff_conditions_and_target_filter()
    {
        RegisterCombatConditions();
        var buff = new BuffDto
        {
            Id = "buff.identity",
            DurationType = EBuffDurationType.Permanent,
            Conditions =
            [
                new ConditionRefDto
                {
                    Kind = "IdentityMatch",
                    Params = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["elementAny"] = new[] { "Green" },
                    },
                },
            ],
        };
        var effect = new EffectDto
        {
            Id = "effect.filtered",
            Kind = EEffectKind.Damage,
            Params = new Dictionary<string, object>
            {
                ["amount"] = 1,
                ["targetFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["excludeSelf"] = true,
                    ["condition"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["kind"] = "IdentityMatch",
                        ["params"] = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["elementAny"] = new[] { "Green" },
                        },
                    },
                },
            },
        };

        var errors = new ContentDefinitionValidator().Validate(StoreWith(
            buffs: Buffs(buff),
            effects: Effects(effect)));

        Assert.That(errors, Is.Empty, string.Join("；", errors.Select(e => e.Message)));
    }

    /// <summary>条件域注册表是进程级静态状态：校验前显式注册内置条件，避免依赖测试执行顺序。</summary>
    private static void RegisterCombatConditions()
    {
        ConditionDomains.Combat.Clear();
        BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);
    }

    #endregion

    #region 装配

    private static BuffDto Buff(string id) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
    };

    /// <summary>按钩子名构造只挂该钩子的 buff（钩子属性 init-only，无法事后反射写入）。</summary>
    private static BuffDto BuffWithHook(string hookName, params EffectRefDto[] refs) => new()
    {
        Id = "buff.hooked",
        DurationType = EBuffDurationType.Permanent,
        Hooks = hookName switch
        {
            "onApply" => new BuffEffectHooksDto { OnApply = [.. refs] },
            "onTurnStart" => new BuffEffectHooksDto { OnTurnStart = [.. refs] },
            "onTurnEnd" => new BuffEffectHooksDto { OnTurnEnd = [.. refs] },
            "onStackChanged" => new BuffEffectHooksDto { OnStackChanged = [.. refs] },
            "onRemove" => new BuffEffectHooksDto { OnRemove = [.. refs] },
            "onWaveStart" => new BuffEffectHooksDto { OnWaveStart = [.. refs] },
            "onActiveSkillCast" => new BuffEffectHooksDto { OnActiveSkillCast = [.. refs] },
            "onSlotCardPlayed" => new BuffEffectHooksDto { OnSlotCardPlayed = [.. refs] },
            _ => throw new ArgumentOutOfRangeException(nameof(hookName), hookName, "未知钩子名"),
        },
    };

    private static EffectDto DamageEffect(string id) => new()
    {
        Id = id,
        Kind = EEffectKind.Damage,
        Params = new Dictionary<string, object> { ["amount"] = 1 },
    };

    private static Dictionary<string, BuffDto> Buffs(params BuffDto[] buffs) =>
        buffs.ToDictionary(buff => buff.Id, StringComparer.Ordinal);

    private static Dictionary<string, EffectDto> Effects(params EffectDto[] effects) =>
        effects.ToDictionary(effect => effect.Id, StringComparer.Ordinal);

    private static Dictionary<string, SkillActionDto> Actions(params SkillActionDto[] actions) =>
        actions.ToDictionary(action => action.Id, StringComparer.Ordinal);

    /// <summary>直接构造 store，绕开 Rebuild 的「先校验后剔除」，以便断言校验器本身的行为。</summary>
    private static GameDefinitionStore StoreWith(
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, SkillActionDto>? skillActions = null)
    {
        var store = new GameDefinitionStore();
        if (buffs is not null)
            foreach (var (id, dto) in buffs)
                store.BuffsMutable[id] = dto;
        if (effects is not null)
            foreach (var (id, dto) in effects)
                store.EffectsMutable[id] = dto;
        if (skillActions is not null)
            foreach (var (id, dto) in skillActions)
                store.SkillActionsMutable[id] = dto;
        return store;
    }

    #endregion
}
