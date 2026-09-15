using KemoCard.Frame.Content;
using Puerts;

namespace KemoCard.Frame.Scripting;

/// <summary>
/// Mod 脚本运行时（PuerTS / V8 宿主）。
/// </summary>
/// <remarks>
/// <para><b>线程模型：</b><c>ScriptEnv</c> 不是线程安全的，因此所有进入 JS 的路径
/// （<see cref="Invoke"/>、<see cref="TryLoadModule"/>）与生命周期操作（<see cref="Recreate"/>、
/// <see cref="Dispose"/>）共用同一把互斥锁串行执行。</para>
/// <para>此前用的是 <c>ReaderWriterLockSlim</c> 读锁：读锁之间不互斥，等于允许多个线程并发进入同一个
/// <c>ScriptEnv</c>；同时 <c>ConcurrentDictionary.GetOrAdd</c> 的工厂在并发下可能被多次执行，
/// 使同一模块被 <c>ExecuteModule</c> 跑两遍、模块级状态分裂。锁本身也无法表达「本类仅主线程调用」
/// 这一约定，所以改为普通互斥锁（<c>Monitor</c> 可重入，不会因脚本回调再次进入而抛
/// <c>LockRecursionException</c>）。</para>
/// </remarks>
public sealed class ModScriptRuntime : IScriptRuntimeResetter, IDisposable
{
    private readonly ModScriptCatalog _catalog;
    private readonly GameDefinitionRegistry _registry;
    private readonly IModScriptLogger _logger;
    private readonly ModScriptLoader _loader;
    private readonly Dictionary<(string ModId, string ScriptPath, string Entry), Func<ScriptContextFacade, object>> _entryCache = [];
    private readonly object _gate = new();

    /// <summary>
    /// 创建本运行时的线程 id。PuerTS 的 V8 isolate 与创建线程绑定，跨线程进入会以
    /// 难以诊断的方式失败（实测为 V8 的 <c>RangeError: Maximum call stack size exceeded</c>）。
    /// 因此互斥锁只负责「同线程重入 / 串行化」，真正的跨线程调用必须显式拒绝。
    /// </summary>
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    private ScriptEnv? _env;
    private bool _rebuildGate;
    private bool _disposed;

    public ModScriptRuntime(
        ModScriptCatalog catalog,
        GameDefinitionRegistry registry,
        IModScriptLogger logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _registry = registry;
        _logger = logger;
        _loader = new ModScriptLoader(catalog);
        Recreate();
    }

    public void BeginRebuild()
    {
        lock (_gate)
        {
            _rebuildGate = true;
        }
    }

    public void Recreate()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _entryCache.Clear();
            _env?.Dispose();
            _env = new ScriptEnv(new BackendV8(_loader));
        }
    }

    public void EndRebuild()
    {
        lock (_gate)
        {
            _rebuildGate = false;
        }
    }

    /// <summary>
    /// 仅 import 指定模块（不执行入口），供预热在 Rebuild 窗口内调用。
    /// 预热是 Rebuild 内部步骤（Recreate 之后、EndRebuild 之前）执行，因此不做 _rebuildGate 拦截；
    /// 并发访问由 <see cref="_gate"/> 串行保护，与 <see cref="Invoke"/> 共用同一把锁。
    /// </summary>
    public bool TryLoadModule(string modId, string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        try
        {
            lock (_gate)
            {
                EnsureOwnerThread();
                EnsureEnv();
                _env!.ExecuteModule(BuildSpecifier(modId, scriptPath));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Log($"TryLoadModule failed: {modId}/{scriptPath}: {ex.Message}");
            return false;
        }
    }

    public ModScriptInvokeResult Invoke(
        string modId,
        string scriptPath,
        string entry,
        ScriptCallContext callContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        ArgumentNullException.ThrowIfNull(callContext);

        try
        {
            lock (_gate)
            {
                if (_rebuildGate)
                {
                    return new ModScriptInvokeResult { Success = false, Error = "Script runtime is rebuilding." };
                }

                EnsureOwnerThread();
                EnsureEnv();
                var cacheKey = (modId, scriptPath, entry);
                if (!_entryCache.TryGetValue(cacheKey, out var fn))
                {
                    var specifier = BuildSpecifier(modId, scriptPath);
                    var module = _env!.ExecuteModule(specifier);
                    fn = module.Get<Func<ScriptContextFacade, object>>(entry);
                    _entryCache[cacheKey] = fn;
                }

                var facade = new ScriptContextFacade(callContext, _logger);
                var rawReturn = fn(facade);
                return new ModScriptInvokeResult { Success = true, RawReturn = rawReturn };
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Invoke failed: {modId}/{scriptPath}#{entry}: {ex.Message}");
            return new ModScriptInvokeResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 幂等释放：重复调用、以及在释放后调用 <see cref="Recreate"/> 都安全。
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _env?.Dispose();
            _env = null;
            _entryCache.Clear();
        }
    }

    private void EnsureEnv()
    {
        if (_env is null)
        {
            throw new InvalidOperationException("ModScriptRuntime is disposed.");
        }
    }

    /// <summary>
    /// 拒绝跨线程进入 JS。互斥锁无法让 V8 isolate 变得线程安全，只会把
    /// 「并发进入」变成「串行但仍在错误的线程上执行」，故障现象依旧隐蔽。
    /// </summary>
    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"ModScriptRuntime 只能在创建它的线程（id={_ownerThreadId}）上调用；"
                + $"当前线程 id={Environment.CurrentManagedThreadId}。PuerTS/V8 isolate 非线程安全。");
        }
    }

    private static string BuildSpecifier(string modId, string scriptPath) =>
        $"{modId}/{scriptPath.Replace('\\', '/')}";
}