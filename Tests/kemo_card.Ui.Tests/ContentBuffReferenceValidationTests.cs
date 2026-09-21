using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
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
