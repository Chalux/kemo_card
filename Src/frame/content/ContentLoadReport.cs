namespace KemoCard.Frame.Content;

public sealed record ScriptLoadError(string ModId, string ScriptPath, string Message);

public sealed class ContentLoadReport
{
	public ContentLoadReport(
		IReadOnlyList<ModSkipEntry> skippedMods,
		IReadOnlyList<ContentIdConflictEntry> idConflicts,
		IReadOnlyList<ContentDefinitionValidationError> validationErrors,
		IReadOnlyList<ScriptLoadError> scriptLoadErrors,
		IReadOnlyList<ContentDefinitionValidationError>? removedValidationErrors = null)
	{
		SkippedMods = skippedMods;
		IdConflicts = idConflicts;
		ValidationErrors = validationErrors;
		ScriptLoadErrors = scriptLoadErrors;
		RemovedValidationErrors = removedValidationErrors ?? Array.Empty<ContentDefinitionValidationError>();
	}

	public IReadOnlyList<ModSkipEntry> SkippedMods { get; }

	public IReadOnlyList<ContentIdConflictEntry> IdConflicts { get; }

	public IReadOnlyList<ContentDefinitionValidationError> ValidationErrors { get; }

	public IReadOnlyList<ContentDefinitionValidationError> RemovedValidationErrors { get; }

	public IReadOnlyList<ScriptLoadError> ScriptLoadErrors { get; }

	public bool HasIssues =>
		SkippedMods.Count > 0
		|| IdConflicts.Count > 0
		|| ValidationErrors.Count > 0
		|| RemovedValidationErrors.Count > 0
		|| ScriptLoadErrors.Count > 0;

	public static ContentLoadReport Empty { get; } = new(
		Array.Empty<ModSkipEntry>(),
		Array.Empty<ContentIdConflictEntry>(),
		Array.Empty<ContentDefinitionValidationError>(),
		Array.Empty<ScriptLoadError>());

	public ContentLoadReport WithSkippedMods(IReadOnlyList<ModSkipEntry> skippedMods) =>
		new(skippedMods, IdConflicts, ValidationErrors, ScriptLoadErrors, RemovedValidationErrors);

	public ContentLoadReport WithScriptLoadErrors(IReadOnlyList<ScriptLoadError> scriptLoadErrors) =>
		new(SkippedMods, IdConflicts, ValidationErrors, scriptLoadErrors, RemovedValidationErrors);
}
