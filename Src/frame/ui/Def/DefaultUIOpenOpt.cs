namespace KemoCard.Frame.UI.Def;

public static class DefaultUIOpenOpt
{
    public static readonly UIOpenOpt Value = new()
    {
        Layer = EUILayer.Dlg,
        CacheTime = 30000,
        AnimType = EAnimType.SkipReOpen,
        Align = EUIAlign.Center,
    };

    /// <summary>
    /// 按界面类型给出各 Base* 基类的默认打开参数（BaseWin / BaseDlg / BasePge / BasePop 与
    /// UIRegistration 工厂的唯一来源，避免两处各写一份而漂移）。
    /// 每次调用返回新实例，调用方可安全修改。
    /// </summary>
    /// <remarks>
    /// 未显式设置的字段保持 <see cref="UIOpenOpt"/> 的默认值（CacheTime=30000、AnimType=SkipReOpen、
    /// NoCover=false），因为 <c>MergeInto</c> 对这些字段是无条件覆盖的：只有与
    /// <see cref="Value"/> 同值，合并才是幂等的。
    /// </remarks>
    public static UIOpenOpt ForType(EUIType type) => type switch
    {
        EUIType.Win => new UIOpenOpt
        {
            Layer = EUILayer.Win,
            Align = EUIAlign.Full,
            HideBelow = true,
        },
        EUIType.Dlg => new UIOpenOpt
        {
            Layer = EUILayer.Dlg,
            Align = EUIAlign.Center,
            HideBelow = false,
        },
        EUIType.Pge => new UIOpenOpt
        {
            Align = EUIAlign.Full,
            HideBelow = false,
        },
        EUIType.Pop => new UIOpenOpt
        {
            Layer = EUILayer.Pop,
            Align = EUIAlign.None,
            HideBelow = false,
        },
        _ => new UIOpenOpt(),
    };
}