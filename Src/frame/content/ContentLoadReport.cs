namespace KemoCard.Frame.Content;

public sealed class ContentLoadReport
{
	public ContentLoadReport(
		IReadOnlyList<ModSkipEntry> skippedMods,
		IReadOnlyList<ContentIdConflictEntry> idConflicts)
	{
		SkippedMods = skippedMods;
		IdConflicts = idConflicts;
	}

	public IReadOnlyList<ModSkipEntry> SkippedMods { get; }

	public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }

	public bool HasIssues => SkippedMods.Count > 0 || IdConflicts.Count > 0;

	public static ContentLoadReport Empty { get; } = new(Array.Empty<ModSkipEntry>(), Array.Empty<ContentIdConflictEntry>());

	public ContentLoadReport WithSkippedMods(IReadOnlyList<ModSkipEntry> skippedMods) =>
		new(skippedMods, IdConflicts);
}
