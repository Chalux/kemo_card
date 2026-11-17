namespace KemoCard.Frame.UI.Def;

public static class UIConsts
{
    /// <summary>Load / PreLoad 阶段单次加载允许的最长耗时（毫秒）。</summary>
    /// <remarks>加载本身是同步的（见 UIResourceLoader），这里兜住的是业务 OnPreLoad 不回调 done/fail 的情况。</remarks>
    public const long UI_LOAD_TIMEOUT_MS = 10_000;

    /// <summary>Close 阶段关闭动画允许的最长耗时（毫秒）；超时强制进入 CloseDone，避免界面永久卡在关闭中。</summary>
    public const long UI_CLOSE_TIMEOUT_MS = 5_000;
}