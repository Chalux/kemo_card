using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class StoryScriptInvoker
{
	private readonly ModScriptRuntime _runtime;

	public StoryScriptInvoker(ModScriptRuntime runtime, GameDefinitionRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(registry);
		_runtime = runtime;
	}

	public bool TryGenerateOptions(
		string modId,
		string scriptPath,
		string scriptEntry,
		ScriptCallContext callContext,
		out IReadOnlyList<StoryScriptOption> options)
	{
		options = Array.Empty<StoryScriptOption>();
		var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
		var result = _runtime.Invoke(modId, scriptPath, entry, callContext);
		if (!result.Success || result.RawReturn is null)
		{
			return false;
		}

		return ModScriptResultParser.TryParseStoryOptions(result.RawReturn, out options, out _);
	}
}

public sealed class EventScriptInvoker
{
	private readonly ModScriptRuntime _runtime;

	public EventScriptInvoker(ModScriptRuntime runtime, GameDefinitionRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(registry);
		_runtime = runtime;
	}

	public bool TryRunEventScript(
		string modId,
		string scriptPath,
		string scriptEntry,
		ScriptCallContext callContext,
		out EventScriptResult eventResult)
	{
		eventResult = EventScriptResult.Empty;
		var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
		var result = _runtime.Invoke(modId, scriptPath, entry, callContext);
		if (!result.Success || result.RawReturn is null)
		{
			return false;
		}

		return ModScriptResultParser.TryParseEventResult(result.RawReturn, out eventResult, out _);
	}
}

public sealed class BattleScriptInvoker
{
	private readonly ModScriptRuntime _runtime;

	public BattleScriptInvoker(ModScriptRuntime runtime, GameDefinitionRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(registry);
		_runtime = runtime;
	}

	public bool TryRunBattleScript(
		string modId,
		string scriptPath,
		string scriptEntry,
		ScriptCallContext callContext,
		out BattleScriptResult battleResult)
	{
		battleResult = BattleScriptResult.Empty;
		var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
		var result = _runtime.Invoke(modId, scriptPath, entry, callContext);
		if (!result.Success || result.RawReturn is null)
		{
			return false;
		}

		return ModScriptResultParser.TryParseBattleResult(result.RawReturn, out battleResult, out _);
	}
}

public sealed class EnemyAiScriptInvoker
{
	private readonly ModScriptRuntime _runtime;

	public EnemyAiScriptInvoker(ModScriptRuntime runtime, GameDefinitionRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(registry);
		_runtime = runtime;
	}

	public bool TryChooseSkill(
		string modId,
		string scriptPath,
		string scriptEntry,
		ScriptCallContext callContext,
		out EnemyAiScriptResult aiResult)
	{
		aiResult = EnemyAiScriptResult.Empty;
		var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
		var result = _runtime.Invoke(modId, scriptPath, entry, callContext);
		if (!result.Success || result.RawReturn is null)
		{
			return false;
		}

		return ModScriptResultParser.TryParseEnemyAiResult(result.RawReturn, out aiResult, out _);
	}
}
