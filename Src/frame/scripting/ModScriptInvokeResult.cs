namespace KemoCard.Frame.Scripting;

public sealed class ModScriptInvokeResult
{
	public bool Success { get; init; }

	public object? RawReturn { get; init; }

	public string? Error { get; init; }
}
