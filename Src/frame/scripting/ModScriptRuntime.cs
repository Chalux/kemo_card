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
	private ScriptEnv? _env;
	private readonly object _gate = new();
	private volatile bool _rebuildGate;
	private int _generation;

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
		_rebuildGate = true;
	}

	public void Recreate()
	{
		lock (_gate)
		{
			_generation++;
			_entryCache.Clear();
			_env?.Dispose();
			_env = new ScriptEnv(new BackendV8(_loader));
		}
	}

	public void EndRebuild()
	{
		_rebuildGate = false;
	}

	public bool TryLoadModule(string modId, string scriptPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

		if (_rebuildGate)
		{
			_logger.Log($"TryLoadModule skipped during rebuild: {modId}/{scriptPath}");
			return false;
		}

		try
		{
			lock (_gate)
			{
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

		if (_rebuildGate)
		{
			return new ModScriptInvokeResult { Success = false, Error = "Script runtime is rebuilding." };
		}

		try
		{
			Func<ScriptContextFacade, object> fn;
			var generation = 0;
			lock (_gate)
			{
				EnsureEnv();
				generation = _generation;
				var cacheKey = (modId, scriptPath, entry);
				fn = _entryCache.GetOrAdd(cacheKey, _ =>
				{
					var specifier = BuildSpecifier(modId, scriptPath);
					var module = _env!.ExecuteModule(specifier);
					return module.Get<Func<ScriptContextFacade, object>>(entry);
				});
			}

			if (_rebuildGate || generation != _generation)
			{
				return new ModScriptInvokeResult { Success = false, Error = "Script runtime was recreated during invoke." };
			}

			var facade = new ScriptContextFacade(callContext, _logger);
			var rawReturn = fn(facade);
			return new ModScriptInvokeResult { Success = true, RawReturn = rawReturn };
		}
		catch (Exception ex)
		{
			_logger.Log($"Invoke failed: {modId}/{scriptPath}#{entry}: {ex.Message}");
			return new ModScriptInvokeResult { Success = false, Error = ex.Message };
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
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

	private static string BuildSpecifier(string modId, string scriptPath) =>
		$"{modId}/{scriptPath.Replace('\\', '/')}";
}
