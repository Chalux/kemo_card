namespace KemoCard.Frame.Content;

public sealed class ContentModLoadException : Exception
{
	public ContentModLoadException(string modId, string message, Exception? innerException = null)
		: base($"Failed to load mod '{modId}': {message}", innerException)
	{
		ModId = modId;
	}

	public string ModId { get; }
}
