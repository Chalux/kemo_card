namespace KemoCard.Frame.Content;

public sealed class GameDefinitionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(EContentCategory Category, string Id), string> _ownerModIds = new();

    public GameDefinitionStore Store { get; } = new();

    public int DefinitionVersion { get; private set; }

    public void Rebuild(IReadOnlyList<ModContentBundle> bundles, out ContentLoadReport report)
    {
        lock (_gate)
        {
            var merger = new ContentRegistryMerger();
            var mergeResult = merger.Merge(bundles, Store);

            _ownerModIds.Clear();
            foreach (var (key, modId) in mergeResult.OwnerModIds)
            {
                _ownerModIds[key] = modId;
            }

            var validator = new ContentDefinitionValidator();
            var foundValidationErrors = validator.Validate(Store);
            var removedValidationErrors = Array.Empty<ContentDefinitionValidationError>();
            if (foundValidationErrors.Count > 0)
            {
                RemoveInvalidDefinitions(foundValidationErrors);
                removedValidationErrors = foundValidationErrors.ToArray();
            }

            DefinitionVersion++;
            report = new ContentLoadReport(
                Array.Empty<ModSkipEntry>(),
                mergeResult.IdConflicts,
                Array.Empty<ContentDefinitionValidationError>(),
                Array.Empty<ScriptLoadError>(),
                removedValidationErrors);
        }
    }

    public bool Contains(EContentCategory category, string id)
    {
        lock (_gate)
        {
            return Store.Contains(category, id);
        }
    }

    public bool TryGetOwnerModId(EContentCategory category, string id, out string modId)
    {
        lock (_gate)
        {
            return _ownerModIds.TryGetValue((category, id), out modId!);
        }
    }

    private void RemoveInvalidDefinitions(IReadOnlyList<ContentDefinitionValidationError> errors)
    {
        foreach (var error in errors)
        {
            Store.Remove(error.Category, error.DefinitionId);
            _ownerModIds.Remove((error.Category, error.DefinitionId));
        }
    }
}