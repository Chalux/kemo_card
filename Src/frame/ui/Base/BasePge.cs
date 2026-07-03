using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 页面基类
/// </summary>
public abstract partial class BasePge : BaseWin
{
    public override EUIType UIType => EUIType.Pge;

    public override UIOpenOpt? BaseOpenOpt => new()
    {
        Align = EUIAlign.Full,
        HideBelow = false,
    };
}