namespace KemoCard.Frame.Content;

public static class ContentModRequiredExpander
{
    public static HashSet<string> Expand(
        IEnumerable<string> enabledModIds,
        IReadOnlyDictionary<string, ContentModManifestDto> manifestsById)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(enabledModIds);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id))
            {
                continue;
            }

            if (!manifestsById.TryGetValue(id, out var manifest))
            {
                continue;
            }

            foreach (var dep in manifest.Dependencies.Required)
            {
                if (manifestsById.ContainsKey(dep))
                {
                    queue.Enqueue(dep);
                }
            }
        }

        return result;
    }
}