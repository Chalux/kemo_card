namespace KemoCard.Frame.Content;

/// <summary>
/// 效果脚本宿主桩。Mod 脚本位于 <c>scripts/</c> 下相对路径（如 <c>effects/strike.ts</c>）。
/// 脚本入口默认 execute，通过 ExecuteScript 效果的 scriptEntry 覆盖。
/// </summary>
public interface IContentEffectScriptHost
{
	bool TryExecute(
		string modId,
		string scriptPath,
		string scriptEntry,
		IReadOnlyDictionary<string, object>? context,
		out IReadOnlyList<Dictionary<string, object>> proposedEffects);
}

public sealed class NullContentEffectScriptHost : IContentEffectScriptHost
{
	public bool TryExecute(
		string modId,
		string scriptPath,
		string scriptEntry,
		IReadOnlyDictionary<string, object>? context,
		out IReadOnlyList<Dictionary<string, object>> proposedEffects)
	{
		proposedEffects = Array.Empty<Dictionary<string, object>>();
		return false;
	}
}
