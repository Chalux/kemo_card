namespace KemoCard.Frame.UI;

/// <summary>
/// UI 异步加载上下文（flag、token），从 UIVo 中拆出的独立对象。
/// </summary>
public sealed class UILoadContext
{
    public int LoadFlag { get; set; }
    public int PreLoadFlag { get; set; }
    public bool HasMaskLoaded { get; set; }
    public CancellationTokenSource LoadToken { get; } = new();

    public void Cancel()
    {
        try
        {
            LoadToken.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    public int IncrementLoadFlag() => ++LoadFlag;

    public int IncrementPreLoadFlag() => ++PreLoadFlag;
}