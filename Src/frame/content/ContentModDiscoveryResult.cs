namespace KemoCard.Frame.Content;

public sealed record ContentModDiscoveryResult(
    IReadOnlyList<DiscoveredModEntry> ValidMods,
    IReadOnlyList<ModSkipEntry> SkippedMods);