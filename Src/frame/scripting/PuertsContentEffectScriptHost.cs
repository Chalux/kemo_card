using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class PuertsContentEffectScriptHost : IContentEffectScriptHost
{
    private readonly ModScriptRuntime _runtime;
    private readonly GameDefinitionRegistry _registry;

    public PuertsContentEffectScriptHost(ModScriptRuntime runtime, GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(registry);
        _runtime = runtime;
        _registry = registry;
    }

    public bool TryExecute(
        string modId,
        string scriptPath,
        string scriptEntry,
        IReadOnlyDictionary<string, object>? context,
        out IReadOnlyList<Dictionary<string, object>> proposedEffects)
    {
        proposedEffects = Array.Empty<Dictionary<string, object>>();
        var entry = string.IsNullOrWhiteSpace(scriptEntry) ? "execute" : scriptEntry;
        var call = new ScriptCallContext
        {
            RunSeed = ExtractInt(context, "runSeed", 0),
            StreamKey = $"effect:{modId}:{scriptPath}:{entry}",
            Registry = _registry,
            CallerId = ExtractString(context, "effectId"),
        };

        var result = _runtime.Invoke(modId, scriptPath, entry, call);
        if (!result.Success || result.RawReturn is null)
        {
            return false;
        }

        return ModScriptResultParser.TryParseProposedEffects(result.RawReturn, out proposedEffects, out _);
    }

    private static int ExtractInt(IReadOnlyDictionary<string, object>? context, string key, int fallback)
    {
        if (context is null || !context.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => TryConvertLongToInt(longValue, fallback),
            double doubleValue => TryConvertDoubleToInt(doubleValue, fallback),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static int TryConvertLongToInt(long value, int fallback)
    {
        if (value is < int.MinValue or > int.MaxValue)
        {
            return fallback;
        }

        return checked((int)value);
    }

    private static int TryConvertDoubleToInt(double value, int fallback)
    {
        if (value is < int.MinValue or > int.MaxValue or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
        {
            return fallback;
        }

        return checked((int)value);
    }

    private static string? ExtractString(IReadOnlyDictionary<string, object>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var value))
        {
            return null;
        }

        return value?.ToString();
    }
}