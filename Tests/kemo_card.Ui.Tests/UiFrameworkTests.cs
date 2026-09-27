using KemoCard.Frame.Logging;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiFrameworkTests
{
    #region 状态机流转

    [Test]
    public void StateMachine_full_lifecycle_load_to_cache()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        var visited = new List<EUIState>();

        sm.Configure(EUIState.Wait, null);
        sm.Configure(EUIState.Load, onEnter: (_, _, _) => visited.Add(EUIState.Load));
        sm.Configure(EUIState.PreLoad, onEnter: (_, _, _) => visited.Add(EUIState.PreLoad));
        sm.Configure(EUIState.Create, onEnter: (_, _, _) => visited.Add(EUIState.Create));
        sm.Configure(EUIState.Open, onEnter: (_, _, _) => visited.Add(EUIState.Open));
        sm.Configure(EUIState.Close, onEnter: (_, _, _) => visited.Add(EUIState.Close));
        sm.Configure(EUIState.CloseDone, onEnter: (_, _, _) => visited.Add(EUIState.CloseDone));
        sm.Configure(EUIState.Cache, onEnter: (_, _, _) => visited.Add(EUIState.Cache));
        sm.Configure(EUIState.Destroy, onEnter: (_, _, _) => visited.Add(EUIState.Destroy));

        sm.SetInitialState(EUIState.Wait);
        sm.TransitionTo(EUIState.Load);
        sm.TransitionTo(EUIState.PreLoad);
        sm.TransitionTo(EUIState.Create);
        sm.TransitionTo(EUIState.Open);
        sm.TransitionTo(EUIState.Close);
        sm.TransitionTo(EUIState.CloseDone);
        sm.TransitionTo(EUIState.Cache);

        Assert.That(visited, Is.EqualTo(new[]
        {
            EUIState.Load,
            EUIState.PreLoad,
            EUIState.Create,
            EUIState.Open,
            EUIState.Close,
            EUIState.CloseDone,
            EUIState.Cache,
        }));
    }

    #endregion

    #region 强类型 OpenTransitionData

    [Test]
    public void FirstOpen_has_no_flags()
    {
        var data = OpenTransitionData.FirstOpen;
        Assert.That(data.IsReopen, Is.False);
        Assert.That(data.IsCloseOpen, Is.False);
    }

    [Test]
    public void Reopen_has_IsReopen_flag()
    {
        var data = OpenTransitionData.Reopen;
        Assert.That(data.IsReopen, Is.True);
        Assert.That(data.IsCloseOpen, Is.False);
    }

    [Test]
    public void CloseOpen_has_both_flags()
    {
        var data = OpenTransitionData.CloseOpen;
        Assert.That(data.IsReopen, Is.True);
        Assert.That(data.IsCloseOpen, Is.True);
    }

    #endregion

    #region 注册与路由

    [Test]
    public void Route_parent_child_resolution()
    {
        var reg = new UIRuntimeRegistry();
        reg.Register(new UIRuntimeEntry
        {
            OwnerModId = "test.mod",
            Id = "page",
            Dir = "ui",
            Type = EUIType.Pge,
        });
        reg.Register(new UIRuntimeEntry
        {
            OwnerModId = "test.mod",
            Id = "page.child",
            Dir = "ui",
            Type = EUIType.Pge,
            RouteMeta = new UIRouteMeta { ParentId = "page" },
        });

        Assert.That(reg.GetParentId("page.child"), Is.EqualTo("page"));
        Assert.That(reg.GetParentId("page"), Is.Null);
        Assert.That(reg.GetChildren("page"), Is.EqualTo(new[] { "page.child" }));
    }

    /// <summary>父路由未注册属于路由一致性问题：<c>Validate</c> 必须留下可诊断的错误日志（不阻断装配）。</summary>
    [Test]
    public void Route_validate_reports_missing_parent()
    {
        var recording = new RecordingAppLog();
        AppLog.Configure(recording);
        try
        {
            var reg = new UIRuntimeRegistry();
            reg.Register(new UIRuntimeEntry
            {
                OwnerModId = "test.mod",
                Id = "orphan",
                Dir = "ui",
                Type = EUIType.Pge,
                RouteMeta = new UIRouteMeta { ParentId = "missing" },
            });

            Assert.That(reg.Validate(), Is.False, "父路由缺失不阻断装配");
            Assert.That(
                recording.Entries.Any(entry => entry.Message.Contains("父路由 missing 未注册")),
                Is.True,
                "父路由未注册必须留下错误日志");
        }
        finally
        {
            AppLog.Configure(NullAppLog.Instance);
        }
    }

    #endregion

    #region 强类型 API

    [Test]
    public void UiId_implicit_conversion_to_string()
    {
        var id = new UiId<EmptyPayload>("test.id");
        string str = id;
        Assert.That(str, Is.EqualTo("test.id"));
        Assert.That(id.Value, Is.EqualTo("test.id"));
        Assert.That(id.ToString(), Is.EqualTo("test.id"));
    }

    #endregion

    #region 声明式注册

    [Test]
    public void UiRegistration_converts_to_runtime_entry()
    {
        var reg = UIRegistration.Dialog("test.mod", "testDlg", "ui/test");
        var entry = reg.ToRuntimeEntry();

        Assert.That(entry.Id, Is.EqualTo("testDlg"));
        Assert.That(entry.Dir, Is.EqualTo("ui/test"));
        Assert.That(entry.Type, Is.EqualTo(EUIType.Dlg));
        Assert.That(entry.ScenePath, Is.EqualTo("res://ui/test/testDlg.tscn"));
    }

    [Test]
    public void UiRegistration_with_parent_sets_route_meta()
    {
        var reg = UIRegistration.Page("test.mod", "child", "ui").WithParent("parent");

        Assert.That(reg.Parent, Is.Not.Null);
        Assert.That(reg.Parent!.ParentId, Is.EqualTo("parent"));

        var entry = reg.ToRuntimeEntry();
        Assert.That(entry.RouteMeta, Is.Not.Null);
        Assert.That(entry.RouteMeta!.ParentId, Is.EqualTo("parent"));
    }

    #endregion
}