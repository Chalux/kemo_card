using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace KemoCard.Mod;

/// <summary>
/// 全局应用根：提供对 ModFactory.Bootstrap() 初始化结果的静态全局访问入口。
/// 调用方通过 <c>AppRoot.Services.XXX</c> 即可获取各核心服务的运行时实例。
/// 仅在 MainRoot._Ready() 完成 Bootstrap 后可用；过早访问会抛出 InvalidOperationException。
/// </summary>
public static class AppRoot
{
    private static ModStartupResult? _services;
    private static bool _unloadHookRegistered;

    /// <summary>
    /// 获取引导完成的全局服务集合。在 Bootstrap 调用后始终非 null。
    /// </summary>
    /// <exception cref="InvalidOperationException">在 Bootstrap 尚未执行时访问。</exception>
    public static ModStartupResult Services =>
        _services ?? throw new InvalidOperationException(
            "AppRoot.Services 尚未初始化，请确保 MainRoot 已调用 ModFactory.Bootstrap()。");

    internal static void Initialize(ModStartupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_services != null)
        {
            Shutdown();
        }

        _services = result;
        EnsureUnloadHook();
    }

    /// <summary>
    /// 释放 Bootstrap 持有的原生/静态资源（尤其是 Puerts ScriptEnv），避免编辑器热重载时 ALC 无法卸载。
    /// </summary>
    public static void Shutdown()
    {
        var services = _services;
        _services = null;
        if (services == null)
        {
            return;
        }

        try
        {
            services.ScriptRuntime.Dispose();
        }
        catch (Exception)
        {
            // 退出/热重载路径上忽略二次释放异常
        }
    }

    private static void EnsureUnloadHook()
    {
        if (_unloadHookRegistered)
        {
            return;
        }

        var alc = AssemblyLoadContext.GetLoadContext(typeof(AppRoot).Assembly);
        if (alc == null)
        {
            return;
        }

        alc.Unloading += OnAssemblyUnloading;
        _unloadHookRegistered = true;
    }

    private static void OnAssemblyUnloading(AssemblyLoadContext _)
    {
        Shutdown();
        ClearSystemTextJsonCache();
    }

    /// <summary>
    /// System.Text.Json 会缓存程序集相关元数据，阻止 ALC 卸载（见 godot#78513）。
    /// </summary>
    private static void ClearSystemTextJsonCache()
    {
        try
        {
            var assembly = typeof(JsonSerializerOptions).Assembly;
            var updateHandlerType = assembly.GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler");
            var clearCacheMethod = updateHandlerType?.GetMethod(
                "ClearCache",
                BindingFlags.Static | BindingFlags.Public);
            clearCacheMethod?.Invoke(null, [null]);
        }
        catch (Exception)
        {
            // 清理失败不应阻断卸载流程
        }
    }
}