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
}