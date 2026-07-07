namespace KemoCard.Frame.UI.Def;

/// <summary>
/// 状态机流转时传递的强类型数据，替代魔法字符串。
/// 用于在 UILoadStateHandler -> UIOpenStateHandler 等链路中传递打开语义。
/// </summary>
public readonly record struct OpenTransitionData
{
    /// <summary>是否为重新打开（已打开过的 UI 再次打开）</summary>
    public bool IsReopen { get; init; }

    /// <summary>是否为关闭后重新打开（CloseDone/Cache -> 重新打开）</summary>
    public bool IsCloseOpen { get; init; }

    /// <summary>首次打开</summary>
    public static OpenTransitionData FirstOpen => default;

    /// <summary>通过缓存路径重新打开</summary>
    public static OpenTransitionData Reopen => new() { IsReopen = true };

    /// <summary>关闭后重新打开</summary>
    public static OpenTransitionData CloseOpen => new() { IsCloseOpen = true, IsReopen = true };
}
