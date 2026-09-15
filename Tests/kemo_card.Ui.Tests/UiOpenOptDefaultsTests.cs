using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// UI 打开参数默认值：<see cref="DefaultUIOpenOpt.ForType"/> 是 Base* 基类与 <see cref="UIRegistration"/>
/// 工厂的唯一来源。此前 <c>UIRegistration</c> 从不填 <c>BaseOpenOpt</c>，导致
/// <c>UIManager.ResolveLayer</c> 对 Window 也退回 Dlg 层，整条类型默认值链失效。
/// </summary>
[TestFixture]
public sealed class UiOpenOptDefaultsTests
{
    [Test]
    public void ForType_returns_fresh_instance_each_call()
    {
        var first = DefaultUIOpenOpt.ForType(EUIType.Win);
        var second = DefaultUIOpenOpt.ForType(EUIType.Win);

        Assert.That(first, Is.Not.SameAs(second), "必须每次返回新实例，避免调用方修改污染后续打开");
        Assert.That(first, Is.Not.SameAs(DefaultUIOpenOpt.Value), "不得返回静态共享默认实例");
    }

    [TestCase(EUIType.Win, EUILayer.Win, EUIAlign.Full, true)]
    [TestCase(EUIType.Dlg, EUILayer.Dlg, EUIAlign.Center, false)]
    [TestCase(EUIType.Pop, EUILayer.Pop, EUIAlign.None, false)]
    public void ForType_maps_type_to_layer_align_and_hide_below(
        EUIType type,
        EUILayer expectedLayer,
        EUIAlign expectedAlign,
        bool expectedHideBelow)
    {
        var opt = DefaultUIOpenOpt.ForType(type);

        Assert.That(opt.Layer, Is.EqualTo(expectedLayer));
        Assert.That(opt.Align, Is.EqualTo(expectedAlign));
        Assert.That(opt.HideBelow, Is.EqualTo(expectedHideBelow));
    }

    [Test]
    public void ForType_page_leaves_layer_unresolved_for_parent_mounting()
    {
        var opt = DefaultUIOpenOpt.ForType(EUIType.Pge);

        Assert.That(opt.Layer, Is.Null, "页面靠 Parent 挂载，不应固定层级");
        Assert.That(opt.Align, Is.EqualTo(EUIAlign.Full));
        Assert.That(opt.HideBelow, Is.False);
    }

    /// <summary>
    /// <c>MergeInto</c> 对 CacheTime / AnimType / NoCover 是无条件覆盖，
    /// 因此类型默认值必须与 <see cref="DefaultUIOpenOpt.Value"/> 同值，合并才是幂等的。
    /// </summary>
    [TestCase(EUIType.Win)]
    [TestCase(EUIType.Dlg)]
    [TestCase(EUIType.Pge)]
    [TestCase(EUIType.Pop)]
    public void ForType_keeps_merge_idempotent_for_unconditionally_overwritten_fields(EUIType type)
    {
        var opt = DefaultUIOpenOpt.ForType(type);

        Assert.That(opt.CacheTime, Is.EqualTo(DefaultUIOpenOpt.Value.CacheTime));
        Assert.That(opt.AnimType, Is.EqualTo(DefaultUIOpenOpt.Value.AnimType));
        Assert.That(opt.NoCover, Is.EqualTo(DefaultUIOpenOpt.Value.NoCover));
    }

    [TestCase(EUIType.Win, EUILayer.Win)]
    [TestCase(EUIType.Dlg, EUILayer.Dlg)]
    [TestCase(EUIType.Pop, EUILayer.Pop)]
    public void Registration_factories_populate_base_open_opt(EUIType type, EUILayer expectedLayer)
    {
        UIRegistration registration = type switch
        {
            EUIType.Win => UIRegistration.Window("ui.test", "Src/mod/test"),
            EUIType.Dlg => UIRegistration.Dialog("ui.test", "Src/mod/test"),
            EUIType.Pop => UIRegistration.Popup("ui.test", "Src/mod/test"),
            _ => UIRegistration.Page("ui.test", "Src/mod/test"),
        };

        var entry = registration.ToRuntimeEntry();

        Assert.That(entry.BaseOpenOpt, Is.Not.Null, "BaseOpenOpt 未填充会让层级/对齐/隐藏链整体失效");
        Assert.That(entry.BaseOpenOpt!.Layer, Is.EqualTo(expectedLayer));
    }

    [Test]
    public void Registration_page_has_base_open_opt_without_layer()
    {
        var entry = UIRegistration.Page("ui.test", "Src/mod/test").ToRuntimeEntry();

        Assert.That(entry.BaseOpenOpt, Is.Not.Null);
        Assert.That(entry.BaseOpenOpt!.Layer, Is.Null);
    }

    [Test]
    public void Clone_is_independent_of_source()
    {
        var source = DefaultUIOpenOpt.ForType(EUIType.Win);
        var clone = source.Clone();

        clone.Layer = EUILayer.Notice;
        clone.CacheTime = 0;
        clone.HideBelow = false;

        Assert.That(source.Layer, Is.EqualTo(EUILayer.Win));
        Assert.That(source.CacheTime, Is.Not.EqualTo(0));
        Assert.That(source.HideBelow, Is.True);
    }
}