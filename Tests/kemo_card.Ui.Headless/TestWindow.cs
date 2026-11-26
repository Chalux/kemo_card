using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Ui.Headless;

public enum EHeadlessEvent
{
    [EventPayload(typeof(HeadlessPingPayload))]
    Ping,

    [EventPayload(typeof(HeadlessPingPayload))]
    GlobalPing,
}

public readonly struct HeadlessPingPayload
{
}

public sealed class HeadlessEventFacade : IEventBusProvider
{
    /// <summary>可替换：模拟 QuickLoad 后 RunController / InternalBus 被换成新实例。</summary>
    public EventDispatcher EventBus { get; set; } = new();
}

/// <summary>把测试窗口（owner = tests）的门面解析到共享的测试总线。</summary>
public sealed class HeadlessFacadeProvider(HeadlessEventFacade facade) : IUiFacadeProvider
{
    public object? ResolveUiFacade(string ownerModId) => ownerModId == "tests" ? facade : null;
}

public partial class TestWindow : BaseWin
{
    public string TestId { get; set; } = "headless.window";
    public EUIType TestType { get; set; } = EUIType.Win;
    public override string UIId => TestId;
    public override string UIDir => "Tests/kemo_card.Ui.Headless";
    public override EUIType UIType => TestType;
    public bool DelayPreload { get; set; }
    public bool DelayClose { get; set; }
    public bool DelayOpen { get; set; }
    public bool ThrowOnExit { get; set; }
    public bool ThrowOnOpen { get; set; }
    public bool ThrowOnCreate { get; set; }
    public bool ThrowOnCloseAnimation { get; set; }
    public Action? VisibilityAction { get; set; }
    public int Creates { get; private set; }
    public int Clicks { get; private set; }
    public int VisibleUpdates { get; private set; }
    public int CancelledAnimations { get; private set; }
    public int BindingCount => Binder.Count;
    public Button ClickButton { get; private set; } = null!;
    public List<(Action Done, Action Fail)> Preloads { get; } = [];
    public List<Action> CloseAnimations { get; } = [];
    public List<Action> OpenAnimations { get; } = [];

    #region 生命周期

    protected override void OnReady()
    {
        ClickButton = new Button();
        AddChild(ClickButton);
    }
    protected override void InitEvent() => Binder.OnPressed(ClickButton, () => Clicks++);
    protected override void OnPreLoad(Action done, Action fail)
    {
        if (DelayPreload) Preloads.Add((done, fail));
        else done();
    }
    protected override void OnCreate()
    {
        Creates++;
        if (ThrowOnCreate) throw new InvalidOperationException("expected create hook failure");
    }
    protected override void OnOpen()
    {
        if (ThrowOnOpen) throw new InvalidOperationException("expected open hook failure");
    }
    protected override Action? OnOpenAnim(Action done)
    {
        if (DelayOpen) OpenAnimations.Add(done);
        else done();
        return () => CancelledAnimations++;
    }
    protected override Action? OnCloseAnim(Action done)
    {
        if (ThrowOnCloseAnimation) throw new InvalidOperationException("expected close animation failure");
        if (DelayClose) CloseAnimations.Add(done);
        else done();
        return () => CancelledAnimations++;
    }
    protected override void OnLayerVisibleUpdate()
    {
        VisibleUpdates++;
        VisibilityAction?.Invoke();
    }
    protected override void UpdateView() { }
    protected override void OnExitTree()
    {
        if (ThrowOnExit) throw new InvalidOperationException("expected exit hook failure");
    }

    #endregion
}

/// <summary>
/// 带装饰器事件订阅的测试窗口。独立子类是为了不改动
/// <see cref="TestWindow"/> 的「InitEvent 只有一条订阅」断言。
/// </summary>
public partial class DecoratedTestWindow : TestWindow
{
    public int PingCount { get; private set; }
    public int GlobalPingCount { get; private set; }

    [EventSubscribe(EHeadlessEvent.Ping)]
    private void OnHeadlessPing() => PingCount++;

    [EventSubscribe(EHeadlessEvent.GlobalPing, Bus = EventBusScope.Global)]
    private void OnHeadlessGlobalPing() => GlobalPingCount++;
}

/// <summary>带装饰器订阅的组件，用于验证窗口已挂载时再次 Open 会重绑到当前功能总线。</summary>
public partial class DecoratedTestCmp : BaseCmp
{
    public int PingCount { get; private set; }

    [EventSubscribe(EHeadlessEvent.Ping)]
    private void OnHeadlessPing() => PingCount++;
}