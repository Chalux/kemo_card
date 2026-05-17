namespace KemoCard.Frame.Content;

public sealed class ContentRegistryMerger
{
	public void Merge(
		IReadOnlyList<ModContentBundle> bundles,
		Dictionary<ContentCategory, HashSet<string>> tables,
		out ContentLoadReport report)
	{
		var conflicts = new List<ContentIdConflictEntry>();
		var ownerById = new Dictionary<(ContentCategory Category, string Id), string>();

		foreach (var bundle in bundles)
		{
			TryAddAll(bundle.ModId, ContentCategory.Character, bundle.Characters, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Battle, bundle.Battles, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Event, bundle.Events, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Card, bundle.Cards, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Item, bundle.Items, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Skill, bundle.Skills, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, ContentCategory.Buff, bundle.Buffs, tables, ownerById, conflicts);
		}

		report = new ContentLoadReport(Array.Empty<ModSkipEntry>(), conflicts);
	}

	private static void TryAddAll(
		string modId,
		ContentCategory category,
		IReadOnlyList<string> ids,
		Dictionary<ContentCategory, HashSet<string>> tables,
		Dictionary<(ContentCategory Category, string Id), string> ownerById,
		List<ContentIdConflictEntry> conflicts)
	{
		var set = tables[category];
		foreach (var id in ids)
		{
			var key = (category, id);
			if (set.Add(id))
			{
				ownerById[key] = modId;
				continue;
			}

			var winnerModId = ownerById[key];
			conflicts.Add(new ContentIdConflictEntry(category, id, winnerModId, modId));
		}
	}
}
