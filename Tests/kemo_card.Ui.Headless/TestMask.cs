using KemoCard.Frame.UI.Base;

namespace KemoCard.Ui.Headless;

public partial class TestMask : BaseMask
{
    public bool DelayClose { get; set; }
    public bool ThrowOnExit { get; set; }
    public int Closed { get; private set; }
    public int BindingCount => Binder.Count;
    public List<Action> CloseAnimations { get; } = [];
    public void AddBinding() => Binder.Add(() => { });
    protected override Action? OnCloseAnim(Action done)
    {
        if (DelayClose) CloseAnimations.Add(done);
        else done();
        return () => { };
    }
    protected override void OnClose() => Closed++;
    protected override void OnExitTree()
    {
        if (ThrowOnExit) throw new InvalidOperationException("expected mask exit failure");
    }
}