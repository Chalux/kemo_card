using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ScriptCallContext
{
    public required int RunSeed { get; init; }

    public required string StreamKey { get; init; }

    public required GameDefinitionRegistry Registry { get; init; }

    public int Layer { get; init; }

    public string? CallerId { get; init; }
}