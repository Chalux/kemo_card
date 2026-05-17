namespace KemoCard.Frame.Content;

public sealed class GameDefinitionRegistry
{
	private readonly Dictionary<ContentCategory, HashSet<string>> _tables = new()
	{
		[ContentCategory.Character] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Battle] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Event] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Card] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Item] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Skill] = new HashSet<string>(StringComparer.Ordinal),
		[ContentCategory.Buff] = new HashSet<string>(StringComparer.Ordinal),
	};

	public int DefinitionVersion { get; private set; }

	public void Rebuild(IReadOnlyList<ModContentBundle> bundles, out ContentLoadReport report)
	{
		foreach (var set in _tables.Values)
		{
			set.Clear();
		}

		var merger = new ContentRegistryMerger();
		merger.Merge(bundles, _tables, out report);
		DefinitionVersion++;
	}

	public bool Contains(ContentCategory category, string id) =>
		_tables[category].Contains(id);
}
