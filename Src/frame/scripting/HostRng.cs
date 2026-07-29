namespace KemoCard.Frame.Scripting;

public sealed class HostRng
{
    private readonly Random _random;

    public HostRng(int runSeed, string streamKey)
    {
        var mixed = HashCode.Combine(runSeed, streamKey);
        _random = new Random(mixed);
    }

    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}