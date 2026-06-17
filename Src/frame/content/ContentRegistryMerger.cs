namespace KemoCard.Frame.Content;

public sealed class ContentRegistryMerger
{
	public void Merge(
		IReadOnlyList<ModContentBundle> bundles,
		Dictionary<EContentCategory, HashSet<string>> tables,
		out ContentLoadReport report)
	{
		var conflicts = new List<ContentIdConflictEntry>();
		var ownerById = new Dictionary<(EContentCategory Category, string Id), string>();

		foreach (var bundle in bundles)
		{
			TryAddAll(bundle.ModId, EContentCategory.Character, bundle.Characters, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Enemy, bundle.Enemies, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Battle, bundle.Battles, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Event, bundle.Events, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Card, bundle.Cards, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Item, bundle.Items, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Skill, bundle.Skills, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Buff, bundle.Buffs, tables, ownerById, conflicts);
			TryAddAll(bundle.ModId, EContentCategory.Effect, bundle.Effects, tables, ownerById, conflicts);
		}

		report = new ContentLoadReport(Array.Empty<ModSkipEntry>(), conflicts, Array.Empty<ContentDefinitionValidationError>());
	}

	private static void TryAddAll(
		string modId,
		EContentCategory category,
		IReadOnlyList<string> ids,
		Dictionary<EContentCategory, HashSet<string>> tables,
		Dictionary<(EContentCategory Category, string Id), string> ownerById,
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
