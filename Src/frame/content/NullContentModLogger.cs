namespace KemoCard.Frame.Content;

public sealed class NullContentModLogger : IContentModLogger
{
	public void LogSkipped(ModSkipEntry entry)
	{
	}

	public void LogConflict(ContentIdConflictEntry entry)
	{
	}
}
