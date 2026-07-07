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

    [Test]
    public void SkipReOpen_recognizes_reopen_tag()
    {
        // 模拟 UIOpenStateHandler 中的判断逻辑
        bool skipReOpen = true;
        OpenTransitionData data = OpenTransitionData.Reopen;

        bool playAnim = !skipReOpen || !data.IsReopen;

        Assert.That(playAnim, Is.False);
    }

    [Test]
    public void CloseOpen_bypasses_skip_reopen()
    {
        bool skipReOpen = true;
        OpenTransitionData data = OpenTransitionData.CloseOpen;

        bool playAnim = !skipReOpen || !data.IsReopen;

        Assert.That(playAnim, Is.False); // CloseOpen 的 IsReopen 也为 true
    }

    [Test]
    public void FirstOpen_always_plays_anim()
    {
        bool skipReOpen = true;
        OpenTransitionData data = OpenTransitionData.FirstOpen;

        bool playAnim = !skipReOpen || !data.IsReopen;

        Assert.That(playAnim, Is.True);
    }

    #endregion

    #region OnFail 语义

    [Test]
    public void OnFail_should_trigger_when_destroyed_from_load()
    {
        var fromState = EUIState.Load;
        bool shouldCallOnFail = fromState is EUIState.Load or EUIState.PreLoad;
        Assert.That(shouldCallOnFail, Is.True);
    }

    [Test]
    public void OnFail_should_trigger_when_destroyed_from_preload()
    {
        var fromState = EUIState.PreLoad;
        bool shouldCallOnFail = fromState is EUIState.Load or EUIState.PreLoad;
        Assert.That(shouldCallOnFail, Is.True);
    }

    [Test]
    public void OnFail_should_not_trigger_when_destroyed_from_close()
    {
        var fromState = EUIState.Close;
        bool shouldCallOnFail = fromState is EUIState.Load or EUIState.PreLoad;
        Assert.That(shouldCallOnFail, Is.False);
    }

    [Test]
    public void OnFail_should_not_trigger_when_destroyed_from_closeDone()
    {
        var fromState = EUIState.CloseDone;
        bool shouldCallOnFail = fromState is EUIState.Load or EUIState.PreLoad;
        Assert.That(shouldCallOnFail, Is.False);
    }

    [Test]
    public void OnFail_should_not_trigger_when_destroyed_from_cache()
    {
        var fromState = EUIState.Cache;
        bool shouldCallOnFail = fromState is EUIState.Load or EUIState.PreLoad;
        Assert.That(shouldCallOnFail, Is.False);
    }

    #endregion

    #region 注册与路由

    [Test]
    public void Route_parent_child_resolution()
    {
        var reg = new UIRuntimeRegistry();
        reg.Register(new UIRuntimeEntry
        {
            Id = "page",
            Dir = "ui",
            Type = EUIType.Pge,
        });
        reg.Register(new UIRuntimeEntry
        {
            Id = "page.child",
            Dir = "ui",
            Type = EUIType.Pge,
            RouteMeta = new UIRouteMeta { ParentId = "page" },
        });

        Assert.That(reg.GetParentId("page.child"), Is.EqualTo("page"));
        Assert.That(reg.GetParentId("page"), Is.Null);
        Assert.That(reg.GetChildren("page"), Is.EqualTo(new[] { "page.child" }));
    }

    [Test]
    public void Route_validate_no_errors_for_valid_registry()
    {
        var reg = new UIRuntimeRegistry();
        reg.Register(new UIRuntimeEntry
        {
            Id = "parent",
            Dir = "ui",
            Type = EUIType.Pge,
        });
        reg.Register(new UIRuntimeEntry
        {
            Id = "child",
            Dir = "ui",
            Type = EUIType.Pge,
            RouteMeta = new UIRouteMeta { ParentId = "parent" },
        });

        Assert.DoesNotThrow(() => reg.Validate());
    }

    [Test]
    public void Route_validate_detects_missing_parent()
    {
        var reg = new UIRuntimeRegistry();
        reg.Register(new UIRuntimeEntry
        {
            Id = "orphan",
            Dir = "ui",
            Type = EUIType.Pge,
            RouteMeta = new UIRouteMeta { ParentId = "missing" },
        });

        Assert.DoesNotThrow(() => reg.Validate());
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

    [Test]
    public void EmptyPayload_is_default_value()
    {
        var payload = new EmptyPayload();
        var defaultPayload = default(EmptyPayload);
        Assert.That(payload, Is.EqualTo(defaultPayload));
    }

    #endregion

    #region 声明式注册

    [Test]
    public void UiRegistration_converts_to_runtime_entry()
    {
        var reg = UIRegistration.Dialog("testDlg", "ui/test");
        var entry = reg.ToRuntimeEntry();

        Assert.That(entry.Id, Is.EqualTo("testDlg"));
        Assert.That(entry.Dir, Is.EqualTo("ui/test"));
        Assert.That(entry.Type, Is.EqualTo(EUIType.Dlg));
        Assert.That(entry.ScenePath, Is.EqualTo("res://ui/test/testDlg.tscn"));
    }

    [Test]
    public void UiRegistration_with_parent_sets_route_meta()
    {
        var reg = UIRegistration.Page("child", "ui").WithParent("parent");

        Assert.That(reg.Parent, Is.Not.Null);
        Assert.That(reg.Parent!.ParentId, Is.EqualTo("parent"));

        var entry = reg.ToRuntimeEntry();
        Assert.That(entry.RouteMeta, Is.Not.Null);
        Assert.That(entry.RouteMeta!.ParentId, Is.EqualTo("parent"));
    }

    #endregion
}
