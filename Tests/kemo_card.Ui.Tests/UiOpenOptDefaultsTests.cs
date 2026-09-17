using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// UI 打开参数默认值与合并语义。
/// <see cref="DefaultUIOpenOpt.ForType"/> 是 Base* 基类与 <see cref="UIRegistration"/> 工厂的唯一来源；
/// <see cref="UIOpenOpt.MergeFrom"/> 只覆盖显式指定的字段。
/// </summary>
[TestFixture]
public sealed class UiOpenOptDefaultsTests
{
    #region ForType

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
        Assert.That(opt.EffectiveAlign, Is.EqualTo(expectedAlign));
        Assert.That(opt.EffectiveHideBelow, Is.EqualTo(expectedHideBelow));
    }

    [Test]
    public void ForType_page_leaves_layer_unresolved_for_parent_mounting()
    {
        var opt = DefaultUIOpenOpt.ForType(EUIType.Pge);

        Assert.That(opt.Layer, Is.Null, "页面靠 Parent 挂载，不应固定层级");
        Assert.That(opt.EffectiveAlign, Is.EqualTo(EUIAlign.Full));
        Assert.That(opt.EffectiveHideBelow, Is.False);
    }

    /// <summary>合并基线必须字段完整，否则「未指定」会漏进合并结果。</summary>
    [Test]
    public void Value_baseline_specifies_every_scalar_field()
    {
        var value = DefaultUIOpenOpt.Value;

        Assert.That(value.Layer, Is.Not.Null);
        Assert.That(value.CacheTime, Is.Not.Null);
        Assert.That(value.AnimType, Is.Not.Null);
        Assert.That(value.HideBelow, Is.Not.Null);
        Assert.That(value.NoCover, Is.Not.Null);
        Assert.That(value.Align, Is.Not.Null);
    }

    #endregion

    #region 合并语义（D5 回归）

    /// <summary>
    /// 核心回归：只覆写 <c>CacheTime</c> 的调用，<b>不得</b>把 <c>BaseOpenOpt</c> 提供的
    /// <c>HideBelow</c> / <c>Align</c> 一并静默改掉（此前 MergeInto 对这 5 个字段是无条件覆盖）。
    /// </summary>
    [Test]
    public void MergeFrom_does_not_clobber_fields_the_source_leaves_unspecified()
    {
        var result = DefaultUIOpenOpt.Value.Clone();
        result.MergeFrom(DefaultUIOpenOpt.ForType(EUIType.Win));
        Assert.That(result.EffectiveHideBelow, Is.True);
        Assert.That(result.EffectiveAlign, Is.EqualTo(EUIAlign.Full));

        // 模拟「注册项只想关掉缓存」——这是 Run 结束销毁界面所需的写法
        result.MergeFrom(new UIOpenOpt { CacheTime = 0 });

        Assert.That(result.EffectiveCacheTime, Is.EqualTo(0));
        Assert.That(result.Layer, Is.EqualTo(EUILayer.Win), "未指定 Layer 时不得覆盖");
        Assert.That(result.EffectiveHideBelow, Is.True, "未指定 HideBelow 时不得覆盖");
        Assert.That(result.EffectiveAlign, Is.EqualTo(EUIAlign.Full), "未指定 Align 时不得覆盖");
        Assert.That(result.EffectiveAnimType, Is.EqualTo(EAnimType.SkipReOpen), "未指定 AnimType 时保持基线");
    }

    [Test]
    public void MergeFrom_overrides_when_the_source_specifies_fields()
    {
        var result = DefaultUIOpenOpt.Value.Clone();
        result.MergeFrom(DefaultUIOpenOpt.ForType(EUIType.Win));

        result.MergeFrom(new UIOpenOpt
        {
            Layer = EUILayer.Notice,
            HideBelow = false,
            AnimType = EAnimType.Always,
            NoCover = true,
            Align = EUIAlign.Center,
            CacheTime = -1,
        });

        Assert.That(result.Layer, Is.EqualTo(EUILayer.Notice));
        Assert.That(result.EffectiveHideBelow, Is.False);
        Assert.That(result.EffectiveAnimType, Is.EqualTo(EAnimType.Always));
        Assert.That(result.EffectiveNoCover, Is.True);
        Assert.That(result.EffectiveAlign, Is.EqualTo(EUIAlign.Center));
        Assert.That(result.EffectiveCacheTime, Is.EqualTo(-1));
    }

    [Test]
    public void MergeFrom_null_source_is_noop()
    {
        var result = DefaultUIOpenOpt.Value.Clone();

        Assert.DoesNotThrow(() => result.MergeFrom(null));

        Assert.That(result.EffectiveCacheTime, Is.EqualTo(UIOpenOpt.DefaultCacheTime));
    }

    [Test]
    public void MergeFrom_ignores_unspecified_callbacks_but_takes_specified_ones()
    {
        var first = new UIOpenOpt { OnFail = () => { } };
        var second = new UIOpenOpt { OnOpen = _ => { } };

        var result = DefaultUIOpenOpt.Value.Clone();
        result.MergeFrom(first);
        result.MergeFrom(second);

        Assert.That(result.OnFail, Is.SameAs(first.OnFail), "先指定的回调必须保留");
        Assert.That(result.OnOpen, Is.SameAs(second.OnOpen));
    }

    [Test]
    public void MergeFrom_later_source_wins_for_callbacks()
    {
        var first = new UIOpenOpt { OnFail = () => { } };
        var second = new UIOpenOpt { OnFail = () => { } };

        var result = DefaultUIOpenOpt.Value.Clone();
        result.MergeFrom(first);
        result.MergeFrom(second);

        Assert.That(result.OnFail, Is.SameAs(second.OnFail), "后者覆盖前者");
    }

    #endregion

    #region 其它

    [TestCase(EUIType.Win, EUILayer.Win)]
    [TestCase(EUIType.Dlg, EUILayer.Dlg)]
    [TestCase(EUIType.Pop, EUILayer.Pop)]
    public void Registration_factories_populate_base_open_opt(EUIType type, EUILayer expectedLayer)
    {
        UIRegistration registration = type switch
        {
            EUIType.Win => UIRegistration.Window("test.mod", "ui.test", "Src/mod/test"),
            EUIType.Dlg => UIRegistration.Dialog("test.mod", "ui.test", "Src/mod/test"),
            EUIType.Pop => UIRegistration.Popup("test.mod", "ui.test", "Src/mod/test"),
            _ => UIRegistration.Page("test.mod", "ui.test", "Src/mod/test"),
        };

        var entry = registration.ToRuntimeEntry();

        Assert.That(entry.BaseOpenOpt, Is.Not.Null, "BaseOpenOpt 未填充会让层级/对齐/隐藏链整体失效");
        Assert.That(entry.BaseOpenOpt!.Layer, Is.EqualTo(expectedLayer));
    }

    [Test]
    public void Registration_page_has_base_open_opt_without_layer()
    {
        var entry = UIRegistration.Page("test.mod", "ui.test", "Src/mod/test").ToRuntimeEntry();

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
        Assert.That(source.EffectiveCacheTime, Is.Not.EqualTo(0));
        Assert.That(source.EffectiveHideBelow, Is.True);
    }

    [Test]
    public void Clone_preserves_unspecified_fields_as_unspecified()
    {
        var clone = new UIOpenOpt { CacheTime = 5 }.Clone();

        Assert.That(clone.CacheTime, Is.EqualTo(5));
        Assert.That(clone.HideBelow, Is.Null, "Clone 不得把「未指定」变成具体值");
        Assert.That(clone.Layer, Is.Null);
    }

    #endregion
}
