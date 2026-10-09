using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiLifecycleRegressionTests
{
    #region 路由替换与卸载

    [Test]
    public void Replacing_with_root_removes_previous_parent_and_cached_children()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(Entry("root"));
        registry.Register(Entry("child", "root"));
        _ = registry.GetChildren("root");
        registry.Register(Entry("child"));
        Assert.That(registry.GetParentId("child"), Is.Null);
        Assert.That(registry.GetChildren("root"), Is.Empty);
    }

    [Test]
    public void Replacing_parent_invalidates_both_children_lists()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(Entry("a"));
        registry.Register(Entry("b"));
        registry.Register(Entry("child", "a"));
        _ = registry.GetChildren("a");
        registry.Register(Entry("child", "b"));
        Assert.That(registry.GetChildren("a"), Is.Empty);
        Assert.That(registry.GetChildren("b"), Is.EqualTo(new[] { "child" }));
    }

    [Test]
    public void Owner_unregistration_removes_declarations_and_parent_relationships()
    {
        var registry = new UIRuntimeRegistry();
        registry.Register(Entry("root"));
        registry.Register(Entry("child", "root"));
        registry.Register(Entry("other", owner: "other"));
        _ = registry.GetChildren("root");
        registry.UnregisterOwner("tests");
        Assert.That(registry.Get("root"), Is.Null);
        Assert.That(registry.Get("child"), Is.Null);
        Assert.That(registry.GetParentId("child"), Is.Null);
        Assert.That(registry.GetChildren("root"), Is.Empty);
        Assert.That(registry.Get("other"), Is.Not.Null);
    }

    #endregion

    #region 请求完成与取消

    [Test]
    public void Completing_old_request_does_not_clear_reentrant_new_request()
    {
        var vo = Vo("window");
        var old = new TaskCompletionSource<UIVo?>();
        var current = new TaskCompletionSource<UIVo?>();
        vo.OpenTaskSource = current;
        vo.CompleteOpen(vo, old);
        Assert.That(old.Task.Result, Is.SameAs(vo));
        Assert.That(vo.OpenTaskSource, Is.SameAs(current));
        Assert.That(current.Task.IsCompleted, Is.False);
    }

    [Test]
    public void Completing_current_request_clears_source()
    {
        var vo = Vo("window");
        var source = new TaskCompletionSource<UIVo?>();
        vo.OpenTaskSource = source;
        vo.CompleteOpen(null);
        Assert.That(vo.OpenTaskSource, Is.Null);
        Assert.That(source.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(source.Task.Result, Is.Null);
    }

    [Test]
    public void New_request_cancels_previous_token_and_invalidates_callbacks_immediately()
    {
        var load = new UILoadContext();
        var previous = load.LoadToken.Token;
        int oldLoad = load.IncrementLoadFlag();
        int oldPreload = load.IncrementPreLoadFlag();
        load.BeginRequest();
        Assert.That(previous.IsCancellationRequested, Is.True);
        Assert.That(load.LoadToken.IsCancellationRequested, Is.False);
        Assert.That(load.LoadFlag, Is.GreaterThan(oldLoad));
        Assert.That(load.PreLoadFlag, Is.GreaterThan(oldPreload));
        load.LoadToken.Dispose();
    }

    [Test]
    public void Closed_load_context_can_begin_fresh_request()
    {
        var load = new UILoadContext();
        load.Cancel();
        load.BeginRequest();
        Assert.That(load.LoadToken.IsCancellationRequested, Is.False);
        load.LoadToken.Dispose();
    }

    #endregion

    #region 清理重入

    [Test]
    public void Recursive_unbind_executes_each_unbinder_once()
    {
        var scope = new BindingScope();
        var calls = 0;
        scope.Add(() => { calls++; if (calls == 1) scope.UnbindAll(); });
        scope.UnbindAll();
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Subscription_added_during_cleanup_is_tracked_for_next_cleanup()
    {
        var scope = new BindingScope();
        var listeners = new List<Action>();
        Action listener = () => { };
        scope.Add(() => scope.Bind(() => listeners.Add(listener), () => listeners.Remove(listener)));
        scope.UnbindAll();
        Assert.That(scope.Count, Is.EqualTo(1));
        Assert.That(listeners, Has.Count.EqualTo(1));
        scope.UnbindAll();
        Assert.That(listeners, Is.Empty);
        Assert.That(scope.Count, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Recursive_animation_cancel_executes_old_cleanup_once(bool mask)
    {
        var anim = new UIAnimController();
        var calls = 0;
        Action clear = mask ? anim.ClearMaskAnim : anim.ClearAnim;
        Action callback = () => { calls++; if (calls == 1) clear(); };
        if (mask) anim.SetMaskAnimCallback(callback);
        else anim.SetAnimCallback(callback);
        clear();
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Animation_cancel_preserves_cleanup_registered_by_reentrant_callback()
    {
        var anim = new UIAnimController();
        var newCalls = 0;
        anim.SetAnimCallback(() => anim.SetAnimCallback(() => newCalls++));
        anim.ClearAnim();
        anim.ClearAnim();
        Assert.That(newCalls, Is.EqualTo(1));
    }

    #endregion

    private static UIRuntimeEntry Entry(string id, string? parent = null, string owner = "tests") => new()
    {
        Id = id, OwnerModId = owner, Dir = "tests", Type = EUIType.Dlg,
        RouteMeta = parent == null ? null : new UIRouteMeta { ParentId = parent },
    };
    private static UIVo Vo(string id) => new(id, EUIType.Dlg, "tests", null, null!, []);
}