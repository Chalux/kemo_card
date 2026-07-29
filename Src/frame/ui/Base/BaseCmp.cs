using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 组件基类
/// </summary>
public abstract partial class BaseCmp : BaseUI
{
    public override EUIType UIType => EUIType.Cmp;
    public override string UIId => GetType().Name;
    public override string UIDir => string.Empty;

    protected override void OnReady()
    {
        base.OnReady();
        InitEvent();
    }

    public override void _ExitTree()
    {
        OnUnbind();
        base._ExitTree();
    }

    protected virtual void InitEvent() { }
    protected virtual void OnUnbind() { }
}