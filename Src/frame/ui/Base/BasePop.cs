using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 气泡基类
/// </summary>
public abstract partial class BasePop : BaseWin
{
    public override EUIType UIType => EUIType.Pop;

    public override UIOpenOpt? BaseOpenOpt => new()
    {
        Layer = EUILayer.Pop,
        Align = EUIAlign.None,
        HideBelow = false,
    };
}