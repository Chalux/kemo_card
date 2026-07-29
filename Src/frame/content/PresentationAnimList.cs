namespace KemoCard.Frame.Content;

public static class PresentationAnimList
{
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> framesAnims,
        IReadOnlyList<string>? whitelist)
    {
        ArgumentNullException.ThrowIfNull(framesAnims);

        var filtered = framesAnims
            .Where(anim => !string.IsNullOrWhiteSpace(anim))
            .ToList();

        if (whitelist is null || whitelist.Count == 0)
            return filtered;

        var allowed = new HashSet<string>(
            whitelist.Where(item => !string.IsNullOrWhiteSpace(item)),
            StringComparer.Ordinal);

        return filtered
            .Where(anim => allowed.Contains(anim))
            .OrderBy(anim => anim, StringComparer.Ordinal)
            .ToList();
    }
}