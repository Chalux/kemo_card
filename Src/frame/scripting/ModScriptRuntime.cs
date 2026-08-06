using System.Collections.Concurrent;
using KemoCard.Frame.Content;
using Puerts;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptRuntime : IScriptRuntimeResetter, IDisposable
{
    private readonly ModScriptCatalog _catalog;
    private readonly GameDefinitionRegistry _registry;
    private readonly IModScriptLogger _logger;
    private readonly ModScriptLoader _loader;
    private readonly ConcurrentDictionary<(string ModId, string ScriptPath, string Entry), Func<ScriptContextFacade, object>> _entryCache = new();
    private readonly ReaderWriterLockSlim _runtimeLock = new(LockRecursionPolicy.NoRecursion);
    private ScriptEnv? _env;
    private volatile bool _rebuildGate;

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
        _runtimeLock.EnterWriteLock();
        try
        {
            _rebuildGate = true;
        }
        finally
        {
            _runtimeLock.ExitWriteLock();
        }
    }

    public void Recreate()
    {
        _runtimeLock.EnterWriteLock();
        try
        {
            _entryCache.Clear();
            _env?.Dispose();
            _env = new ScriptEnv(new BackendV8(_loader));
        }
        finally
        {
            _runtimeLock.ExitWriteLock();
        }
    }

    public void EndRebuild()
    {
        _runtimeLock.EnterWriteLock();
        try
        {
            _rebuildGate = false;
        }
        finally
        {
            _runtimeLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 仅 import 指定模块（不执行入口），供预热在 Rebuild 窗口内调用。
    /// 预热是 Rebuild 内部步骤（Recreate 之后、EndRebuild 之前）执行，因此不做 _rebuildGate 拦截；
    /// 并发访问由 _runtimeLock 读锁保护，与 Invoke 共用同一把锁。
    /// </summary>
    public bool TryLoadModule(string modId, string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        try
        {
            _runtimeLock.EnterReadLock();
            try
            {
                EnsureEnv();
                _env!.ExecuteModule(BuildSpecifier(modId, scriptPath));
            }
            finally
            {
                _runtimeLock.ExitReadLock();
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
            _runtimeLock.EnterReadLock();
            try
            {
                if (_rebuildGate)
                {
                    return new ModScriptInvokeResult { Success = false, Error = "Script runtime is rebuilding." };
                }

                EnsureEnv();
                var cacheKey = (modId, scriptPath, entry);
                var fn = _entryCache.GetOrAdd(cacheKey, _ =>
                {
                    var specifier = BuildSpecifier(modId, scriptPath);
                    var module = _env!.ExecuteModule(specifier);
                    return module.Get<Func<ScriptContextFacade, object>>(entry);
                });

                var facade = new ScriptContextFacade(callContext, _logger);
                var rawReturn = fn(facade);
                return new ModScriptInvokeResult { Success = true, RawReturn = rawReturn };
            }
            finally
            {
                _runtimeLock.ExitReadLock();
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Invoke failed: {modId}/{scriptPath}#{entry}: {ex.Message}");
            return new ModScriptInvokeResult { Success = false, Error = ex.Message };
        }
    }

    public void Dispose()
    {
        _runtimeLock.EnterWriteLock();
        try
        {
            _env?.Dispose();
            _env = null;
            _entryCache.Clear();
        }
        finally
        {
            _runtimeLock.ExitWriteLock();
        }

        _runtimeLock.Dispose();
    }

    private void EnsureEnv()
    {
        if (_env is null)
        {
            throw new InvalidOperationException("ModScriptRuntime is disposed.");
        }
    }

    private static string BuildSpecifier(string modId, string scriptPath) =>
        $"{modId}/{scriptPath.Replace('\\', '/')}";
}