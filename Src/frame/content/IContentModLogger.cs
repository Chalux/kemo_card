namespace KemoCard.Frame.Content;

public interface IContentModLogger
{
	void LogSkipped(ModSkipEntry entry);

	void LogConflict(ContentIdConflictEntry entry);

	void LogValidationError(ContentDefinitionValidationError entry);
}
