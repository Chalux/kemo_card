using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ScriptContextFacade
{
    private readonly ScriptCallContext _call;
    private readonly HostRng _rng;
    private readonly IModScriptLogger _logger;

    public ScriptContextFacade(ScriptCallContext call, IModScriptLogger logger)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(logger);
        _call = call;
        _rng = new HostRng(call.RunSeed, call.StreamKey);
        _logger = logger;
    }

    public bool Contains(string category, string id)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!Enum.TryParse<EContentCategory>(category, ignoreCase: true, out var contentCategory))
        {
            return false;
        }

        return _call.Registry.Contains(contentCategory, id);
    }

    public int NextInt(int minInclusive, int maxExclusive) =>
        _rng.NextInt(minInclusive, maxExclusive);

    public void Log(string message) => _logger.Log(message);
}