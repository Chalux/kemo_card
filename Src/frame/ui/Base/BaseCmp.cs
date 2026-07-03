using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 组件基类
/// </summary>
public abstract partial class BaseCmp : BaseUI
{
    public override EUIType UIType => EUIType.Cmp;
}