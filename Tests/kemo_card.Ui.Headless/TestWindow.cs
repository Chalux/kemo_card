using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Ui.Headless;

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