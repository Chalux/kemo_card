namespace KemoCard.Frame.Scripting;

public sealed class NullScriptRuntimeResetter : IScriptRuntimeResetter
{
	public static NullScriptRuntimeResetter Instance { get; } = new();

	public void Recreate()
	{
	}
}
