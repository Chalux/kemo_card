using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Buffs;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 手牌槽充能口径（战斗规格 §13.2）：<c>charge</c> 参数决定触发所需牌数，
/// <c>ChargePlayed</c> / <c>ChargeProgress</c> 跟着计数走，触发并重置后回到 0；休眠实例不参与 UI。
/// </summary>
[TestFixture]
public sealed class SlotChargeTests
{
    [Test]
    public void Charge_required_defaults_to_one_without_param()
    {
        var instance = new BuffInstance(ChargeDef("charge.i"), parameters: null);

        Assert.That(instance.ChargeRequired, Is.EqualTo(1));
        Assert.That(instance.ChargePlayed, Is.EqualTo(0));
        Assert.That(instance.ChargeProgress, Is.EqualTo(0f));
    }

    [Test]
    public void Charge_progress_tracks_consumption_and_reset()
    {
        var instance = new BuffInstance(
            ChargeDef("charge.ii"),
            new Dictionary<string, object> { ["charge"] = 2 });

        Assert.That(instance.ChargeRequired, Is.EqualTo(2));
        Assert.That(instance.TryConsumeCharge(), Is.False, "第一张牌：还差一张触发");
        Assert.That(instance.ChargePlayed, Is.EqualTo(1));
        Assert.That(instance.ChargeProgress, Is.EqualTo(0.5f).Within(0.001f));

        Assert.That(instance.TryConsumeCharge(), Is.True, "第二张牌：触发载荷");
        Assert.That(instance.ChargePlayed, Is.EqualTo(2));
        Assert.That(instance.ChargeProgress, Is.EqualTo(1f).Within(0.001f));

        instance.ResetCharge();
        Assert.That(instance.ChargePlayed, Is.EqualTo(0));
        Assert.That(instance.ChargeProgress, Is.EqualTo(0f));
    }

    [Test]
    public void FindByTag_returns_active_charge_and_skips_dormant()
    {
        var container = new BuffContainer();
        var dormant = container.Add(
            new BuffDto
            {
                Id = "charge.dormant",
                DurationType = EBuffDurationType.Turns,
                Duration = 2,
                Tags = [BuiltinBuffTags.SlotCharge],
                Condition = new BuffConditionDto { ElementAny = [EElement.Green] },
            },
            parameters: null);
        Assert.That(dormant.IsDormant, Is.True, "容器无持有者属性，元素条件不满足 → 休眠");

        Assert.That(container.FindByTag(BuiltinBuffTags.SlotCharge), Is.Null, "休眠充能不参与 UI");

        var active = container.Add(ChargeDef("charge.active"), parameters: null);
        Assert.That(container.FindByTag(BuiltinBuffTags.SlotCharge), Is.SameAs(active));
    }

    private static BuffDto ChargeDef(string id) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Turns,
        Duration = 2,
        Tags = [BuiltinBuffTags.SlotCharge],
    };
}
