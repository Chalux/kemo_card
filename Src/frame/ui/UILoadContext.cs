using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 异步加载上下文（flag、token），从 UIVo 中拆出的独立对象。
/// </summary>
public sealed class UILoadContext
{
    public int LoadFlag { get; set; }
    public int PreLoadFlag { get; set; }
    public bool HasMaskLoaded { get; set; }
    public CancellationTokenSource LoadToken { get; private set; } = new();

    /// <summary>同一 VO 开始新请求：立即使旧回调失效，再替换取消源。</summary>
    public void BeginRequest()
    {
        IncrementLoadFlag();
        IncrementPreLoadFlag();
        var previous = LoadToken;
        LoadToken = new CancellationTokenSource();
        try { CancelToken(previous); }
        finally { previous.Dispose(); }
    }

    public void Cancel()
    {
        IncrementLoadFlag();
        IncrementPreLoadFlag();
        CancelToken(LoadToken);
    }

    private static void CancelToken(CancellationTokenSource token)
    {
        try
        {
            token.Cancel();
        }
        catch (ObjectDisposedException) { }
        catch (AggregateException ex) { AppLog.Error($"UI 加载取消回调失败：{ex.Message}", "UI"); }
    }

    public int IncrementLoadFlag() => ++LoadFlag;

    public int IncrementPreLoadFlag() => ++PreLoadFlag;
}