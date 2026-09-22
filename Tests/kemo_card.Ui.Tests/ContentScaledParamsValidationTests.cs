using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 2026-09-21 新增的"参数化机制"取值必须走内容准入校验：
/// <c>attackScaleFromOrbs</c> / <c>tiers</c> / <c>perCard</c> / <c>offset</c> /
/// <c>elementMask</c> / <c>oncePerTurn</c> 在运行期对非法取值一律静默退化
/// （漏写 <c>maxBonus</c> ⇒ 动态攻击系数恒为 1、<c>tiers</c> 写错 ⇒ 一个球都不发、
/// <c>oncePerTurn</c> 非布尔 ⇒ "每回合仅 1 次"失效），因此与 <c>damageType</c> 同口径：
/// 写错必须在加载阶段被拒，而不是在战斗中悄悄少打球 / 多触发被动。
/// </summary>
[TestFixture]
public sealed class ContentScaledParamsValidationTests
{
    private const string EffectId = "effect.scaled";
    private const string ActionId = "action.scaled";

    #region 合法内容不得误报

    [Test]
    public void Shipped_shapes_pass_validation()
    {
        // 出货内容里的两种真实写法：三重奏的动态攻击系数、康塔塔的分档发球。
        var store = StoreWith(
            effects:
            [
                Effect(EffectId, EEffectKind.GainOrbByDeckCount, Params(
                    ("orbTypeId", "green"),
                    ("elementMask", 4),
                    ("tiers", Json("[[0, 2], [4, 3], [7, 4]]")))),
            ],
            skillActions:
            [
                Action(ActionId, ESkillActionKind.ApplyGameplayEffect, Params(
                    ("gameplayEffectId", "ge.any"),
                    ("attackScaleFromOrbs", Json("""{ "elementMask": 4, "perOrb": 1, "maxBonus": 3 }""")))),
            ]);

        var errors = Validate(store);

        Assert.That(errors, Is.Empty, string.Join("; ", errors.Select(error => error.Message)));
    }

    [Test]
    public void Per_card_offset_and_once_per_turn_accept_valid_values()
    {
        var store = StoreWith(effects:
        [
            Effect(EffectId, EEffectKind.GainOrbPerPlayedCard, Params(
                ("orbTypeId", "green"),
                ("perCard", 1),
                ("offset", -1))),
            Effect("effect.once", EEffectKind.GainOrb, Params(
                ("orbTypeId", "green"),
                ("oncePerTurn", true))),
        ]);

        var errors = Validate(store);

        Assert.That(errors, Is.Empty, string.Join("; ", errors.Select(error => error.Message)));
    }

    #endregion

    #region 非法取值必须报错

    [TestCase("""{ "elementMask": 4, "perOrb": 1 }""", TestName = "Attack_scale_without_max_bonus_is_rejected")]
    [TestCase("""{ "elementMask": 4, "maxBonus": 0 }""", TestName = "Attack_scale_with_zero_max_bonus_is_rejected")]
    public void Attack_scale_without_a_positive_max_bonus_is_rejected(string raw)
    {
        var errors = Validate(StoreWith(skillActions:
        [
            Action(ActionId, ESkillActionKind.ApplyGameplayEffect, Params(
                ("gameplayEffectId", "ge.any"),
                ("attackScaleFromOrbs", Json(raw)))),
        ]));

        Assert.That(
            errors.Any(error => error.Message.Contains("attackScaleFromOrbs.maxBonus")),
            Is.True,
            $"缺 maxBonus 会让参数静默变成永远 +0%：{raw}");
    }

    [Test]
    public void Attack_scale_with_a_non_numeric_per_orb_is_rejected()
    {
        var errors = Validate(StoreWith(skillActions:
        [
            Action(ActionId, ESkillActionKind.ApplyGameplayEffect, Params(
                ("gameplayEffectId", "ge.any"),
                ("attackScaleFromOrbs", Json("""{ "perOrb": "fast", "maxBonus": 3 }""")))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("perOrb")), Is.True);
    }

    [Test]
    public void Attack_scale_that_is_not_an_object_is_rejected()
    {
        var errors = Validate(StoreWith(skillActions:
        [
            Action(ActionId, ESkillActionKind.ApplyGameplayEffect, Params(
                ("gameplayEffectId", "ge.any"),
                ("attackScaleFromOrbs", 3))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("must be an object")), Is.True);
    }

    [TestCase("[]", TestName = "Tiers_empty_array_is_rejected")]
    [TestCase("[[0, 2], [4]]", TestName = "Tiers_pair_with_one_element_is_rejected")]
    [TestCase("""[[0, 2], [4, "three"]]""", TestName = "Tiers_non_integer_count_is_rejected")]
    [TestCase("[0, 2]", TestName = "Tiers_flat_array_is_rejected")]
    [TestCase("[[-1, 2]]", TestName = "Tiers_negative_threshold_is_rejected")]
    public void Malformed_tiers_are_rejected(string raw)
    {
        var errors = Validate(StoreWith(effects:
        [
            Effect(EffectId, EEffectKind.GainOrbByDeckCount, Params(
                ("orbTypeId", "green"),
                ("tiers", Json(raw)))),
        ]));

        Assert.That(
            errors.Any(error => error.Message.Contains("tiers")),
            Is.True,
            $"写错的 tiers 会静默发 0 个球：{raw}");
    }

    [Test]
    public void Non_integer_per_card_or_offset_is_rejected()
    {
        var errors = Validate(StoreWith(effects:
        [
            Effect(EffectId, EEffectKind.GainOrbPerPlayedCard, Params(
                ("orbTypeId", "green"),
                ("perCard", "many"),
                ("offset", "lots"))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("params.perCard")), Is.True);
        Assert.That(errors.Any(error => error.Message.Contains("params.offset")), Is.True);
    }

    [Test]
    public void Negative_element_mask_is_rejected()
    {
        var errors = Validate(StoreWith(effects:
        [
            Effect(EffectId, EEffectKind.GainOrbByDeckCount, Params(
                ("orbTypeId", "green"),
                ("elementMask", -1),
                ("tiers", Json("[[0, 2]]")))),
        ]));

        Assert.That(errors.Any(error => error.Message.Contains("params.elementMask")), Is.True);
    }

    [Test]
    public void Non_boolean_once_per_turn_is_rejected()
    {
        var errors = Validate(StoreWith(effects:
        [
            Effect(EffectId, EEffectKind.GainOrb, Params(
                ("orbTypeId", "green"),
                ("oncePerTurn", "yes"))),
        ]));

        Assert.That(
            errors.Any(error => error.Message.Contains("params.oncePerTurn")),
            Is.True,
            "非布尔的 oncePerTurn 会让「每回合仅 1 次」失效，被动每张牌都触发");
    }

    #endregion

    #region 装配

    private static List<ContentDefinitionValidationError> Validate(GameDefinitionStore store) =>
        new ContentDefinitionValidator().Validate(store);

    private static EffectDto Effect(
        string id,
        EEffectKind kind,
        Dictionary<string, object> parameters) => new()
        {
            Id = id,
            Kind = kind,
            Params = parameters,
        };

    private static SkillActionDto Action(
        string id,
        ESkillActionKind kind,
        Dictionary<string, object> parameters) => new()
        {
            Id = id,
            Kind = kind,
            Params = parameters,
        };

    /// <summary>内容 JSON 反序列化后参数值是 <see cref="JsonElement"/>，校验器必须吃得下这种形态。</summary>
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static Dictionary<string, object> Params(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    /// <summary>直接构造 store，绕开 Rebuild 的「先校验后剔除」，以便断言校验器本身的行为。</summary>
    private static GameDefinitionStore StoreWith(
        IReadOnlyList<EffectDto>? effects = null,
        IReadOnlyList<SkillActionDto>? skillActions = null)
    {
        var store = new GameDefinitionStore();
        // orbTypeId 的存在性也在准入校验里，发球类效果需要一个真实球类型做参照。
        store.OrbTypesMutable["green"] = new OrbTypeDto { Id = "green", Element = EElement.Green };
        if (effects is not null)
        {
            foreach (var effect in effects)
            {
                store.EffectsMutable[effect.Id] = effect;
            }
        }

        if (skillActions is not null)
        {
            foreach (var action in skillActions)
            {
                store.SkillActionsMutable[action.Id] = action;
            }
        }

        return store;
    }

    #endregion
}
