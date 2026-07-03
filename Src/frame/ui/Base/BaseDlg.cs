using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 对话框基类
/// </summary>
public abstract partial class BaseDlg : BaseWin
{
    public override EUIType UIType => EUIType.Dlg;

    public override UIOpenOpt? BaseOpenOpt => new()
    {
        Layer = EUILayer.Dlg,
        Align = EUIAlign.Center,
        HideBelow = false,
    };
}
