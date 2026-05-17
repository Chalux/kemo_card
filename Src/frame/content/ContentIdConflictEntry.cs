namespace KemoCard.Frame.Content;

public sealed record ContentIdConflictEntry(
	ContentCategory Category,
	string ContentId,
	string WinnerModId,
	string LoserModId);
