using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Fixed.Godot;

public sealed class GodotAppLog : IAppLog
{
    private readonly bool _enableVerbose;

    public GodotAppLog(bool? enableVerbose = null)
    {
        _enableVerbose = enableVerbose ?? OS.IsDebugBuild();
    }

    /// <summary>调试级输出：仅开发构建可见（跟随 <c>OS.IsDebugBuild()</c>）。</summary>
    public void Debug(string message, string? category = null)
    {
        if (_enableVerbose)
            GD.Print(Format(message, category));
    }

    /// <summary>
    /// 信息级输出：始终可见，<b>不受</b> <c>OS.IsDebugBuild()</c> 门控。
    /// 导出包中 Mod 脚本日志走的就是这一级（<c>AppLogModScriptLogger</c>），
    /// 若与 Debug 一起被门控，出货版本的 Mod 日志会全部消失，线上问题无从排查。
    /// </summary>
    public void Info(string message, string? category = null) =>
        GD.Print(Format(message, category));

    public void Warning(string message, string? category = null) =>
        GD.PushWarning(Format(message, category));

    public void Error(string message, string? category = null) =>
        GD.PushError(Format(message, category));

    private static string Format(string message, string? category) =>
        string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
}