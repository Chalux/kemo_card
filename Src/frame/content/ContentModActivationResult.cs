namespace KemoCard.Frame.Content;

public sealed record ContentModActivationResult(
    IReadOnlyList<DiscoveredModEntry> OrderedActiveMods,
    IReadOnlyList<ModSkipEntry> SkippedMods,
    IReadOnlySet<string> ExpandedEnabledSet);