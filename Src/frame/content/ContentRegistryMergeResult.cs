namespace KemoCard.Frame.Content;

public sealed class ContentRegistryMergeResult
{
    public ContentRegistryMergeResult(
        IReadOnlyList<ContentIdConflictEntry> idConflicts,
        IReadOnlyDictionary<(EContentCategory Category, string Id), string> ownerModIds)
    {
        IdConflicts = idConflicts;
        OwnerModIds = ownerModIds;
    }

    public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }

    public IReadOnlyDictionary<(EContentCategory Category, string Id), string> OwnerModIds { get; }
}