namespace KemoCard.Frame.Content;

public sealed class ContentLoadReport
{
	public ContentLoadReport(
		IReadOnlyList<ModSkipEntry> skippedMods,
		IReadOnlyList<ContentIdConflictEntry> idConflicts,
		IReadOnlyList<ContentDefinitionValidationError> validationErrors)
	{
		SkippedMods = skippedMods;
		IdConflicts = idConflicts;
		ValidationErrors = validationErrors;
	}

	public IReadOnlyList<ModSkipEntry> SkippedMods { get; }

	public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }

	public IReadOnlyList<ContentDefinitionValidationError> ValidationErrors { get; }

	public bool HasIssues =>
		SkippedMods.Count > 0 || IdConflicts.Count > 0 || ValidationErrors.Count > 0;

	public static ContentLoadReport Empty { get; } = new(
		Array.Empty<ModSkipEntry>(),
		Array.Empty<ContentIdConflictEntry>(),
		Array.Empty<ContentDefinitionValidationError>());

	public ContentLoadReport WithSkippedMods(IReadOnlyList<ModSkipEntry> skippedMods) =>
		new(skippedMods, IdConflicts, ValidationErrors);
}
