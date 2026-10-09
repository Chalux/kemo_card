using System.Reflection;
using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Ui.Headless;

/// <summary>通过真实 Godot 节点和默认状态处理器验证 UI 契约；正常构建不包含本类。</summary>
public partial class UiFrameworkHeadlessTests : Node
{
    private readonly UIRuntimeRegistry _registry = new();
    private UIManager _manager = null!;
    private Control _stage = null!;
    private int _id;
    private int _failed;

    public override void _Ready() => _ = RunAllAsync();

    #region 执行与夹具

    private async Task RunAllAsync()
    {
        _stage = new Control();
        AddChild(_stage);
        _manager = new UIManager();
        AddChild(_manager);
        _manager.Init(new UIManagerInitOpt
        {
            Registry = _registry, StageRoot = _stage,
            Layers = [EUILayer.Win, EUILayer.Dlg, EUILayer.Pop],
            TopLayers = [EUILayer.Debug], KnownOwnerModIds = ["tests", "other", "remove"],
        });
        (string Name, Func<Task> Run)[] cases =
        [
            (nameof(WindowDefaults), () => Run(WindowDefaults)),
            (nameof(HideBelowAndBackAsync), HideBelowAndBackAsync),
            (nameof(CrossLayerVisibility), () => Run(CrossLayerVisibility)),
            (nameof(VisibilityCallbackCanCloseWindows), () => Run(VisibilityCallbackCanCloseWindows)),
            (nameof(TopLayerQueries), () => Run(TopLayerQueries)),
            (nameof(DialogsCoexist), () => Run(DialogsCoexist)),
            (nameof(MaskCacheAsync), MaskCacheAsync),
            ("close waits for window animation", () => Run(() => CloseWaitsForBothAnimations(false))),
            ("close waits for mask animation", () => Run(() => CloseWaitsForBothAnimations(true))),
            (nameof(ReopenCancelsOldClose), () => Run(ReopenCancelsOldClose)),
            (nameof(CloseAnimationFailureStillCloses), () => Run(CloseAnimationFailureStillCloses)),
            (nameof(StalePreloadFailure), () => Run(StalePreloadFailure)),
            (nameof(CloseCachedPreload), () => Run(CloseCachedPreload)),
            (nameof(ThrowingCallbacksAdvanceQueue), () => Run(ThrowingCallbacksAdvanceQueue)),
            (nameof(CallbackReentryPreservesRequest), () => Run(CallbackReentryPreservesRequest)),
            (nameof(ExitFailureStillUnbinds), () => Run(ExitFailureStillUnbinds)),
            (nameof(MaskExitFailureStillUnbinds), () => Run(MaskExitFailureStillUnbinds)),
            (nameof(FitExitFailureStillUnbinds), () => Run(FitExitFailureStillUnbinds)),
            (nameof(UnregisterOwnerAdvancesQueue), () => Run(UnregisterOwnerAdvancesQueue)),
            (nameof(CachedPreloadGetsFreshDeadline), () => Run(CachedPreloadGetsFreshDeadline)),
            (nameof(TimeoutCallbackReentry), () => Run(TimeoutCallbackReentry)),
            (nameof(ColdSceneLifecycle), () => Run(ColdSceneLifecycle)),
            (nameof(ColdMaskLifecycleAsync), ColdMaskLifecycleAsync),
            (nameof(FailedResourceFactory), () => Run(FailedResourceFactory)),
            (nameof(BusinessHookFailure), () => Run(BusinessHookFailure)),
            (nameof(CloseFromOpenBefore), () => Run(CloseFromOpenBefore)),
            (nameof(CustomParentMaskCache), () => Run(CustomParentMaskCache)),
            (nameof(DestroyDuringCloseAsync), DestroyDuringCloseAsync),
        ];
        foreach (var test in cases)
        {
            try
            {
                await test.Run();
                GD.Print($"UI_HEADLESS_PASS {test.Name}");
            }
            catch (Exception ex)
            {
                _failed++;
                GD.PrintErr($"UI_HEADLESS_FAIL {test.Name}: {ex}");
            }
            finally { Cleanup(); }
        }
        GD.Print($"UI_HEADLESS_SUMMARY total={cases.Length} failed={_failed}");
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Opened(Task<UIVo?> task, UIVo vo) =>
        Require(task.IsCompletedSuccessfully && task.Result == vo, $"{vo.Id}: open task not successful");
    private static void Rejected(Task<UIVo?> task) =>
        Require(task.IsCompletedSuccessfully && task.Result == null, "request should complete with null");

    private (UIVo Vo, TestWindow Win) Seed(EUIType type = EUIType.Win, string owner = "tests")
    {
        string id = $"headless.{++_id}";
        Register(id, type, owner);
        var vo = _manager.VoRegistry.GetOrCreate(id, type, owner, null);
        var win = new TestWindow { TestId = id, TestType = type, UIVo = vo };
        vo.Runtime.UI = win;
        vo.Lifecycle.OpenTime = 1;
        vo.Lifecycle.DestroyTime = -1;
        vo.StateMachine.SetInitialState(EUIState.Cache);
        return (vo, win);
    }
    private void Register(string id, EUIType type = EUIType.Win, string owner = "tests") =>
        _registry.Register(new UIRuntimeEntry
        {
            Id = id, OwnerModId = owner, Dir = "Tests/kemo_card.Ui.Headless", Type = type,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(type),
        });
    private static TestMask AttachMask(UIVo vo)
    {
        var mask = new TestMask { UIVo = vo };
        vo.Runtime.Mask = mask;
        return mask;
    }
    private void Cleanup()
    {
        foreach (var vo in _manager.VoRegistry.Map.Values.ToArray())
        {
            if (vo.UI is TestWindow win) win.ThrowOnExit = false;
            if (vo.Mask is TestMask mask) mask.ThrowOnExit = false;
            vo.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(vo, _manager));
        }
        _manager.OpenNext();
    }
    private async Task NextFramesAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    #endregion

    #region 默认参数、显示与叠放

    private void WindowDefaults()
    {
        var s = Seed();
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        Require(s.Vo.OpenOpt.EffectiveHideBelow && s.Vo.OpenOpt.EffectiveAlign == EUIAlign.Full, "layer baseline overwrote window defaults");
        Require(_manager.NavStack.Contains(s.Vo.Id), "window missing from navigation stack");
    }

    private async Task HideBelowAndBackAsync()
    {
        var lower = Seed();
        var upper = Seed();
        Opened(_manager.OpenAsync(lower.Vo.Id), lower.Vo);
        Opened(_manager.OpenAsync(upper.Vo.Id), upper.Vo);
        Require(!lower.Win.Visible && lower.Vo.Runtime.HideBool.Value, "same-layer lower window remained visible");
        Require(lower.Win.VisibleUpdates > 0, "visibility lifecycle hook not invoked");
        Require(await _manager.BackAsync() == lower.Vo, "BackAsync did not return lower window");
        Require(lower.Win.Visible && lower.Win.IsVisibleInTree(), "lower window did not restore");
        _manager.Close(lower.Vo.Id);
        Opened(_manager.OpenAsync(lower.Vo.Id), lower.Vo);
        Require(lower.Win.Visible, "cached hidden window remained hidden after reopen");
    }

    private void CrossLayerVisibility()
    {
        var lower = Seed();
        var upper = Seed(EUIType.Dlg);
        _ = _manager.OpenAsync(lower.Vo.Id);
        _ = _manager.OpenAsync(upper.Vo.Id, openOpt: new UIOpenOpt { HideBelow = true });
        Require(!lower.Win.IsVisibleInTree(), "lower layer not hidden");
        _manager.Close(upper.Vo.Id);
        Require(lower.Win.IsVisibleInTree(), "lower layer not restored");
    }

    private void TopLayerQueries()
    {
        var lower = Seed();
        var debug = Seed(EUIType.Dlg);
        _ = _manager.OpenAsync(lower.Vo.Id);
        _ = _manager.OpenAsync(debug.Vo.Id, openOpt: new UIOpenOpt { Layer = EUILayer.Debug });
        Require(!_manager.IsUITop(lower.Vo.Id) && _manager.IsUITop(debug.Vo.Id), "TopLayers ignored by top query");
        _ = _manager.OpenAsync(debug.Vo.Id, openOpt: new UIOpenOpt { Layer = EUILayer.Debug, NoCover = true });
        Require(_manager.IsUITop(lower.Vo.Id), "NoCover top window should allow lower top query");
    }

    private void VisibilityCallbackCanCloseWindows()
    {
        var windows = Enumerable.Range(0, 4).Select(_ => Seed(EUIType.Dlg)).ToArray();
        foreach (var window in windows) Opened(_manager.OpenAsync(window.Vo.Id), window.Vo);
        windows[2].Win.VisibilityAction = () =>
        {
            windows[2].Win.VisibilityAction = null;
            foreach (var window in windows) _manager.Close(window.Vo.Id);
        };
        windows[3].Vo.OpenOpt.HideBelow = true;
        _manager.LayerManager.UpdateLayers();
        Require(_manager.GetLayer(EUILayer.Dlg)!.UISort.Count == 0, "visibility callback did not close all dialogs");
    }

    private void DialogsCoexist()
    {
        var first = Seed(EUIType.Dlg);
        var second = Seed(EUIType.Dlg);
        Opened(_manager.OpenAsync(first.Vo.Id), first.Vo);
        Opened(_manager.OpenAsync(second.Vo.Id), second.Vo);
        Require(first.Vo.IsOpen && second.Vo.IsOpen && first.Win.Visible, "new dialog replaced previous dialog");
        Require(_manager.GetLayer(EUILayer.Dlg)!.UISort.Count == 2, "dialog layer should contain two instances");
        _manager.Close(second.Vo.Id);
        Require(first.Vo.IsOpen, "closing upper dialog also closed lower");
        Opened(_manager.OpenAsync(first.Vo.Id), first.Vo);
        Require(_manager.GetWin(first.Vo.Id) == first.Win && first.Win.BindingCount == 1, "same id should reuse node with one subscription");
        first.Win.ClickButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(first.Win.Clicks == 1, "reopen duplicated click handler");
    }

    #endregion

    #region 缓存和动画

    private async Task MaskCacheAsync()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        await NextFramesAsync();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache && GodotObject.IsInstanceValid(mask), "cached mask was freed");
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "cached nodes still attached");
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        Require(s.Vo.Mask == mask && mask.GetParent() == s.Win.GetParent(), "cached mask was not reattached");
    }

    private void CloseWaitsForBothAnimations(bool delayMask)
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = !delayMask;
        mask.DelayClose = delayMask;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Close, "cache entered before both animations completed");
        if (delayMask) mask.CloseAnimations[0]();
        else s.Win.CloseAnimations[0]();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "close did not complete after both animations");
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "mask left attached after close");
    }

    private void ReopenCancelsOldClose()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = mask.DelayClose = true;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        var oldWindowDone = s.Win.CloseAnimations[0];
        var oldMaskDone = mask.CloseAnimations[0];
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        oldWindowDone();
        oldMaskDone();
        Require(s.Vo.IsOpen && GodotObject.IsInstanceValid(mask) && mask.Closed == 0, "old close callback affected reopened UI");
    }

    private void CloseAnimationFailureStillCloses()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        mask.DelayClose = true;
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        s.Win.ThrowOnCloseAnimation = true;
        _manager.Close(s.Vo.Id);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "throwing close animation left UI stuck in Close");
        mask.CloseAnimations[0]();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache && mask.Closed == 0, "cancelled mask animation still invoked close callback");
    }

    private async Task DestroyDuringCloseAsync()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = mask.DelayClose = true;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        var oldWindowDone = s.Win.CloseAnimations[0];
        var oldMaskDone = mask.CloseAnimations[0];
        s.Vo.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(s.Vo, _manager));
        await NextFramesAsync();
        oldWindowDone();
        oldMaskDone();
        Require(!GodotObject.IsInstanceValid(s.Win) && !GodotObject.IsInstanceValid(mask), "destroy did not release both nodes");
    }

    #endregion

    #region 请求边界与异常

    private void StalePreloadFailure()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        var old = _manager.OpenAsync(s.Vo.Id);
        var latest = _manager.OpenAsync(s.Vo.Id);
        Rejected(old);
        s.Win.Preloads[1].Done();
        s.Win.Preloads[0].Fail();
        s.Win.Preloads[1].Fail();
        Opened(latest, s.Vo);
        Require(s.Vo.IsOpen && _manager.GetUIVo(s.Vo.Id) == s.Vo, "stale/duplicate failure destroyed successful request");
    }

    private void CloseCachedPreload()
    {
        var s = Seed();
        _ = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { CacheTime = -1 });
        _manager.Close(s.Vo.Id);
        s.Win.DelayPreload = true;
        var pending = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { CacheTime = -1 });
        _manager.Close(s.Vo.Id);
        Rejected(pending);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "cached request should remain reusable");
        s.Win.Preloads[0].Done();
        Require(!s.Vo.IsOpen, "closed preload completion reopened window");
    }

    private void ThrowingCallbacksAdvanceQueue()
    {
        var first = Seed();
        var next = Seed();
        first.Win.DelayPreload = true;
        var failed = _manager.OpenAsync(first.Vo.Id, openOpt: new UIOpenOpt { OnFail = () => throw new InvalidOperationException("expected OnFail") });
        var queued = _manager.OpenAsync(next.Vo.Id, openOpt: new UIOpenOpt { OnOpen = _ => throw new InvalidOperationException("expected OnOpen") });
        first.Win.Preloads[0].Fail();
        Rejected(failed);
        Opened(queued, next.Vo);
        Require(_manager.OpenCoordinator.CurrentOpening == null, "callback exception stalled queue");
    }

    private void CallbackReentryPreservesRequest()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        Task<UIVo?>? latest = null;
        var first = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnOpen = _ => latest = _manager.OpenAsync(s.Vo.Id) });
        s.Win.Preloads[0].Done();
        Opened(first, s.Vo);
        Require(latest != null && !latest.IsCompleted && s.Vo.OpenTaskSource != null, "old callback cleared reentrant request");
        s.Win.Preloads[1].Done();
        Opened(latest!, s.Vo);
    }

    private void CloseFromOpenBefore()
    {
        var s = Seed();
        var task = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnOpenBefore = _ => _manager.Close(s.Vo.Id) });
        Rejected(task);
        Require(!s.Vo.IsOpen && s.Win.BindingCount == 0, "open continued after OnOpenBefore closed UI");
    }

    private void BusinessHookFailure()
    {
        var s = Seed();
        s.Win.ThrowOnOpen = true;
        Rejected(_manager.OpenAsync(s.Vo.Id));
        Require(_manager.GetUIVo(s.Vo.Id) == null, "failed OnOpen retained VO");
    }

    #endregion

    #region 离场与卸载

    private void ExitFailureStillUnbinds()
    {
        var s = Seed();
        _ = _manager.OpenAsync(s.Vo.Id);
        s.Win.ThrowOnExit = true;
        s.Win.GetParent().RemoveChild(s.Win);
        s.Win.ClickButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(s.Win.BindingCount == 0 && s.Win.Clicks == 0, "exit failure bypassed native signal unbind");
        s.Win.ThrowOnExit = false;
    }

    private void MaskExitFailureStillUnbinds()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        _ = _manager.OpenAsync(s.Vo.Id);
        mask.AddBinding();
        mask.ThrowOnExit = true;
        mask.GetParent().RemoveChild(mask);
        Require(mask.BindingCount == 0, "mask exit failure bypassed unbind");
        mask.ThrowOnExit = false;
    }

    private void FitExitFailureStillUnbinds()
    {
        var fit = new TestFitScaleBox();
        fit.AddChild(new Control());
        _stage.AddChild(fit);
        var field = typeof(FitScaleBox).GetField("_binder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var binder = (BindingScope)field.GetValue(fit)!;
        Require(binder.Count > 0, "fit fixture has no subscription");
        _stage.RemoveChild(fit);
        Require(binder.Count == 0, "fit exit failure bypassed unbind");
        fit.QueueFree();
    }

    private void UnregisterOwnerAdvancesQueue()
    {
        var current = Seed(owner: "remove");
        var sameOwner = Seed(owner: "remove");
        var other = Seed(owner: "other");
        current.Win.DelayPreload = true;
        var oldTask = _manager.OpenAsync(current.Vo.Id);
        var sameTask = _manager.OpenAsync(sameOwner.Vo.Id);
        var nextTask = _manager.OpenAsync(other.Vo.Id);
        _manager.UnregisterOwner("remove");
        Rejected(oldTask);
        Rejected(sameTask);
        Opened(nextTask, other.Vo);
        Require(_manager.GetUIVo(current.Vo.Id) == null && _manager.GetUIVo(sameOwner.Vo.Id) == null, "unregistered owner retained VO");
        Rejected(_manager.OpenAsync(current.Vo.Id));
    }

    private void CachedPreloadGetsFreshDeadline()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        s.Vo.Lifecycle.LoadTime = 1;
        var task = _manager.OpenAsync(s.Vo.Id);
        _manager.OpenCoordinator.CheckLoadTimeout();
        Require(!task.IsCompleted && s.Vo.StateMachine.CurrentState == EUIState.PreLoad, "cached preload used historical timeout");
        s.Win.Preloads[0].Done();
        Opened(task, s.Vo);
    }

    private void TimeoutCallbackReentry()
    {
        var s = Seed();
        var next = Seed();
        s.Win.DelayPreload = next.Win.DelayPreload = true;
        Task<UIVo?>? nextTask = null;
        var failed = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnFail = () => nextTask = _manager.OpenAsync(next.Vo.Id) });
        s.Vo.Lifecycle.LoadTime = 1;
        _manager.OpenCoordinator.CheckLoadTimeout();
        Rejected(failed);
        Require(_manager.OpenCoordinator.CurrentOpening == next.Vo, "timeout erased request started by failure callback");
        next.Win.Preloads[0].Done();
        Opened(nextTask!, next.Vo);
    }

    #endregion

    #region 冷加载与自定义父节点

    private void ColdSceneLifecycle()
    {
        Register("headless.window");
        var task = _manager.OpenAsync("headless.window");
        var vo = _manager.GetUIVo("headless.window")!;
        Opened(task, vo);
        var win = (TestWindow)vo.UI!;
        Require(win.Creates == 1, "cold scene did not create exactly once");
        _manager.Close(vo.Id);
        Opened(_manager.OpenAsync(vo.Id), vo);
        Require(vo.UI == win && win.Creates == 1 && win.BindingCount == 1, "cold scene cache lifecycle duplicated create/bind");
    }

    private async Task ColdMaskLifecycleAsync()
    {
        Register("headless.window");
        Register("headless.mask", EUIType.Dlg);
        var opt = new UIOpenOpt { Mask = new UIMaskOpt { Runtime = "headless.mask" } };
        var task = _manager.OpenAsync("headless.window", openOpt: opt);
        var vo = _manager.GetUIVo("headless.window")!;
        Opened(task, vo);
        var mask = vo.Mask;
        Require(mask is TestMask && mask.UIVo == vo, "cold mask not created/bound");
        _manager.Close(vo.Id);
        await NextFramesAsync();
        Opened(_manager.OpenAsync(vo.Id, openOpt: opt), vo);
        Require(vo.Mask == mask && GodotObject.IsInstanceValid(mask), "cold mask cache reopen failed");
    }

    private void FailedResourceFactory()
    {
        Register("headless.window");
        Rejected(_manager.OpenAsync("headless.window", openOpt: new UIOpenOpt
        {
            PreLoadResList = _ => throw new InvalidOperationException("expected preload resource factory failure"),
        }));
        Require(_manager.GetUIVo("headless.window") == null, "failed resource factory left VO loading");
        Rejected(_manager.OpenAsync("unregistered", openOpt: new UIOpenOpt { OnFail = () => throw new InvalidOperationException("expected rejected callback failure") }));
    }

    private void CustomParentMaskCache()
    {
        var s = Seed(EUIType.Pge);
        var mask = AttachMask(s.Vo);
        var first = new Control();
        var second = new Control();
        _stage.AddChild(first);
        _stage.AddChild(second);
        _ = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { Parent = first });
        _manager.Close(s.Vo.Id);
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "custom parent retained cached mask");
        Opened(_manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { Parent = second }), s.Vo);
        Require(s.Win.GetParent() == second && mask.GetParent() == second, "cached page did not reparent both nodes");
        first.QueueFree();
        // second owns the active fixture and is released after its UI in Cleanup.
        second.CallDeferred(Node.MethodName.QueueFree);
    }

    #endregion
}