namespace KemoCard.Frame.UI.Def;

public static class DefaultUIOpenOpt
{
    /// <summary>
    /// 合并基线：字段<b>全部显式指定</b>，保证任何合并结果都是具体值。
    /// <see cref="UIOpenOpt.MergeFrom"/> 只覆盖显式设置的字段，因此基线必须完整。
    /// </summary>
    public static readonly UIOpenOpt Value = new()
    {
        Layer = EUILayer.Dlg,
        CacheTime = UIOpenOpt.DefaultCacheTime,
        AnimType = EAnimType.SkipReOpen,
        HideBelow = false,
        NoCover = false,
        Align = EUIAlign.Center,
    };

    /// <summary>
    /// 按界面类型给出各 Base* 基类的默认打开参数（BaseWin / BaseDlg / BasePge / BasePop 与
    /// UIRegistration 工厂的唯一来源，避免两处各写一份而漂移）。
    /// 每次调用返回新实例，调用方可安全修改。
    /// </summary>
    /// <remarks>
    /// 只设置与基线<b>不同</b>的字段：未设置的字段保持 <c>null</c>（「未指定」），
    /// 合并时不会覆盖下层来源（例如调用点显式传入的值）。
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
        },
        EUIType.Pge => new UIOpenOpt
        {
            // 页面靠 Parent 挂载，不固定层级。
            Align = EUIAlign.Full,
        },
        EUIType.Pop => new UIOpenOpt
        {
            Layer = EUILayer.Pop,
            Align = EUIAlign.None,
        },
        _ => new UIOpenOpt(),
    };
}