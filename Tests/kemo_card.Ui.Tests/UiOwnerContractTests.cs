using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod;
using KemoCard.Mod.Global;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 界面归属契约（ui-mod-binding 规格 §5）：每个界面必须声明唯一归属功能 Mod，
/// 注册期做三条硬校验；决策 1 的「Run 结束销毁 RunMain」也依赖逐界面选项覆写。
/// </summary>
[TestFixture]
public sealed class UiOwnerContractTests
{
    private const string Owner = "test.mod";

    #region 归属落库

    [Test]
    public void Registration_carries_owner_into_runtime_entry()
    {
        var entry = UIRegistration.Dialog(Owner, "dlg", "ui/test").ToRuntimeEntry();

        Assert.That(entry.OwnerModId, Is.EqualTo(Owner));
        Assert.That(entry.Id, Is.EqualTo("dlg"));
    }

    [Test]
    public void All_shipped_registrations_declare_a_known_owner()
    {
        var knownOwners = FeatureModCatalog.Features.Select(feature => feature.ModId).ToHashSet(StringComparer.Ordinal);

        foreach (var feature in FeatureModCatalog.Features)
        {
            foreach (var registration in feature.Declare())
            {
                Assert.That(registration.OwnerModId, Is.EqualTo(feature.ModId), $"{registration.Id} 的归属与声明方不一致");
                Assert.That(knownOwners, Does.Contain(registration.OwnerModId));
            }
        }
    }

    /// <summary>防漂移：<c>FeatureId</c> 常量必须与 <c>BaseMod.ModId</c> 一致（注册用的 id 与运行时不一致会导致界面找不到归属功能）。</summary>
    [Test]
    public void FeatureId_constants_match_mod_runtime_ids()
    {
        Assert.That(new GlobalMod().ModId, Is.EqualTo(GlobalMod.FeatureId));
        Assert.That(new RunMod().ModId, Is.EqualTo(RunMod.FeatureId));
    }

    #endregion

    #region Validate 三条硬校验

    [Test]
    public void Validate_flags_blank_owner()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(new UIRuntimeEntry
        {
            OwnerModId = "",
            Id = "noOwner",
            Dir = "ui/test",
            Type = EUIType.Dlg,
        });

        Assert.That(registry.Validate(["test.mod"]), Is.True);
        Assert.That(registry.HasErrors, Is.True);
    }

    [Test]
    public void Validate_flags_owner_missing_from_catalog()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog("typo.mod", "dlg", "ui/test").ToRuntimeEntry());

        Assert.That(registry.Validate(["test.mod"]), Is.True, "归属不在已装配清单里属于装配错误");
    }

    [Test]
    public void Validate_accepts_registered_owner()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog(Owner, "dlg", "ui/test").ToRuntimeEntry());

        Assert.That(registry.Validate([Owner]), Is.False);
        Assert.That(registry.HasErrors, Is.False);
    }

    [Test]
    public void Validate_skips_catalog_check_when_owner_list_not_provided()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog(Owner, "dlg", "ui/test").ToRuntimeEntry());

        Assert.That(registry.Validate(), Is.False, "未提供清单时应只校验归属非空");
    }

    [Test]
    public void Register_flags_same_id_claimed_by_two_owners()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog("owner.a", "shared", "ui/a").ToRuntimeEntry());

        registry.Register(UIRegistration.Dialog("owner.b", "shared", "ui/b").ToRuntimeEntry());

        Assert.That(registry.HasErrors, Is.True, "同一界面 id 被两个功能抢占必须报错（后者会静默覆盖前者）");
    }

    [Test]
    public void Register_allows_same_owner_to_replace_its_own_entry()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog(Owner, "dlg", "ui/a").ToRuntimeEntry());

        registry.Register(UIRegistration.Dialog(Owner, "dlg", "ui/b").ToRuntimeEntry());

        Assert.That(registry.HasErrors, Is.False);
        Assert.That(registry.Get("dlg")!.Dir, Is.EqualTo("ui/b"));
    }

    #endregion

    #region 按归属查询

    [Test]
    public void GetByOwner_returns_only_that_owners_entries()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(UIRegistration.Dialog("owner.a", "a1", "ui/a").ToRuntimeEntry());
        registry.Register(UIRegistration.Dialog("owner.a", "a2", "ui/a").ToRuntimeEntry());
        registry.Register(UIRegistration.Dialog("owner.b", "b1", "ui/b").ToRuntimeEntry());

        var owned = registry.GetByOwner("owner.a");

        Assert.That(owned.Select(entry => entry.Id), Is.EquivalentTo(new[] { "a1", "a2" }));
        Assert.That(registry.OwnerModIds, Is.EquivalentTo(new[] { "owner.a", "owner.b" }));
    }

    [Test]
    public void GetByOwner_rejects_blank_id()
    {
        var registry = new UIRuntimeRegistry();

        Assert.Throws<ArgumentException>(() => registry.GetByOwner(" "));
    }

    #endregion

    #region 决策 1：RunMain 关闭即销毁，且不破坏 Window 基类默认值

    /// <summary>
    /// 端到端护栏：<c>RunMain</c> 只覆写 <c>CacheTime = 0</c>，
    /// 必须<b>同时</b>保留 Win 类型的 <c>HideBelow = true</c> / <c>Align = Full</c>——
    /// 这正是 D5（合并无条件覆盖 5 字段）修复前做不到的。
    /// </summary>
    [Test]
    public void RunMain_registration_disables_cache_without_losing_win_defaults()
    {
        var entry = RunMod.GetUIRegistrations().Single(item => item.Id == RunUiIds.RunMain).ToRuntimeEntry();

        var merged = DefaultUIOpenOpt.Value.Clone();
        merged.MergeFrom(entry.BaseOpenOpt);
        merged.MergeFrom(entry.OpenOpt);

        Assert.That(merged.EffectiveCacheTime, Is.EqualTo(0), "Run 结束必须销毁界面");
        Assert.That(merged.Layer, Is.EqualTo(EUILayer.Win));
        Assert.That(merged.EffectiveHideBelow, Is.True, "只覆写 CacheTime 不得抹掉 Win 的 HideBelow");
        Assert.That(merged.EffectiveAlign, Is.EqualTo(EUIAlign.Full), "只覆写 CacheTime 不得抹掉 Win 的 Align");
    }

    [Test]
    public void StorySelect_registration_keeps_default_cache()
    {
        var entry = RunMod.GetUIRegistrations().Single(item => item.Id == RunUiIds.StorySelect).ToRuntimeEntry();

        Assert.That(entry.OpenOpt, Is.Null, "StorySelect 是从菜单反复进出的弹窗，应保留缓存语义");
    }

    #endregion
}