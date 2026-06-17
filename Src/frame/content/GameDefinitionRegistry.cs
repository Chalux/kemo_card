namespace KemoCard.Frame.Content;

public sealed class GameDefinitionRegistry
{
	private readonly Dictionary<EContentCategory, HashSet<string>> _tables = new()
	{
		[EContentCategory.Character] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Enemy] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Battle] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Event] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Card] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Item] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Skill] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Buff] = new HashSet<string>(StringComparer.Ordinal),
		[EContentCategory.Effect] = new HashSet<string>(StringComparer.Ordinal),
	};

	public GameDefinitionStore Store { get; } = new();

	public int DefinitionVersion { get; private set; }

	private readonly Dictionary<(EContentCategory Category, string Id), string> _ownerModIds = new();

	public void Rebuild(IReadOnlyList<ModContentBundle> bundles, out ContentLoadReport report)
	{
		foreach (var set in _tables.Values)
		{
			set.Clear();
		}

		_ownerModIds.Clear();

		var merger = new ContentRegistryMerger();
		merger.Merge(bundles, _tables, out var mergeReport, out var ownerModIds);
		foreach (var (key, modId) in ownerModIds)
		{
			_ownerModIds[key] = modId;
		}

		Store.Rebuild(bundles, mergeReport.IdConflicts);

		var validator = new ContentDefinitionValidator();
		var validationErrors = validator.Validate(Store);
		if (validationErrors.Count > 0)
		{
			RemoveInvalidDefinitions(validationErrors);
		}

		DefinitionVersion++;
		report = new ContentLoadReport(
			mergeReport.SkippedMods,
			mergeReport.IdConflicts,
			validationErrors,
			Array.Empty<ScriptLoadError>());
	}

	public bool Contains(EContentCategory category, string id) =>
		_tables[category].Contains(id);

	public bool TryGetOwnerModId(EContentCategory category, string id, out string modId) =>
		_ownerModIds.TryGetValue((category, id), out modId!);

	private void RemoveInvalidDefinitions(IReadOnlyList<ContentDefinitionValidationError> errors)
	{
		foreach (var error in errors)
		{
			_tables[error.Category].Remove(error.DefinitionId);
			Store.Remove(error.Category, error.DefinitionId);
			_ownerModIds.Remove((error.Category, error.DefinitionId));
		}
	}
}
