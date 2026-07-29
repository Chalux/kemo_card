namespace KemoCard.Frame.Content;

public sealed class ContentModActivationPlanner
{
    public ContentModActivationResult Plan(
        IReadOnlyList<DiscoveredModEntry> validMods,
        IReadOnlyList<string> enabledModIds,
        IReadOnlyList<ModSkipEntry> discoverySkipped)
    {
        var manifestsById = validMods.ToDictionary(static m => m.Manifest.ModId, static m => m.Manifest, StringComparer.Ordinal);
        var entriesById = validMods.ToDictionary(static m => m.Manifest.ModId, static m => m, StringComparer.Ordinal);
        var expanded = ContentModRequiredExpander.Expand(enabledModIds, manifestsById);
        var skipped = new List<ModSkipEntry>(discoverySkipped);

        var candidates = new List<DiscoveredModEntry>();
        foreach (var modId in expanded)
        {
            if (!entriesById.TryGetValue(modId, out var entry))
            {
                skipped.Add(new ModSkipEntry(modId, ModSkipReason.MissingRequiredDependency, "mod not found on disk"));
                continue;
            }

            var missingRequired = entry.Manifest.Dependencies.Required
                .Where(dep => !entriesById.ContainsKey(dep) || !expanded.Contains(dep))
                .ToList();
            if (missingRequired.Count > 0)
            {
                skipped.Add(new ModSkipEntry(
                    modId,
                    ModSkipReason.MissingRequiredDependency,
                    string.Join(", ", missingRequired)));
                continue;
            }

            candidates.Add(entry);
        }

        var ordered = TopologicalSort(candidates, skipped);
        return new ContentModActivationResult(ordered, skipped, expanded);
    }

    private static List<DiscoveredModEntry> TopologicalSort(
        IReadOnlyList<DiscoveredModEntry> candidates,
        List<ModSkipEntry> skipped)
    {
        var candidateIds = candidates.Select(static c => c.Manifest.ModId).ToHashSet(StringComparer.Ordinal);
        var inDegree = candidateIds.ToDictionary(static id => id, static _ => 0, StringComparer.Ordinal);
        var dependents = candidateIds.ToDictionary(static id => id, static _ => new List<string>(), StringComparer.Ordinal);

        foreach (var entry in candidates)
        {
            foreach (var dep in entry.Manifest.Dependencies.Required.Where(candidateIds.Contains))
            {
                inDegree[entry.Manifest.ModId]++;
                dependents[dep].Add(entry.Manifest.ModId);
            }
        }

        var ready = candidates
            .Where(c => inDegree[c.Manifest.ModId] == 0)
            .OrderBy(static c => c.Manifest.LoadOrder)
            .ThenBy(static c => c.Manifest.ModId, StringComparer.Ordinal)
            .ToList();

        var ordered = new List<DiscoveredModEntry>();
        while (ready.Count > 0)
        {
            var current = ready[0];
            ready.RemoveAt(0);
            ordered.Add(current);

            foreach (var dependentId in dependents[current.Manifest.ModId].OrderBy(static id => id, StringComparer.Ordinal))
            {
                inDegree[dependentId]--;
                if (inDegree[dependentId] != 0)
                {
                    continue;
                }

                var dependent = candidates.First(c => c.Manifest.ModId == dependentId);
                InsertReadySorted(ready, dependent);
            }
        }

        if (ordered.Count == candidates.Count)
        {
            return ordered;
        }

        var cyclicIds = candidateIds.Where(id => inDegree[id] > 0).OrderBy(static id => id, StringComparer.Ordinal).ToList();
        foreach (var modId in cyclicIds)
        {
            skipped.Add(new ModSkipEntry(modId, ModSkipReason.CyclicDependency));
        }

        return ordered;
    }

    private static void InsertReadySorted(List<DiscoveredModEntry> ready, DiscoveredModEntry entry)
    {
        var index = ready.FindIndex(c =>
            c.Manifest.LoadOrder > entry.Manifest.LoadOrder
            || (c.Manifest.LoadOrder == entry.Manifest.LoadOrder
                && string.CompareOrdinal(c.Manifest.ModId, entry.Manifest.ModId) > 0));
        if (index < 0)
        {
            ready.Add(entry);
            return;
        }

        ready.Insert(index, entry);
    }
}